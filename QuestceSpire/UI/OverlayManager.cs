using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using QuestceSpire.Core;
using QuestceSpire.GameBridge;
using QuestceSpire.Tracking;

namespace QuestceSpire.UI;

public class OverlayManager
{
	private CanvasLayer _layer;

	private PanelContainer _panel;

	private VBoxContainer _content;

	private Label _compactToggle;

	// _archetypeLabel removed — deck info lives in DECK BREAKDOWN section

	private Label _screenLabel;

	// Settings menu
	private PanelContainer _settingsMenu;
	private Button _gearButton;



	private bool _visible = true;

	private bool _showTooltips = true;

	private bool _showInGameBadges = true;

	private List<ScoredCard> _currentCards;

	private List<ScoredRelic> _currentRelics;

	private DeckAnalysis _currentDeckAnalysis;

	private string _currentScreen = "IDLE";
	public string CurrentScreen => _currentScreen;
	private int _badgeEpoch = 0; // Incremented on every screen change; stale deferred calls check this

	// In-game badges: rendered on our own CanvasLayer to avoid polluting the game's scene tree.
	// Each badge tracks the game node it should overlay so we can update position.
	private readonly List<(PanelContainer badge, WeakReference<Control> target)> _inGameBadges = new();
	// The screen node that was active when badges were injected — used to detect screen changes
	private WeakReference<Node> _badgeScreenNode;
	// The expected NGridCardHolder count when badges were injected
	private int _badgeExpectedHolderCount;

	private List<(string icon, string text, Color color)> _mapAdvice;

	private string _currentCharacter;

	// Drag state
	private bool _dragging;
	private Vector2 _dragOffset;

	// Feature 3: Opacity control
	private float _panelOpacity = 1.0f;
	private static readonly float[] OpacitySteps = { 1.0f, 0.75f, 0.50f };
	private int _opacityIndex;

	// Feature 4: Collapsible mode
	private bool _collapsed;
	// _archChipPanel removed — deck info lives in DECK BREAKDOWN section
	private VBoxContainer _deckVizContainer;

	// Feature 1: Decision history
	private bool _showHistory;

	// Section toggles (persisted)
	private bool _showDeckBreakdown = true;
	// _showDrawProb removed — feature wasn't useful

	// Staleness tracking: clear overlay when game screen changes without a patch firing
	private ulong _lastUpdateTick;

	// Feature 5: Draw probability
	private GameState _currentGameState;

	// Feature 6: Relic tenure
	private int _currentFloor;

	// Stored context for advice re-generation on toggle
	private string _currentEventId;
	private List<string> _currentEnemyIds;

	// Debug overlay (F10)
	private bool _showDebug;

	// V1: Color-coded title separator
	private HSeparator _titleSep;

	// V2: Animated transitions
	private string _previousScreen = "";

	// V4: Card art hover preview
	private PanelContainer _hoverPreview;
	private TextureRect _hoverPreviewTex;

	// A1: Win rate tracker
	private Label _winRateLabel;

	// _minimizeBtn removed — collapse to title bar instead

	// Layout stabilization counter (re-runs FitPanelHeight for first N process ticks)
	private int _layoutTicksRemaining;

	// Shop refresh: track item count and IDs to detect purchases
	private int _shopItemCount;
	private HashSet<string> _shopCardIds = new HashSet<string>();
	private HashSet<string> _shopRelicIds = new HashSet<string>();

	// _archChipVBox removed — deck info lives in DECK BREAKDOWN section

	// STS2 color palette (matched from game scenes/DLL)
	private static readonly Color ClrBg = new Color(0.034f, 0.057f, 0.11f, 0.97f);

	private static readonly Color ClrBorder = new Color(0.624f, 0.490f, 0.322f);

	private static readonly Color ClrHeader = new Color(0.92f, 0.78f, 0.35f);

	private static readonly Color ClrAccent = new Color(0.831f, 0.714f, 0.357f);

	private static readonly Color ClrSub = new Color(0.580f, 0.545f, 0.404f);

	private static readonly Color ClrPositive = new Color(0.3f, 0.8f, 0.4f);

	private static readonly Color ClrNegative = new Color(0.9f, 0.35f, 0.3f);

	private static readonly Color ClrNotes = new Color(0.72f, 0.68f, 0.6f);

	private static readonly Color ClrSkip = new Color(0.557f, 0.212f, 0.882f);

	private static readonly Color ClrExpensive = new Color(1f, 0.6f, 0.3f);

	private static readonly Color ClrHover = new Color(0.1f, 0.12f, 0.2f, 0.8f);

	private static readonly Color ClrSkipSub = new Color(0.6f, 0.6f, 0.8f);

	private static readonly Color ClrAqua = new Color(0.529f, 0.808f, 0.922f);

	private static readonly Color ClrOutline = new Color(0.02f, 0.02f, 0.04f);

	private static readonly Color ClrCream = new Color(0.92f, 0.88f, 0.78f);

	// Game fonts (loaded from res://fonts/)
	private Font _fontBody;
	private Font _fontBold;
	private Font _fontHeader;

	// Game art
	private Texture2D _goldIcon;
	private readonly Dictionary<string, Texture2D> _cardPortraitCache = new Dictionary<string, Texture2D>();
	private readonly Dictionary<string, Texture2D> _relicIconCache = new Dictionary<string, Texture2D>();

	private StyleBoxFlat _sbPanel;

	private StyleBoxFlat _sbEntry;

	private StyleBoxFlat _sbBest;

	private StyleBoxFlat _sbChip;

	private OverlaySettings _settings;
	public OverlaySettings Settings => _settings;

	private StyleBoxFlat _sbHover;

	private StyleBoxFlat _sbHoverBest;

	private StyleBoxFlat _sbSTier;

	private StyleBoxFlat _sbSTierHover;

	public OverlayManager()
	{
		_settings = OverlaySettings.Load();
		_visible = true; // Always start visible — hide is session-only
		_showTooltips = _settings.ShowTooltips;
		_showInGameBadges = _settings.ShowInGameBadges;
		_panelOpacity = _settings.PanelOpacity;
		_opacityIndex = Array.IndexOf(OpacitySteps, _panelOpacity);
		if (_opacityIndex < 0) _opacityIndex = 0;
		_collapsed = _settings.Collapsed;
		_showDeckBreakdown = _settings.ShowDeckBreakdown;
		_showHistory = false; // Always start off — user can enable in settings
		LoadGameFonts();
		LoadGameIcons();
		InitializeStyles();
		ApplyOpacity(_panelOpacity);
		BuildOverlay();
	}

	private void LoadGameFonts()
	{
		try
		{
			_fontBody = ResourceLoader.Load<Font>("res://fonts/kreon_regular.ttf");
			_fontBold = ResourceLoader.Load<Font>("res://fonts/kreon_bold.ttf");
			_fontHeader = ResourceLoader.Load<Font>("res://fonts/spectral_bold.ttf");
			Plugin.Log($"Game fonts loaded: body={_fontBody != null} bold={_fontBold != null} header={_fontHeader != null}");
		}
		catch (System.Exception ex)
		{
			Plugin.Log("Could not load game fonts, using defaults: " + ex.Message);
		}
	}

	private void LoadGameIcons()
	{
		try
		{
			_goldIcon = ResourceLoader.Load<Texture2D>("res://images/packed/sprite_fonts/gold_icon.png");
			Plugin.Log($"Game icons loaded: gold={_goldIcon != null}");
		}
		catch (System.Exception ex)
		{
			Plugin.Log("Could not load game icons: " + ex.Message);
		}
	}

	private static readonly string[] PortraitFallbackFolders = new[]
	{
		"colorless", "neutral", "shared", "common", ""
	};

	private Texture2D GetCardPortrait(string cardId, string character)
	{
		string key = $"{character}/{cardId}";
		if (_cardPortraitCache.TryGetValue(key, out var cached)) return cached;
		try
		{
			string fileName = cardId.Replace(" ", "_").Replace("'", "").Replace("-", "_").ToLowerInvariant();
			string charFolder = character?.ToLowerInvariant() ?? "ironclad";
			// Primary: character folder under packed
			string[] basePaths = new[]
			{
				"res://images/packed/card_portraits",
				"res://images/card_portraits"
			};
			Texture2D tex = null;
			foreach (string basePath in basePaths)
			{
				// Try character folder first
				tex = ResourceLoader.Load<Texture2D>($"{basePath}/{charFolder}/{fileName}.png");
				if (tex != null) break;
				// Try fallback folders
				foreach (string fallback in PortraitFallbackFolders)
				{
					string path = string.IsNullOrEmpty(fallback)
						? $"{basePath}/{fileName}.png"
						: $"{basePath}/{fallback}/{fileName}.png";
					tex = ResourceLoader.Load<Texture2D>(path);
					if (tex != null) break;
				}
				if (tex != null) break;
			}
			if (tex == null)
				Plugin.Log($"No portrait found for card '{cardId}' (file: {fileName}, char: {charFolder})");
			_cardPortraitCache[key] = tex;
			return tex;
		}
		catch
		{
			_cardPortraitCache[key] = null;
			return null;
		}
	}

	private Texture2D GetRelicIcon(string relicId)
	{
		if (_relicIconCache.TryGetValue(relicId, out var cached)) return cached;
		try
		{
			string fileName = relicId.Replace(" ", "_").Replace("'", "").Replace("-", "_").ToLowerInvariant();
			string path = $"res://images/relics/{fileName}.png";
			var tex = ResourceLoader.Load<Texture2D>(path);
			_relicIconCache[relicId] = tex;
			return tex;
		}
		catch
		{
			_relicIconCache[relicId] = null;
			return null;
		}
	}

	private void ApplyFont(Label label, Font font)
	{
		if (font != null)
			label.AddThemeFontOverride("font", font);
	}

	private bool IsOverlayValid()
	{
		if (_layer != null && GodotObject.IsInstanceValid(_layer) && _panel != null && GodotObject.IsInstanceValid(_panel) && _content != null)
		{
			return GodotObject.IsInstanceValid(_content);
		}
		return false;
	}

	private bool EnsureOverlay()
	{
		if (IsOverlayValid())
		{
			return true;
		}
		// Remove old nodes from scene tree before rebuilding
		try
		{
			if (_layer != null && GodotObject.IsInstanceValid(_layer))
			{
				_layer.GetParent()?.RemoveChild(_layer);
				_layer.QueueFree();
			}
			if (_hoverPreview != null && GodotObject.IsInstanceValid(_hoverPreview))
			{
				_hoverPreview.GetParent()?.RemoveChild(_hoverPreview);
				_hoverPreview.QueueFree();
			}
		}
		catch { }
		_layer = null;
		_panel = null;
		_content = null;
		_screenLabel = null;
		_deckVizContainer = null;
		_titleSep = null;
		_winRateLabel = null;
		_hoverPreview = null;
		_hoverPreviewTex = null;
		InitializeStyles();
		BuildOverlay();
		return IsOverlayValid();
	}

	private void InitializeStyles()
	{
		_sbPanel = new StyleBoxFlat();
		_sbPanel.BgColor = ClrBg;
		_sbPanel.BorderWidthTop = 3;
		_sbPanel.BorderWidthLeft = 3;
		_sbPanel.BorderWidthRight = 1;
		_sbPanel.BorderWidthBottom = 1;
		_sbPanel.BorderColor = ClrBorder;
		_sbPanel.CornerRadiusTopLeft = 0;
		_sbPanel.CornerRadiusTopRight = 18;
		_sbPanel.CornerRadiusBottomLeft = 18;
		_sbPanel.CornerRadiusBottomRight = 0;
		StyleBoxFlat sbPanel3 = _sbPanel;
		float contentMarginLeft = (_sbPanel.ContentMarginRight = 20f);
		sbPanel3.ContentMarginLeft = contentMarginLeft;
		StyleBoxFlat sbPanel4 = _sbPanel;
		contentMarginLeft = (_sbPanel.ContentMarginBottom = 20f);
		sbPanel4.ContentMarginTop = contentMarginLeft;
		_sbPanel.ShadowSize = 12;
		_sbPanel.ShadowColor = new Color(0f, 0f, 0f, 0.5f);
		_sbEntry = new StyleBoxFlat();
		_sbEntry.BgColor = new Color(0.06f, 0.08f, 0.14f, 0.6f);
		_sbEntry.CornerRadiusTopLeft = 8;
		_sbEntry.CornerRadiusTopRight = 8;
		_sbEntry.CornerRadiusBottomLeft = 8;
		_sbEntry.CornerRadiusBottomRight = 8;
		StyleBoxFlat sbEntry3 = _sbEntry;
		contentMarginLeft = (_sbEntry.ContentMarginRight = 14f);
		sbEntry3.ContentMarginLeft = contentMarginLeft;
		StyleBoxFlat sbEntry4 = _sbEntry;
		contentMarginLeft = (_sbEntry.ContentMarginBottom = 8f);
		sbEntry4.ContentMarginTop = contentMarginLeft;
		_sbHover = _sbEntry.Duplicate() as StyleBoxFlat;
		if (_sbHover != null)
		{
			_sbHover.BgColor = ClrHover;
			_sbHover.BorderWidthLeft = 3;
			_sbHover.BorderColor = ClrSub;
		}
		_sbBest = _sbEntry.Duplicate() as StyleBoxFlat;
		if (_sbBest != null)
		{
			_sbBest.BgColor = new Color(0.831f, 0.714f, 0.357f, 0.1f);
			_sbBest.BorderWidthLeft = 4;
			_sbBest.BorderColor = ClrAccent;
		}
		_sbHoverBest = _sbBest?.Duplicate() as StyleBoxFlat;
		if (_sbHoverBest != null)
		{
			_sbHoverBest.BgColor = new Color(0.831f, 0.714f, 0.357f, 0.15f);
		}
		_sbSTier = _sbBest?.Duplicate() as StyleBoxFlat;
		if (_sbSTier != null)
		{
			_sbSTier.BorderWidthLeft = 5;
			_sbSTier.ShadowSize = 10;
			_sbSTier.ShadowColor = new Color(ClrAccent, 0.6f);
		}
		_sbSTierHover = _sbSTier?.Duplicate() as StyleBoxFlat;
		if (_sbSTierHover != null)
		{
			_sbSTierHover.BgColor = new Color(0.831f, 0.714f, 0.357f, 0.18f);
		}
		_sbChip = new StyleBoxFlat();
		_sbChip.BgColor = new Color(0.02f, 0.03f, 0.07f, 0.7f);
		StyleBoxFlat sbChip = _sbChip;
		int chipRadius = (_sbChip.CornerRadiusTopRight = 16);
		sbChip.CornerRadiusTopLeft = chipRadius;
		StyleBoxFlat sbChip2 = _sbChip;
		chipRadius = (_sbChip.CornerRadiusBottomRight = 16);
		sbChip2.CornerRadiusBottomLeft = chipRadius;
		StyleBoxFlat sbChip3 = _sbChip;
		contentMarginLeft = (_sbChip.ContentMarginRight = 12f);
		sbChip3.ContentMarginLeft = contentMarginLeft;
		StyleBoxFlat sbChip4 = _sbChip;
		contentMarginLeft = (_sbChip.ContentMarginBottom = 4f);
		sbChip4.ContentMarginTop = contentMarginLeft;
		_sbChip.BorderWidthBottom = 1;
		_sbChip.BorderWidthTop = 1;
		_sbChip.BorderColor = new Color(ClrAccent, 0.3f);
	}

