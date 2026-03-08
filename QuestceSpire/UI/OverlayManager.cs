using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
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

	private Label _archetypeLabel;

	private Label _screenLabel;



	private bool _visible = true;

	private bool _showTooltips = true;

	private bool _showInGameBadges = true;

	private List<ScoredCard> _currentCards;

	private List<ScoredRelic> _currentRelics;

	private DeckAnalysis _currentDeckAnalysis;

	private string _currentScreen = "IDLE";

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
	private PanelContainer _archChipPanel;
	private VBoxContainer _deckVizContainer;

	// Feature 1: Decision history
	private bool _showHistory;

	// Section toggles (persisted)
	private bool _showDeckBreakdown = true;
	private bool _showDrawProb = true;

	// Staleness tracking: clear overlay when game screen changes without a patch firing
	private ulong _lastUpdateTick;

	// Feature 5: Draw probability
	private GameState _currentGameState;

	// Feature 6: Relic tenure
	private int _currentFloor;

	// V1: Color-coded title separator
	private HSeparator _titleSep;

	// V2: Animated transitions
	private string _previousScreen = "";

	// V4: Card art hover preview
	private PanelContainer _hoverPreview;
	private TextureRect _hoverPreviewTex;

	// A1: Win rate tracker
	private Label _winRateLabel;

	private VBoxContainer _archChipVBox;

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

	private StyleBoxFlat _sbHover;

	private StyleBoxFlat _sbHoverBest;

	private StyleBoxFlat _sbSTier;

	private StyleBoxFlat _sbSTierHover;

	public OverlayManager()
	{
		_settings = OverlaySettings.Load();
		_visible = _settings.Visible;
		_showTooltips = _settings.ShowTooltips;
		_showInGameBadges = _settings.ShowInGameBadges;
		_panelOpacity = _settings.PanelOpacity;
		_opacityIndex = Array.IndexOf(OpacitySteps, _panelOpacity);
		if (_opacityIndex < 0) _opacityIndex = 0;
		_collapsed = _settings.Collapsed;
		_showDeckBreakdown = _settings.ShowDeckBreakdown;
		_showHistory = _settings.ShowDecisionHistory;
		_showDrawProb = _settings.ShowDrawProbability;
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

	private Texture2D GetCardPortrait(string cardId, string character)
	{
		string key = $"{character}/{cardId}";
		if (_cardPortraitCache.TryGetValue(key, out var cached)) return cached;
		try
		{
			// Convert "Feel No Pain" -> "feel_no_pain", character "ironclad" stays lowercase
			string fileName = cardId.Replace(" ", "_").Replace("'", "").Replace("-", "_").ToLowerInvariant();
			string charFolder = character?.ToLowerInvariant() ?? "ironclad";
			string path = $"res://images/packed/card_portraits/{charFolder}/{fileName}.png";
			var tex = ResourceLoader.Load<Texture2D>(path);
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
		_archetypeLabel = null;
		_screenLabel = null;
		_archChipPanel = null;
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
		// OffsetBottom not loaded — auto-calculated by FitPanelHeight
		_panel.AddThemeStyleboxOverride("panel", _sbPanel);
		_panel.MouseFilter = Control.MouseFilterEnum.Stop;
		VBoxContainer vBoxContainer = new VBoxContainer();
		vBoxContainer.AddThemeConstantOverride("separation", 10);
		_panel.AddChild(vBoxContainer, forceReadableName: false, Node.InternalMode.Disabled);

		// Title bar area (draggable)
		VBoxContainer titleBar = new VBoxContainer();
		titleBar.MouseFilter = Control.MouseFilterEnum.Stop;
		titleBar.MouseDefaultCursorShape = Control.CursorShape.Drag;
		titleBar.GuiInput += (InputEvent ev) => OnTitleBarInput(ev);
		vBoxContainer.AddChild(titleBar, forceReadableName: false, Node.InternalMode.Disabled);

		HBoxContainer titleRow = new HBoxContainer();
		titleRow.MouseFilter = Control.MouseFilterEnum.Ignore;
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
			{
				ToggleCollapsed();
			}
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

		_archChipPanel = new PanelContainer();
		_archChipPanel.AddThemeStyleboxOverride("panel", _sbChip);
		_archChipVBox = new VBoxContainer();
		_archChipVBox.AddThemeConstantOverride("separation", 3);
		_archetypeLabel = new Label();
		_archetypeLabel.Text = "ANALYZING DECK...";
		ApplyFont(_archetypeLabel, _fontBold);
		_archetypeLabel.AddThemeFontSizeOverride("font_size", 15);
		_archetypeLabel.AddThemeColorOverride("font_color", ClrCream);
		_archetypeLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_archChipVBox.AddChild(_archetypeLabel, forceReadableName: false, Node.InternalMode.Disabled);
		_archChipPanel.AddChild(_archChipVBox, forceReadableName: false, Node.InternalMode.Disabled);
		vBoxContainer.AddChild(_archChipPanel, forceReadableName: false, Node.InternalMode.Disabled);
		// Deck composition visualization container (Feature 2)
		_deckVizContainer = new VBoxContainer();
		_deckVizContainer.AddThemeConstantOverride("separation", 4);
		vBoxContainer.AddChild(_deckVizContainer, forceReadableName: false, Node.InternalMode.Disabled);
		// Content container — no scroll, panel auto-expands to fit
		_content = new VBoxContainer();
		_content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_content.AddThemeConstantOverride("separation", 6);
		vBoxContainer.AddChild(_content, forceReadableName: false, Node.InternalMode.Disabled);
		_layer.AddChild(_panel, forceReadableName: false, Node.InternalMode.Disabled);
		OverlayInputHandler node = new OverlayInputHandler(this);
		_layer.AddChild(node, forceReadableName: false, Node.InternalMode.Disabled);
		_panel.Visible = _visible;
		// Apply collapsed state
		if (_collapsed)
		{
			_content.Visible = false;
			_archChipPanel.Visible = false;
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

		sceneTree.Root.CallDeferred("add_child", _layer);
		Plugin.Log("Overlay built and attached to scene tree.");
	}

	private void OnTitleBarInput(InputEvent ev)
	{
		if (ev is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex == MouseButton.Left)
			{
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
		ulong elapsed = Time.GetTicksMsec() - _lastUpdateTick;
		// If we have card/relic advice showing for 8+ seconds, check if screen is still valid
		if (elapsed > 8000 && (_currentCards != null || _currentRelics != null))
		{
			// Check if the game's screen node that generated this advice is still active
			// by looking for known screen type nodes in the scene tree
			try
			{
				SceneTree tree = Engine.GetMainLoop() as SceneTree;
				if (tree?.Root == null) return;
				bool hasCardScreen = HasNodeOfType(tree.Root, "NCardRewardSelectionScreen", 4);
				bool hasRelicScreen = HasNodeOfType(tree.Root, "NChooseARelicSelection", 4);
				bool hasShopScreen = HasNodeOfType(tree.Root, "NMerchantInventory", 4);
				bool isCardAdvice = _currentScreen == "CARD REWARD" || _currentScreen == "CARD REMOVAL" || _currentScreen == "CARD UPGRADE";
				bool isRelicAdvice = _currentScreen == "RELIC REWARD";
				bool isShopAdvice = _currentScreen == "MERCHANT SHOP";

				bool screenGone = false;
				if (isCardAdvice && !hasCardScreen) screenGone = true;
				if (isRelicAdvice && !hasRelicScreen) screenGone = true;
				if (isShopAdvice && !hasShopScreen) screenGone = true;

				if (screenGone)
				{
					Plugin.Log($"Stale screen detected: {_currentScreen} no longer active — clearing overlay");
					Clear();
				}
			}
			catch { }
		}
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

	public void ShowCardAdvice(List<ScoredCard> cards, DeckAnalysis deckAnalysis = null, string character = null)
	{
		_currentCards = cards;
		_currentRelics = null;
		_currentDeckAnalysis = deckAnalysis;
		_currentCharacter = character;
		_currentScreen = "CARD REWARD";
		_mapAdvice = null;
		MarkUpdated();
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

	public void ShowCombatAdvice(DeckAnalysis deckAnalysis, int currentHP, int maxHP, int actNumber, int floor, GameState gameState = null)
	{
		_currentCards = null;
		_currentRelics = null;
		_currentDeckAnalysis = deckAnalysis;
		_currentScreen = "COMBAT";
		_currentFloor = floor;
		_currentGameState = gameState;
		_mapAdvice = GenerateCombatAdvice(deckAnalysis, currentHP, maxHP, actNumber, floor);
		MarkUpdated();
		Rebuild();
	}

	public void ShowEventAdvice(DeckAnalysis deckAnalysis, int currentHP, int maxHP, int gold, int actNumber, int floor)
	{
		_currentCards = null;
		_currentRelics = null;
		_currentDeckAnalysis = deckAnalysis;
		_currentScreen = "EVENT";
		_currentFloor = floor;
		_currentGameState = null;
		_mapAdvice = GenerateEventAdvice(deckAnalysis, currentHP, maxHP, gold, actNumber, floor);
		MarkUpdated();
		Rebuild();
	}

	public void ShowMapAdvice(DeckAnalysis deckAnalysis, int currentHP, int maxHP, int gold, int actNumber, int floor)
	{
		_currentFloor = floor;
		_currentGameState = null;
		_currentCards = null;
		_currentRelics = null;
		_currentDeckAnalysis = deckAnalysis;
		_currentScreen = "MAP";
		_mapAdvice = GenerateMapAdvice(deckAnalysis, currentHP, maxHP, gold, actNumber, floor);
		MarkUpdated();
		Rebuild();
	}

	public void ShowShopAdvice(List<ScoredCard> cards, List<ScoredRelic> relics, DeckAnalysis deckAnalysis = null, string character = null)
	{
		_currentCards = cards;
		_currentRelics = relics;
		_currentDeckAnalysis = deckAnalysis;
		_currentCharacter = character;
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

	public void ToggleVisible()
	{
		if (EnsureOverlay())
		{
			_visible = !_visible;
			_panel.Visible = _visible;
			_settings.Visible = _visible;
			_settings.Save();
			Plugin.Log("Overlay " + (_visible ? "shown" : "hidden"));
		}
	}

	public void ToggleTooltips()
	{
		_showTooltips = !_showTooltips;
		_settings.ShowTooltips = _showTooltips;
		_settings.Save();
		Rebuild();
		Plugin.Log("Tooltips " + (_showTooltips ? "shown" : "hidden"));
	}

	public void ToggleInGameBadges()
	{
		_showInGameBadges = !_showInGameBadges;
		_settings.ShowInGameBadges = _showInGameBadges;
		_settings.Save();
		if (!_showInGameBadges)
		{
			// Clean up existing badges
			SceneTree tree = Engine.GetMainLoop() as SceneTree;
			if (tree?.Root != null)
				CleanupInjectedBadges(tree.Root);
		}
		Plugin.Log("In-game badges " + (_showInGameBadges ? "shown" : "hidden"));
	}

	public void HandleInput(InputEvent ev)
	{
		if (ev is InputEventKey { Pressed: not false, Echo: false } inputEventKey)
		{
			bool ctrl = inputEventKey.CtrlPressed;
			bool alt = inputEventKey.AltPressed;
			bool shift = inputEventKey.ShiftPressed;
			Key key = inputEventKey.Keycode;
			// F-keys, Ctrl+number, or Alt+number — multiple combos for keyboard compatibility
			if ((key == Key.F7 && shift) || (ctrl && key == Key.Key7 && shift) || (alt && key == Key.Key7 && shift))
			{
				CycleOpacity();
			}
			else if (key == Key.F7 || (ctrl && key == Key.Key7) || (alt && key == Key.Key7))
			{
				ToggleVisible();
			}
			else if (key == Key.F8 || (ctrl && key == Key.Key8) || (alt && key == Key.Key8))
			{
				ToggleTooltips();
			}
			else if (key == Key.F9 || (ctrl && key == Key.Key9) || (alt && key == Key.Key9))
			{
				ToggleInGameBadges();
			}
			else if (key == Key.F10 || (ctrl && key == Key.Key0) || (alt && key == Key.Key0))
			{
				ToggleHistory();
			}
			else if (key == Key.F11 || (ctrl && key == Key.Minus) || (alt && key == Key.Minus))
			{
				ToggleCollapsed();
			}
			// Debug: log unrecognized key presses with modifiers to help diagnose
			else if ((ctrl || alt) && !shift)
			{
				Plugin.Log($"Key press: {key} ctrl={ctrl} alt={alt} shift={shift}");
			}
		}
	}

	private void Rebuild()
	{
		if (!EnsureOverlay())
		{
			return;
		}
		bool screenChanged = _currentScreen != _previousScreen;
		// Clean up any stale in-game badges when switching screens
		if (screenChanged)
		{
			try
			{
				SceneTree tree = Engine.GetMainLoop() as SceneTree;
				if (tree?.Root != null)
					CleanupInjectedBadges(tree.Root);
			}
			catch { }
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
		if (_archChipPanel != null && GodotObject.IsInstanceValid(_archChipPanel))
			_archChipPanel.Visible = false;
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
		bool hasCards = _currentCards != null && _currentCards.Count > 0;
		bool hasRelics = _currentRelics != null && _currentRelics.Count > 0;
		bool isRemoval = _currentScreen == "CARD REMOVAL";
		bool isUpgrade = _currentScreen == "CARD UPGRADE";
		// Upgrade screen: show ranked upgrade priorities
		if (isUpgrade && _currentGameState != null && _currentDeckAnalysis != null)
		{
			AddSectionHeader("BEST CARDS TO UPGRADE");
			string character = _currentCharacter ?? _currentGameState.Character ?? "unknown";
			var priorities = GetUpgradePriorities(_currentGameState, _currentDeckAnalysis, character);
			if (priorities.Count > 0)
			{
				int rank = 1;
				foreach (var (icon, text, color) in priorities)
				{
					PanelContainer upgPanel = new PanelContainer();
					StyleBoxFlat upgStyle = new StyleBoxFlat();
					upgStyle.BgColor = rank == 1 ? new Color(0.831f, 0.714f, 0.357f, 0.1f) : new Color(0.06f, 0.08f, 0.14f, 0.6f);
					upgStyle.CornerRadiusTopRight = 12;
					upgStyle.CornerRadiusBottomRight = 12;
					upgStyle.BorderWidthLeft = rank == 1 ? 4 : 3;
					upgStyle.BorderColor = rank == 1 ? ClrAccent : new Color(color, 0.6f);
					upgStyle.ContentMarginLeft = 14f;
					upgStyle.ContentMarginRight = 10f;
					upgStyle.ContentMarginTop = 8f;
					upgStyle.ContentMarginBottom = 8f;
					upgPanel.AddThemeStyleboxOverride("panel", upgStyle);
					Label upgLbl = new Label();
					string prefix = rank == 1 ? "\u2605 BEST: " : $"#{rank} ";
					upgLbl.Text = prefix + text;
					ApplyFont(upgLbl, rank == 1 ? _fontBold : _fontBody);
					upgLbl.AddThemeColorOverride("font_color", color);
					upgLbl.AddThemeFontSizeOverride("font_size", rank == 1 ? 17 : 15);
					upgLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
					upgPanel.AddChild(upgLbl, forceReadableName: false, Node.InternalMode.Disabled);
					_content.AddChild(upgPanel, forceReadableName: false, Node.InternalMode.Disabled);
					rank++;
				}
			}
			else
			{
				Label noUpg = new Label();
				noUpg.Text = "No significant upgrade targets found.";
				ApplyFont(noUpg, _fontBody);
				noUpg.AddThemeColorOverride("font_color", ClrSub);
				noUpg.AddThemeFontSizeOverride("font_size", 15);
				_content.AddChild(noUpg, forceReadableName: false, Node.InternalMode.Disabled);
			}
		}
		else if (hasCards)
		{
			bool isShop = _currentScreen == "MERCHANT SHOP";
			AddSectionHeader(isRemoval ? "BEST CARDS TO REMOVE" : isShop ? "BEST CARDS IN SHOP" : "CARD ANALYSIS");
			// Shop: only show top picks (grade B+ or best 3, whichever is more)
			var cardsToShow = isShop
				? _currentCards.Where(c => c.IsBestPick || c.FinalGrade >= TierGrade.B).Take(3).ToList()
				: _currentCards.ToList();
			if (isShop && cardsToShow.Count == 0 && _currentCards.Count > 0)
				cardsToShow = _currentCards.Take(3).ToList();
			foreach (ScoredCard currentCard in cardsToShow)
			{
				AddCardEntry(currentCard);
			}
			int skippedCards = _currentCards.Count - cardsToShow.Count;
			if (isShop && skippedCards > 0)
			{
				Label skipLbl = new Label();
				skipLbl.Text = $"  + {skippedCards} lower-rated cards";
				ApplyFont(skipLbl, _fontBody);
				skipLbl.AddThemeColorOverride("font_color", ClrSub);
				skipLbl.AddThemeFontSizeOverride("font_size", 13);
				_content.AddChild(skipLbl, forceReadableName: false, Node.InternalMode.Disabled);
			}
			float skipThreshold = 1.5f;
			if (_currentDeckAnalysis != null && _currentDeckAnalysis.TotalCards <= 15)
			{
				skipThreshold = 2.0f;
			}
			if (!isRemoval && _currentCards.All((ScoredCard c) => c.FinalScore < skipThreshold))
			{
				string skipMsg = skipThreshold > 1.5f
					? "All offerings are weak — thin deck, be very selective."
					: "All offerings are weak — keep your deck lean.";
				AddSkipEntry("SKIP CARDS", skipMsg);
			}
		}
		if (hasRelics)
		{
			bool isShop = _currentScreen == "MERCHANT SHOP";
			AddSectionHeader(isShop ? "BEST RELICS IN SHOP" : "RELIC ANALYSIS");
			var relicsToShow = isShop
				? _currentRelics.Where(r => r.IsBestPick || r.FinalGrade >= TierGrade.B).Take(3).ToList()
				: _currentRelics.ToList();
			if (isShop && relicsToShow.Count == 0 && _currentRelics.Count > 0)
				relicsToShow = _currentRelics.Take(2).ToList();
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
			foreach (var (icon, text, color) in _mapAdvice)
			{
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
		else if (!hasCards && !hasRelics)
		{
			Label label = new Label();
			label.Text = "Ready for decision screen.\n(Cards, Relics, or Shop)";
			label.HorizontalAlignment = HorizontalAlignment.Center;
			ApplyFont(label, _fontBody);
			label.AddThemeColorOverride("font_color", ClrSub);
			label.AddThemeFontSizeOverride("font_size", 17);
			_content.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
		}
		// Deck breakdown — always available as collapsible section
		if (_currentDeckAnalysis != null)
		{
			var deckSection = AddCollapsibleSection("DECK BREAKDOWN", "deck", ref _showDeckBreakdown);
			if (deckSection != null)
				AddInlineDeckVizTo(deckSection, _currentDeckAnalysis);
		}
		// Feature 5: Draw probability panel in combat
		if (_currentScreen == "COMBAT" && _currentGameState != null && _currentGameState.DrawPile.Count > 0)
		{
			var drawSection = AddCollapsibleSection("DRAW CHANCES", "draw", ref _showDrawProb);
			if (drawSection != null)
				AddDrawProbabilityTo(drawSection, _currentGameState);
		}
		// Feature 1: Decision history log
		if (_showHistory)
		{
			AddDecisionHistory();
		}
		// On MAP screen: always show last 3 decisions as mini-section
		else if (_currentScreen == "MAP")
		{
			var histSection = AddCollapsibleSection("RECENT CHOICES", "history", ref _showHistory);
			if (histSection != null)
				AddRecentDecisionsTo(histSection, 3);
		}
		// A3: Archetype trajectory on MAP screen
		if (_currentScreen == "MAP")
		{
			AddArchetypeTrajectory();
		}
		// Hotkey hints — always visible at the bottom
		{
			HSeparator hkSep = new HSeparator();
			hkSep.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(ClrBorder, 0.3f), Thickness = 1 });
			_content.AddChild(hkSep, forceReadableName: false, Node.InternalMode.Disabled);
			Label hkLabel = new Label();
			hkLabel.Text = "Alt+7 Toggle  |  Alt+8 Tooltips  |  Alt+9 Badges\nAlt+0 History  |  Alt+- Minimize  (or F7-F11)";
			hkLabel.HorizontalAlignment = HorizontalAlignment.Center;
			ApplyFont(hkLabel, _fontBody);
			hkLabel.AddThemeColorOverride("font_color", new Color(ClrSub, 0.7f));
			hkLabel.AddThemeFontSizeOverride("font_size", 12);
			_content.AddChild(hkLabel, forceReadableName: false, Node.InternalMode.Disabled);
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
		Vector2 minSize = _panel.GetCombinedMinimumSize();
		float height = Math.Max(minSize.Y, 40f);
		_panel.OffsetBottom = _panel.OffsetTop + height;
		// Force panel size to match content — prevents stale dead space
		_panel.Size = new Vector2(_panel.Size.X, height);
	}

	// Archetype bar colors — distinct per slot for easy visual parsing
	private static readonly Color[] ArchColors = {
		new Color(0.4f, 0.8f, 0.95f),  // cyan
		new Color(0.95f, 0.6f, 0.3f),  // orange
		new Color(0.7f, 0.5f, 0.95f),  // purple
		new Color(0.3f, 0.9f, 0.5f),   // green
	};

	private void UpdateArchetypeChip()
	{
		if (_archetypeLabel == null || !GodotObject.IsInstanceValid(_archetypeLabel))
		{
			return;
		}
		// Clear previous archetype rows (keep _archetypeLabel as first child)
		if (_archChipVBox != null && GodotObject.IsInstanceValid(_archChipVBox))
		{
			var children = _archChipVBox.GetChildren().ToArray();
			foreach (Node child in children)
			{
				if (child != _archetypeLabel)
				{
					_archChipVBox.RemoveChild(child);
					child.QueueFree();
				}
			}
		}
		string deckLabel = _currentDeckAnalysis != null ? $"{_currentDeckAnalysis.TotalCards} cards" : "";
		if (_currentDeckAnalysis == null || _currentDeckAnalysis.DetectedArchetypes.Count == 0)
		{
			_archetypeLabel.Text = deckLabel.Length > 0 ? $"YOUR DECK ({deckLabel})" : "YOUR DECK";
			// "No clear focus" sub-label
			if (_archChipVBox != null && GodotObject.IsInstanceValid(_archChipVBox) && deckLabel.Length > 0)
			{
				Label noFocus = new Label();
				noFocus.Text = "No clear archetype focus";
				ApplyFont(noFocus, _fontBody);
				noFocus.AddThemeColorOverride("font_color", ClrSub);
				noFocus.AddThemeFontSizeOverride("font_size", 13);
				_archChipVBox.AddChild(noFocus, forceReadableName: false, Node.InternalMode.Disabled);
			}
			return;
		}
		_archetypeLabel.Text = $"YOUR DECK ({deckLabel})";
		// Build colored archetype rows
		if (_archChipVBox == null || !GodotObject.IsInstanceValid(_archChipVBox))
			return;
		float totalStrength = _currentDeckAnalysis.DetectedArchetypes.Sum(a => a.Strength);
		if (totalStrength <= 0) totalStrength = 1f;
		int colorIdx = 0;
		foreach (ArchetypeMatch arch in _currentDeckAnalysis.DetectedArchetypes)
		{
			int pct = (int)(arch.Strength / totalStrength * 100f);
			Color archColor = ArchColors[colorIdx % ArchColors.Length];
			// Row: [colored bar] Name  pct%
			HBoxContainer row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);
			// Percentage bar (proportional width)
			ColorRect bar = new ColorRect();
			bar.Color = new Color(archColor, 0.7f);
			bar.CustomMinimumSize = new Vector2(Math.Max(pct * 0.8f, 4f), 12f);
			row.AddChild(bar, forceReadableName: false, Node.InternalMode.Disabled);
			// Archetype name + percentage
			Label archLbl = new Label();
			archLbl.Text = $"{arch.Archetype.DisplayName}  {pct}%";
			ApplyFont(archLbl, _fontBold);
			archLbl.AddThemeColorOverride("font_color", archColor);
			archLbl.AddThemeFontSizeOverride("font_size", 14);
			row.AddChild(archLbl, forceReadableName: false, Node.InternalMode.Disabled);
			// Core card count hint
			if (arch.CoreCount > 0)
			{
				Label coreLbl = new Label();
				coreLbl.Text = $"({arch.CoreCount} core)";
				ApplyFont(coreLbl, _fontBody);
				coreLbl.AddThemeColorOverride("font_color", ClrSub);
				coreLbl.AddThemeFontSizeOverride("font_size", 12);
				row.AddChild(coreLbl, forceReadableName: false, Node.InternalMode.Disabled);
			}
			_archChipVBox.AddChild(row, forceReadableName: false, Node.InternalMode.Disabled);
			colorIdx++;
		}
	}

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
			case "draw": _showDrawProb = value; _settings.ShowDrawProbability = value; break;
		}
		_settings.Save();
	}

	private void AddCardEntry(ScoredCard card)
	{
		PanelContainer panelContainer = CreateEntryPanel(card.IsBestPick, card.FinalGrade);
		HBoxContainer hBoxContainer = new HBoxContainer();
		hBoxContainer.AddThemeConstantOverride("separation", 8);
		panelContainer.AddChild(hBoxContainer, forceReadableName: false, Node.InternalMode.Disabled);
		CenterContainer badge = CreateBadge(card.FinalGrade);
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
		// Card portrait thumbnail (rounded corners)
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
		// Card name
		Label label = new Label();
		string text = card.Name ?? card.Id;
		string upgradeTag = card.Upgraded ? " +" : "";
		label.Text = (card.IsBestPick ? "\u2605 " : "") + text + upgradeTag;
		ApplyFont(label, _fontBold);
		label.AddThemeColorOverride("font_color", card.IsBestPick ? ClrAccent : ClrCream);
		label.AddThemeFontSizeOverride("font_size", 17);
		label.AddThemeConstantOverride("outline_size", 2);
		label.AddThemeColorOverride("font_outline_color", ClrOutline);
		label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		vBoxContainer.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
		// Compact summary: "Attack \u2022 2 energy" + optional price + one-liner
		string typeLower = card.Type?.ToLowerInvariant() ?? "";
		string costStr = card.Cost == 0 ? "0 cost" : card.Cost == 1 ? "1 energy" : $"{card.Cost} energy";
		string priceStr = "";
		if (card.Price > 0)
		{
			priceStr = _goldIcon != null ? "" : $" \u2022 {card.Price}g";
		}
		// Build a one-line reason summary
		string oneLiner = BuildOneLiner(card.SynergyReasons, card.AntiSynergyReasons, card.BaseTier, card.FinalGrade);
		Label metaLbl = new Label();
		metaLbl.Text = $"{typeLower} \u2022 {costStr}{priceStr}";
		ApplyFont(metaLbl, _fontBody);
		metaLbl.AddThemeColorOverride("font_color", card.Cost >= 3 ? ClrExpensive : ClrSub);
		metaLbl.AddThemeFontSizeOverride("font_size", 15);
		metaLbl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		vBoxContainer.AddChild(metaLbl, forceReadableName: false, Node.InternalMode.Disabled);
		// One-liner on its own line so it never gets cut off
		if (oneLiner.Length > 0)
		{
			Label reasonLbl = new Label();
			reasonLbl.Text = oneLiner;
			ApplyFont(reasonLbl, _fontBody);
			bool isNegative = card.AntiSynergyReasons != null && card.AntiSynergyReasons.Count > 0;
			reasonLbl.AddThemeColorOverride("font_color", isNegative ? ClrNegative : ClrPositive);
			reasonLbl.AddThemeFontSizeOverride("font_size", 15);
			reasonLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			vBoxContainer.AddChild(reasonLbl, forceReadableName: false, Node.InternalMode.Disabled);
		}
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
		CenterContainer relicBadge = CreateBadge(relic.FinalGrade);
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
		// Relic icon thumbnail
		Texture2D relicIcon = GetRelicIcon(relic.Id);
		if (relicIcon != null)
		{
			TextureRect thumb = new TextureRect();
			thumb.Texture = relicIcon;
			thumb.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			thumb.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
			thumb.CustomMinimumSize = new Vector2(36f, 36f);
			hBoxContainer.AddChild(thumb, forceReadableName: false, Node.InternalMode.Disabled);
		}
		VBoxContainer vBoxContainer = new VBoxContainer();
		vBoxContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		vBoxContainer.AddThemeConstantOverride("separation", 0);
		hBoxContainer.AddChild(vBoxContainer, forceReadableName: false, Node.InternalMode.Disabled);
		// Relic name (always prettify from ID — game Title can be a localization key)
		Label label = new Label();
		string text = PrettifyId(relic.Id);
		label.Text = (relic.IsBestPick ? "\u2605 " : "") + text;
		ApplyFont(label, _fontBold);
		label.AddThemeColorOverride("font_color", relic.IsBestPick ? ClrAccent : ClrCream);
		label.AddThemeFontSizeOverride("font_size", 17);
		label.AddThemeConstantOverride("outline_size", 2);
		label.AddThemeColorOverride("font_outline_color", ClrOutline);
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		vBoxContainer.AddChild(label, forceReadableName: false, Node.InternalMode.Disabled);
		// Compact summary with relic tenure
		string rarityLower = relic.Rarity?.ToLowerInvariant() ?? "";
		string oneLiner = BuildOneLiner(relic.SynergyReasons, relic.AntiSynergyReasons, relic.BaseTier, relic.FinalGrade);
		string tenureStr = "";
		if (Plugin.RunTracker != null && _currentFloor > 0)
		{
			int tenure = Plugin.RunTracker.GetRelicTenure(relic.Id, _currentFloor);
			if (tenure > 0) tenureStr = $" \u2022 held {tenure} floors";
		}
		Label metaLbl = new Label();
		metaLbl.Text = rarityLower + tenureStr;
		ApplyFont(metaLbl, _fontBody);
		metaLbl.AddThemeColorOverride("font_color", ClrSub);
		metaLbl.AddThemeFontSizeOverride("font_size", 15);
		metaLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		vBoxContainer.AddChild(metaLbl, forceReadableName: false, Node.InternalMode.Disabled);
		// One-liner on its own line (prevents cutoff)
		if (oneLiner.Length > 0)
		{
			Label oneLinerLbl = new Label();
			oneLinerLbl.Text = oneLiner;
			ApplyFont(oneLinerLbl, _fontBody);
			bool isNegative = oneLiner.Contains("doesn't") || oneLiner.Contains("conflict") || oneLiner.Contains("weaker") || oneLiner.Contains("costly") || oneLiner.Contains("redundant");
			oneLinerLbl.AddThemeColorOverride("font_color", isNegative ? ClrNegative : ClrPositive);
			oneLinerLbl.AddThemeFontSizeOverride("font_size", 14);
			oneLinerLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			vBoxContainer.AddChild(oneLinerLbl, forceReadableName: false, Node.InternalMode.Disabled);
		}
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

		// Scoring breakdown line — show when there are adjustments OR when score source is notable
		bool hasAdjustments = synergyDelta != 0f || floorAdjust != 0f || deckSizeAdjust != 0f || upgradeAdjust != 0f;
		bool notableSource = scoreSource == "adaptive" || scoreSource == "default";
		if (baseScore > 0f && (hasAdjustments || notableSource))
		{
			string srcTag = scoreSource == "adaptive" ? " [learned]" : scoreSource == "default" ? " [no data]" : "";
			string breakdown = $"Score: {TierEngine.ScoreToGrade(baseScore)}({baseScore:F1}){srcTag}";
			if (synergyDelta != 0f) breakdown += $" {synergyDelta:+0.0;-0.0} syn";
			if (floorAdjust != 0f) breakdown += $" {floorAdjust:+0.0;-0.0} floor";
			if (deckSizeAdjust != 0f) breakdown += $" {deckSizeAdjust:+0.0;-0.0} size";
			if (upgradeAdjust != 0f) breakdown += $" {upgradeAdjust:+0.0;-0.0} upg";
			breakdown += $" = {finalScore:F1}";
			Label breakdownLbl = new Label();
			breakdownLbl.Text = breakdown;
			ApplyFont(breakdownLbl, _fontBody);
			breakdownLbl.AddThemeColorOverride("font_color", ClrAqua);
			breakdownLbl.AddThemeFontSizeOverride("font_size", 15);
			breakdownLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			parent.AddChild(breakdownLbl, forceReadableName: false, Node.InternalMode.Disabled);
			hasContent = true;
		}

		if (synergies != null && synergies.Count > 0)
		{
			foreach (string reason in synergies.Take(3))
			{
				Label lbl = new Label();
				lbl.Text = "\u2714 " + reason;
				ApplyFont(lbl, _fontBody);
				lbl.AddThemeColorOverride("font_color", ClrPositive);
				lbl.AddThemeFontSizeOverride("font_size", 15);
				lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				parent.AddChild(lbl, forceReadableName: false, Node.InternalMode.Disabled);
				hasContent = true;
			}
		}

		if (antiSynergies != null && antiSynergies.Count > 0)
		{
			foreach (string reason in antiSynergies.Take(2))
			{
				Label lbl = new Label();
				lbl.Text = "\u2718 " + reason;
				ApplyFont(lbl, _fontBody);
				lbl.AddThemeColorOverride("font_color", ClrNegative);
				lbl.AddThemeFontSizeOverride("font_size", 15);
				lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				parent.AddChild(lbl, forceReadableName: false, Node.InternalMode.Disabled);
				hasContent = true;
			}
		}

		if (!string.IsNullOrEmpty(notes))
		{
			Label lbl = new Label();
			lbl.Text = (hasContent ? "" : "") + notes;
			ApplyFont(lbl, _fontBody);
			lbl.AddThemeColorOverride("font_color", ClrNotes);
			lbl.AddThemeFontSizeOverride("font_size", 15);
			lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			parent.AddChild(lbl, forceReadableName: false, Node.InternalMode.Disabled);
		}
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
		if (_archChipPanel != null && GodotObject.IsInstanceValid(_archChipPanel))
			_archChipPanel.Visible = !_collapsed;
		if (_deckVizContainer != null && GodotObject.IsInstanceValid(_deckVizContainer))
			_deckVizContainer.Visible = !_collapsed;
		if (_titleSep != null && GodotObject.IsInstanceValid(_titleSep))
			_titleSep.Visible = !_collapsed;
		if (_screenLabel != null && GodotObject.IsInstanceValid(_screenLabel))
			_screenLabel.Visible = !_collapsed;
		if (_winRateLabel != null && GodotObject.IsInstanceValid(_winRateLabel))
			_winRateLabel.Visible = !_collapsed;
		// Update compact toggle arrow
		if (_compactToggle != null && GodotObject.IsInstanceValid(_compactToggle))
			_compactToggle.Text = _collapsed ? "\u25BC" : "\u25B2";
		ResizePanelToContent();
		Plugin.Log("Overlay " + (_collapsed ? "collapsed" : "expanded"));
	}

	private string GetCollapsedSummary()
	{
		string gradeStr = "";
		if (_currentCards != null && _currentCards.Count > 0)
		{
			var best = _currentCards.FirstOrDefault(c => c.IsBestPick) ?? _currentCards[0];
			gradeStr = $"\u2605{best.FinalGrade}";
		}
		else if (_currentRelics != null && _currentRelics.Count > 0)
		{
			var best = _currentRelics.FirstOrDefault(r => r.IsBestPick) ?? _currentRelics[0];
			gradeStr = $"\u2605{best.FinalGrade}";
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
		AddSectionHeader("DECISION LOG (F10)");
		int count = Math.Min(events.Count, 10);
		for (int i = events.Count - 1; i >= events.Count - count; i--)
		{
			AddDecisionEntry(events[i]);
		}
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

	// === Feature 5: Draw probability panel ===

	private void AddDrawProbabilityPanel(GameState gameState)
	{
		if (gameState.DrawPile == null || gameState.DrawPile.Count == 0) return;
		string character = _currentCharacter ?? gameState.Character ?? "unknown";
		AddSectionHeader("DRAW CHANCES");
		AddDrawProbabilityTo(_content, gameState);
	}

	private void AddDrawProbabilityTo(VBoxContainer target, GameState gameState)
	{
		if (gameState.DrawPile == null || gameState.DrawPile.Count == 0) return;
		string character = _currentCharacter ?? gameState.Character ?? "unknown";

		// Count unique cards in draw pile
		var cardCounts = new Dictionary<string, int>();
		foreach (var card in gameState.DrawPile)
		{
			string key = card.Id;
			if (cardCounts.ContainsKey(key)) cardCounts[key]++;
			else cardCounts[key] = 1;
		}
		int totalDraw = gameState.DrawPile.Count;

		// Sort by grade (best first), then by probability
		var sorted = cardCounts
			.Select(kvp => new { Id = kvp.Key, Count = kvp.Value, Grade = LookupGrade(kvp.Key, character) })
			.OrderByDescending(x => (int)x.Grade)
			.ThenByDescending(x => x.Count)
			.Take(5)
			.ToList();

		foreach (var entry in sorted)
		{
			float probability = (float)entry.Count / totalDraw * 100f;
			Color badgeColor = TierBadge.GetGodotColor(entry.Grade);

			HBoxContainer row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);

			// Grade badge (small)
			Label gradeLbl = new Label();
			gradeLbl.Text = entry.Grade.ToString();
			ApplyFont(gradeLbl, _fontBold);
			gradeLbl.AddThemeColorOverride("font_color", badgeColor);
			gradeLbl.AddThemeFontSizeOverride("font_size", 17);
			gradeLbl.CustomMinimumSize = new Vector2(14f, 0);
			row.AddChild(gradeLbl, forceReadableName: false, Node.InternalMode.Disabled);

			// Card name
			Label nameLbl = new Label();
			nameLbl.Text = PrettifyId(entry.Id);
			ApplyFont(nameLbl, _fontBody);
			nameLbl.AddThemeColorOverride("font_color", ClrCream);
			nameLbl.AddThemeFontSizeOverride("font_size", 17);
			nameLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			nameLbl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
			row.AddChild(nameLbl, forceReadableName: false, Node.InternalMode.Disabled);

			// Probability bar
			ColorRect probBar = new ColorRect();
			probBar.Color = new Color(badgeColor, 0.6f);
			probBar.CustomMinimumSize = new Vector2(probability * 0.8f, 10f);
			row.AddChild(probBar, forceReadableName: false, Node.InternalMode.Disabled);

			// Percentage
			Label pctLbl = new Label();
			pctLbl.Text = $"{probability:F0}%";
			ApplyFont(pctLbl, _fontBody);
			pctLbl.AddThemeColorOverride("font_color", ClrSub);
			pctLbl.AddThemeFontSizeOverride("font_size", 17);
			pctLbl.CustomMinimumSize = new Vector2(30f, 0);
			pctLbl.HorizontalAlignment = HorizontalAlignment.Right;
			row.AddChild(pctLbl, forceReadableName: false, Node.InternalMode.Disabled);

			target.AddChild(row, forceReadableName: false, Node.InternalMode.Disabled);
		}
	}

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
		var candidates = new List<(float priority, string icon, string text, Color color)>();
		string primaryArchId = deck.DetectedArchetypes.Count > 0 ? deck.DetectedArchetypes[0].Archetype.Id : null;

		foreach (var card in gs.DeckCards)
		{
			if (card.Upgraded) continue;

			var tierEntry = Plugin.TierEngine?.GetCardTier(character, card.Id);
			if (tierEntry == null) continue;

			TierGrade baseGrade = TierEngine.ParseGrade(tierEntry.BaseTier);
			float baseScore = (float)baseGrade;

			// Calculate upgrade value based on card properties
			float upgBonus = 0.3f; // base upgrade value
			// Higher-tier cards benefit more from upgrades
			if (baseGrade >= TierGrade.A) upgBonus += 0.3f;
			else if (baseGrade >= TierGrade.B) upgBonus += 0.15f;
			// Archetype synergy bonus
			if (primaryArchId != null && tierEntry.Synergies != null && tierEntry.Synergies.Any(s => s.Contains(primaryArchId)))
				upgBonus += 0.3f;
			// Scaling cards (typically have synergy tags) benefit more
			if (tierEntry.Synergies != null && tierEntry.Synergies.Count > 2)
				upgBonus += 0.1f;

			float priority = upgBonus;
			string cardName = PrettifyId(card.Name ?? card.Id);
			// Show reason instead of misleading grade change
			string reason = "";
			if (primaryArchId != null && tierEntry.Synergies != null && tierEntry.Synergies.Any(s => s.Contains(primaryArchId)))
				reason = " — core card";
			else if (baseGrade >= TierGrade.A)
				reason = " — high impact";
			else if (baseGrade >= TierGrade.B)
				reason = " — solid pick";

			string text = $"\u2B06 {cardName} [{baseGrade}]{reason}";
			Color color = upgBonus >= 0.6f ? ClrPositive : upgBonus >= 0.4f ? ClrAqua : ClrCream;
			candidates.Add((priority, "\u2022", text, color));
		}

		return candidates
			.OrderByDescending(c => c.priority)
			.Take(5)
			.Select(c => (c.icon, c.text, c.color))
			.ToList();
	}

	private List<(string icon, string text, Color color)> GenerateCombatAdvice(DeckAnalysis deck, int hp, int maxHP, int act, int floor)
	{
		var advice = new List<(string, string, Color)>();
		float hpRatio = maxHP > 0 ? (float)hp / maxHP : 1f;
		int deckSize = deck?.TotalCards ?? 0;

		if (deck != null && deck.DetectedArchetypes.Count > 0)
		{
			var primary = deck.DetectedArchetypes[0];
			advice.Add(("\u2694", $"Strategy: {primary.Archetype.DisplayName} ({(int)(primary.Strength * 100)}%)", ClrAccent));
		}

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
		if (act >= 2 && hpRatio > 0.7f && isDefined)
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
		panel.MouseFilter = Control.MouseFilterEnum.Pass;
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

	private CenterContainer CreateBadge(TierGrade grade)
	{
		CenterContainer obj = new CenterContainer
		{
			CustomMinimumSize = new Vector2(34f, 34f)
		};
		PanelContainer panelContainer = new PanelContainer
		{
			CustomMinimumSize = new Vector2(30f, 30f)
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
		label.Text = grade.ToString();
		ApplyFont(label, _fontHeader);
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.AddThemeColorOverride("font_color", TierBadge.GetTextColor(grade));
		label.AddThemeFontSizeOverride("font_size", 20);
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
			"CARD REWARD" or "CARD REMOVAL" => ClrAccent,
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

	// === A3: Archetype trajectory + sparkline ===

	private void DrawSparkline(VBoxContainer parent, string archetypeId)
	{
		var history = Plugin.RunTracker?.GetArchetypeHistory();
		if (history == null || history.Count < 2)
			return;
		var dataPoints = new List<float>();
		foreach (var kvp in history.OrderBy(k => k.Key))
		{
			var match = kvp.Value.FirstOrDefault(a => a.archetypeId == archetypeId);
			dataPoints.Add(match.archetypeId != null ? match.strength : 0f);
		}
		if (dataPoints.Count < 2)
			return;
		HBoxContainer sparkRow = new HBoxContainer();
		sparkRow.AddThemeConstantOverride("separation", 1);
		float maxVal = dataPoints.Max();
		if (maxVal <= 0) maxVal = 1f;
		bool trending = dataPoints.Count >= 2 && dataPoints[dataPoints.Count - 1] >= dataPoints[dataPoints.Count - 2];
		Color barColor = trending ? ClrPositive : ClrNegative;
		// Show last 10 data points max
		var points = dataPoints.Skip(Math.Max(0, dataPoints.Count - 10)).ToList();
		foreach (float val in points)
		{
			ColorRect bar = new ColorRect();
			bar.Color = new Color(barColor, 0.7f);
			float h = val / maxVal * 14f;
			bar.CustomMinimumSize = new Vector2(2f, Math.Max(h, 1f));
			sparkRow.AddChild(bar, forceReadableName: false, Node.InternalMode.Disabled);
		}
		parent.AddChild(sparkRow, forceReadableName: false, Node.InternalMode.Disabled);
	}

	private void AddArchetypeTrajectory()
	{
		var history = Plugin.RunTracker?.GetArchetypeHistory();
		if (history == null || history.Count < 2)
			return;
		AddSectionHeader("ARCHETYPE TRAJECTORY");
		// Collect all archetypes that appeared
		var allArchetypes = new Dictionary<string, string>(); // id -> displayName
		foreach (var kvp in history)
		{
			foreach (var (archetypeId, _) in kvp.Value)
			{
				if (!allArchetypes.ContainsKey(archetypeId))
				{
					string displayName = archetypeId;
					if (_currentDeckAnalysis?.DetectedArchetypes != null)
					{
						var match = _currentDeckAnalysis.DetectedArchetypes.FirstOrDefault(a => a.Archetype.Id == archetypeId);
						if (match != null) displayName = match.Archetype.DisplayName;
					}
					allArchetypes[archetypeId] = displayName;
				}
			}
		}
		// Show max 3 archetypes, last 8 floors
		var floors = history.Keys.OrderBy(k => k).ToList();
		var recentFloors = floors.Skip(Math.Max(0, floors.Count - 8)).ToList();
		int shown = 0;
		foreach (var (archId, displayName) in allArchetypes)
		{
			if (shown >= 3) break;
			// Get first and last strength for trend
			float firstStr = 0f, lastStr = 0f;
			if (recentFloors.Count > 0 && history.TryGetValue(recentFloors[0], out var firstData))
			{
				var e = firstData.FirstOrDefault(a => a.archetypeId == archId);
				if (e.archetypeId != null) firstStr = e.strength;
			}
			if (recentFloors.Count > 0 && history.TryGetValue(recentFloors[recentFloors.Count - 1], out var lastData))
			{
				var e = lastData.FirstOrDefault(a => a.archetypeId == archId);
				if (e.archetypeId != null) lastStr = e.strength;
			}
			// Trend arrow
			string trend = lastStr > firstStr + 0.05f ? "\u2197" : lastStr < firstStr - 0.05f ? "\u2198" : "\u2192";
			Color trendColor = lastStr > firstStr + 0.05f ? ClrPositive : lastStr < firstStr - 0.05f ? ClrNegative : ClrSub;
			// Build row: "Strength ↗ 45% → 68%"
			HBoxContainer archRow = new HBoxContainer();
			archRow.AddThemeConstantOverride("separation", 6);
			Label nameLbl = new Label();
			nameLbl.Text = displayName;
			ApplyFont(nameLbl, _fontBold);
			nameLbl.AddThemeColorOverride("font_color", ClrCream);
			nameLbl.AddThemeFontSizeOverride("font_size", 15);
			nameLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			archRow.AddChild(nameLbl, forceReadableName: false, Node.InternalMode.Disabled);
			Label trendLbl = new Label();
			trendLbl.Text = $"{trend} {(int)(firstStr * 100)}% \u2192 {(int)(lastStr * 100)}%";
			ApplyFont(trendLbl, _fontBody);
			trendLbl.AddThemeColorOverride("font_color", trendColor);
			trendLbl.AddThemeFontSizeOverride("font_size", 15);
			archRow.AddChild(trendLbl, forceReadableName: false, Node.InternalMode.Disabled);
			_content.AddChild(archRow, forceReadableName: false, Node.InternalMode.Disabled);
			// Fixed-height progress bar showing current strength
			HBoxContainer barRow = new HBoxContainer();
			barRow.AddThemeConstantOverride("separation", 0);
			ColorRect filledBar = new ColorRect();
			filledBar.Color = new Color(trendColor, 0.6f);
			filledBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			filledBar.SizeFlagsStretchRatio = Math.Max(lastStr, 0.01f);
			filledBar.CustomMinimumSize = new Vector2(0, 4f);
			barRow.AddChild(filledBar, forceReadableName: false, Node.InternalMode.Disabled);
			ColorRect emptyBar = new ColorRect();
			emptyBar.Color = new Color(ClrSub, 0.2f);
			emptyBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			emptyBar.SizeFlagsStretchRatio = Math.Max(1f - lastStr, 0.01f);
			emptyBar.CustomMinimumSize = new Vector2(0, 4f);
			barRow.AddChild(emptyBar, forceReadableName: false, Node.InternalMode.Disabled);
			_content.AddChild(barRow, forceReadableName: false, Node.InternalMode.Disabled);
			shown++;
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
			string character = _currentCharacter ?? "unknown";
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
					// Skipped — controversial if S or A was available
					if (bestGrade >= TierGrade.A)
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

	private const string GradeBadgeGroup = "qcs_grade_badge";

	/// <summary>
	/// Inject grade badges directly onto the game's card reward screen nodes.
	/// Walks the scene tree from the screen node to find card-holder children.
	/// </summary>
	public void InjectCardGrades(Node screenNode, List<ScoredCard> scoredCards)
	{
		if (!_showInGameBadges || screenNode == null || !GodotObject.IsInstanceValid(screenNode) || scoredCards == null || scoredCards.Count == 0)
			return;
		try
		{
			// Clean up ALL previous badges globally (prevents stale badges from other screens)
			SceneTree tree = Engine.GetMainLoop() as SceneTree;
			if (tree?.Root != null)
				CleanupInjectedBadges(tree.Root);
			// Always log tree structure for card reward debugging
			LogNodeTree(screenNode, "CardReward", 0, 5);
			// Defer to next frame so the game's UI is fully laid out
			Callable.From(() => InjectCardGradesDeferred(screenNode, scoredCards)).CallDeferred();
		}
		catch (Exception ex)
		{
			Plugin.Log($"InjectCardGrades error: {ex.Message}");
		}
	}

	private void InjectCardGradesDeferred(Node screenNode, List<ScoredCard> scoredCards)
	{
		if (screenNode == null || !GodotObject.IsInstanceValid(screenNode))
			return;
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
			// Match holders to scored cards by index (same order as ShowScreen receives them)
			for (int i = 0; i < Math.Min(cardHolders.Count, scoredCards.Count); i++)
			{
				AttachGradeBadge(cardHolders[i], scoredCards[i].FinalGrade, scoredCards[i].IsBestPick);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log($"InjectCardGradesDeferred error: {ex.Message}");
		}
	}

	/// <summary>
	/// Inject grade badges onto relic reward screen nodes.
	/// </summary>
	public void InjectRelicGrades(Node screenNode, List<ScoredRelic> scoredRelics)
	{
		if (!_showInGameBadges || screenNode == null || !GodotObject.IsInstanceValid(screenNode) || scoredRelics == null || scoredRelics.Count == 0)
			return;
		try
		{
			SceneTree tree = Engine.GetMainLoop() as SceneTree;
			if (tree?.Root != null)
				CleanupInjectedBadges(tree.Root);
			LogNodeTree(screenNode, "RelicReward", 0, 5);
			Callable.From(() => InjectRelicGradesDeferred(screenNode, scoredRelics)).CallDeferred();
		}
		catch (Exception ex)
		{
			Plugin.Log($"InjectRelicGrades error: {ex.Message}");
		}
	}

	private void InjectRelicGradesDeferred(Node screenNode, List<ScoredRelic> scoredRelics)
	{
		if (screenNode == null || !GodotObject.IsInstanceValid(screenNode))
			return;
		try
		{
			var relicHolders = FindCardHolderNodes(screenNode, scoredRelics.Count);
			if (relicHolders == null || relicHolders.Count == 0)
			{
				Plugin.Log($"Could not find relic holder nodes in {screenNode.GetType().Name} (expected {scoredRelics.Count} relics)");
				return;
			}
			Plugin.Log($"Found {relicHolders.Count} relic holders — injecting grade badges");
			for (int i = 0; i < Math.Min(relicHolders.Count, scoredRelics.Count); i++)
			{
				AttachGradeBadge(relicHolders[i], scoredRelics[i].FinalGrade, scoredRelics[i].IsBestPick);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log($"InjectRelicGradesDeferred error: {ex.Message}");
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
	/// Attaches a floating grade badge to a game UI node (card or relic holder).
	/// Badge is positioned at the bottom-center of the node.
	/// </summary>
	private void AttachGradeBadge(Control targetNode, TierGrade grade, bool isBestPick)
	{
		if (targetNode == null || !GodotObject.IsInstanceValid(targetNode))
			return;

		Color badgeColor = TierBadge.GetGodotColor(grade);
		Color textColor = TierBadge.GetTextColor(grade);

		// Create badge panel
		PanelContainer badge = new PanelContainer();
		badge.AddToGroup(GradeBadgeGroup);
		badge.CustomMinimumSize = new Vector2(44f, 28f);

		StyleBoxFlat badgeStyle = new StyleBoxFlat();
		badgeStyle.BgColor = badgeColor;
		badgeStyle.CornerRadiusTopLeft = 6;
		badgeStyle.CornerRadiusTopRight = 6;
		badgeStyle.CornerRadiusBottomLeft = 6;
		badgeStyle.CornerRadiusBottomRight = 6;
		badgeStyle.BorderWidthTop = 2;
		badgeStyle.BorderWidthBottom = 2;
		badgeStyle.BorderWidthLeft = 2;
		badgeStyle.BorderWidthRight = 2;
		badgeStyle.BorderColor = isBestPick ? ClrAccent : badgeColor.Darkened(0.4f);
		if (isBestPick)
		{
			badgeStyle.ShadowSize = 8;
			badgeStyle.ShadowColor = new Color(ClrAccent, 0.5f);
		}
		else
		{
			badgeStyle.ShadowSize = 4;
			badgeStyle.ShadowColor = new Color(0f, 0f, 0f, 0.6f);
		}
		badge.AddThemeStyleboxOverride("panel", badgeStyle);

		Label gradeLbl = new Label();
		string gradeText = isBestPick ? $"\u2605{grade}" : grade.ToString();
		gradeLbl.Text = gradeText;
		ApplyFont(gradeLbl, _fontHeader);
		gradeLbl.AddThemeColorOverride("font_color", textColor);
		gradeLbl.AddThemeFontSizeOverride("font_size", 18);
		gradeLbl.HorizontalAlignment = HorizontalAlignment.Center;
		gradeLbl.VerticalAlignment = VerticalAlignment.Center;
		gradeLbl.AddThemeConstantOverride("outline_size", 2);
		gradeLbl.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
		badge.AddChild(gradeLbl, forceReadableName: false, Node.InternalMode.Disabled);

		// Position at bottom-center of the target node
		badge.ZIndex = 10; // Above game UI
		badge.MouseFilter = Control.MouseFilterEnum.Ignore;

		// Add as child of target and position at bottom-center
		targetNode.AddChild(badge, forceReadableName: false, Node.InternalMode.Disabled);
		// Position after adding (deferred so size is known)
		Callable.From(() => PositionBadge(badge, targetNode)).CallDeferred();
	}

	private static void PositionBadge(PanelContainer badge, Control parent)
	{
		if (badge == null || !GodotObject.IsInstanceValid(badge) || parent == null || !GodotObject.IsInstanceValid(parent))
			return;
		// Place at bottom-center of parent
		float parentW = parent.Size.X;
		float parentH = parent.Size.Y;
		float badgeW = badge.GetCombinedMinimumSize().X;
		float badgeH = badge.GetCombinedMinimumSize().Y;
		badge.Position = new Vector2((parentW - badgeW) / 2f, parentH - badgeH - 4f);
	}

	private static void CleanupInjectedBadges(Node root)
	{
		try
		{
			SceneTree tree = root.GetTree();
			if (tree == null) return;
			var badges = tree.GetNodesInGroup(GradeBadgeGroup);
			foreach (Node badge in badges)
			{
				if (badge != null && GodotObject.IsInstanceValid(badge))
				{
					badge.GetParent()?.RemoveChild(badge);
					badge.QueueFree();
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log($"CleanupInjectedBadges error: {ex.Message}");
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