	private void BuildOverlay()
	{
		SceneTree sceneTree = Engine.GetMainLoop() as SceneTree;
		if (sceneTree?.Root == null)
		{
			Plugin.Log("SceneTree not ready — overlay deferred.");
			return;
		}
		_layer = new CanvasLayer();
		_layer.Layer = 100;
		_panel = new PanelContainer();
		_panel.AnchorLeft = 1f;
		_panel.AnchorRight = 1f;
		_panel.AnchorTop = 0f;
		_panel.AnchorBottom = 0f;
		_panel.OffsetLeft = _settings.OffsetLeft;
		_panel.OffsetRight = _settings.OffsetRight;
		_panel.OffsetTop = _settings.OffsetTop;
		_panel.OffsetBottom = _settings.OffsetTop + 40;
		_panel.GrowVertical = Control.GrowDirection.End;
		_panel.AddThemeStyleboxOverride("panel", _sbPanel);
		_panel.MouseFilter = Control.MouseFilterEnum.Stop;
		VBoxContainer vBoxContainer = new VBoxContainer();
		vBoxContainer.AddThemeConstantOverride("separation", 10);
		_panel.AddChild(vBoxContainer, forceReadableName: false, Node.InternalMode.Disabled);

		// Title bar area (draggable) — Pass so child buttons still receive clicks
		VBoxContainer titleBar = new VBoxContainer();
		titleBar.MouseFilter = Control.MouseFilterEnum.Pass;
		titleBar.MouseDefaultCursorShape = Control.CursorShape.Drag;
		titleBar.GuiInput += (InputEvent ev) => OnTitleBarInput(ev);
		vBoxContainer.AddChild(titleBar, forceReadableName: false, Node.InternalMode.Disabled);

		HBoxContainer titleRow = new HBoxContainer();
		titleRow.MouseFilter = Control.MouseFilterEnum.Pass;
		titleBar.AddChild(titleRow, forceReadableName: false, Node.InternalMode.Disabled);
		Label label = new Label();
		label.Text = "QU'EST-CE SPIRE?";
		ApplyFont(label, _fontBold);
		label.AddThemeFontSizeOverride("font_size", 28);
		label.AddThemeColorOverride("font_color", ClrHeader);
		label.AddThemeConstantOverride("outline_size", 4);
		label.AddThemeColorOverride("font_outline_color", ClrOutline);
		label.MouseFilter = Control.MouseFilterEnum.Ignore;
		label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		titleRow.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
		// (Settings button added at bottom of content in Rebuild)
		// Compact/expand toggle
		_compactToggle = new Label();
		_compactToggle.Text = "\u25B2";
		ApplyFont(_compactToggle, _fontBold);
		_compactToggle.AddThemeFontSizeOverride("font_size", 18);
		_compactToggle.AddThemeColorOverride("font_color", ClrSub);
		_compactToggle.MouseFilter = Control.MouseFilterEnum.Stop;
		_compactToggle.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		_compactToggle.GuiInput += (InputEvent ev) =>
		{
			if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
				ToggleCollapsed();
		};
		titleRow.AddChild(_compactToggle, forceReadableName: false, Node.InternalMode.Disabled);

		// Decorative separator under title (V1: color-coded)
		_titleSep = new HSeparator();
		_titleSep.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(ClrBorder, 0.6f), Thickness = 2 });
		_titleSep.MouseFilter = Control.MouseFilterEnum.Ignore;
		titleBar.AddChild(_titleSep, forceReadableName: false, Node.InternalMode.Disabled);

		_screenLabel = new Label();
		_screenLabel.Text = "WAITING...  (drag to move)";
		ApplyFont(_screenLabel, _fontBold);
		_screenLabel.AddThemeFontSizeOverride("font_size", 14);
		_screenLabel.AddThemeColorOverride("font_color", ClrSub);
		_screenLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		titleBar.AddChild(_screenLabel, forceReadableName: false, Node.InternalMode.Disabled);

		// A1: Win rate label
		_winRateLabel = new Label();
		_winRateLabel.Text = "";
		_winRateLabel.Visible = false;
		ApplyFont(_winRateLabel, _fontBody);
		_winRateLabel.AddThemeFontSizeOverride("font_size", 17);
		_winRateLabel.AddThemeColorOverride("font_color", ClrSub);
		_winRateLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		titleBar.AddChild(_winRateLabel, forceReadableName: false, Node.InternalMode.Disabled);

		// Archetype chip panel removed — deck info lives in DECK BREAKDOWN collapsible section
		// Deck composition visualization container (Feature 2)
		_deckVizContainer = new VBoxContainer();
		_deckVizContainer.AddThemeConstantOverride("separation", 4);
		_deckVizContainer.Visible = false; // hidden — deck info lives in DECK BREAKDOWN section
		vBoxContainer.AddChild(_deckVizContainer, forceReadableName: false, Node.InternalMode.Disabled);
		// Content container — no scroll, panel auto-expands to fit
		_content = new VBoxContainer();
		_content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_content.AddThemeConstantOverride("separation", 6);
		vBoxContainer.AddChild(_content, forceReadableName: false, Node.InternalMode.Disabled);
		_layer.AddChild(_panel, forceReadableName: false, Node.InternalMode.Disabled);
		OverlayInputHandler node = new OverlayInputHandler(this);
		_layer.AddChild(node, forceReadableName: false, Node.InternalMode.Disabled);
		BuildSettingsMenu();
		_panel.Visible = _visible;
		// Apply collapsed state
		if (_collapsed)
		{
			_content.Visible = false;
			_deckVizContainer.Visible = false;
		}
		// V4: Card art hover preview
		_hoverPreview = new PanelContainer();
		_hoverPreview.Visible = false;
		_hoverPreview.ZIndex = 101;
		_hoverPreview.ClipContents = true;
		_hoverPreview.MouseFilter = Control.MouseFilterEnum.Ignore;
		StyleBoxFlat hpStyle = new StyleBoxFlat();
		hpStyle.BgColor = ClrBg;
		hpStyle.BorderWidthTop = 2;
		hpStyle.BorderWidthBottom = 2;
		hpStyle.BorderWidthLeft = 2;
		hpStyle.BorderWidthRight = 2;
		hpStyle.BorderColor = ClrBorder;
		hpStyle.CornerRadiusTopLeft = 8;
		hpStyle.CornerRadiusTopRight = 8;
		hpStyle.CornerRadiusBottomLeft = 8;
		hpStyle.CornerRadiusBottomRight = 8;
		hpStyle.ShadowSize = 8;
		hpStyle.ShadowColor = new Color(0f, 0f, 0f, 0.6f);
		hpStyle.ContentMarginTop = 4f;
		hpStyle.ContentMarginBottom = 4f;
		hpStyle.ContentMarginLeft = 4f;
		hpStyle.ContentMarginRight = 4f;
		_hoverPreview.AddThemeStyleboxOverride("panel", hpStyle);
		_hoverPreviewTex = new TextureRect();
		_hoverPreviewTex.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_hoverPreviewTex.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		_hoverPreviewTex.CustomMinimumSize = new Vector2(200f, 200f);
		_hoverPreview.AddChild(_hoverPreviewTex, forceReadableName: false, Node.InternalMode.Disabled);
		_layer.AddChild(_hoverPreview, forceReadableName: false, Node.InternalMode.Disabled);

		_layoutTicksRemaining = 5;

		sceneTree.Root.CallDeferred("add_child", _layer);
		Plugin.Log("Overlay built and attached to scene tree.");
	}

	private bool IsClickOnControl(Vector2 globalPos, Control control)
	{
		if (control == null || !GodotObject.IsInstanceValid(control)) return false;
		return control.GetGlobalRect().HasPoint(globalPos);
	}

	private void OnTitleBarInput(InputEvent ev)
	{
		if (ev is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex == MouseButton.Left)
			{
				// Compact toggle now handles its own click via GuiInput
				if (mb.DoubleClick)
				{
					ToggleCollapsed();
					return;
				}
				if (mb.Pressed)
				{
					_dragging = true;
					_dragOffset = mb.GlobalPosition - _panel.GlobalPosition;
				}
				else
				{
					_dragging = false;
					SavePosition();
				}
			}
		}
		else if (ev is InputEventMouseMotion mm && _dragging)
		{
			Vector2 newPos = mm.GlobalPosition - _dragOffset;
			float panelW = _panel.OffsetRight - _panel.OffsetLeft;
			float panelH = _panel.OffsetBottom - _panel.OffsetTop;
			// Convert from anchor-relative offsets to absolute position
			// Panel is anchored to top-right (AnchorLeft=1, AnchorRight=1)
			Vector2 viewportSize = _panel.GetViewportRect().Size;
			_panel.OffsetLeft = newPos.X - viewportSize.X;
			_panel.OffsetRight = newPos.X - viewportSize.X + panelW;
			_panel.OffsetTop = newPos.Y;
			_panel.OffsetBottom = newPos.Y + panelH;
		}
	}

	private void MarkUpdated()
	{
		_lastUpdateTick = Time.GetTicksMsec();
	}

	/// <summary>
	/// Called periodically (~1s) by OverlayInputHandler._Process.
	/// Detects when game screen has changed without a patch firing and clears stale data.
	/// </summary>
	public void CheckForStaleScreen()
	{
		if (_lastUpdateTick == 0) return;
		// Badge lifecycle: badges live on our CanvasLayer, not in the game tree.
		// Clear them when screen changes away from card reward.
		// Update positions when still on card reward (targets may have moved).
		try
		{
			if (_inGameBadges.Count > 0)
			{
				if (_currentScreen != "CARD REWARD" || !GamePatches.IsGenuineCardReward)
				{
					ClearInGameBadges();
				}
				else
				{
					// Still on card reward — update positions and prune dead targets
					UpdateInGameBadgePositions();
				}
			}
		}
		catch { }
		// Shop live-refresh: re-read shop state every tick to catch purchases
		if (_currentScreen == "MERCHANT SHOP")
		{
			try { RefreshShopIfChanged(); } catch { }
		}
		// Event card offering: detect when an event offers cards (not handled by ShowScreen patch)
		if (_currentScreen == "EVENT" && _currentCards == null)
		{
			try { CheckForEventCardOffering(); } catch { }
		}
		ulong elapsed = Time.GetTicksMsec() - _lastUpdateTick;
		// If advice is showing for 8+ seconds, check if the game screen is still valid
		if (elapsed > 8000 && _currentScreen != null)
		{
			try
			{
				SceneTree tree = Engine.GetMainLoop() as SceneTree;
				if (tree?.Root == null) return;
				bool hasCardScreen = HasNodeOfType(tree.Root, "NCardRewardSelectionScreen", 4);
				bool hasRelicScreen = HasNodeOfType(tree.Root, "NChooseARelicSelection", 4);
				bool hasShopScreen = HasNodeOfType(tree.Root, "NMerchantInventory", 4);
				bool hasRestSite = HasNodeOfType(tree.Root, "NRestSiteRoom", 4);
				bool hasCombat = HasNodeOfType(tree.Root, "NCombatRoom", 4);
				bool hasEvent = HasNodeOfType(tree.Root, "NEventRoom", 4);
				bool isEventCardOffer = _currentScreen == "EVENT CARD OFFER";
				bool isCardAdvice = _currentScreen == "CARD REWARD" || _currentScreen == "CARD REMOVAL" || _currentScreen == "CARD UPGRADE" || isEventCardOffer;
				bool isRelicAdvice = _currentScreen == "RELIC REWARD";
				bool isShopAdvice = _currentScreen == "MERCHANT SHOP";
				bool isRestAdvice = _currentScreen == "REST SITE";
				bool isCombatAdvice = _currentScreen == "COMBAT";
				bool isEventAdvice = _currentScreen == "EVENT";

				bool screenGone = false;
				if (isCardAdvice && !hasCardScreen) screenGone = true;
				if (isRelicAdvice && !hasRelicScreen) screenGone = true;
				if (isShopAdvice && !hasShopScreen) screenGone = true;
				if (isRestAdvice && !hasRestSite) screenGone = true;
				if (isCombatAdvice && !hasCombat) screenGone = true;
				if (isEventAdvice && !hasEvent) screenGone = true;

				if (screenGone)
				{
					Plugin.Log($"Stale screen detected: {_currentScreen} no longer active — clearing overlay");
					Clear();
				}
			}
			catch { }
		}
	}

	private void CheckForEventCardOffering()
	{
		// First check if _lastCardOptions was already set (ShowScreen fired)
		GameState gameState = GameStateReader.ReadCurrentState();
		if (gameState == null) return;
		if (gameState.OfferedCards != null && gameState.OfferedCards.Count > 0)
		{
			Plugin.Log($"Event card offering detected (from state): {gameState.OfferedCards.Count} cards");
			DeckAnalysis da = Plugin.DeckAnalyzer.Analyze(gameState.Character, gameState.DeckCards, Plugin.TierEngine);
			List<ScoredCard> scored = Plugin.SynergyScorer.ScoreOfferings(gameState.OfferedCards, da, gameState.Character, gameState.ActNumber, gameState.Floor, Plugin.TierEngine, Plugin.AdaptiveScorer);
			ShowCardAdvice(scored, da, gameState.Character, "EVENT CARD OFFER");
			// No in-game badges for events — can't distinguish reward from upgrade/transform
			return;
		}
		// ShowScreen may not have fired — try to find card screen node and extract cards via reflection
		SceneTree sceneTree = Engine.GetMainLoop() as SceneTree;
		if (sceneTree?.Root == null) return;
		Node cardScreen = FindNodeOfType(sceneTree.Root, "NCardRewardSelectionScreen", 4);
		if (cardScreen == null) return;
		// Try to extract cards from the screen
		try
		{
			var offeredCards = ExtractCardsFromScreen(cardScreen);
			if (offeredCards == null || offeredCards.Count == 0) return;
			Plugin.Log($"Event card offering detected (from screen node): {offeredCards.Count} cards");
			DeckAnalysis deckAnalysis = Plugin.DeckAnalyzer.Analyze(gameState.Character, gameState.DeckCards, Plugin.TierEngine);
			List<ScoredCard> cards = Plugin.SynergyScorer.ScoreOfferings(offeredCards, deckAnalysis, gameState.Character, gameState.ActNumber, gameState.Floor, Plugin.TierEngine, Plugin.AdaptiveScorer);
			ShowCardAdvice(cards, deckAnalysis, gameState.Character, "EVENT CARD OFFER");
		}
		catch (Exception ex)
		{
			Plugin.Log($"CheckForEventCardOffering reflection error: {ex.Message}");
		}
	}

	private static List<CardInfo> ExtractCardsFromScreen(Node screen)
	{
		var cardsField = screen.GetType().GetField("_cards",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
		var cardsProp = screen.GetType().GetProperty("Cards",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
		object cardsObj = cardsField?.GetValue(screen) ?? cardsProp?.GetValue(screen);
		if (cardsObj is IReadOnlyList<CardCreationResult> screenCards && screenCards.Count > 0)
		{
			var result = new List<CardInfo>();
			foreach (var cr in screenCards)
			{
				if (cr.Card != null)
					result.Add(GameStateReader.CardModelToInfo(cr.Card));
			}
			return result;
		}
		// Try card holders
		var holdersField = screen.GetType().GetField("_cardHolders",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
		if (holdersField != null)
		{
			var holders = holdersField.GetValue(screen);
			if (holders is System.Collections.IList holderList && holderList.Count > 0 && holderList.Count <= 5)
			{
				var result = new List<CardInfo>();
				foreach (var holder in holderList)
				{
					var crProp = holder.GetType().GetProperty("CreationResult",
						System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
					if (crProp?.GetValue(holder) is CardCreationResult cr && cr.Card != null)
						result.Add(GameStateReader.CardModelToInfo(cr.Card));
				}
				return result;
			}
		}
		return null;
	}

	private void RefreshShopIfChanged()
	{
		GameState gameState = GameStateReader.ReadCurrentState();
		if (gameState == null) return;
		int currentCount = (gameState.ShopCards?.Count ?? 0) + (gameState.ShopRelics?.Count ?? 0);
		if (currentCount == _shopItemCount) return;
		// Item count changed — a purchase happened
		Plugin.Log($"Shop inventory changed ({_shopItemCount} → {currentCount}), refreshing...");

		// Detect what was purchased by diffing current vs previous item IDs
		var currentCardIds = new HashSet<string>(gameState.ShopCards?.Select(c => c.Id) ?? Enumerable.Empty<string>());
		var currentRelicIds = new HashSet<string>(gameState.ShopRelics?.Select(r => r.Id) ?? Enumerable.Empty<string>());
		var purchasedCards = _shopCardIds.Except(currentCardIds).ToList();
		var purchasedRelics = _shopRelicIds.Except(currentRelicIds).ToList();

		// Record shop purchases as decisions
		if (Plugin.RunTracker != null && (purchasedCards.Count > 0 || purchasedRelics.Count > 0))
		{
			var deckIds = gameState.DeckCards?.ConvertAll(c => c.Id) ?? new List<string>();
			var relicIds = gameState.CurrentRelics?.ConvertAll(r => r.Id) ?? new List<string>();
			foreach (string cardId in purchasedCards)
			{
				var offeredIds = _shopCardIds.ToList();
				Plugin.RunTracker.RecordDecision(
					DecisionEventType.ShopCard, offeredIds, cardId,
					deckIds, relicIds,
					gameState.CurrentHP, gameState.MaxHP, gameState.Gold,
					gameState.ActNumber, gameState.Floor);
				Plugin.Log($"Shop card purchase tracked: {cardId}");
			}
			foreach (string relicId in purchasedRelics)
			{
				var offeredIds = _shopRelicIds.ToList();
				Plugin.RunTracker.RecordDecision(
					DecisionEventType.ShopRelic, offeredIds, relicId,
					deckIds, relicIds,
					gameState.CurrentHP, gameState.MaxHP, gameState.Gold,
					gameState.ActNumber, gameState.Floor);
				Plugin.Log($"Shop relic purchase tracked: {relicId}");
			}
		}

		DeckAnalysis deckAnalysis = Plugin.DeckAnalyzer.Analyze(gameState.Character, gameState.DeckCards, Plugin.TierEngine);
		List<ScoredCard> cards = Plugin.SynergyScorer.ScoreOfferings(gameState.ShopCards, deckAnalysis, gameState.Character, gameState.ActNumber, gameState.Floor, Plugin.TierEngine, Plugin.AdaptiveScorer);
		List<ScoredRelic> relics = Plugin.SynergyScorer.ScoreRelicOfferings(gameState.ShopRelics, deckAnalysis, gameState.Character, gameState.ActNumber, gameState.Floor, Plugin.TierEngine, Plugin.AdaptiveScorer);
		ShowShopAdvice(cards, relics, deckAnalysis, gameState.Character);
	}

	private static bool IsInsideMerchant(Node node)
	{
		Node current = node;
		while (current != null)
		{
			string typeName = current.GetType().Name;
			if (typeName.Contains("Merchant") || typeName.Contains("merchant"))
				return true;
			current = current.GetParent();
		}
		return false;
	}

	private static Node FindNodeOfType(Node root, string typeName, int maxDepth)
	{
		if (maxDepth <= 0 || root == null) return null;
		if (root.GetType().Name == typeName) return root;
		foreach (Node child in root.GetChildren())
		{
			Node found = FindNodeOfType(child, typeName, maxDepth - 1);
			if (found != null) return found;
		}
		return null;
	}



	private static bool HasNodeOfType(Node root, string typeName, int maxDepth)
	{
		if (maxDepth <= 0 || root == null) return false;
		if (root.GetType().Name == typeName) return true;
		foreach (Node child in root.GetChildren())
		{
			if (HasNodeOfType(child, typeName, maxDepth - 1)) return true;
		}
		return false;
	}

	public void ShowCardAdvice(List<ScoredCard> cards, DeckAnalysis deckAnalysis = null, string character = null, string screenLabel = "CARD REWARD")
	{
		_currentCards = cards;
		_currentRelics = null;
		_currentDeckAnalysis = deckAnalysis;
		_currentCharacter = character;
		_currentScreen = screenLabel;
		_mapAdvice = null;
		MarkUpdated();
		Rebuild();
	}

	public void SetScreenLabel(string screen)
	{
		_currentScreen = screen;
		Rebuild();
	}

	public void ShowRelicAdvice(List<ScoredRelic> relics, DeckAnalysis deckAnalysis = null, string character = null)
	{
		_currentRelics = relics;
		_currentCards = null;
		_currentDeckAnalysis = deckAnalysis;
		_currentCharacter = character;
		_currentScreen = "RELIC REWARD";
		_mapAdvice = null;
		MarkUpdated();
		Rebuild();
	}

	public void ShowCardRemovalAdvice(List<ScoredCard> removalCandidates, DeckAnalysis deckAnalysis = null, string character = null)
	{
		_currentCards = removalCandidates?.Take(5).ToList();
		_currentRelics = null;
		_currentDeckAnalysis = deckAnalysis;
		_currentCharacter = character;
		_currentScreen = "CARD REMOVAL";
		_mapAdvice = null;
		MarkUpdated();
		Rebuild();
	}

	private void SavePosition()
	{
		if (_panel != null && GodotObject.IsInstanceValid(_panel) && _settings != null)
		{
			_settings.OffsetLeft = _panel.OffsetLeft;
			_settings.OffsetRight = _panel.OffsetRight;
			_settings.OffsetTop = _panel.OffsetTop;
			// Don't save OffsetBottom — auto-calculated from content height
			_settings.Save();
		}
	}

	public void ShowRestSiteAdvice(DeckAnalysis deckAnalysis, int currentHP, int maxHP, int actNumber, int floor, GameState gameState = null)
	{
		_currentCards = null;
		_currentRelics = null;
		_currentDeckAnalysis = deckAnalysis;
		_currentCharacter = deckAnalysis?.Character ?? _currentCharacter;
		_currentScreen = "REST SITE";
		_currentFloor = floor;
		_currentGameState = gameState;
		_mapAdvice = GenerateRestSiteAdvice(deckAnalysis, currentHP, maxHP, actNumber, floor, gameState);
		MarkUpdated();
		Rebuild();
	}

	public void ShowUpgradeAdvice(DeckAnalysis deckAnalysis, GameState gameState, string character)
	{
		_currentCards = null;
		_currentRelics = null;
		_currentDeckAnalysis = deckAnalysis;
		_currentCharacter = character;
		_currentScreen = "CARD UPGRADE";
		_currentGameState = gameState;
		_mapAdvice = null;
		MarkUpdated();
		Rebuild();
	}

	public void ShowCombatAdvice(DeckAnalysis deckAnalysis, int currentHP, int maxHP, int actNumber, int floor, GameState gameState = null, List<string> enemyIds = null)
	{
		_currentCards = null;
		_currentRelics = null;
		_currentDeckAnalysis = deckAnalysis;
		_currentCharacter = deckAnalysis?.Character ?? _currentCharacter;
		_currentScreen = "COMBAT";
		_currentFloor = floor;
		_currentGameState = gameState;
		_currentEnemyIds = enemyIds;
		_currentEventId = null;
		_mapAdvice = new List<(string, string, Color)>();

		// Enemy-specific tips (prepended before generic)
		if (_settings.ShowEnemyTips && enemyIds != null && Plugin.EnemyAdvisor != null)
		{
			var tips = Plugin.EnemyAdvisor.GetTips(enemyIds);
			if (tips != null && tips.Count > 0)
			{
				_mapAdvice.Add(("##", "ENEMY INTEL", ClrAccent));
				foreach (var enemy in tips)
				{
					Color dangerColor = enemy.DangerLevel switch
					{
						"extreme" => ClrNegative,
						"high" => ClrExpensive,
						"medium" => ClrAccent,
						_ => ClrSub
					};
					string dangerIcon = enemy.DangerLevel switch
					{
						"extreme" => "\u2620",
						"high" => "\u26a0",
						"medium" => "\u25c6",
						_ => "\u25cb"
					};
					_mapAdvice.Add((dangerIcon, $"{enemy.EnemyName} [{enemy.DangerLevel?.ToUpperInvariant()}]", dangerColor));
					if (enemy.Tips != null)
					{
						foreach (var tip in enemy.Tips)
						{
							_mapAdvice.Add(("\u2022", tip, ClrCream));
						}
					}
				}
			}
		}

		// Generic combat advice (appended)
		if (_settings.ShowCombatAdvice)
		{
			var combatAdvice = GenerateCombatAdvice(deckAnalysis, currentHP, maxHP, actNumber, floor);
			if (combatAdvice.Count > 0)
			{
				_mapAdvice.Add(("##", "DECK STRATEGY", ClrAccent));
				_mapAdvice.AddRange(combatAdvice);
			}
		}
		MarkUpdated();
		Rebuild();
	}

	public void ShowEventAdvice(DeckAnalysis deckAnalysis, int currentHP, int maxHP, int gold, int actNumber, int floor, string eventId = null)
	{
		_currentCards = null;
		_currentRelics = null;
		_currentDeckAnalysis = deckAnalysis;
		_currentCharacter = deckAnalysis?.Character ?? _currentCharacter;
		_currentScreen = "EVENT";
		_currentFloor = floor;
		_currentGameState = new GameState { CurrentHP = currentHP, MaxHP = maxHP, Gold = gold, ActNumber = actNumber, Floor = floor };
		_currentEventId = eventId;
		_currentEnemyIds = null;
		_mapAdvice = new List<(string, string, Color)>();

		// Event-specific advice (prepended before generic)
		if (_settings.ShowEventAdvice && eventId != null && Plugin.EventAdvisor != null)
		{
			var entry = Plugin.EventAdvisor.GetAdvice(eventId);
			if (entry != null)
			{
				int deckSize = deckAnalysis?.TotalCards ?? 0;
				var choices = Plugin.EventAdvisor.EvaluateChoices(entry, currentHP, maxHP, gold, deckSize, actNumber);
				if (choices != null && choices.Count > 0)
				{
					_mapAdvice.Add(("##", $"EVENT: {entry.EventName}", ClrAccent));
					foreach (var (label, rating, notes) in choices)
					{
						string icon = rating switch
						{
							"good" => "\u2714",
							"bad" => "\u2716",
							_ => "\u25c6"
						};
						Color color = rating switch
						{
							"good" => ClrPositive,
							"bad" => ClrNegative,
							_ => ClrExpensive
						};
						string text = string.IsNullOrEmpty(notes) ? label : $"{label} — {notes}";
						_mapAdvice.Add((icon, text, color));
					}
				}
			}
		}

		// Generic event advice (appended)
		if (_settings.ShowEventAdvice)
		{
			var eventAdvice = GenerateEventAdvice(deckAnalysis, currentHP, maxHP, gold, actNumber, floor);
			if (eventAdvice.Count > 0)
			{
				_mapAdvice.Add(("##", "GENERAL TIPS", ClrAccent));
				_mapAdvice.AddRange(eventAdvice);
			}
		}
		MarkUpdated();
		Rebuild();
	}

	public void ShowMapAdvice(DeckAnalysis deckAnalysis, int currentHP, int maxHP, int gold, int actNumber, int floor)
	{
		_currentFloor = floor;
		_currentGameState = new GameState { CurrentHP = currentHP, MaxHP = maxHP, Gold = gold, ActNumber = actNumber, Floor = floor };
		_currentCards = null;
		_currentRelics = null;
		_currentDeckAnalysis = deckAnalysis;
		_currentCharacter = deckAnalysis?.Character ?? _currentCharacter;
		_currentScreen = "MAP";
		_currentEventId = null;
		_currentEnemyIds = null;
		_mapAdvice = _settings.ShowMapAdvice
			? GenerateMapAdvice(deckAnalysis, currentHP, maxHP, gold, actNumber, floor)
			: new List<(string, string, Color)>();
		MarkUpdated();
		Rebuild();
	}

	/// <summary>
	/// Re-generates _mapAdvice from stored context after a settings toggle change.
	/// </summary>
	private void RegenerateAdvice()
	{
		var da = _currentDeckAnalysis;
		if (da == null) { Rebuild(); return; }
		var gs = _currentGameState;
		int hp = gs?.CurrentHP ?? 0;
		int maxHP = gs?.MaxHP ?? 1;
		int gold = gs?.Gold ?? 0;
		int act = gs?.ActNumber ?? 1;
		int floor = _currentFloor;

		switch (_currentScreen)
		{
			case "COMBAT":
				_mapAdvice = new List<(string, string, Color)>();
				if (_settings.ShowEnemyTips && _currentEnemyIds != null && Plugin.EnemyAdvisor != null)
				{
					var tips = Plugin.EnemyAdvisor.GetTips(_currentEnemyIds);
					if (tips != null && tips.Count > 0)
					{
						_mapAdvice.Add(("##", "ENEMY INTEL", ClrAccent));
						foreach (var enemy in tips)
						{
							Color dangerColor = enemy.DangerLevel switch
							{
								"extreme" => ClrNegative, "high" => ClrExpensive,
								"medium" => ClrAccent, _ => ClrSub
							};
							string dangerIcon = enemy.DangerLevel switch
							{
								"extreme" => "\u2620", "high" => "\u26a0",
								"medium" => "\u25c6", _ => "\u25cb"
							};
							_mapAdvice.Add((dangerIcon, $"{enemy.EnemyName} [{enemy.DangerLevel?.ToUpperInvariant()}]", dangerColor));
							if (enemy.Tips != null)
								foreach (var tip in enemy.Tips)
									_mapAdvice.Add(("\u2022", tip, ClrCream));
						}
					}
				}
				if (_settings.ShowCombatAdvice)
				{
					var combatAdvice = GenerateCombatAdvice(da, hp, maxHP, act, floor);
					if (combatAdvice.Count > 0)
					{
						_mapAdvice.Add(("##", "DECK STRATEGY", ClrAccent));
						_mapAdvice.AddRange(combatAdvice);
					}
				}
				break;

			case "EVENT":
				_mapAdvice = new List<(string, string, Color)>();
				if (_settings.ShowEventAdvice && _currentEventId != null && Plugin.EventAdvisor != null)
				{
					var entry = Plugin.EventAdvisor.GetAdvice(_currentEventId);
					if (entry != null)
					{
						int deckSize = da?.TotalCards ?? 0;
						var choices = Plugin.EventAdvisor.EvaluateChoices(entry, hp, maxHP, gold, deckSize, act);
						if (choices != null && choices.Count > 0)
						{
							_mapAdvice.Add(("##", $"EVENT: {entry.EventName}", ClrAccent));
							foreach (var (label, rating, notes) in choices)
							{
								string icon = rating switch { "good" => "\u2714", "bad" => "\u2716", _ => "\u25c6" };
								Color color = rating switch { "good" => ClrPositive, "bad" => ClrNegative, _ => ClrExpensive };
								string text = string.IsNullOrEmpty(notes) ? label : $"{label} — {notes}";
								_mapAdvice.Add((icon, text, color));
							}
						}
					}
				}
				if (_settings.ShowEventAdvice)
				{
					var eventAdvice = GenerateEventAdvice(da, hp, maxHP, gold, act, floor);
					if (eventAdvice.Count > 0)
					{
						_mapAdvice.Add(("##", "GENERAL TIPS", ClrAccent));
						_mapAdvice.AddRange(eventAdvice);
					}
				}
				break;

			case "MAP":
				_mapAdvice = _settings.ShowMapAdvice
					? GenerateMapAdvice(da, hp, maxHP, gold, act, floor)
					: new List<(string, string, Color)>();
				break;

			case "REST SITE":
				_mapAdvice = GenerateRestSiteAdvice(da, hp, maxHP, act, floor, gs);
				break;

			default:
				Rebuild();
				return;
		}
		Rebuild();
	}

	public void ShowShopAdvice(List<ScoredCard> cards, List<ScoredRelic> relics, DeckAnalysis deckAnalysis = null, string character = null)
	{
		_currentCards = cards;
		_currentRelics = relics;
		_currentDeckAnalysis = deckAnalysis;
		_currentCharacter = character;
		_shopItemCount = (cards?.Count ?? 0) + (relics?.Count ?? 0);
		_shopCardIds = new HashSet<string>(cards?.Select(c => c.Id) ?? Enumerable.Empty<string>());
		_shopRelicIds = new HashSet<string>(relics?.Select(r => r.Id) ?? Enumerable.Empty<string>());
		_currentScreen = "MERCHANT SHOP";
		_mapAdvice = null;
		MarkUpdated();
		Rebuild();
	}

	public void Clear()
	{
		_currentCards = null;
		_currentRelics = null;
		_currentDeckAnalysis = null;
		_mapAdvice = null;
		_currentScreen = "MAP / COMBAT";
		MarkUpdated();
		if (_hoverPreview != null && GodotObject.IsInstanceValid(_hoverPreview))
			_hoverPreview.Visible = false;
		Rebuild();
	}

	private void BuildSettingsMenu()
	{
		_settingsMenu = new PanelContainer();
		_settingsMenu.AnchorLeft = 1f;
		_settingsMenu.AnchorRight = 1f;
		_settingsMenu.AnchorTop = 0f;
		_settingsMenu.AnchorBottom = 0f;
		_settingsMenu.Visible = false;
		_settingsMenu.ZIndex = 102;
		_settingsMenu.MouseFilter = Control.MouseFilterEnum.Stop;

		StyleBoxFlat menuStyle = new StyleBoxFlat();
		menuStyle.BgColor = new Color(0.03f, 0.05f, 0.1f, 0.97f);
		menuStyle.BorderWidthTop = menuStyle.BorderWidthBottom = menuStyle.BorderWidthLeft = menuStyle.BorderWidthRight = 2;
		menuStyle.BorderColor = ClrBorder;
		menuStyle.CornerRadiusTopLeft = menuStyle.CornerRadiusTopRight = menuStyle.CornerRadiusBottomLeft = menuStyle.CornerRadiusBottomRight = 6;
		menuStyle.ContentMarginLeft = menuStyle.ContentMarginRight = 12;
		menuStyle.ContentMarginTop = menuStyle.ContentMarginBottom = 8;
		_settingsMenu.AddThemeStyleboxOverride("panel", menuStyle);

		VBoxContainer menuVBox = new VBoxContainer();
		menuVBox.AddThemeConstantOverride("separation", 4);
		_settingsMenu.AddChild(menuVBox, forceReadableName: false, Node.InternalMode.Disabled);

		// Header row with close button
		HBoxContainer headerRow = new HBoxContainer();
		headerRow.MouseFilter = Control.MouseFilterEnum.Ignore;
		Label header = new Label();
		header.Text = "SETTINGS";
		ApplyFont(header, _fontBold);
		header.AddThemeFontSizeOverride("font_size", 14);
		header.AddThemeColorOverride("font_color", ClrHeader);
		header.MouseFilter = Control.MouseFilterEnum.Ignore;
		header.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		headerRow.AddChild(header, forceReadableName: false, Node.InternalMode.Disabled);

		Label closeBtn = new Label();
		closeBtn.Text = "[X]";
		ApplyFont(closeBtn, _fontBold);
		closeBtn.AddThemeFontSizeOverride("font_size", 14);
		closeBtn.AddThemeColorOverride("font_color", ClrSub);
		closeBtn.MouseFilter = Control.MouseFilterEnum.Stop;
		closeBtn.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		closeBtn.GuiInput += (InputEvent ev2) =>
		{
			if (ev2 is InputEventMouseButton mb2 && mb2.Pressed && mb2.ButtonIndex == MouseButton.Left)
				_settingsMenu.Visible = false;
		};
		headerRow.AddChild(closeBtn, forceReadableName: false, Node.InternalMode.Disabled);
		menuVBox.AddChild(headerRow, forceReadableName: false, Node.InternalMode.Disabled);

		HSeparator sep = new HSeparator();
		sep.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(ClrBorder, 0.4f), Thickness = 1 });
		menuVBox.AddChild(sep, forceReadableName: false, Node.InternalMode.Disabled);

		// Menu items
		AddSettingsToggle(menuVBox, "In-Game Badges", _showInGameBadges, () => { ToggleInGameBadges(); RefreshSettingsMenu(); });
		AddSettingsToggle(menuVBox, "Decision Log", _showHistory, () => { ToggleHistory(); RefreshSettingsMenu(); });
		AddSettingsToggle(menuVBox, "Deck Breakdown", _showDeckBreakdown, () => { _showDeckBreakdown = !_showDeckBreakdown; _settings.ShowDeckBreakdown = _showDeckBreakdown; _settings.Save(); Rebuild(); RefreshSettingsMenu(); });
		AddSettingsToggle(menuVBox, "Enemy Tips", _settings.ShowEnemyTips, () => { _settings.ShowEnemyTips = !_settings.ShowEnemyTips; _settings.Save(); RegenerateAdvice(); RefreshSettingsMenu(); });
		AddSettingsToggle(menuVBox, "Event Advice", _settings.ShowEventAdvice, () => { _settings.ShowEventAdvice = !_settings.ShowEventAdvice; _settings.Save(); RegenerateAdvice(); RefreshSettingsMenu(); });
		AddSettingsToggle(menuVBox, "Map Advice", _settings.ShowMapAdvice, () => { _settings.ShowMapAdvice = !_settings.ShowMapAdvice; _settings.Save(); RegenerateAdvice(); RefreshSettingsMenu(); });
		AddSettingsToggle(menuVBox, "Combat Advice", _settings.ShowCombatAdvice, () => { _settings.ShowCombatAdvice = !_settings.ShowCombatAdvice; _settings.Save(); RegenerateAdvice(); RefreshSettingsMenu(); });
		AddSettingsToggle(menuVBox, "Cloud Sync", _settings.CloudSyncEnabled, () => { _settings.CloudSyncEnabled = !_settings.CloudSyncEnabled; _settings.Save(); RefreshSettingsMenu(); });

		// Opacity section
		HSeparator sep2 = new HSeparator();
		sep2.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(ClrBorder, 0.3f), Thickness = 1 });
		menuVBox.AddChild(sep2, forceReadableName: false, Node.InternalMode.Disabled);

		HBoxContainer opacityRow = new HBoxContainer();
		opacityRow.MouseFilter = Control.MouseFilterEnum.Ignore;
		Label opLabel = new Label();
		opLabel.Text = "Opacity:";
		ApplyFont(opLabel, _fontBody);
		opLabel.AddThemeFontSizeOverride("font_size", 13);
		opLabel.AddThemeColorOverride("font_color", ClrCream);
		opLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		opLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		opacityRow.AddChild(opLabel, forceReadableName: false, Node.InternalMode.Disabled);

		foreach (float step in OpacitySteps)
		{
			Label stepBtn = new Label();
			int pct = (int)(step * 100);
			bool isActive = Math.Abs(_panelOpacity - step) < 0.01f;
			stepBtn.Text = $" {pct}% ";
			ApplyFont(stepBtn, _fontBold);
			stepBtn.AddThemeFontSizeOverride("font_size", 13);
			stepBtn.AddThemeColorOverride("font_color", isActive ? ClrHeader : ClrSub);
			stepBtn.MouseFilter = Control.MouseFilterEnum.Stop;
			stepBtn.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
			float capturedStep = step;
			stepBtn.GuiInput += (InputEvent ev2) =>
			{
				if (ev2 is InputEventMouseButton mb2 && mb2.Pressed && mb2.ButtonIndex == MouseButton.Left)
				{
					_panelOpacity = capturedStep;
					_opacityIndex = Array.IndexOf(OpacitySteps, capturedStep);
					ApplyOpacity(_panelOpacity);
					_settings.PanelOpacity = _panelOpacity;
					_settings.Save();
					RefreshSettingsMenu();
				}
			};
			opacityRow.AddChild(stepBtn, forceReadableName: false, Node.InternalMode.Disabled);
		}
		menuVBox.AddChild(opacityRow, forceReadableName: false, Node.InternalMode.Disabled);

		// Community stats export/import
		HSeparator sepStats = new HSeparator();
		sepStats.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(ClrBorder, 0.3f), Thickness = 1 });
		menuVBox.AddChild(sepStats, forceReadableName: false, Node.InternalMode.Disabled);

		Label exportBtn = new Label();
		exportBtn.Text = "Export Stats";
		ApplyFont(exportBtn, _fontBody);
		exportBtn.AddThemeFontSizeOverride("font_size", 13);
		exportBtn.AddThemeColorOverride("font_color", ClrAqua);
		exportBtn.MouseFilter = Control.MouseFilterEnum.Stop;
		exportBtn.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		exportBtn.GuiInput += (InputEvent ev2) =>
		{
			if (ev2 is InputEventMouseButton mb2 && mb2.Pressed && mb2.ButtonIndex == MouseButton.Left)
			{
				try
				{
					var (path, cards, relics) = StatsExporter.ExportToFile(Plugin.RunDatabase, Plugin.PluginFolder);
					Plugin.Log($"Stats exported: {cards} cards, {relics} relics → {path}");
					exportBtn.Text = $"Exported! ({cards} cards, {relics} relics)";
					exportBtn.AddThemeColorOverride("font_color", ClrPositive);
				}
				catch (Exception ex)
				{
					Plugin.Log($"Export failed: {ex.Message}");
					exportBtn.Text = "Export failed!";
					exportBtn.AddThemeColorOverride("font_color", ClrNegative);
				}
			}
		};
		menuVBox.AddChild(exportBtn, forceReadableName: false, Node.InternalMode.Disabled);

		Label importBtn = new Label();
		importBtn.Text = "Import Stats";
		ApplyFont(importBtn, _fontBody);
		importBtn.AddThemeFontSizeOverride("font_size", 13);
		importBtn.AddThemeColorOverride("font_color", ClrAqua);
		importBtn.MouseFilter = Control.MouseFilterEnum.Stop;
		importBtn.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		importBtn.GuiInput += (InputEvent ev2) =>
		{
			if (ev2 is InputEventMouseButton mb2 && mb2.Pressed && mb2.ButtonIndex == MouseButton.Left)
			{
				try
				{
					string importPath = StatsExporter.FindImportFile(Plugin.PluginFolder);
					if (importPath == null)
					{
						importBtn.Text = "Drop questcespire_stats_export.json in Downloads";
						importBtn.AddThemeColorOverride("font_color", ClrSub);
						return;
					}
					var (cards, relics) = StatsExporter.ImportFromFile(Plugin.RunDatabase, importPath);
					if (cards == -1)
					{
						importBtn.Text = "Import file not readable";
						importBtn.AddThemeColorOverride("font_color", ClrSub);
					}
					else
					{
						string fileName = System.IO.Path.GetFileName(importPath);
						Plugin.Log($"Stats imported from {fileName}: {cards} cards, {relics} relics");
						importBtn.Text = $"Imported {fileName} ({cards}c/{relics}r)";
						importBtn.AddThemeColorOverride("font_color", ClrPositive);
					}
				}
				catch (Exception ex)
				{
					Plugin.Log($"Import failed: {ex.Message}");
					importBtn.Text = "Import failed!";
					importBtn.AddThemeColorOverride("font_color", ClrNegative);
				}
			}
		};
		menuVBox.AddChild(importBtn, forceReadableName: false, Node.InternalMode.Disabled);

		// Hide overlay option
		HSeparator sep3 = new HSeparator();
		sep3.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(ClrBorder, 0.3f), Thickness = 1 });
		menuVBox.AddChild(sep3, forceReadableName: false, Node.InternalMode.Disabled);

		Label hideBtn = new Label();
		hideBtn.Text = "Collapse Overlay";
		ApplyFont(hideBtn, _fontBody);
		hideBtn.AddThemeFontSizeOverride("font_size", 13);
		hideBtn.AddThemeColorOverride("font_color", ClrSub);
		hideBtn.MouseFilter = Control.MouseFilterEnum.Stop;
		hideBtn.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		hideBtn.GuiInput += (InputEvent ev2) =>
		{
			if (ev2 is InputEventMouseButton mb2 && mb2.Pressed && mb2.ButtonIndex == MouseButton.Left)
			{
				_settingsMenu.Visible = false;
				if (!_collapsed) ToggleCollapsed();
			}
		};
		menuVBox.AddChild(hideBtn, forceReadableName: false, Node.InternalMode.Disabled);

		_layer.AddChild(_settingsMenu, forceReadableName: false, Node.InternalMode.Disabled);
	}

	private void AddSettingsToggle(VBoxContainer parent, string label, bool currentValue, Action onToggle)
	{
		HBoxContainer row = new HBoxContainer();
		row.MouseFilter = Control.MouseFilterEnum.Stop;
		row.MouseDefaultCursorShape = Control.CursorShape.PointingHand;

		Label checkmark = new Label();
		checkmark.Text = currentValue ? "\u2611" : "\u2610";
		ApplyFont(checkmark, _fontBody);
		checkmark.AddThemeFontSizeOverride("font_size", 15);
		checkmark.AddThemeColorOverride("font_color", currentValue ? ClrPositive : ClrSub);
		checkmark.MouseFilter = Control.MouseFilterEnum.Ignore;
		row.AddChild(checkmark, forceReadableName: false, Node.InternalMode.Disabled);

		Label text = new Label();
		text.Text = $" {label}";
		ApplyFont(text, _fontBody);
		text.AddThemeFontSizeOverride("font_size", 13);
		text.AddThemeColorOverride("font_color", ClrCream);
		text.MouseFilter = Control.MouseFilterEnum.Ignore;
		text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(text, forceReadableName: false, Node.InternalMode.Disabled);

		row.GuiInput += (InputEvent ev) =>
		{
			if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			{
				onToggle();
			}
		};

		parent.AddChild(row, forceReadableName: false, Node.InternalMode.Disabled);
	}

	private void ToggleSettingsMenu()
	{
		if (_settingsMenu == null || !GodotObject.IsInstanceValid(_settingsMenu))
			return;
		_settingsMenu.Visible = !_settingsMenu.Visible;
		if (_settingsMenu.Visible)
		{
			PositionSettingsMenu();
		}
	}

	private void PositionSettingsMenu()
	{
		if (_settingsMenu == null || _panel == null) return;
		// Position below the gear button, aligned to right edge of panel
		float panelRight = _panel.OffsetRight;
		float panelTop = _panel.OffsetTop;
		_settingsMenu.OffsetLeft = panelRight - 220;
		_settingsMenu.OffsetRight = panelRight;
		_settingsMenu.OffsetTop = panelTop + 40;
		_settingsMenu.OffsetBottom = panelTop + 300;
		_settingsMenu.ResetSize();
	}

	private void RefreshSettingsMenu()
	{
		// Rebuild the menu to reflect new toggle states
		if (_settingsMenu != null && GodotObject.IsInstanceValid(_settingsMenu))
		{
			bool wasVisible = _settingsMenu.Visible;
			_settingsMenu.QueueFree();
			_settingsMenu = null;
			BuildSettingsMenu();
			if (wasVisible)
			{
				_settingsMenu.Visible = true;
				PositionSettingsMenu();
			}
		}
	}

	public void ToggleVisible()
	{
		if (EnsureOverlay())
		{
			_visible = !_visible;
			_panel.Visible = _visible;
			if (!_visible && _settingsMenu != null)
				_settingsMenu.Visible = false;
			Plugin.Log("Overlay " + (_visible ? "shown" : "hidden"));
		}
	}

	public void ToggleInGameBadges()
	{
		_showInGameBadges = !_showInGameBadges;
		_settings.ShowInGameBadges = _showInGameBadges;
		_settings.Save();
		// Always clean up existing badges first
		ClearInGameBadges();
		// Re-inject if turning on and a badge-supporting screen is active
		if (_showInGameBadges && _currentScreen == "CARD REWARD" && _currentCards != null)
		{
			try
			{
				SceneTree tree = Engine.GetMainLoop() as SceneTree;
				Node screenNode = tree?.Root != null ? FindNodeOfType(tree.Root, "NCardRewardSelectionScreen", 4) : null;
				if (screenNode != null)
					InjectCardGrades(screenNode, _currentCards, force: true);
			}
			catch (Exception ex)
			{
				Plugin.Log($"ToggleInGameBadges reinject error: {ex.Message}");
			}
		}
		Rebuild();
		Plugin.Log("In-game badges " + (_showInGameBadges ? "shown" : "hidden"));
	}

	/// <summary>
	/// Called from _Input — handles settings menu close on Escape/click-outside.
	/// </summary>
	public void HandleSettingsClose(InputEvent ev)
	{
		if (_settingsMenu != null && _settingsMenu.Visible)
		{
			if (ev is InputEventKey { Pressed: not false } escKey && escKey.Keycode == Key.Escape)
			{
				_settingsMenu.Visible = false;
				return;
			}
			if (ev is InputEventMouseButton { Pressed: true } click)
			{
				Rect2 menuRect = _settingsMenu.GetGlobalRect();
				if (!menuRect.HasPoint(click.GlobalPosition))
				{
					_settingsMenu.Visible = false;
				}
			}
		}
	}

	/// <summary>
	/// Called from _UnhandledKeyInput — hotkeys that the game didn't consume.
	/// </summary>
	public void HandleInput(InputEvent ev)
	{
		if (ev is InputEventKey { Pressed: not false, Echo: false } inputEventKey)
		{
			Key key = inputEventKey.Keycode;
			Key pkey = inputEventKey.PhysicalKeycode;
			if (key == Key.F7 || pkey == Key.F7)
				ToggleVisible();
			else if (inputEventKey.AltPressed && (key == Key.H || pkey == Key.H))
				ToggleVisible();
			// Backtick/tilde as no-modifier fallback (STS2 doesn't use it)
			else if (!inputEventKey.AltPressed && !inputEventKey.CtrlPressed && !inputEventKey.ShiftPressed
				&& (key == Key.Quoteleft || pkey == Key.Quoteleft))
				ToggleVisible();
		}
	}

	public void ToggleDebugOverlay()
	{
		_showDebug = !_showDebug;
		Plugin.Log($"Debug overlay: {(_showDebug ? "ON" : "OFF")}");
		Rebuild();
	}

	private void Rebuild()
	{
		if (!EnsureOverlay())
		{
			return;
		}
		bool screenChanged = _currentScreen != _previousScreen;
		// Invalidate any pending deferred badge calls on screen change
		if (screenChanged)
			_badgeEpoch++;
		// Clean up in-game badges whenever we're not on a genuine card reward screen
		if (_currentScreen != "CARD REWARD" || !GamePatches.IsGenuineCardReward)
		{
			ClearInGameBadges();
		}
		if (_screenLabel != null && GodotObject.IsInstanceValid(_screenLabel))
		{
			if (_collapsed)
				_screenLabel.Text = GetCollapsedSummary();
			else
			{
				// Show a helpful context label instead of raw screen name
				string screenText = _currentScreen switch
				{
					"MAP" => "MAP — Choose your path",
					"REST SITE" => "REST SITE — Rest or upgrade?",
					"CARD UPGRADE" => "CARD UPGRADE — Pick wisely",
					"CARD REMOVAL" => "CARD REMOVAL — Trim your deck",
					"MERCHANT SHOP" => "SHOP — Browse carefully",
					"EVENT CARD OFFER" => "EVENT — Card offering",
					"COMBAT" => "COMBAT",
					"EVENT" => "EVENT",
					"IDLE" => "WAITING...  (drag to move)",
					_ => _currentScreen
				};
				_screenLabel.Text = screenText;
			}
		}
		// V1: Color-coded title separator
		UpdateTitleSepColor();
		// A1: Win rate
		UpdateWinRate();
		// Hide top-level chip + viz — deck info now always in collapsible DECK BREAKDOWN section
		// Archetype chip removed — deck info in DECK BREAKDOWN section
		ClearDeckViz();
		// Collapsed guard: only update labels, skip full content rebuild
		if (_collapsed)
		{
			ResizePanelToContent();
			_previousScreen = _currentScreen;
			return;
		}
		var children = _content.GetChildren().ToArray();
		foreach (Node child in children)
		{
			if (child != null)
			{
				_content.RemoveChild(child);
				child.QueueFree();
			}
		}
		// Update notification banner
		if (Plugin.LatestVersion != null)
		{
			Label updateLbl = new Label();
			updateLbl.Text = $"\u26a0 Update Available: v{Plugin.LatestVersion}";
			ApplyFont(updateLbl, _fontBold);
			updateLbl.AddThemeFontSizeOverride("font_size", 14);
			updateLbl.AddThemeColorOverride("font_color", ClrExpensive);
			_content.AddChild(updateLbl, forceReadableName: false, Node.InternalMode.Disabled);
		}
		bool hasCards = _currentCards != null && _currentCards.Count > 0;
		bool hasRelics = _currentRelics != null && _currentRelics.Count > 0;
		bool isRemoval = _currentScreen == "CARD REMOVAL";
		bool isUpgrade = _currentScreen == "CARD UPGRADE";
		bool isEventOffer = _currentScreen == "EVENT CARD OFFER";
		if (hasCards)
		{
			bool isShop = _currentScreen == "MERCHANT SHOP";
			AddSectionHeader(isRemoval ? "BEST CARDS TO REMOVE" : isShop ? "BEST CARDS IN SHOP" : isUpgrade ? "BEST UPGRADE TARGETS" : isEventOffer ? "EVENT CARD OFFER" : "CARD ANALYSIS");
			// Shop: show top 3 by score to keep panel compact
			// Upgrade: show top 3 upgrade targets
			// Non-shop: preserve game order so overlay matches on-screen badge positions
			var cardsToShow = (isShop || isUpgrade)
				? _currentCards.OrderByDescending(c => c.FinalScore).Take(3).ToList()
				: _currentCards.ToList();
			foreach (ScoredCard currentCard in cardsToShow)
			{
				AddCardEntry(currentCard);
			}
			int skippedCards = _currentCards.Count - cardsToShow.Count;
			if ((isShop || isUpgrade) && skippedCards > 0)
			{
				Label skipLbl = new Label();
				skipLbl.Text = $"  + {skippedCards} lower-rated cards";
				ApplyFont(skipLbl, _fontBody);
				skipLbl.AddThemeColorOverride("font_color", ClrSub);
				skipLbl.AddThemeFontSizeOverride("font_size", 13);
				_content.AddChild(skipLbl, forceReadableName: false, Node.InternalMode.Disabled);
			}
			// Skip recommendation — tighter thresholds aligned with community consensus
			// Guides: ideal deck 20-25 cards, skip aggressively, every card dilutes draw pool
			if (!isRemoval && !isShop && !isUpgrade && !isEventOffer)
			{
				int deckSize = _currentDeckAnalysis?.TotalCards ?? 20;
				bool hasFocusedDeck = _currentDeckAnalysis?.DetectedArchetypes?.Count > 0 &&
					_currentDeckAnalysis.DetectedArchetypes[0].Strength > 0.4f;
				// Tighter skip thresholds: 20-25 is ideal, past 25 is bloated
				float skipThreshold = deckSize <= 15 ? 2.5f
					: deckSize <= 20 ? 2.2f
					: deckSize <= 25 ? (hasFocusedDeck ? 2.8f : 2.5f)
					: 3.0f; // 25+ cards: only take A-tier or better
				bool allWeak = _currentCards.All(c => c.FinalScore < skipThreshold);
				bool bestIsLow = _currentCards.Count > 0 && _currentCards.Max(c => c.FinalScore) < 2.5f;
				if (allWeak)
				{
					string skipMsg = deckSize <= 18
						? "None of these improve your deck — skip to stay lean."
						: deckSize <= 25
							? (hasFocusedDeck
								? "Nothing fits your archetype — consider skipping."
								: "All offerings are weak — skip to keep deck focused.")
							: $"At {deckSize} cards your deck is bloated — only take S/A-tier cards.";
					AddSkipEntry("\u26A0 CONSIDER SKIPPING", skipMsg);
				}
				else if (bestIsLow && deckSize >= 20)
				{
					AddSkipEntry("\u26A0 DECK IS BLOATED", $"At {deckSize} cards, only take high-impact additions.");
				}
			}
		}
		if (hasRelics)
		{
			bool isShop = _currentScreen == "MERCHANT SHOP";
			AddSectionHeader(isShop ? "BEST RELICS IN SHOP" : "RELIC ANALYSIS");
			var relicsToShow = isShop
				? _currentRelics.OrderByDescending(r => r.FinalScore).Take(3).ToList()
				: _currentRelics.ToList();
			foreach (ScoredRelic currentRelic in relicsToShow)
			{
				AddRelicEntry(currentRelic);
			}
			int skippedRelics = _currentRelics.Count - relicsToShow.Count;
			if (isShop && skippedRelics > 0)
			{
				Label skipRLbl = new Label();
				skipRLbl.Text = $"  + {skippedRelics} lower-rated relics";
				ApplyFont(skipRLbl, _fontBody);
				skipRLbl.AddThemeColorOverride("font_color", ClrSub);
				skipRLbl.AddThemeFontSizeOverride("font_size", 13);
				_content.AddChild(skipRLbl, forceReadableName: false, Node.InternalMode.Disabled);
			}
		}
		if (!hasCards && !hasRelics && _mapAdvice != null && _mapAdvice.Count > 0)
		{
			string adviceHeader = _currentScreen switch
			{
				"COMBAT" => "COMBAT TIPS",
				"EVENT" => "EVENT ADVICE",
				"MAP" => "PATH ADVICE",
				"REST SITE" => "REST SITE",
				_ => "ADVICE"
			};
			AddSectionHeader(adviceHeader);
			foreach (var (icon, text, color) in _mapAdvice)
			{
				// Sub-section header marker
				if (icon == "##")
				{
					HSeparator subSep = new HSeparator();
					subSep.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(ClrBorder, 0.3f), Thickness = 1 });
					_content.AddChild(subSep, forceReadableName: false, Node.InternalMode.Disabled);
					Label subHeader = new Label();
					subHeader.Text = text;
					ApplyFont(subHeader, _fontBold);
					subHeader.AddThemeColorOverride("font_color", color);
					subHeader.AddThemeFontSizeOverride("font_size", 15);
					_content.AddChild(subHeader, forceReadableName: false, Node.InternalMode.Disabled);
					continue;
				}
				PanelContainer advPanel = new PanelContainer();
				StyleBoxFlat advStyle = new StyleBoxFlat();
				advStyle.BgColor = new Color(0.05f, 0.07f, 0.12f, 0.5f);
				advStyle.CornerRadiusTopRight = 8;
				advStyle.CornerRadiusBottomRight = 8;
				advStyle.BorderWidthLeft = 3;
				advStyle.BorderColor = new Color(color, 0.6f);
				advStyle.ContentMarginLeft = 12f;
				advStyle.ContentMarginRight = 10f;
				advStyle.ContentMarginTop = 6f;
				advStyle.ContentMarginBottom = 6f;
				advPanel.AddThemeStyleboxOverride("panel", advStyle);
				Label advLbl = new Label();
				advLbl.Text = $"{icon}  {text}";
				ApplyFont(advLbl, _fontBody);
				advLbl.AddThemeColorOverride("font_color", color);
				advLbl.AddThemeFontSizeOverride("font_size", 17);
				advLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				advPanel.AddChild(advLbl, forceReadableName: false, Node.InternalMode.Disabled);
				_content.AddChild(advPanel, forceReadableName: false, Node.InternalMode.Disabled);
			}
		}
		// (no placeholder when no cards/relics — deck breakdown below is sufficient)
		// YOUR STATS — show on MAP/IDLE screens when enough local data exists
		string statsCharacter = _currentCharacter ?? Plugin.RunTracker?.CurrentCharacter;
		if ((_currentScreen == "MAP" || _currentScreen == "IDLE" || _currentScreen == "MAP / COMBAT") && statsCharacter != null)
		{
			try
			{
				var stats = Plugin.RunDatabase?.GetStatsComparison(statsCharacter);
				if (stats.HasValue && stats.Value.localRuns >= 3)
				{
					var (localWR, localN, commWR, commN) = stats.Value;
					AddSectionHeader("YOUR STATS");
					float delta = localWR - commWR;
					Color deltaColor = delta >= 0 ? ClrPositive : ClrNegative;
					string deltaStr = delta >= 0 ? $"+{delta:F1}%" : $"{delta:F1}%";

					Label statsLbl = new Label();
					statsLbl.Text = $"Win rate: {localWR:F1}% ({localN} runs)";
					ApplyFont(statsLbl, _fontBody);
					statsLbl.AddThemeFontSizeOverride("font_size", 15);
					statsLbl.AddThemeColorOverride("font_color", ClrCream);
					_content.AddChild(statsLbl, forceReadableName: false, Node.InternalMode.Disabled);

					if (commN > 0)
					{
						Label commLbl = new Label();
						commLbl.Text = $"Community: {commWR:F1}%  (delta: {deltaStr})";
						ApplyFont(commLbl, _fontBody);
						commLbl.AddThemeFontSizeOverride("font_size", 14);
						commLbl.AddThemeColorOverride("font_color", deltaColor);
						_content.AddChild(commLbl, forceReadableName: false, Node.InternalMode.Disabled);
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log($"Stats comparison error: {ex.Message}");
			}
		}
		// Deck breakdown — only shown when enabled in settings
		if (_showDeckBreakdown && _currentDeckAnalysis != null)
		{
			AddSectionHeader("DECK BREAKDOWN");
			AddInlineDeckVizTo(_content, _currentDeckAnalysis);
		}
		// Decision history — only shown when explicitly enabled in settings
		if (_showHistory)
		{
			AddDecisionHistory();
		}
		// Debug overlay (F10)
		if (_showDebug)
		{
			AddSectionHeader("DEBUG (F10)");
			// Hook status
			string[] hookNames = { "OnCardRewardOpened", "OnRelicRewardOpened", "OnShopOpened",
				"OnMapScreenEntered", "OnEventShowChoices", "OnCombatSetup", "OnRestSiteOpened",
				"OnRunLaunched", "OnRunEnded" };
			foreach (var hookName in hookNames)
			{
				string timeStr = GamePatches.HookLastFired.TryGetValue(hookName, out var dt)
					? dt.ToString("HH:mm:ss")
					: "never";
				Label hookLbl = new Label();
				hookLbl.Text = $"  {hookName}: {timeStr}";
				ApplyFont(hookLbl, _fontBody);
				hookLbl.AddThemeFontSizeOverride("font_size", 12);
				hookLbl.AddThemeColorOverride("font_color", timeStr == "never" ? ClrSub : ClrCream);
				_content.AddChild(hookLbl, forceReadableName: false, Node.InternalMode.Disabled);
			}
			// State info
			Label stateLbl = new Label();
			int deckSize = _currentDeckAnalysis?.TotalCards ?? 0;
			stateLbl.Text = $"  Screen: {_currentScreen}  Char: {_currentCharacter ?? "?"}  Floor: {_currentFloor}  Deck: {deckSize}  v{Plugin.ModVersion}";
			ApplyFont(stateLbl, _fontBody);
			stateLbl.AddThemeFontSizeOverride("font_size", 12);
			stateLbl.AddThemeColorOverride("font_color", ClrAqua);
			_content.AddChild(stateLbl, forceReadableName: false, Node.InternalMode.Disabled);
		}
		// Settings button at bottom
		{
			_gearButton = new Button();
			_gearButton.Text = "\u2699 Settings";
			_gearButton.Flat = true;
			if (_fontBody != null) _gearButton.AddThemeFontOverride("font", _fontBody);
			_gearButton.AddThemeFontSizeOverride("font_size", 13);
			_gearButton.AddThemeColorOverride("font_color", new Color(ClrSub, 0.7f));
			_gearButton.AddThemeColorOverride("font_hover_color", ClrHeader);
			_gearButton.MouseFilter = Control.MouseFilterEnum.Stop;
			_gearButton.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
			_gearButton.Pressed += () => ToggleSettingsMenu();
			_content.AddChild(_gearButton, forceReadableName: false, Node.InternalMode.Disabled);
		}
		ResizePanelToContent();
		// V2: Fade-in on screen change
		if (screenChanged && _content != null && GodotObject.IsInstanceValid(_content))
		{
			_content.Modulate = new Color(1, 1, 1, 0);
			_content.CreateTween()?.TweenProperty(_content, "modulate", Colors.White, 0.2f);
		}
		_previousScreen = _currentScreen;
	}

	private void ResizePanelToContent()
	{
		if (_panel == null || !GodotObject.IsInstanceValid(_panel))
			return;
		// Defer to next frame so Godot computes layout first
		Callable.From(FitPanelHeight).CallDeferred();
	}

	private void FitPanelHeight()
	{
		if (_panel == null || !GodotObject.IsInstanceValid(_panel))
			return;
		// Reset to let Godot recalculate from children
		_panel.CustomMinimumSize = Vector2.Zero;
		_panel.ResetSize();
		// After ResetSize, the panel collapses to minimum. Read its actual size.
		// Use a second deferred call so Godot has a frame to re-layout.
		Callable.From(FitPanelHeightFinalize).CallDeferred();
	}

	private void FitPanelHeightFinalize()
	{
		if (_panel == null || !GodotObject.IsInstanceValid(_panel))
			return;
		// Use the tighter of actual size vs combined minimum to avoid excess space
		float sizeY = _panel.Size.Y;
		float minY = _panel.GetCombinedMinimumSize().Y;
		float height = (minY > 40f) ? Math.Min(sizeY, minY) : sizeY;
		height = Math.Max(height, 40f);
		var viewport = _panel.GetViewportRect().Size;
		if (viewport.Y > 0) height = Math.Min(height, viewport.Y - _panel.OffsetTop - 10f);
		_panel.OffsetBottom = _panel.OffsetTop + height;
	}

	/// <summary>
	/// Called from OverlayInputHandler._Process for the first few ticks to let layout stabilize.
	/// </summary>
	public void StabilizeLayout()
	{
		if (_layoutTicksRemaining <= 0) return;
		_layoutTicksRemaining--;
		FitPanelHeight();
	}

	// Archetype bar colors — distinct per slot for easy visual parsing
	private static readonly Color[] ArchColors = {
		new Color(0.4f, 0.8f, 0.95f),  // cyan
		new Color(0.95f, 0.6f, 0.3f),  // orange
		new Color(0.7f, 0.5f, 0.95f),  // purple
		new Color(0.3f, 0.9f, 0.5f),   // green
	};

	private void AddSectionHeader(string text)
	{
		// Separator line before header (except first section)
		if (_content.GetChildCount() > 0)
		{
			HSeparator sep = new HSeparator();
			sep.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(ClrBorder, 0.4f), Thickness = 1 });
			_content.AddChild(sep, forceReadableName: false, Node.InternalMode.Disabled);
		}
		Label label = new Label();
		label.Text = text;
		ApplyFont(label, _fontBold);
		label.AddThemeColorOverride("font_color", ClrAccent);
		label.AddThemeFontSizeOverride("font_size", 18);
		label.AddThemeConstantOverride("outline_size", 3);
		label.AddThemeColorOverride("font_outline_color", ClrOutline);
		_content.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
	}

	/// <summary>
	/// Adds a collapsible section: clickable header with toggle arrow, returns content VBox.
	/// sectionKey is used for persisting collapsed state in settings.
	/// Returns null if section is collapsed (caller should skip adding children).
	/// </summary>
	private VBoxContainer AddCollapsibleSection(string text, string sectionKey, ref bool isExpanded)
	{
		// Separator
		if (_content.GetChildCount() > 0)
		{
			HSeparator sep = new HSeparator();
			sep.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(ClrBorder, 0.4f), Thickness = 1 });
			_content.AddChild(sep, forceReadableName: false, Node.InternalMode.Disabled);
		}
		// Header row: clickable toggle
		HBoxContainer headerRow = new HBoxContainer();
		headerRow.MouseFilter = Control.MouseFilterEnum.Stop;
		headerRow.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		Label arrow = new Label();
		arrow.Text = isExpanded ? "\u25BC " : "\u25B6 ";
		ApplyFont(arrow, _fontBold);
		arrow.AddThemeColorOverride("font_color", ClrSub);
		arrow.AddThemeFontSizeOverride("font_size", 14);
		arrow.MouseFilter = Control.MouseFilterEnum.Ignore;
		headerRow.AddChild(arrow, forceReadableName: false, Node.InternalMode.Disabled);
		Label headerLabel = new Label();
		headerLabel.Text = text;
		ApplyFont(headerLabel, _fontBold);
		headerLabel.AddThemeColorOverride("font_color", ClrAccent);
		headerLabel.AddThemeFontSizeOverride("font_size", 16);
		headerLabel.AddThemeConstantOverride("outline_size", 3);
		headerLabel.AddThemeColorOverride("font_outline_color", ClrOutline);
		headerLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		headerRow.AddChild(headerLabel, forceReadableName: false, Node.InternalMode.Disabled);
		_content.AddChild(headerRow, forceReadableName: false, Node.InternalMode.Disabled);
		if (!isExpanded)
		{
			// Capture for click handler
			bool localExpanded = isExpanded;
			string localKey = sectionKey;
			headerRow.GuiInput += (InputEvent ev) =>
			{
				if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
				{
					ToggleSectionSetting(localKey, true);
					Rebuild();
				}
			};
			return null;
		}
		// Section is expanded — add content container
		VBoxContainer sectionContent = new VBoxContainer();
		sectionContent.AddThemeConstantOverride("separation", 4);
		_content.AddChild(sectionContent, forceReadableName: false, Node.InternalMode.Disabled);
		// Click to collapse
		string collapseKey = sectionKey;
		headerRow.GuiInput += (InputEvent ev) =>
		{
			if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			{
				ToggleSectionSetting(collapseKey, false);
				Rebuild();
			}
		};
		return sectionContent;
	}

	private void ToggleSectionSetting(string key, bool value)
	{
		switch (key)
		{
			case "deck": _showDeckBreakdown = value; _settings.ShowDeckBreakdown = value; break;
			case "history": _showHistory = value; _settings.ShowDecisionHistory = value; break;
			// "draw" case removed — draw probability feature removed
		}
		_settings.Save();
	}

	private void AddCardEntry(ScoredCard card)
	{
		PanelContainer panelContainer = CreateEntryPanel(card.IsBestPick, card.FinalGrade);
		HBoxContainer hBoxContainer = new HBoxContainer();
		hBoxContainer.AddThemeConstantOverride("separation", 8);
		panelContainer.AddChild(hBoxContainer, forceReadableName: false, Node.InternalMode.Disabled);
		CenterContainer badge = CreateBadge(card.FinalGrade, card.FinalScore);
		hBoxContainer.AddChild(badge, forceReadableName: false, Node.InternalMode.Disabled);
		// V2: Pulse animation on best pick badge
		if (card.IsBestPick)
		{
			CenterContainer pulseBadge = badge;
			Callable.From(() =>
			{
				if (GodotObject.IsInstanceValid(pulseBadge))
				{
					var tween = pulseBadge.CreateTween();
					if (tween != null)
					{
						tween.SetLoops(2);
						tween.TweenProperty(pulseBadge, "modulate:a", 0.6f, 0.2f);
						tween.TweenProperty(pulseBadge, "modulate:a", 1.0f, 0.2f);
					}
				}
			}).CallDeferred();
		}
		// Card portrait thumbnail (rounded corners with border)
		Texture2D portrait = GetCardPortrait(card.Id, _currentCharacter);
		if (portrait != null)
		{
			PanelContainer thumbClip = new PanelContainer();
			thumbClip.ClipContents = true;
			thumbClip.CustomMinimumSize = new Vector2(36f, 36f);
			StyleBoxFlat thumbStyle = new StyleBoxFlat();
			thumbStyle.BgColor = new Color(0, 0, 0, 0);
			thumbStyle.CornerRadiusTopLeft = 6;
			thumbStyle.CornerRadiusTopRight = 6;
			thumbStyle.CornerRadiusBottomLeft = 6;
			thumbStyle.CornerRadiusBottomRight = 6;
			thumbStyle.BorderWidthTop = 2;
			thumbStyle.BorderWidthBottom = 2;
			thumbStyle.BorderWidthLeft = 2;
			thumbStyle.BorderWidthRight = 2;
			thumbStyle.BorderColor = new Color(ClrBorder, 0.8f);
			thumbClip.AddThemeStyleboxOverride("panel", thumbStyle);
			TextureRect thumb = new TextureRect();
			thumb.Texture = portrait;
			thumb.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			thumb.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
			thumb.CustomMinimumSize = new Vector2(36f, 36f);
			thumbClip.AddChild(thumb, forceReadableName: false, Node.InternalMode.Disabled);
			hBoxContainer.AddChild(thumbClip, forceReadableName: false, Node.InternalMode.Disabled);
		}
		// V4: Card art hover preview
		if (_showTooltips && portrait != null)
		{
			Texture2D hoverTex = portrait;
			panelContainer.Connect("mouse_entered", Callable.From(delegate
			{
				if (_hoverPreview != null && GodotObject.IsInstanceValid(_hoverPreview) &&
				    _hoverPreviewTex != null && _panel != null && GodotObject.IsInstanceValid(_panel))
				{
					_hoverPreviewTex.Texture = hoverTex;
					_hoverPreview.Position = new Vector2(_panel.GlobalPosition.X - 218f,
						panelContainer.GlobalPosition.Y);
					_hoverPreview.Visible = true;
				}
			}));
			panelContainer.Connect("mouse_exited", Callable.From(delegate
			{
				if (_hoverPreview != null && GodotObject.IsInstanceValid(_hoverPreview))
					_hoverPreview.Visible = false;
			}));
		}
		VBoxContainer vBoxContainer = new VBoxContainer();
		vBoxContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		vBoxContainer.AddThemeConstantOverride("separation", 0);
		hBoxContainer.AddChild(vBoxContainer, forceReadableName: false, Node.InternalMode.Disabled);
		// Card name + sub-grade inline
		Label label = new Label();
		string text = PrettifyId(card.Id);
		string upgradeTag = card.Upgraded ? " +" : "";
		label.Text = $"{text}{upgradeTag}";
		ApplyFont(label, _fontBold);
		label.AddThemeColorOverride("font_color", card.IsBestPick ? ClrAccent : ClrCream);
		label.AddThemeFontSizeOverride("font_size", 17);
		label.AddThemeConstantOverride("outline_size", 2);
		label.AddThemeColorOverride("font_outline_color", ClrOutline);
		label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		vBoxContainer.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
		// Line 2: type • cost • one-line reason
		string typeLower = card.Type?.ToLowerInvariant() ?? "";
		string costStr = card.Cost == 0 ? "0 cost" : card.Cost == 1 ? "1 energy" : $"{card.Cost} energy";
		string priceStr = "";
		if (card.Price > 0)
		{
			priceStr = _goldIcon != null ? "" : $" \u2022 {card.Price}g";
		}
		string oneLiner = BuildOneLiner(card.SynergyReasons, card.AntiSynergyReasons, card.BaseTier, card.FinalGrade);
		string metaText = $"{typeLower} \u2022 {costStr}{priceStr}";
		if (oneLiner.Length > 0)
			metaText += $" \u2022 {oneLiner}";
		Label metaLbl = new Label();
		metaLbl.Text = metaText;
		ApplyFont(metaLbl, _fontBody);
		bool hasAnti = card.AntiSynergyReasons != null && card.AntiSynergyReasons.Count > 0;
		metaLbl.AddThemeColorOverride("font_color", hasAnti ? ClrNegative : card.Cost >= 3 ? ClrExpensive : ClrSub);
		metaLbl.AddThemeFontSizeOverride("font_size", 14);
		metaLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		vBoxContainer.AddChild(metaLbl, forceReadableName: false, Node.InternalMode.Disabled);
		// Shop price with gold icon (inline after meta)
		if (card.Price > 0 && _goldIcon != null)
		{
			HBoxContainer priceRow = new HBoxContainer();
			priceRow.AddThemeConstantOverride("separation", 2);
			TextureRect goldTex = new TextureRect();
			goldTex.Texture = _goldIcon;
			goldTex.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			goldTex.StretchMode = TextureRect.StretchModeEnum.KeepAspect;
			goldTex.CustomMinimumSize = new Vector2(12f, 12f);
			priceRow.AddChild(goldTex, forceReadableName: false, Node.InternalMode.Disabled);
			Label priceLbl = new Label();
			priceLbl.Text = $"{card.Price}";
			ApplyFont(priceLbl, _fontBody);
			priceLbl.AddThemeColorOverride("font_color", ClrAccent);
			priceLbl.AddThemeFontSizeOverride("font_size", 17);
			priceRow.AddChild(priceLbl, forceReadableName: false, Node.InternalMode.Disabled);
			vBoxContainer.AddChild(priceRow, forceReadableName: false, Node.InternalMode.Disabled);
		}
		// Expandable details on hover (hidden by default)
		if (_showTooltips)
		{
			VBoxContainer detailBox = new VBoxContainer();
			detailBox.Visible = false;
			detailBox.AddThemeConstantOverride("separation", 1);
			AddTooltipLines(detailBox, card.SynergyReasons, card.AntiSynergyReasons, card.Notes, card.BaseScore, card.SynergyDelta, card.FloorAdjust, card.DeckSizeAdjust, card.UpgradeAdjust, card.FinalScore, card.ScoreSource);
			if (detailBox.GetChildCount() > 0)
			{
				vBoxContainer.AddChild(detailBox, forceReadableName: false, Node.InternalMode.Disabled);
				panelContainer.Connect("mouse_entered", Callable.From(delegate
				{
					if (GodotObject.IsInstanceValid(detailBox)) detailBox.Visible = true;
				}));
				panelContainer.Connect("mouse_exited", Callable.From(delegate
				{
					if (GodotObject.IsInstanceValid(detailBox)) detailBox.Visible = false;
				}));
			}
		}
		_content.AddChild(panelContainer, forceReadableName: false, Node.InternalMode.Disabled);
	}

	private void AddRelicEntry(ScoredRelic relic)
	{
		PanelContainer panelContainer = CreateEntryPanel(relic.IsBestPick, relic.FinalGrade);
		HBoxContainer hBoxContainer = new HBoxContainer();
		hBoxContainer.AddThemeConstantOverride("separation", 8);
		panelContainer.AddChild(hBoxContainer, forceReadableName: false, Node.InternalMode.Disabled);
		CenterContainer relicBadge = CreateBadge(relic.FinalGrade, relic.FinalScore);
		hBoxContainer.AddChild(relicBadge, forceReadableName: false, Node.InternalMode.Disabled);
		// V2: Pulse animation on best pick badge
		if (relic.IsBestPick)
		{
			CenterContainer pulseBadge = relicBadge;
			Callable.From(() =>
			{
				if (GodotObject.IsInstanceValid(pulseBadge))
				{
					var tween = pulseBadge.CreateTween();
					if (tween != null)
					{
						tween.SetLoops(2);
						tween.TweenProperty(pulseBadge, "modulate:a", 0.6f, 0.2f);
						tween.TweenProperty(pulseBadge, "modulate:a", 1.0f, 0.2f);
					}
				}
			}).CallDeferred();
		}
		// Relic icon thumbnail (rounded corners with border)
		Texture2D relicIcon = GetRelicIcon(relic.Id);
		if (relicIcon != null)
		{
			PanelContainer relicClip = new PanelContainer();
			relicClip.ClipContents = true;
			relicClip.CustomMinimumSize = new Vector2(36f, 36f);
			StyleBoxFlat relicThumbStyle = new StyleBoxFlat();
			relicThumbStyle.BgColor = new Color(0, 0, 0, 0);
			relicThumbStyle.CornerRadiusTopLeft = 6;
			relicThumbStyle.CornerRadiusTopRight = 6;
			relicThumbStyle.CornerRadiusBottomLeft = 6;
			relicThumbStyle.CornerRadiusBottomRight = 6;
			relicThumbStyle.BorderWidthTop = 2;
			relicThumbStyle.BorderWidthBottom = 2;
			relicThumbStyle.BorderWidthLeft = 2;
			relicThumbStyle.BorderWidthRight = 2;
			relicThumbStyle.BorderColor = new Color(ClrBorder, 0.8f);
			relicClip.AddThemeStyleboxOverride("panel", relicThumbStyle);
			TextureRect thumb = new TextureRect();
			thumb.Texture = relicIcon;
			thumb.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			thumb.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
			thumb.CustomMinimumSize = new Vector2(36f, 36f);
			relicClip.AddChild(thumb, forceReadableName: false, Node.InternalMode.Disabled);
			hBoxContainer.AddChild(relicClip, forceReadableName: false, Node.InternalMode.Disabled);
		}
		VBoxContainer vBoxContainer = new VBoxContainer();
		vBoxContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		vBoxContainer.AddThemeConstantOverride("separation", 0);
		hBoxContainer.AddChild(vBoxContainer, forceReadableName: false, Node.InternalMode.Disabled);
		// Relic name + sub-grade inline
		Label label = new Label();
		string text = PrettifyId(relic.Id);
		label.Text = text;
		ApplyFont(label, _fontBold);
		label.AddThemeColorOverride("font_color", relic.IsBestPick ? ClrAccent : ClrCream);
		label.AddThemeFontSizeOverride("font_size", 17);
		label.AddThemeConstantOverride("outline_size", 2);
		label.AddThemeColorOverride("font_outline_color", ClrOutline);
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		vBoxContainer.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
		// Meta: rarity • tenure • one-liner
		string rarityLower = relic.Rarity?.ToLowerInvariant() ?? "";
		string oneLiner = BuildOneLiner(relic.SynergyReasons, relic.AntiSynergyReasons, relic.BaseTier, relic.FinalGrade);
		string tenureStr = "";
		if (Plugin.RunTracker != null && _currentFloor > 0)
		{
			int tenure = Plugin.RunTracker.GetRelicTenure(relic.Id, _currentFloor);
			if (tenure > 0) tenureStr = $" \u2022 held {tenure} floors";
		}
		string metaText = rarityLower + tenureStr;
		if (oneLiner.Length > 0)
			metaText += $" \u2022 {oneLiner}";
		Label metaLbl = new Label();
		metaLbl.Text = metaText;
		ApplyFont(metaLbl, _fontBody);
		bool hasAnti = relic.AntiSynergyReasons != null && relic.AntiSynergyReasons.Count > 0;
		metaLbl.AddThemeColorOverride("font_color", hasAnti ? ClrNegative : ClrSub);
		metaLbl.AddThemeFontSizeOverride("font_size", 14);
		metaLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		vBoxContainer.AddChild(metaLbl, forceReadableName: false, Node.InternalMode.Disabled);
		// Archetype synergy tags
		var archTags = ExtractArchetypeTags(relic.SynergyReasons);
		if (archTags.Count > 0)
		{
			HBoxContainer tagRow = new HBoxContainer();
			tagRow.AddThemeConstantOverride("separation", 4);
			foreach (string tag in archTags)
			{
				Label tagLbl = new Label();
				tagLbl.Text = $"[{tag}]";
				ApplyFont(tagLbl, _fontBody);
				tagLbl.AddThemeFontSizeOverride("font_size", 17);
				tagLbl.AddThemeColorOverride("font_color", ClrAccent);
				tagRow.AddChild(tagLbl, forceReadableName: false, Node.InternalMode.Disabled);
			}
			vBoxContainer.AddChild(tagRow, forceReadableName: false, Node.InternalMode.Disabled);
		}
		// Price with gold icon
		if (relic.Price > 0)
		{
			if (_goldIcon != null)
			{
				HBoxContainer priceRow = new HBoxContainer();
				priceRow.AddThemeConstantOverride("separation", 2);
				TextureRect goldTex = new TextureRect();
				goldTex.Texture = _goldIcon;
				goldTex.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				goldTex.StretchMode = TextureRect.StretchModeEnum.KeepAspect;
				goldTex.CustomMinimumSize = new Vector2(12f, 12f);
				priceRow.AddChild(goldTex, forceReadableName: false, Node.InternalMode.Disabled);
				Label priceLbl = new Label();
				priceLbl.Text = $"{relic.Price}";
				ApplyFont(priceLbl, _fontBody);
				priceLbl.AddThemeColorOverride("font_color", ClrAccent);
				priceLbl.AddThemeFontSizeOverride("font_size", 17);
				priceRow.AddChild(priceLbl, forceReadableName: false, Node.InternalMode.Disabled);
				vBoxContainer.AddChild(priceRow, forceReadableName: false, Node.InternalMode.Disabled);
			}
			else
			{
				Label pLbl = new Label();
				pLbl.Text = $"{relic.Price}g";
				ApplyFont(pLbl, _fontBody);
				pLbl.AddThemeColorOverride("font_color", ClrAccent);
				pLbl.AddThemeFontSizeOverride("font_size", 17);
				vBoxContainer.AddChild(pLbl, forceReadableName: false, Node.InternalMode.Disabled);
			}
		}
		// Expandable details on hover
		if (_showTooltips)
		{
			VBoxContainer detailBox = new VBoxContainer();
			detailBox.Visible = false;
			detailBox.AddThemeConstantOverride("separation", 1);
			AddTooltipLines(detailBox, relic.SynergyReasons, relic.AntiSynergyReasons, relic.Notes, relic.BaseScore, relic.SynergyDelta, relic.FloorAdjust, relic.DeckSizeAdjust, 0f, relic.FinalScore, relic.ScoreSource);
			if (detailBox.GetChildCount() > 0)
			{
				vBoxContainer.AddChild(detailBox, forceReadableName: false, Node.InternalMode.Disabled);
				panelContainer.Connect("mouse_entered", Callable.From(delegate
				{
					if (GodotObject.IsInstanceValid(detailBox)) detailBox.Visible = true;
				}));
				panelContainer.Connect("mouse_exited", Callable.From(delegate
				{
					if (GodotObject.IsInstanceValid(detailBox)) detailBox.Visible = false;
				}));
			}
		}
		_content.AddChild(panelContainer, forceReadableName: false, Node.InternalMode.Disabled);
	}

	private static List<string> ExtractArchetypeTags(List<string> synergyReasons)
	{
		var tags = new List<string>();
		if (synergyReasons == null) return tags;
		foreach (string reason in synergyReasons)
		{
			int idx = reason.IndexOf("synergy with ");
			if (idx >= 0)
			{
				string archName = reason.Substring(idx + 13).Trim();
				if (archName.Length > 0 && !tags.Contains(archName))
					tags.Add(archName);
			}
		}
		return tags;
	}

	private static string BuildOneLiner(List<string> synergies, List<string> antiSynergies, TierGrade baseTier, TierGrade finalGrade)
	{
		// Priority: show most impactful single reason in plain English
		if (antiSynergies != null && antiSynergies.Count > 0)
		{
			string anti = antiSynergies[0];
			if (anti.IndexOf("conflict") >= 0) return "conflicts with deck";
			if (anti.IndexOf("expensive") >= 0) return "too costly right now";
			if (anti.IndexOf("redundant") >= 0) return "redundant pick";
			return "doesn't fit your deck";
		}
		if (synergies != null && synergies.Count > 0)
		{
			string syn = synergies[0];
			if (syn.IndexOf("synergy with ") >= 0)
			{
				// Extract archetype name for specificity
				string arch = syn.Substring(syn.IndexOf("synergy with ") + 13).Trim();
				if (arch.Length > 0 && arch.Length <= 20) return $"synergizes with {arch}";
				return "fits your deck well";
			}
			if (syn.IndexOf("fills gap: ") >= 0) return "adds " + syn.Substring(syn.IndexOf("fills gap: ") + 11).Trim();
			if (syn.IndexOf("scaling") >= 0) return "scales well late";
			if (syn.IndexOf("flexible") >= 0) return "versatile pick";
			if (syn.IndexOf("defense") >= 0) return "shores up defense";
			if (syn.IndexOf("upgraded") >= 0) return "upgraded";
			if (syn.IndexOf("aoe") >= 0 || syn.IndexOf("AoE") >= 0) return "good AoE";
			if (syn.IndexOf("draw") >= 0) return "adds card draw";
			if (syn.IndexOf("energy") >= 0) return "energy efficient";
			return syn.Length > 25 ? syn.Substring(0, 25) + "..." : syn;
		}
		if (finalGrade != baseTier)
		{
			return finalGrade > baseTier ? "strong right now" : "weaker right now";
		}
		return "";
	}

	private void AddTooltipLines(VBoxContainer parent, List<string> synergies, List<string> antiSynergies, string notes, float baseScore = 0f, float synergyDelta = 0f, float floorAdjust = 0f, float deckSizeAdjust = 0f, float upgradeAdjust = 0f, float finalScore = 0f, string scoreSource = "static")
	{
		bool hasContent = false;

		// Card description / notes first — what does this card do?
		if (!string.IsNullOrEmpty(notes) && !IsFillerNote(notes))
		{
			Label lbl = new Label();
			lbl.Text = notes;
			ApplyFont(lbl, _fontBody);
			lbl.AddThemeColorOverride("font_color", ClrNotes);
			lbl.AddThemeFontSizeOverride("font_size", 14);
			lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			parent.AddChild(lbl, forceReadableName: false, Node.InternalMode.Disabled);
			hasContent = true;
		}

		// Synergy / anti-synergy reasons — why is it rated this way?
		if (synergies != null && synergies.Count > 0)
		{
			foreach (string reason in synergies.Take(3))
			{
				string clean = CleanSynergyText(reason);
				if (string.IsNullOrEmpty(clean)) continue;
				Label lbl = new Label();
				lbl.Text = "\u2714 " + clean;
				ApplyFont(lbl, _fontBody);
				lbl.AddThemeColorOverride("font_color", ClrPositive);
				lbl.AddThemeFontSizeOverride("font_size", 14);
				lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				parent.AddChild(lbl, forceReadableName: false, Node.InternalMode.Disabled);
				hasContent = true;
			}
		}

		if (antiSynergies != null && antiSynergies.Count > 0)
		{
			foreach (string reason in antiSynergies.Take(2))
			{
				string clean = CleanSynergyText(reason);
				if (string.IsNullOrEmpty(clean)) continue;
				Label lbl = new Label();
				lbl.Text = "\u2718 " + clean;
				ApplyFont(lbl, _fontBody);
				lbl.AddThemeColorOverride("font_color", ClrNegative);
				lbl.AddThemeFontSizeOverride("font_size", 14);
				lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				parent.AddChild(lbl, forceReadableName: false, Node.InternalMode.Disabled);
				hasContent = true;
			}
		}

		// Score source hint — only show if learned from play data
		if (scoreSource == "adaptive")
		{
			Label srcLbl = new Label();
			srcLbl.Text = "\u2139 rated from your play data";
			ApplyFont(srcLbl, _fontBody);
			srcLbl.AddThemeColorOverride("font_color", ClrAqua);
			srcLbl.AddThemeFontSizeOverride("font_size", 12);
			parent.AddChild(srcLbl, forceReadableName: false, Node.InternalMode.Disabled);
		}
	}

	/// <summary>
	/// Strip numeric scoring jargon from synergy/anti-synergy reason strings.
	/// "+0.8 synergy with Minion / Summoner" → "synergy with Minion / Summoner"
	/// </summary>
	private static string CleanSynergyText(string text)
	{
		if (string.IsNullOrEmpty(text)) return text;
		// Strip leading "+0.8 " or "-0.4 " numeric prefixes
		string cleaned = text;
		if (cleaned.Length > 2 && (cleaned[0] == '+' || cleaned[0] == '-') && char.IsDigit(cleaned[1]))
		{
			int spaceIdx = cleaned.IndexOf(' ');
			if (spaceIdx > 0 && spaceIdx < 8)
				cleaned = cleaned.Substring(spaceIdx + 1);
		}
		// Capitalize first letter
		if (cleaned.Length > 0)
			cleaned = char.ToUpper(cleaned[0]) + cleaned.Substring(1);
		return cleaned;
	}

	/// <summary>
	/// Filter out notes that just describe card type/rarity/cost (already shown in meta line).
	/// </summary>
	private static bool IsFillerNote(string notes)
	{
		string lower = notes.ToLowerInvariant();
		// Skip notes like "1-cost common strike variant", "Filler attack with Strike tag"
		if (lower.Contains("filler")) return true;
		if (lower.Contains("-cost") && lower.Contains("variant")) return true;
		if (lower.Contains("basic") && lower.Contains("strike")) return true;
		if (lower.Contains("basic") && lower.Contains("defend")) return true;
		if (lower.Contains("starter card")) return true;
		return false;
	}

	// === Feature 3: Opacity control ===

	public void CycleOpacity()
	{
		_opacityIndex = (_opacityIndex + 1) % OpacitySteps.Length;
		_panelOpacity = OpacitySteps[_opacityIndex];
		ApplyOpacity(_panelOpacity);
		_settings.PanelOpacity = _panelOpacity;
		_settings.Save();
		Plugin.Log($"Opacity set to {(int)(_panelOpacity * 100)}%");
	}

	private void ApplyOpacity(float opacity)
	{
		if (_sbPanel != null) _sbPanel.BgColor = new Color(ClrBg.R, ClrBg.G, ClrBg.B, 0.97f * opacity);
		if (_sbEntry != null) _sbEntry.BgColor = new Color(0.06f, 0.08f, 0.14f, 0.6f * opacity);
		if (_sbBest != null) _sbBest.BgColor = new Color(0.831f, 0.714f, 0.357f, 0.1f * opacity);
		if (_sbHover != null) _sbHover.BgColor = new Color(ClrHover.R, ClrHover.G, ClrHover.B, 0.8f * opacity);
		if (_sbHoverBest != null) _sbHoverBest.BgColor = new Color(0.831f, 0.714f, 0.357f, 0.15f * opacity);
		if (_sbSTier != null) _sbSTier.BgColor = new Color(0.831f, 0.714f, 0.357f, 0.1f * opacity);
		if (_sbSTierHover != null) _sbSTierHover.BgColor = new Color(0.831f, 0.714f, 0.357f, 0.18f * opacity);
		if (_sbChip != null) _sbChip.BgColor = new Color(0.02f, 0.03f, 0.07f, 0.7f * opacity);
	}

	// === Feature 4: Collapsible mode ===

	public void ToggleCollapsed()
	{
		_collapsed = !_collapsed;
		_settings.Collapsed = _collapsed;
		_settings.Save();
		if (_content != null && GodotObject.IsInstanceValid(_content))
			_content.Visible = !_collapsed;
		// _deckVizContainer stays hidden — deck info in DECK BREAKDOWN
		if (_titleSep != null && GodotObject.IsInstanceValid(_titleSep))
			_titleSep.Visible = !_collapsed;
		if (_screenLabel != null && GodotObject.IsInstanceValid(_screenLabel))
			_screenLabel.Visible = true; // Always visible — shows collapsed summary when collapsed
		if (_winRateLabel != null && GodotObject.IsInstanceValid(_winRateLabel))
			_winRateLabel.Visible = !_collapsed;
		// Update compact toggle arrow
		if (_compactToggle != null && GodotObject.IsInstanceValid(_compactToggle))
			_compactToggle.Text = _collapsed ? "\u25BC" : "\u25B2";
		// When expanding, rebuild content since Rebuild() skips content when collapsed
		if (!_collapsed)
			Rebuild();
		else
		{
			// Update the screen label to show collapsed summary
			if (_screenLabel != null && GodotObject.IsInstanceValid(_screenLabel))
				_screenLabel.Text = GetCollapsedSummary();
			ResizePanelToContent();
		}
		Plugin.Log("Overlay " + (_collapsed ? "collapsed" : "expanded"));
	}

	private string GetCollapsedSummary()
	{
		string gradeStr = "";
		if (_currentCards != null && _currentCards.Count > 0)
		{
			var best = _currentCards.FirstOrDefault(c => c.IsBestPick) ?? _currentCards[0];
			gradeStr = TierEngine.ScoreToSubGrade(best.FinalScore);
		}
		else if (_currentRelics != null && _currentRelics.Count > 0)
		{
			var best = _currentRelics.FirstOrDefault(r => r.IsBestPick) ?? _currentRelics[0];
			gradeStr = TierEngine.ScoreToSubGrade(best.FinalScore);
		}
		string screen = _currentScreen ?? "IDLE";
		return gradeStr.Length > 0 ? $"{gradeStr}  {screen}" : screen;
	}

	// === Feature 1: Decision history ===

	public void ToggleHistory()
	{
		_showHistory = !_showHistory;
		_settings.ShowDecisionHistory = _showHistory;
		_settings.Save();
		Rebuild();
		Plugin.Log("Decision history " + (_showHistory ? "shown" : "hidden"));
	}

	private TierGrade LookupGrade(string id, string character)
	{
		var cardTier = Plugin.TierEngine?.GetCardTier(character, id);
		if (cardTier != null) return TierEngine.ParseGrade(cardTier.BaseTier);
		var relicTier = Plugin.TierEngine?.GetRelicTier(character, id);
		if (relicTier != null) return TierEngine.ParseGrade(relicTier.BaseTier);
		return TierGrade.C;
	}

	/// <summary>Convert UPPER_SNAKE_CASE game IDs to readable Title Case names.</summary>
	private static string PrettifyId(string id)
	{
		if (string.IsNullOrEmpty(id)) return id;
		// Try to look up the real name from tier data (it stores Title Case)
		var cardTier = Plugin.TierEngine?.GetCardTier(null, id);
		if (cardTier != null && !string.IsNullOrEmpty(cardTier.Id)) return cardTier.Id;
		var relicTier = Plugin.TierEngine?.GetRelicTier(null, id);
		if (relicTier != null && !string.IsNullOrEmpty(relicTier.Id)) return relicTier.Id;
		// Fallback: UPPER_SNAKE_CASE → Title Case
		return string.Join(" ", id.Split('_').Select(w =>
			w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1).ToLower() : w));
	}

	private void AddDecisionHistory()
	{
		var events = Plugin.RunTracker?.GetCurrentRunEvents();
		if (events == null || events.Count == 0) return;
		AddSectionHeader("DECISION LOG");

		// Wrap in a scroll container so long logs don't stretch the panel
		ScrollContainer scroll = new ScrollContainer();
		scroll.CustomMinimumSize = new Vector2(0, 0);
		// Cap height so it doesn't push the panel too tall
		scroll.SizeFlagsVertical = Control.SizeFlags.Fill;
		float maxScrollHeight = 250f;
		var viewport = _panel?.GetViewportRect().Size ?? new Vector2(0, 800);
		if (viewport.Y > 0) maxScrollHeight = Math.Min(maxScrollHeight, viewport.Y * 0.3f);
		scroll.CustomMinimumSize = new Vector2(0, Math.Min(events.Count * 50f, maxScrollHeight));
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.MouseFilter = Control.MouseFilterEnum.Stop;

		VBoxContainer scrollContent = new VBoxContainer();
		scrollContent.AddThemeConstantOverride("separation", 6);
		scrollContent.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scroll.AddChild(scrollContent, forceReadableName: false, Node.InternalMode.Disabled);

		int count = Math.Min(events.Count, 15);
		for (int i = events.Count - 1; i >= events.Count - count; i--)
		{
			AddDecisionEntry(scrollContent, events[i]);
		}
		_content.AddChild(scroll, forceReadableName: false, Node.InternalMode.Disabled);
	}

	private void AddRecentDecisions(int maxCount)
	{
		var events = Plugin.RunTracker?.GetCurrentRunEvents();
		if (events == null || events.Count == 0) return;
		AddSectionHeader("RECENT CHOICES");
		AddRecentDecisionsTo(_content, maxCount);
	}

	private void AddRecentDecisionsTo(VBoxContainer target, int maxCount)
	{
		var events = Plugin.RunTracker?.GetCurrentRunEvents();
		if (events == null || events.Count == 0) return;
		int count = Math.Min(events.Count, maxCount);
		for (int i = events.Count - 1; i >= events.Count - count; i--)
		{
			AddDecisionEntry(target, events[i]);
		}
	}

	private void AddDecisionEntry(DecisionEvent evt)
	{
		AddDecisionEntry(_content, evt);
	}

	private void AddDecisionEntry(VBoxContainer target, DecisionEvent evt)
	{
		string character = _currentCharacter ?? _currentDeckAnalysis?.Character ?? "unknown";
		string typeIcon = evt.EventType switch
		{
			DecisionEventType.CardReward => "\u2694",
			DecisionEventType.RelicReward => "\u2B50",
			DecisionEventType.BossRelic => "\u2B50",
			DecisionEventType.Shop => "\uD83D\uDCB0",
			DecisionEventType.CardRemove => "\u2702",
			_ => "\u2022"
		};
		Color borderColor = evt.EventType switch
		{
			DecisionEventType.CardReward => ClrPositive,
			DecisionEventType.RelicReward => ClrAccent,
			DecisionEventType.BossRelic => ClrAccent,
			DecisionEventType.Shop => ClrExpensive,
			DecisionEventType.CardRemove => ClrAqua,
			_ => ClrSub
		};

		PanelContainer entryPanel = new PanelContainer();
		StyleBoxFlat entryStyle = new StyleBoxFlat();
		entryStyle.BgColor = new Color(0.04f, 0.06f, 0.1f, 0.5f);
		entryStyle.CornerRadiusTopRight = 6;
		entryStyle.CornerRadiusBottomRight = 6;
		entryStyle.BorderWidthLeft = 3;
		entryStyle.BorderColor = borderColor;
		entryStyle.ContentMarginLeft = 10f;
		entryStyle.ContentMarginRight = 8f;
		entryStyle.ContentMarginTop = 4f;
		entryStyle.ContentMarginBottom = 4f;
		entryPanel.AddThemeStyleboxOverride("panel", entryStyle);

		VBoxContainer vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 1);
		entryPanel.AddChild(vbox, forceReadableName: false, Node.InternalMode.Disabled);

		// Main line: chosen card/relic with grade
		string chosenName = evt.ChosenId != null ? PrettifyId(evt.ChosenId) : "Skipped";
		TierGrade chosenGrade = evt.ChosenId != null ? LookupGrade(evt.ChosenId, character) : TierGrade.F;
		string gradeStr = evt.ChosenId != null ? $" [{chosenGrade}]" : "";
		Label mainLine = new Label();
		mainLine.Text = $"{typeIcon} F{evt.Floor}: {chosenName}{gradeStr}";
		ApplyFont(mainLine, _fontBold);
		mainLine.AddThemeColorOverride("font_color", evt.ChosenId != null ? ClrAccent : ClrSub);
		mainLine.AddThemeFontSizeOverride("font_size", 14);
		mainLine.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		vbox.AddChild(mainLine, forceReadableName: false, Node.InternalMode.Disabled);

		// Alternatives line
		var alternatives = evt.OfferedIds.Where(id => id != evt.ChosenId).Take(3).ToList();
		if (alternatives.Count > 0)
		{
			var altParts = alternatives.Select(id =>
			{
				TierGrade g = LookupGrade(id, character);
				return $"{PrettifyId(id)} [{g}]";
			});
			Label altLine = new Label();
			altLine.Text = $"  over {string.Join(", ", altParts)}";
			ApplyFont(altLine, _fontBody);
			altLine.AddThemeColorOverride("font_color", ClrSub);
			altLine.AddThemeFontSizeOverride("font_size", 17);
			altLine.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
			vbox.AddChild(altLine, forceReadableName: false, Node.InternalMode.Disabled);
		}

		target.AddChild(entryPanel, forceReadableName: false, Node.InternalMode.Disabled);
	}

	// === Feature 2: Deck composition visualization ===

	private void ClearDeckViz()
	{
		if (_deckVizContainer == null || !GodotObject.IsInstanceValid(_deckVizContainer))
			return;
		var vizChildren = _deckVizContainer.GetChildren().ToArray();
		foreach (Node child in vizChildren)
		{
			_deckVizContainer.RemoveChild(child);
			child.QueueFree();
		}
		_deckVizContainer.Visible = false;
		_deckVizContainer.CustomMinimumSize = Vector2.Zero;
	}

	private void AddInlineDeckViz(DeckAnalysis analysis)
	{
		if (analysis == null || analysis.TotalCards == 0)
			return;
		AddSectionHeader("DECK BREAKDOWN");
		AddInlineDeckVizTo(_content, analysis);
	}

	private void AddInlineDeckVizTo(VBoxContainer target, DeckAnalysis analysis)
	{
		if (analysis == null || analysis.TotalCards == 0)
			return;
		// Inline archetype info — replicate colored breakdown
		Label deckHeader = new Label();
		deckHeader.Text = $"YOUR DECK ({analysis.TotalCards} cards)";
		ApplyFont(deckHeader, _fontBold);
		deckHeader.AddThemeFontSizeOverride("font_size", 15);
		deckHeader.AddThemeColorOverride("font_color", ClrCream);
		target.AddChild(deckHeader, forceReadableName: false, Node.InternalMode.Disabled);
		if (analysis.DetectedArchetypes != null && analysis.DetectedArchetypes.Count > 0)
		{
			float totalStr = analysis.DetectedArchetypes.Sum(a => a.Strength);
			if (totalStr <= 0) totalStr = 1f;
			int cIdx = 0;
			foreach (var arch in analysis.DetectedArchetypes)
			{
				int pct = (int)(arch.Strength / totalStr * 100f);
				Color ac = ArchColors[cIdx % ArchColors.Length];
				HBoxContainer aRow = new HBoxContainer();
				aRow.AddThemeConstantOverride("separation", 6);
				ColorRect aBar = new ColorRect();
				aBar.Color = new Color(ac, 0.7f);
				aBar.CustomMinimumSize = new Vector2(Math.Max(pct * 0.6f, 3f), 10f);
				aRow.AddChild(aBar, forceReadableName: false, Node.InternalMode.Disabled);
				Label aLbl = new Label();
				aLbl.Text = $"{arch.Archetype.DisplayName}  {pct}%";
				ApplyFont(aLbl, _fontBold);
				aLbl.AddThemeColorOverride("font_color", ac);
				aLbl.AddThemeFontSizeOverride("font_size", 13);
				aRow.AddChild(aLbl, forceReadableName: false, Node.InternalMode.Disabled);
				target.AddChild(aRow, forceReadableName: false, Node.InternalMode.Disabled);
				cIdx++;
			}
		}
		else
		{
			Label noArch = new Label();
			noArch.Text = "No clear archetype focus";
			ApplyFont(noArch, _fontBody);
			noArch.AddThemeColorOverride("font_color", ClrSub);
			noArch.AddThemeFontSizeOverride("font_size", 13);
			target.AddChild(noArch, forceReadableName: false, Node.InternalMode.Disabled);
		}
		AddInlineEnergyCurve(target, analysis);
		AddInlineTypeDistribution(target, analysis);
	}

	private void AddInlineEnergyCurve(VBoxContainer target, DeckAnalysis analysis)
	{
		if (analysis.EnergyCurve.Count == 0) return;
		Label curveHeader = new Label();
		curveHeader.Text = "Energy Cost";
		ApplyFont(curveHeader, _fontBody);
		curveHeader.AddThemeColorOverride("font_color", ClrSub);
		curveHeader.AddThemeFontSizeOverride("font_size", 14);
		target.AddChild(curveHeader, forceReadableName: false, Node.InternalMode.Disabled);

		HBoxContainer costRow = new HBoxContainer();
		costRow.AddThemeConstantOverride("separation", 2);
		for (int cost = 0; cost <= 5; cost++)
		{
			Label costLbl = new Label();
			costLbl.Text = cost == 5 ? "5+" : cost.ToString();
			ApplyFont(costLbl, _fontBody);
			costLbl.AddThemeColorOverride("font_color", ClrSub);
			costLbl.AddThemeFontSizeOverride("font_size", 14);
			costLbl.HorizontalAlignment = HorizontalAlignment.Center;
			costLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			costRow.AddChild(costLbl, forceReadableName: false, Node.InternalMode.Disabled);
		}
		target.AddChild(costRow, forceReadableName: false, Node.InternalMode.Disabled);

		HBoxContainer curveRow = new HBoxContainer();
		curveRow.AddThemeConstantOverride("separation", 2);
		curveRow.CustomMinimumSize = new Vector2(0, 18f);
		int maxCount = analysis.EnergyCurve.Values.Max();
		Color[] costColors = {
			new Color(0.3f, 0.8f, 0.4f),
			new Color(0.4f, 0.8f, 0.9f),
			new Color(0.92f, 0.88f, 0.78f),
			new Color(0.9f, 0.75f, 0.3f),
			new Color(1f, 0.6f, 0.3f),
			new Color(0.9f, 0.3f, 0.3f)
		};
		for (int cost = 0; cost <= 5; cost++)
		{
			int count = analysis.EnergyCurve.TryGetValue(cost, out int c) ? c : 0;
			VBoxContainer col = new VBoxContainer();
			col.AddThemeConstantOverride("separation", 1);
			col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			float barHeight = maxCount > 0 ? (float)count / maxCount * 12f : 0f;
			ColorRect bar = new ColorRect();
			bar.Color = costColors[cost];
			bar.CustomMinimumSize = new Vector2(0, Math.Max(barHeight, 1f));
			col.AddChild(bar, forceReadableName: false, Node.InternalMode.Disabled);
			Label lbl = new Label();
			lbl.Text = count.ToString();
			ApplyFont(lbl, _fontBody);
			lbl.AddThemeColorOverride("font_color", ClrSub);
			lbl.AddThemeFontSizeOverride("font_size", 14);
			lbl.HorizontalAlignment = HorizontalAlignment.Center;
			col.AddChild(lbl, forceReadableName: false, Node.InternalMode.Disabled);
			curveRow.AddChild(col, forceReadableName: false, Node.InternalMode.Disabled);
		}
		target.AddChild(curveRow, forceReadableName: false, Node.InternalMode.Disabled);
	}

	private void AddInlineTypeDistribution(VBoxContainer target, DeckAnalysis analysis)
	{
		int totalTyped = analysis.AttackCount + analysis.SkillCount + analysis.PowerCount;
		if (totalTyped == 0) return;
		HBoxContainer typeRow = new HBoxContainer();
		typeRow.AddThemeConstantOverride("separation", 0);
		typeRow.CustomMinimumSize = new Vector2(0, 8f);
		if (analysis.AttackCount > 0)
		{
			ColorRect atkBar = new ColorRect();
			atkBar.Color = new Color(0.9f, 0.35f, 0.3f);
			atkBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			atkBar.SizeFlagsStretchRatio = analysis.AttackCount;
			atkBar.CustomMinimumSize = new Vector2(0, 8f);
			typeRow.AddChild(atkBar, forceReadableName: false, Node.InternalMode.Disabled);
		}
		if (analysis.SkillCount > 0)
		{
			ColorRect sklBar = new ColorRect();
			sklBar.Color = new Color(0.3f, 0.5f, 1f);
			sklBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			sklBar.SizeFlagsStretchRatio = analysis.SkillCount;
			sklBar.CustomMinimumSize = new Vector2(0, 8f);
			typeRow.AddChild(sklBar, forceReadableName: false, Node.InternalMode.Disabled);
		}
		if (analysis.PowerCount > 0)
		{
			ColorRect pwrBar = new ColorRect();
			pwrBar.Color = new Color(0.3f, 0.8f, 0.4f);
			pwrBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			pwrBar.SizeFlagsStretchRatio = analysis.PowerCount;
			pwrBar.CustomMinimumSize = new Vector2(0, 8f);
			typeRow.AddChild(pwrBar, forceReadableName: false, Node.InternalMode.Disabled);
		}
		target.AddChild(typeRow, forceReadableName: false, Node.InternalMode.Disabled);
		HBoxContainer typeLblRow = new HBoxContainer();
		typeLblRow.AddThemeConstantOverride("separation", 8);
		typeLblRow.Alignment = BoxContainer.AlignmentMode.Center;
		if (analysis.AttackCount > 0)
		{
			Label atkLbl = new Label();
			atkLbl.Text = $"{analysis.AttackCount} Atk";
			ApplyFont(atkLbl, _fontBody);
			atkLbl.AddThemeColorOverride("font_color", new Color(0.9f, 0.35f, 0.3f));
			atkLbl.AddThemeFontSizeOverride("font_size", 15);
			typeLblRow.AddChild(atkLbl, forceReadableName: false, Node.InternalMode.Disabled);
		}
		if (analysis.SkillCount > 0)
		{
			Label sklLbl = new Label();
			sklLbl.Text = $"{analysis.SkillCount} Skl";
			ApplyFont(sklLbl, _fontBody);
			sklLbl.AddThemeColorOverride("font_color", new Color(0.3f, 0.5f, 1f));
			sklLbl.AddThemeFontSizeOverride("font_size", 15);
			typeLblRow.AddChild(sklLbl, forceReadableName: false, Node.InternalMode.Disabled);
		}
		if (analysis.PowerCount > 0)
		{
			Label pwrLbl = new Label();
			pwrLbl.Text = $"{analysis.PowerCount} Pwr";
			ApplyFont(pwrLbl, _fontBody);
			pwrLbl.AddThemeColorOverride("font_color", new Color(0.3f, 0.8f, 0.4f));
			pwrLbl.AddThemeFontSizeOverride("font_size", 15);
			typeLblRow.AddChild(pwrLbl, forceReadableName: false, Node.InternalMode.Disabled);
		}
		target.AddChild(typeLblRow, forceReadableName: false, Node.InternalMode.Disabled);
	}

	private void UpdateDeckViz(DeckAnalysis analysis)
	{
		if (_deckVizContainer == null || !GodotObject.IsInstanceValid(_deckVizContainer))
			return;
		// Clear previous
		var vizChildren = _deckVizContainer.GetChildren().ToArray();
		foreach (Node child in vizChildren)
		{
			_deckVizContainer.RemoveChild(child);
			child.QueueFree();
		}
		if (analysis == null || analysis.TotalCards == 0)
			return;

		// Energy curve row
		if (analysis.EnergyCurve.Count > 0)
		{
			// Header
			Label curveHeader = new Label();
			curveHeader.Text = "Energy Cost";
			ApplyFont(curveHeader, _fontBody);
			curveHeader.AddThemeColorOverride("font_color", ClrSub);
			curveHeader.AddThemeFontSizeOverride("font_size", 14);
			_deckVizContainer.AddChild(curveHeader, forceReadableName: false, Node.InternalMode.Disabled);

			// Cost number row above bars
			HBoxContainer costRow = new HBoxContainer();
			costRow.AddThemeConstantOverride("separation", 2);
			for (int cost = 0; cost <= 5; cost++)
			{
				Label costLbl = new Label();
				costLbl.Text = cost == 5 ? "5+" : cost.ToString();
				ApplyFont(costLbl, _fontBody);
				costLbl.AddThemeColorOverride("font_color", ClrSub);
				costLbl.AddThemeFontSizeOverride("font_size", 14);
				costLbl.HorizontalAlignment = HorizontalAlignment.Center;
				costLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				costRow.AddChild(costLbl, forceReadableName: false, Node.InternalMode.Disabled);
			}
			_deckVizContainer.AddChild(costRow, forceReadableName: false, Node.InternalMode.Disabled);

			HBoxContainer curveRow = new HBoxContainer();
			curveRow.AddThemeConstantOverride("separation", 2);
			curveRow.CustomMinimumSize = new Vector2(0, 18f);
			int maxCount = analysis.EnergyCurve.Values.Max();
			Color[] costColors = {
				new Color(0.3f, 0.8f, 0.4f),   // 0: green
				new Color(0.4f, 0.8f, 0.9f),   // 1: aqua
				new Color(0.92f, 0.88f, 0.78f), // 2: cream
				new Color(0.9f, 0.75f, 0.3f),   // 3: gold
				new Color(1f, 0.6f, 0.3f),       // 4: orange
				new Color(0.9f, 0.3f, 0.3f)      // 5+: red
			};
			for (int cost = 0; cost <= 5; cost++)
			{
				int count = analysis.EnergyCurve.TryGetValue(cost, out int c) ? c : 0;
				VBoxContainer col = new VBoxContainer();
				col.AddThemeConstantOverride("separation", 1);
				col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

				// Bar
				float barHeight = maxCount > 0 ? (float)count / maxCount * 12f : 0f;
				ColorRect bar = new ColorRect();
				bar.Color = costColors[cost];
				bar.CustomMinimumSize = new Vector2(0, Math.Max(barHeight, 1f));
				col.AddChild(bar, forceReadableName: false, Node.InternalMode.Disabled);

				// Count label below bar
				Label lbl = new Label();
				lbl.Text = count.ToString();
				ApplyFont(lbl, _fontBody);
				lbl.AddThemeColorOverride("font_color", ClrSub);
				lbl.AddThemeFontSizeOverride("font_size", 14);
				lbl.HorizontalAlignment = HorizontalAlignment.Center;
				col.AddChild(lbl, forceReadableName: false, Node.InternalMode.Disabled);

				curveRow.AddChild(col, forceReadableName: false, Node.InternalMode.Disabled);
			}
			_deckVizContainer.AddChild(curveRow, forceReadableName: false, Node.InternalMode.Disabled);
		}

		// Type distribution row
		int totalTyped = analysis.AttackCount + analysis.SkillCount + analysis.PowerCount;
		if (totalTyped > 0)
		{
			HBoxContainer typeRow = new HBoxContainer();
			typeRow.AddThemeConstantOverride("separation", 0);
			typeRow.CustomMinimumSize = new Vector2(0, 8f);
			// Attack segment (red)
			if (analysis.AttackCount > 0)
			{
				ColorRect atkBar = new ColorRect();
				atkBar.Color = new Color(0.9f, 0.35f, 0.3f);
				atkBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				atkBar.SizeFlagsStretchRatio = analysis.AttackCount;
				atkBar.CustomMinimumSize = new Vector2(0, 8f);
				typeRow.AddChild(atkBar, forceReadableName: false, Node.InternalMode.Disabled);
			}
			// Skill segment (blue)
			if (analysis.SkillCount > 0)
			{
				ColorRect sklBar = new ColorRect();
				sklBar.Color = new Color(0.3f, 0.5f, 1f);
				sklBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				sklBar.SizeFlagsStretchRatio = analysis.SkillCount;
				sklBar.CustomMinimumSize = new Vector2(0, 8f);
				typeRow.AddChild(sklBar, forceReadableName: false, Node.InternalMode.Disabled);
			}
			// Power segment (green)
			if (analysis.PowerCount > 0)
			{
				ColorRect pwrBar = new ColorRect();
				pwrBar.Color = new Color(0.3f, 0.8f, 0.4f);
				pwrBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				pwrBar.SizeFlagsStretchRatio = analysis.PowerCount;
				pwrBar.CustomMinimumSize = new Vector2(0, 8f);
				typeRow.AddChild(pwrBar, forceReadableName: false, Node.InternalMode.Disabled);
			}
			_deckVizContainer.AddChild(typeRow, forceReadableName: false, Node.InternalMode.Disabled);

			// Type labels — color-matched
			HBoxContainer typeLblRow = new HBoxContainer();
			typeLblRow.AddThemeConstantOverride("separation", 8);
			typeLblRow.Alignment = BoxContainer.AlignmentMode.Center;
			if (analysis.AttackCount > 0)
			{
				Label atkLbl = new Label();
				atkLbl.Text = $"{analysis.AttackCount} Atk";
				ApplyFont(atkLbl, _fontBody);
				atkLbl.AddThemeColorOverride("font_color", new Color(0.9f, 0.35f, 0.3f));
				atkLbl.AddThemeFontSizeOverride("font_size", 17);
				typeLblRow.AddChild(atkLbl, forceReadableName: false, Node.InternalMode.Disabled);
			}
			if (analysis.SkillCount > 0)
			{
				Label sklLbl = new Label();
				sklLbl.Text = $"{analysis.SkillCount} Skl";
				ApplyFont(sklLbl, _fontBody);
				sklLbl.AddThemeColorOverride("font_color", new Color(0.3f, 0.5f, 1f));
				sklLbl.AddThemeFontSizeOverride("font_size", 17);
				typeLblRow.AddChild(sklLbl, forceReadableName: false, Node.InternalMode.Disabled);
			}
			if (analysis.PowerCount > 0)
			{
				Label pwrLbl = new Label();
				pwrLbl.Text = $"{analysis.PowerCount} Pwr";
				ApplyFont(pwrLbl, _fontBody);
				pwrLbl.AddThemeColorOverride("font_color", new Color(0.3f, 0.8f, 0.4f));
				pwrLbl.AddThemeFontSizeOverride("font_size", 17);
				typeLblRow.AddChild(pwrLbl, forceReadableName: false, Node.InternalMode.Disabled);
			}
			_deckVizContainer.AddChild(typeLblRow, forceReadableName: false, Node.InternalMode.Disabled);
		}
	}

	// Draw probability feature removed — never fired (draw pile empty at combat setup) and not useful

	private List<(string icon, string text, Color color)> GenerateRestSiteAdvice(DeckAnalysis deck, int hp, int maxHP, int act, int floor, GameState gameState = null)
	{
		var advice = new List<(string, string, Color)>();
		float hpRatio = maxHP > 0 ? (float)hp / maxHP : 1f;

		if (hpRatio < 0.5f)
		{
			advice.Add(("\u2B50", "REST recommended — HP is low", ClrNegative));
			if (hpRatio < 0.3f)
			{
				advice.Add(("\u26a0", "HP critical — resting is almost always correct here", ClrNegative));
			}
		}
		else if (hpRatio >= 0.75f)
		{
			advice.Add(("\u2B06", "UPGRADE recommended — HP is healthy", ClrPositive));
			if (deck != null && deck.DetectedArchetypes.Count > 0)
			{
				advice.Add(("\u2694", $"Upgrade key {deck.DetectedArchetypes[0].Archetype.DisplayName} cards", ClrAccent));
			}
		}
		else
		{
			// 50-75% HP: context-dependent
			bool isBossSoon = (floor % 8) >= 6; // rough heuristic
			if (isBossSoon)
			{
				advice.Add(("\u2764", "Boss approaching — rest to be safe", ClrExpensive));
			}
			else
			{
				advice.Add(("\u2B06", "Consider upgrading — HP is adequate", ClrAqua));
			}
		}

		// Upgrade priority list when upgrade is recommended (HP >= 50%)
		if (hpRatio >= 0.5f && gameState != null && deck != null)
		{
			string character = gameState.Character ?? deck.Character ?? "unknown";
			var priorities = GetUpgradePriorities(gameState, deck, character);
			if (priorities.Count > 0)
			{
				advice.Add(("\u2B06", "UPGRADE PRIORITY:", ClrAccent));
				advice.AddRange(priorities);
			}
		}

		return advice;
	}

	private List<(string icon, string text, Color color)> GetUpgradePriorities(GameState gs, DeckAnalysis deck, string character)
	{
		// Filter to non-upgraded, upgradeable cards
		var upgradeable = new List<CardInfo>();
		foreach (var card in gs.DeckCards)
		{
			if (card.Upgraded) continue;
			if (card.Type == "Status" || card.Type == "Curse") continue;
			upgradeable.Add(card);
		}
		if (upgradeable.Count == 0) return new List<(string, string, Color)>();

		// Score upgrade delta — how much value each card gains from upgrading
		var scored = Plugin.SynergyScorer.ScoreForUpgrade(upgradeable, deck, character,
			gs.ActNumber, gs.Floor, Plugin.TierEngine, Plugin.AdaptiveScorer);

		// Build display from top 3 by upgrade delta
		return scored
			.Take(3)
			.Select(c =>
			{
				string cardName = PrettifyId(c.Id);
				string subGrade = TierEngine.ScoreToSubGrade(c.FinalScore);
				string reason;
				if (c.UpgradeDelta >= 0.6f)
					reason = $" — big upgrade (+{c.UpgradeDelta:F1})";
				else if (c.SynergyDelta > 0.4f)
					reason = " — core synergy";
				else if (c.FinalGrade >= TierGrade.A)
					reason = " — high impact";
				else if (c.FinalGrade >= TierGrade.B)
					reason = " — solid pick";
				else
					reason = " — basic upgrade";
				string text = $"\u2B06 {cardName} [{subGrade}]{reason}";
				Color color = c.FinalGrade >= TierGrade.A ? ClrPositive : c.FinalGrade >= TierGrade.B ? ClrAqua : ClrCream;
				return ((string)"\u2022", text, color);
			})
			.ToList();
	}

	private List<(string icon, string text, Color color)> GenerateCombatAdvice(DeckAnalysis deck, int hp, int maxHP, int act, int floor)
	{
		var advice = new List<(string, string, Color)>();
		float hpRatio = maxHP > 0 ? (float)hp / maxHP : 1f;
		int deckSize = deck?.TotalCards ?? 0;

		// Deck size combat tips
		if (deckSize <= 12)
		{
			advice.Add(("\u2714", "Thin deck — strong draw consistency", ClrPositive));
		}
		else if (deckSize >= 30)
		{
			advice.Add(("\u26a0", "Large deck — key cards may be slow to draw", ClrExpensive));
		}

		// HP warning
		if (hpRatio < 0.3f)
		{
			advice.Add(("\u26a0", "Low HP — play defensively, prioritize block", ClrNegative));
		}

		return advice;
	}

	private List<(string icon, string text, Color color)> GenerateEventAdvice(DeckAnalysis deck, int hp, int maxHP, int gold, int act, int floor)
	{
		var advice = new List<(string, string, Color)>();
		float hpRatio = maxHP > 0 ? (float)hp / maxHP : 1f;
		int deckSize = deck?.TotalCards ?? 0;
		bool isDefined = deck != null && !deck.IsUndefined;

		// Event-specific guidance
		if (hpRatio < 0.35f)
		{
			advice.Add(("\u26a0", "HP is critical — avoid options that cost HP", ClrNegative));
		}
		if (gold < 50)
		{
			advice.Add(("\u26a0", "Gold is low — avoid options that cost gold", ClrExpensive));
		}
		if (deckSize >= 25)
		{
			advice.Add(("\u2714", "Deck is large — card removal options are valuable", ClrAqua));
		}
		if (deckSize <= 15 && isDefined)
		{
			advice.Add(("\u2714", "Deck is lean — be cautious adding cards", ClrAqua));
		}
		if (!isDefined)
		{
			advice.Add(("\u2714", "Deck unfocused — card rewards help find direction", ClrCream));
		}

		return advice;
	}

	private List<(string icon, string text, Color color)> GenerateMapAdvice(DeckAnalysis deck, int hp, int maxHP, int gold, int act, int floor)
	{
		var advice = new List<(string, string, Color)>();
		float hpRatio = maxHP > 0 ? (float)hp / maxHP : 1f;
		bool isDefined = deck != null && !deck.IsUndefined;
		int deckSize = deck?.TotalCards ?? 0;

		// HP-based priorities
		if (hpRatio < 0.4f)
		{
			advice.Add(("\u2764", $"HP critical ({hp}/{maxHP}) — prioritize REST sites", ClrNegative));
			advice.Add(("\u26a0", "Avoid elites unless no other option", ClrExpensive));
		}
		else if (hpRatio < 0.65f)
		{
			advice.Add(("\u2764", $"HP moderate ({hp}/{maxHP}) — rest sites valuable", ClrExpensive));
		}

		// Deck composition priorities
		if (!isDefined && floor <= 6)
		{
			advice.Add(("\u2694", "Early game — fights and events for deck building", ClrPositive));
		}
		else if (isDefined && deckSize >= 25)
		{
			advice.Add(("\u2702", $"Deck bloated ({deckSize} cards) — shop for card removal", ClrAqua));
		}
		else if (!isDefined && floor > 6)
		{
			advice.Add(("\u2694", "Deck unfocused — seek card rewards to find direction", ClrExpensive));
		}

		// Gold-based
		if (gold >= 300)
		{
			advice.Add(("\u2B50", $"Gold: {gold} — shop has great value", ClrAccent));
		}
		else if (gold >= 150 && deckSize >= 20)
		{
			advice.Add(("\u2B50", $"Gold: {gold} — consider shop for removal", ClrSub));
		}

		// Act-based
		if (act >= 2 && hpRatio > 0.7f && isDefined && deckSize < 25)
		{
			advice.Add(("\u2694", "Deck focused + healthy — elites for relics", ClrPositive));
		}

		// Treasure/question mark
		if (floor <= 4)
		{
			advice.Add(("\u2753", "Early floors — question marks are high value", ClrAqua));
		}

		if (advice.Count == 0)
		{
			advice.Add(("\u2714", "Balanced state — play to your deck's strengths", ClrCream));
		}

		return advice;
	}

	private void AddSkipEntry(string title, string reasoning)
	{
		PanelContainer panelContainer = CreateEntryPanel(isBest: false);
		HBoxContainer hBoxContainer = new HBoxContainer();
		hBoxContainer.AddThemeConstantOverride("separation", 16);
		panelContainer.AddChild(hBoxContainer, forceReadableName: false, Node.InternalMode.Disabled);
		hBoxContainer.AddChild(CreateSkipBadge(), forceReadableName: false, Node.InternalMode.Disabled);
		VBoxContainer vBoxContainer = new VBoxContainer();
		vBoxContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		hBoxContainer.AddChild(vBoxContainer, forceReadableName: false, Node.InternalMode.Disabled);
		Label label = new Label();
		label.Text = title;
		ApplyFont(label, _fontBold);
		label.AddThemeColorOverride("font_color", ClrSkip);
		label.AddThemeFontSizeOverride("font_size", 17);
		vBoxContainer.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
		Label label2 = new Label();
		label2.Text = reasoning;
		ApplyFont(label2, _fontBody);
		label2.AddThemeColorOverride("font_color", ClrSkipSub);
		label2.AddThemeFontSizeOverride("font_size", 17);
		label2.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		vBoxContainer.AddChild(label2, forceReadableName: false, Node.InternalMode.Disabled);
		_content.AddChild(panelContainer, forceReadableName: false, Node.InternalMode.Disabled);
	}

	private PanelContainer CreateEntryPanel(bool isBest, TierGrade grade = TierGrade.C)
	{
		PanelContainer panel = new PanelContainer();
		bool isSTier = grade == TierGrade.S && isBest;
		StyleBoxFlat normalStyle = isSTier ? (_sbSTier ?? _sbBest ?? _sbEntry) :
			(isBest ? _sbBest : _sbEntry) ?? _sbEntry;
		StyleBoxFlat hoverStyle = isSTier ? (_sbSTierHover ?? _sbHoverBest ?? _sbEntry) :
			(isBest ? _sbHoverBest : _sbHover) ?? _sbEntry;
		panel.AddThemeStyleboxOverride("panel", normalStyle);
		panel.MouseFilter = Control.MouseFilterEnum.Stop;
		panel.Connect("mouse_entered", Callable.From(delegate
		{
			if (GodotObject.IsInstanceValid(panel))
			{
				panel.AddThemeStyleboxOverride("panel", hoverStyle);
			}
		}));
		panel.Connect("mouse_exited", Callable.From(delegate
		{
			if (GodotObject.IsInstanceValid(panel))
			{
				panel.AddThemeStyleboxOverride("panel", normalStyle);
			}
		}));
		// S-tier pulsing golden glow
		if (isSTier)
		{
			PanelContainer glowPanel = panel;
			Callable.From(() =>
			{
				if (GodotObject.IsInstanceValid(glowPanel))
				{
					var tween = glowPanel.CreateTween();
					if (tween != null)
					{
						tween.SetLoops(0); // infinite
						tween.TweenProperty(glowPanel, "self_modulate:a", 0.75f, 0.6f)
							.SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
						tween.TweenProperty(glowPanel, "self_modulate:a", 1.0f, 0.6f)
							.SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
					}
				}
			}).CallDeferred();
		}
		return panel;
	}

	private CenterContainer CreateBadge(TierGrade grade, float score = -1f)
	{
		string subGrade = score >= 0f ? TierEngine.ScoreToSubGrade(score) : grade.ToString();
		CenterContainer obj = new CenterContainer
		{
			CustomMinimumSize = new Vector2(34f, 34f)
		};
		PanelContainer panelContainer = new PanelContainer
		{
			CustomMinimumSize = new Vector2(subGrade.Length > 1 ? 38f : 30f, 30f)
		};
		Color badgeColor = TierBadge.GetGodotColor(grade);
		StyleBoxFlat styleBoxFlat = new StyleBoxFlat
		{
			BgColor = badgeColor
		};
		styleBoxFlat.CornerRadiusTopLeft = 0;
		styleBoxFlat.CornerRadiusTopRight = 10;
		styleBoxFlat.CornerRadiusBottomLeft = 10;
		styleBoxFlat.CornerRadiusBottomRight = 0;
		styleBoxFlat.BorderWidthTop = 1;
		styleBoxFlat.BorderWidthBottom = 1;
		styleBoxFlat.BorderWidthLeft = 1;
		styleBoxFlat.BorderWidthRight = 1;
		styleBoxFlat.BorderColor = badgeColor.Darkened(0.3f);
		panelContainer.AddThemeStyleboxOverride("panel", styleBoxFlat);
		Label label = new Label();
		label.Text = subGrade;
		ApplyFont(label, _fontHeader);
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.AddThemeColorOverride("font_color", TierBadge.GetTextColor(grade));
		label.AddThemeFontSizeOverride("font_size", subGrade.Length > 1 ? 17 : 20);
		label.AddThemeConstantOverride("outline_size", 0);
		panelContainer.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
		obj.AddChild(panelContainer, forceReadableName: false, Node.InternalMode.Disabled);
		return obj;
	}

	private CenterContainer CreateSkipBadge()
	{
		CenterContainer obj = new CenterContainer
		{
			CustomMinimumSize = new Vector2(34f, 34f)
		};
		PanelContainer panelContainer = new PanelContainer
		{
			CustomMinimumSize = new Vector2(30f, 30f)
		};
		StyleBoxFlat styleBoxFlat = new StyleBoxFlat
		{
			BgColor = new Color(0.2f, 0.15f, 0.3f)
		};
		styleBoxFlat.CornerRadiusTopLeft = 0;
		styleBoxFlat.CornerRadiusTopRight = 10;
		styleBoxFlat.CornerRadiusBottomLeft = 10;
		styleBoxFlat.CornerRadiusBottomRight = 0;
		styleBoxFlat.BorderWidthTop = 1;
		styleBoxFlat.BorderWidthBottom = 1;
		styleBoxFlat.BorderWidthLeft = 1;
		styleBoxFlat.BorderWidthRight = 1;
		styleBoxFlat.BorderColor = ClrSkip;
		panelContainer.AddThemeStyleboxOverride("panel", styleBoxFlat);
		Label label = new Label();
		label.Text = "\u2014";
		ApplyFont(label, _fontHeader);
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.AddThemeColorOverride("font_color", ClrSkip);
		label.AddThemeFontSizeOverride("font_size", 22);
		panelContainer.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
		obj.AddChild(panelContainer, forceReadableName: false, Node.InternalMode.Disabled);
		return obj;
	}

	// === V1: Color-coded title separator ===

	private static Color GetScreenColor(string screen)
	{
		return screen switch
		{
			"CARD REWARD" or "CARD REMOVAL" or "CARD UPGRADE" => ClrAccent,
			"EVENT CARD OFFER" => ClrSkip,
			"RELIC REWARD" => ClrPositive,
			"MERCHANT SHOP" => ClrExpensive,
			"COMBAT" => ClrNegative,
			"MAP" or "MAP / COMBAT" => ClrAqua,
			"EVENT" => ClrSkip,
			"REST SITE" => ClrPositive,
			_ when screen != null && screen.Contains("RUN") => ClrAccent,
			_ => ClrBorder,
		};
	}

	private void UpdateTitleSepColor()
	{
		if (_titleSep == null || !GodotObject.IsInstanceValid(_titleSep))
			return;
		Color sepColor = GetScreenColor(_currentScreen);
		_titleSep.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(sepColor, 0.8f), Thickness = 2 });
	}

	// === A1: Win rate tracker ===

	private string _lastWinRateCharacter;

	private void UpdateWinRate()
	{
		if (_winRateLabel == null || !GodotObject.IsInstanceValid(_winRateLabel))
			return;
		if (string.IsNullOrEmpty(_currentCharacter) || Plugin.RunDatabase == null)
		{
			_winRateLabel.Visible = false;
			return;
		}
		// Only re-query if character changed
		if (_currentCharacter == _lastWinRateCharacter && _winRateLabel.Visible)
			return;
		_lastWinRateCharacter = _currentCharacter;
		try
		{
			var (wins, total) = Plugin.RunDatabase.GetCharacterWinRate(_currentCharacter);
			if (total == 0)
			{
				_winRateLabel.Visible = false;
				return;
			}
			float rate = (float)wins / total * 100f;
			string charName = char.ToUpper(_currentCharacter[0]) + _currentCharacter.Substring(1);
			_winRateLabel.Text = $"{charName}: {wins}W / {total} ({rate:F0}%)";
			_winRateLabel.AddThemeColorOverride("font_color", rate >= 50f ? ClrPositive : rate >= 30f ? ClrSub : ClrNegative);
			_winRateLabel.Visible = true;
		}
		catch
		{
			_winRateLabel.Visible = false;
		}
	}

	// === A2: Post-run controversial picks summary ===

	public void ShowRunSummary(RunOutcome outcome, int finalFloor, int finalAct)
	{
		try
		{
			var events = Plugin.RunTracker?.GetCurrentRunEvents();
			if (events == null || events.Count == 0)
			{
				Clear();
				return;
			}
			string character = Plugin.RunTracker?.CurrentCharacter ?? _currentCharacter ?? "unknown";
			_currentCards = null;
			_currentRelics = null;
			_currentDeckAnalysis = null;
			_currentScreen = outcome == RunOutcome.Win ? "RUN WON!" : "RUN LOST";
			_mapAdvice = null;
			// Force win rate label to refresh next time
			_lastWinRateCharacter = null;
			if (!EnsureOverlay()) return;
			if (_screenLabel != null && GodotObject.IsInstanceValid(_screenLabel))
				_screenLabel.Text = _currentScreen;
			UpdateTitleSepColor();
			UpdateWinRate();
			if (_collapsed)
			{
				ResizePanelToContent();
				return;
			}
			var children = _content.GetChildren().ToArray();
			foreach (Node child in children)
			{
				if (child != null)
				{
					_content.RemoveChild(child);
					child.QueueFree();
				}
			}
			// Section: RUN SUMMARY
			Color outcomeColor = outcome == RunOutcome.Win ? ClrPositive : ClrNegative;
			AddSectionHeader($"RUN SUMMARY \u2014 {outcome.ToString().ToUpper()} (Floor {finalFloor})");
			// Find controversial picks
			var controversial = new List<(DecisionEvent evt, TierGrade chosenGrade, TierGrade bestGrade)>();
			// Check if choice tracking is working (if most choices are null, ID extraction failed)
			int nullChoices = events.Count(e => e.ChosenId == null && e.OfferedIds?.Count > 0);
			bool choiceTrackingBroken = nullChoices > events.Count * 0.7f;
			foreach (var evt in events)
			{
				if (evt.OfferedIds == null || evt.OfferedIds.Count == 0) continue;
				// Find best available grade
				TierGrade bestGrade = TierGrade.F;
				foreach (string id in evt.OfferedIds)
				{
					TierGrade g = LookupGrade(id, character);
					if (g > bestGrade) bestGrade = g;
				}
				if (evt.ChosenId == null)
				{
					// Only flag skips if choice tracking is working
					if (!choiceTrackingBroken && bestGrade >= TierGrade.A)
						controversial.Add((evt, TierGrade.F, bestGrade));
				}
				else
				{
					TierGrade chosenGrade = LookupGrade(evt.ChosenId, character);
					int gap = (int)bestGrade - (int)chosenGrade;
					if (gap >= 2)
						controversial.Add((evt, chosenGrade, bestGrade));
				}
			}
			if (controversial.Count > 0)
			{
				Label contrHeader = new Label();
				contrHeader.Text = "CONTROVERSIAL PICKS";
				ApplyFont(contrHeader, _fontBold);
				contrHeader.AddThemeColorOverride("font_color", ClrExpensive);
				contrHeader.AddThemeFontSizeOverride("font_size", 14);
				_content.AddChild(contrHeader, forceReadableName: false, Node.InternalMode.Disabled);
				foreach (var (evt, chosenGrade, bestGrade) in controversial.Take(8))
				{
					string chosen = evt.ChosenId != null ? PrettifyId(evt.ChosenId) : "Skipped";
					string bestId = evt.OfferedIds.OrderByDescending(id => (int)LookupGrade(id, character)).First();
					string bestName = PrettifyId(bestId);
					int gap = (int)bestGrade - (int)chosenGrade;
					PanelContainer cPanel = new PanelContainer();
					StyleBoxFlat cStyle = new StyleBoxFlat();
					cStyle.BgColor = new Color(outcomeColor, 0.08f);
					cStyle.CornerRadiusTopRight = 6;
					cStyle.CornerRadiusBottomRight = 6;
					cStyle.BorderWidthLeft = 3;
					cStyle.BorderColor = outcomeColor;
					cStyle.ContentMarginLeft = 10f;
					cStyle.ContentMarginRight = 8f;
					cStyle.ContentMarginTop = 4f;
					cStyle.ContentMarginBottom = 4f;
					cPanel.AddThemeStyleboxOverride("panel", cStyle);
					Label cLbl = new Label();
					cLbl.Text = $"F{evt.Floor}: Chose {chosen} [{chosenGrade}] \u2014 Best: {bestName} [{bestGrade}] ({gap} grades below)";
					ApplyFont(cLbl, _fontBody);
					cLbl.AddThemeColorOverride("font_color", outcome == RunOutcome.Win ? ClrPositive : ClrNegative);
					cLbl.AddThemeFontSizeOverride("font_size", 17);
					cLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
					cPanel.AddChild(cLbl, forceReadableName: false, Node.InternalMode.Disabled);
					_content.AddChild(cPanel, forceReadableName: false, Node.InternalMode.Disabled);
				}
			}
			else
			{
				Label noContr = new Label();
				noContr.Text = "No controversial picks \u2014 solid decision-making!";
				ApplyFont(noContr, _fontBody);
				noContr.AddThemeColorOverride("font_color", ClrPositive);
				noContr.AddThemeFontSizeOverride("font_size", 14);
				_content.AddChild(noContr, forceReadableName: false, Node.InternalMode.Disabled);
			}
			// Stats line
			Label statsLbl = new Label();
			statsLbl.Text = $"Decisions: {events.Count} | Controversial: {controversial.Count}";
			ApplyFont(statsLbl, _fontBold);
			statsLbl.AddThemeColorOverride("font_color", ClrSub);
			statsLbl.AddThemeFontSizeOverride("font_size", 14);
			_content.AddChild(statsLbl, forceReadableName: false, Node.InternalMode.Disabled);
			ResizePanelToContent();
			// V2: Fade in the summary
			if (_content != null && GodotObject.IsInstanceValid(_content))
			{
				_content.Modulate = new Color(1, 1, 1, 0);
				_content.CreateTween()?.TweenProperty(_content, "modulate", Colors.White, 0.3f);
			}
			_previousScreen = _currentScreen;
		}
		catch (Exception ex)
		{
			Plugin.Log($"ShowRunSummary error: {ex}");
			Clear();
		}
	}

	// === In-game grade badge injection (STS1 mod inspired) ===

	// Badge group no longer needed — badges live on our CanvasLayer, not in the game tree

	/// <summary>
	/// Inject grade badges directly onto the game's card reward screen nodes.
	/// Walks the scene tree from the screen node to find card-holder children.
	/// </summary>
	public void InjectCardGrades(Node screenNode, List<ScoredCard> scoredCards, bool force = false)
	{
		if (!_showInGameBadges || screenNode == null || !GodotObject.IsInstanceValid(screenNode) || scoredCards == null || scoredCards.Count == 0)
			return;
		// Only inject when GamePatches confirmed this is a genuine card reward (not reused screen)
		// force=true bypasses this check (used by toggle reinject where we know the context is valid)
		if (!force && !GamePatches.IsGenuineCardReward)
		{
			Plugin.Log("InjectCardGrades skipped — not a genuine card reward");
			return;
		}
		if (_currentScreen != "CARD REWARD" || IsInsideMerchant(screenNode))
			return;
		// Card rewards have 3-4 cards; draw/discard pile viewers have many more
		if (scoredCards.Count > 5)
			return;
		try
		{
			// Clean up ALL previous badges (they live on our layer, so this is clean)
			ClearInGameBadges();
			// Capture epoch so deferred call can detect stale invocations
			int epoch = _badgeEpoch;
			LogNodeTree(screenNode, "CardReward", 0, 5);
			Callable.From(() => InjectCardGradesDeferred(screenNode, scoredCards, epoch)).CallDeferred();
		}
		catch (Exception ex)
		{
			Plugin.Log($"InjectCardGrades error: {ex.Message}");
		}
	}

	private void InjectCardGradesDeferred(Node screenNode, List<ScoredCard> scoredCards, int epoch)
	{
		if (screenNode == null || !GodotObject.IsInstanceValid(screenNode))
			return;
		// Stale deferred call — screen changed since injection was queued
		if (epoch != _badgeEpoch)
			return;
		if (!_showInGameBadges || _currentScreen != "CARD REWARD" || !GamePatches.IsGenuineCardReward)
			return;
		// No need to scan game tree for overlay screens — badges are on our layer
		// and will be cleaned up by ClearInGameBadges when screen changes.
		try
		{
			// Strategy: Find all Control children that look like card holders
			// Card reward screens typically have a container with N children (one per card)
			// We look for containers whose child count matches our scored card count
			var cardHolders = FindCardHolderNodes(screenNode, scoredCards.Count);
			if (cardHolders == null || cardHolders.Count == 0)
			{
				Plugin.Log($"Could not find card holder nodes in {screenNode.GetType().Name} (expected {scoredCards.Count} cards)");
				return;
			}
			Plugin.Log($"Found {cardHolders.Count} card holders — injecting grade badges");
			// Store context for later validation
			_badgeScreenNode = new WeakReference<Node>(screenNode);
			_badgeExpectedHolderCount = cardHolders.Count;
			// Match holders to scored cards by index (same order as ShowScreen receives them)
			for (int i = 0; i < Math.Min(cardHolders.Count, scoredCards.Count); i++)
			{
				AttachGradeBadge(cardHolders[i], scoredCards[i].FinalGrade, scoredCards[i].IsBestPick, scoredCards[i].FinalScore);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log($"InjectCardGradesDeferred error: {ex.Message}");
		}
	}

	/// <summary>
	/// Inject grade badges onto shop screen nodes.
	/// Shop has multiple item groups, so we use a broader search.
	/// </summary>
	public void InjectShopGrades(Node shopNode, List<ScoredCard> scoredCards, List<ScoredRelic> scoredRelics)
	{
		// Shop badge injection disabled — positional matching is unreliable
		// (hits potions, card removal, nav buttons). Overlay panel shows grades instead.
	}

	private void InjectShopGradesDeferred(Node shopNode, List<(TierGrade grade, bool isBest)> allGrades)
	{
		if (shopNode == null || !GodotObject.IsInstanceValid(shopNode))
			return;
		try
		{
			// Shop items may be in multiple containers. Find all sizeable Control leaves.
			var shopItems = FindAllSizeableControls(shopNode, minW: 60, minH: 60, maxDepth: 8);
			// Log found controls for debugging
			foreach (var item in shopItems)
				Plugin.Log($"  Shop item: {item.Name} ({item.GetType().Name}) size={item.Size} children={item.GetChildCount()}");
			Plugin.Log($"Shop: found {shopItems.Count} sizeable controls, have {allGrades.Count} grades");
			// Only badge up to the number of grades we have
			int matched = Math.Min(shopItems.Count, allGrades.Count);
			for (int i = 0; i < matched; i++)
			{
				AttachGradeBadge(shopItems[i], allGrades[i].grade, allGrades[i].isBest);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log($"InjectShopGradesDeferred error: {ex.Message}");
		}
	}

	/// <summary>
	/// Find all sizeable, visible, leaf-like Control nodes (no sizeable Control children themselves).
	/// Used for shop screens where items may be in multiple groups.
	/// </summary>
	private List<Control> FindAllSizeableControls(Node root, float minW, float minH, int maxDepth)
	{
		var result = new List<Control>();
		var stack = new Stack<(Node node, int depth)>();
		stack.Push((root, 0));
		while (stack.Count > 0)
		{
			var (current, depth) = stack.Pop();
			if (depth > maxDepth) continue;
			if (current is Control ctrl && ctrl.Visible && ctrl.Size.X >= minW && ctrl.Size.Y >= minH)
			{
				// Check if this is a "leaf" (no large Control children) — likely an item
				bool hasLargeChild = false;
				foreach (Node child in ctrl.GetChildren())
				{
					if (child is Control cc && cc.Visible && cc.Size.X >= minW && cc.Size.Y >= minH)
					{
						hasLargeChild = true;
						break;
					}
				}
				if (!hasLargeChild && depth >= 2)
				{
					// Skip navigation buttons — check node name AND walk up ancestors
					if (IsButtonNode(ctrl))
						continue;
					result.Add(ctrl);
					continue; // Don't recurse further into this item
				}
			}
			foreach (Node child in current.GetChildren())
			{
				stack.Push((child, depth + 1));
			}
		}
		return result;
	}

	/// <summary>
	/// Check if a node is a button or part of a button (back, close, nav, etc.)
	/// Checks the node itself, its name, its type, and up to 3 ancestors.
	/// </summary>
	private static bool IsButtonNode(Control ctrl)
	{
		// Check the node and its ancestors (up to 3 levels)
		Node current = ctrl;
		for (int i = 0; i < 4 && current != null; i++)
		{
			if (current is Godot.BaseButton)
				return true;
			string name = current.Name.ToString().ToLowerInvariant();
			string typeName = current.GetType().Name.ToLowerInvariant();
			if (name.Contains("back") || name.Contains("close") || name.Contains("exit") ||
				name.Contains("return") || name.Contains("button") || name.Contains("btn") ||
				name.Contains("nav") || name.Contains("cancel") ||
				typeName.Contains("button"))
				return true;
			current = current.GetParent();
		}
		return false;
	}

	/// <summary>
	/// Finds Control nodes that are likely card/relic holders.
	/// Searches for a container whose direct Control children count matches expectedCount.
	/// </summary>
	private List<Control> FindCardHolderNodes(Node root, int expectedCount)
	{
		// Strategy 1: Find NGridCardHolder nodes by type name (card reward screen)
		// Tree structure: CardRow > NGridCardHolder > NCardHolderHitbox (300x422)
		var gridHolders = new List<Control>();
		FindNodesByTypeName(root, "NGridCardHolder", gridHolders, 8);
		if (gridHolders.Count == expectedCount)
		{
			// Use the hitbox child of each grid holder (it has the actual size)
			var hitboxes = new List<Control>();
			foreach (var holder in gridHolders)
			{
				foreach (Node child in holder.GetChildren())
				{
					if (child is Control ctrl && ctrl.GetType().Name.Contains("Hitbox"))
					{
						hitboxes.Add(ctrl);
						break;
					}
				}
				if (hitboxes.Count < gridHolders.IndexOf(holder) + 1)
					hitboxes.Add(holder); // fallback to holder itself
			}
			Plugin.Log($"Found {hitboxes.Count} card holders via NGridCardHolder hitboxes");
			return hitboxes;
		}

		// Strategy 2: Find by container child count (relic reward, etc.)
		var queue = new Queue<Node>();
		queue.Enqueue(root);
		List<Control> bestMatch = null;

		while (queue.Count > 0)
		{
			Node current = queue.Dequeue();
			if (current is Control container)
			{
				var controlChildren = new List<Control>();
				foreach (Node child in container.GetChildren())
				{
					if (child is Control ctrl && ctrl.Visible && ctrl.Size.X >= 50 && ctrl.Size.Y >= 50)
						controlChildren.Add(ctrl);
				}
				if (controlChildren.Count == expectedCount && expectedCount >= 2)
				{
					bool allSizeable = controlChildren.All(c => c.Size.X >= 80 && c.Size.Y >= 80);
					if (allSizeable)
					{
						Plugin.Log($"Found holder container: {current.GetType().Name} with {controlChildren.Count} children");
						bestMatch = controlChildren;
						break;
					}
				}
			}
			if (GetDepth(current, root) < 8)
			{
				foreach (Node child in current.GetChildren())
					queue.Enqueue(child);
			}
		}
		return bestMatch;
	}

	private static void FindNodesByTypeName(Node root, string typeName, List<Control> results, int maxDepth)
	{
		if (root == null || maxDepth <= 0) return;
		if (root is Control ctrl && root.GetType().Name == typeName)
			results.Add(ctrl);
		foreach (Node child in root.GetChildren())
			FindNodesByTypeName(child, typeName, results, maxDepth - 1);
	}

	private static int GetDepth(Node node, Node root)
	{
		int depth = 0;
		Node current = node;
		while (current != null && current != root && depth < 20)
		{
			current = current.GetParent();
			depth++;
		}
		return depth;
	}

	/// <summary>
	/// Creates a floating grade badge as a child of the target game node.
	/// Badges are tracked in _inGameBadges for reliable cleanup (no node groups needed).
	/// </summary>
	private void AttachGradeBadge(Control targetNode, TierGrade grade, bool isBestPick, float score = -1f)
	{
		if (targetNode == null || !GodotObject.IsInstanceValid(targetNode))
			return;

		string subGrade = score >= 0f ? TierEngine.ScoreToSubGrade(score) : grade.ToString();
		Color badgeColor = TierBadge.GetGodotColor(grade);
		Color textColor = TierBadge.GetTextColor(grade);

		// Create badge panel — matches overlay CreateBadge style
		PanelContainer badge = new PanelContainer();
		badge.CustomMinimumSize = new Vector2(subGrade.Length > 1 ? 38f : 30f, 30f);

		StyleBoxFlat badgeStyle = new StyleBoxFlat();
		badgeStyle.BgColor = badgeColor;
		badgeStyle.CornerRadiusTopLeft = 0;
		badgeStyle.CornerRadiusTopRight = 10;
		badgeStyle.CornerRadiusBottomLeft = 10;
		badgeStyle.CornerRadiusBottomRight = 0;
		badgeStyle.BorderWidthTop = isBestPick ? 2 : 1;
		badgeStyle.BorderWidthBottom = isBestPick ? 2 : 1;
		badgeStyle.BorderWidthLeft = isBestPick ? 2 : 1;
		badgeStyle.BorderWidthRight = isBestPick ? 2 : 1;
		badgeStyle.BorderColor = isBestPick ? ClrAccent : badgeColor.Darkened(0.3f);
		badgeStyle.ShadowSize = isBestPick ? 12 : 4;
		badgeStyle.ShadowColor = isBestPick ? new Color(ClrAccent, 0.7f) : new Color(0f, 0f, 0f, 0.6f);
		badge.AddThemeStyleboxOverride("panel", badgeStyle);

		Label gradeLbl = new Label();
		gradeLbl.Text = subGrade;
		ApplyFont(gradeLbl, _fontHeader);
		gradeLbl.AddThemeColorOverride("font_color", textColor);
		gradeLbl.AddThemeFontSizeOverride("font_size", subGrade.Length > 1 ? 17 : 20);
		gradeLbl.HorizontalAlignment = HorizontalAlignment.Center;
		gradeLbl.VerticalAlignment = VerticalAlignment.Center;
		badge.AddChild(gradeLbl, forceReadableName: false, Node.InternalMode.Disabled);

		badge.ZIndex = 10; // Above sibling game UI
		badge.MouseFilter = Control.MouseFilterEnum.Ignore;

		// Add as child of target node (inherits z-order, goes behind popups)
		targetNode.AddChild(badge, forceReadableName: false, Node.InternalMode.Disabled);
		_inGameBadges.Add((badge, new WeakReference<Control>(targetNode)));
		// Position after adding (deferred so size is known)
		Callable.From(() => PositionBadgeInParent(badge, targetNode)).CallDeferred();
	}

	/// <summary>
	/// Position a badge within its parent node (bottom-center, local coordinates).
	/// Badge is a child of the target, so we use parent size, not global coords.
	/// </summary>
	private static void PositionBadgeInParent(PanelContainer badge, Control parent)
	{
		if (badge == null || !GodotObject.IsInstanceValid(badge) ||
		    parent == null || !GodotObject.IsInstanceValid(parent))
			return;
		float parentW = parent.Size.X;
		float parentH = parent.Size.Y;
		float badgeW = badge.GetCombinedMinimumSize().X;
		float badgeH = badge.GetCombinedMinimumSize().Y;
		badge.Position = new Vector2((parentW - badgeW) / 2f, parentH - badgeH - 4f);
	}

	public void CleanupAllBadges()
	{
		ClearInGameBadges();
	}

	/// <summary>
	/// Remove all in-game badges (children of game card nodes).
	/// Tracked list makes cleanup simple — no game tree scanning needed.
	/// </summary>
	private void ClearInGameBadges()
	{
		foreach (var (badge, _) in _inGameBadges)
		{
			try
			{
				if (badge != null && GodotObject.IsInstanceValid(badge))
				{
					badge.GetParent()?.RemoveChild(badge);
					badge.QueueFree();
				}
			}
			catch { }
		}
		_inGameBadges.Clear();
		_badgeScreenNode = null;
		_badgeExpectedHolderCount = 0;
	}

	/// <summary>
	/// Update badge positions to track their target game nodes.
	/// Also removes badges whose targets are no longer valid/visible,
	/// or if the screen context has changed (pile viewer opened, etc.).
	/// Called from CheckForStaleScreen on each tick.
	/// </summary>
	private void UpdateInGameBadgePositions()
	{
		if (_inGameBadges.Count == 0) return;

		// Context check: if the card reward screen node is gone/hidden, clear all badges
		if (_badgeScreenNode != null)
		{
			if (!_badgeScreenNode.TryGetTarget(out var screenNode) ||
			    !GodotObject.IsInstanceValid(screenNode) ||
			    (screenNode is Control screenCtrl && !screenCtrl.IsVisibleInTree()))
			{
				ClearInGameBadges();
				return;
			}
			// Context check: if more NGridCardHolder nodes appeared, a pile/overlay opened
			var allHolders = new List<Control>();
			FindNodesByTypeName(screenNode.GetTree()?.Root ?? screenNode, "NGridCardHolder", allHolders, 8);
			if (allHolders.Count > _badgeExpectedHolderCount + 1)
			{
				Plugin.Log($"Badge context changed: {allHolders.Count} NGridCardHolder nodes vs expected {_badgeExpectedHolderCount} — clearing badges");
				ClearInGameBadges();
				return;
			}
		}

		bool anyInvalid = false;
		foreach (var (badge, targetRef) in _inGameBadges)
		{
			if (badge == null || !GodotObject.IsInstanceValid(badge))
			{
				anyInvalid = true;
				continue;
			}
			if (!targetRef.TryGetTarget(out var target) ||
			    !GodotObject.IsInstanceValid(target) ||
			    !target.IsVisibleInTree())
			{
				// Target gone or hidden — remove this badge
				badge.Visible = false;
				anyInvalid = true;
				continue;
			}
			// Update position within parent (local coords)
			PositionBadgeInParent(badge, target);
		}
		// Clean up invalid entries
		if (anyInvalid)
		{
			for (int i = _inGameBadges.Count - 1; i >= 0; i--)
			{
				var (badge, targetRef) = _inGameBadges[i];
				bool badgeGone = badge == null || !GodotObject.IsInstanceValid(badge);
				bool targetGone = !targetRef.TryGetTarget(out var t) || !GodotObject.IsInstanceValid(t) || !t.IsVisibleInTree();
				if (badgeGone || targetGone)
				{
					if (!badgeGone)
					{
						badge.GetParent()?.RemoveChild(badge);
						badge.QueueFree();
					}
					_inGameBadges.RemoveAt(i);
				}
			}
		}
	}

	private static void LogNodeTree(Node node, string label, int depth, int maxDepth)
	{
		if (node == null || !GodotObject.IsInstanceValid(node) || depth > maxDepth)
			return;
		string indent = new string(' ', depth * 2);
		string sizeInfo = node is Control ctrl ? $" [{ctrl.Size.X:F0}x{ctrl.Size.Y:F0}]" : "";
		string visInfo = node is Control ctrl2 ? (ctrl2.Visible ? "" : " (hidden)") : "";
		Plugin.Log($"  {indent}{label}> {node.GetType().Name} \"{node.Name}\"{sizeInfo}{visInfo}");
		int i = 0;
		foreach (Node child in node.GetChildren())
		{
			LogNodeTree(child, $"[{i}]", depth + 1, maxDepth);
			i++;
		}
	}
}
