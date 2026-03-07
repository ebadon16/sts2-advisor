using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using STS2Advisor.Core;

namespace STS2Advisor.UI
{
    /// <summary>
    /// Small Node added to the scene tree to forward input events to OverlayManager.
    /// </summary>
    internal partial class OverlayInputHandler : Node
    {
        private OverlayManager _owner;

        public OverlayInputHandler(OverlayManager owner)
        {
            _owner = owner;
        }

        public override void _UnhandledInput(InputEvent ev)
        {
            _owner.HandleInput(ev);
        }
    }

    /// <summary>
    /// Manages the Godot CanvasLayer overlay that displays tier badges and advice.
    ///
    /// Creates a CanvasLayer on layer 100 (above all game UI) with Control nodes
    /// for the advisor panel. Toggle with F7, tooltips with F8.
    ///
    /// Visual design inspired by Gemini's polished overlay theme.
    /// Safety features (auto-recovery, null guards) from original implementation.
    /// </summary>
    public class OverlayManager
    {
        private CanvasLayer _layer;
        private PanelContainer _panel;
        private VBoxContainer _content;
        private Label _archetypeLabel;
        private Label _screenLabel;
        private ScrollContainer _scroll;
        private bool _visible = true;
        private bool _showTooltips = true;

        private List<ScoredCard> _currentCards;
        private List<ScoredRelic> _currentRelics;
        private DeckAnalysis _currentDeckAnalysis;
        private string _currentScreen = "IDLE";

        // --- THEME ---
        private static readonly Color ClrBg = new Color(0.04f, 0.04f, 0.06f, 0.98f);
        private static readonly Color ClrBorder = new Color(0.55f, 0.45f, 0.3f);
        private static readonly Color ClrHeader = new Color(1.0f, 0.98f, 0.92f);
        private static readonly Color ClrAccent = new Color(1.0f, 0.84f, 0.0f);
        private static readonly Color ClrSub = new Color(0.5f, 0.5f, 0.55f);
        private static readonly Color ClrPositive = new Color(0.45f, 1.0f, 0.45f);
        private static readonly Color ClrSkip = new Color(1.0f, 0.5f, 0.3f);

        private StyleBoxFlat _sbPanel;
        private StyleBoxFlat _sbEntry;
        private StyleBoxFlat _sbBest;
        private StyleBoxFlat _sbChip;
        private StyleBoxFlat _sbHover;

        public OverlayManager()
        {
            InitializeStyles();
            BuildOverlay();
        }

        private bool IsOverlayValid()
        {
            return _layer != null && GodotObject.IsInstanceValid(_layer)
                && _panel != null && GodotObject.IsInstanceValid(_panel)
                && _content != null && GodotObject.IsInstanceValid(_content);
        }

        private bool EnsureOverlay()
        {
            if (IsOverlayValid()) return true;
            _layer = null; _panel = null; _content = null;
            _scroll = null; _archetypeLabel = null; _screenLabel = null;
            InitializeStyles();
            BuildOverlay();
            return IsOverlayValid();
        }

        private void InitializeStyles()
        {
            _sbPanel = new StyleBoxFlat();
            _sbPanel.BgColor = ClrBg;
            _sbPanel.BorderWidthLeft = _sbPanel.BorderWidthRight = 2;
            _sbPanel.BorderWidthTop = _sbPanel.BorderWidthBottom = 2;
            _sbPanel.BorderColor = ClrBorder;
            _sbPanel.CornerRadiusTopLeft = 8;
            _sbPanel.CornerRadiusTopRight = 8;
            _sbPanel.CornerRadiusBottomLeft = 8;
            _sbPanel.CornerRadiusBottomRight = 8;
            _sbPanel.ContentMarginLeft = _sbPanel.ContentMarginRight = 16;
            _sbPanel.ContentMarginTop = _sbPanel.ContentMarginBottom = 16;
            _sbPanel.ShadowSize = 20;
            _sbPanel.ShadowColor = new Color(0, 0, 0, 0.8f);

            _sbEntry = new StyleBoxFlat();
            _sbEntry.BgColor = new Color(1, 1, 1, 0.02f);
            _sbEntry.CornerRadiusTopLeft = _sbEntry.CornerRadiusTopRight = 8;
            _sbEntry.CornerRadiusBottomLeft = _sbEntry.CornerRadiusBottomRight = 8;
            _sbEntry.ContentMarginLeft = _sbEntry.ContentMarginRight = 12;
            _sbEntry.ContentMarginTop = _sbEntry.ContentMarginBottom = 10;

            _sbHover = _sbEntry.Duplicate() as StyleBoxFlat;
            if (_sbHover != null) _sbHover.BgColor = new Color(1, 1, 1, 0.05f);

            _sbBest = _sbEntry.Duplicate() as StyleBoxFlat;
            if (_sbBest != null)
            {
                _sbBest.BgColor = new Color(1, 0.84f, 0, 0.08f);
                _sbBest.BorderWidthLeft = 4;
                _sbBest.BorderColor = ClrAccent;
            }

            _sbChip = new StyleBoxFlat();
            _sbChip.BgColor = new Color(0, 0, 0, 0.5f);
            _sbChip.CornerRadiusTopLeft = _sbChip.CornerRadiusTopRight = 12;
            _sbChip.CornerRadiusBottomLeft = _sbChip.CornerRadiusBottomRight = 12;
            _sbChip.ContentMarginLeft = _sbChip.ContentMarginRight = 14;
            _sbChip.ContentMarginTop = _sbChip.ContentMarginBottom = 5;
            _sbChip.BorderWidthTop = _sbChip.BorderWidthBottom = 1;
            _sbChip.BorderColor = new Color(ClrAccent, 0.3f);
        }

        private void BuildOverlay()
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root == null)
            {
                Plugin.Log("SceneTree not ready — overlay deferred.");
                return;
            }

            _layer = new CanvasLayer();
            _layer.Layer = 100;

            _panel = new PanelContainer();
            _panel.AnchorLeft = 1.0f; _panel.AnchorRight = 1.0f;
            _panel.AnchorTop = 0.0f; _panel.AnchorBottom = 0.0f;
            _panel.OffsetLeft = -340; _panel.OffsetRight = -20;
            _panel.OffsetTop = 30; _panel.OffsetBottom = 850;
            _panel.AddThemeStyleboxOverride("panel", _sbPanel);
            _panel.MouseFilter = Control.MouseFilterEnum.Stop;

            var mainVBox = new VBoxContainer();
            mainVBox.AddThemeConstantOverride("separation", 14);
            _panel.AddChild(mainVBox);

            // --- HEADER ---
            var titleVBox = new VBoxContainer();
            mainVBox.AddChild(titleVBox);

            var titleLabel = new Label();
            titleLabel.Text = "STS2 ADVISOR";
            titleLabel.AddThemeFontSizeOverride("font_size", 24);
            titleLabel.AddThemeColorOverride("font_color", ClrHeader);
            titleVBox.AddChild(titleLabel);

            _screenLabel = new Label();
            _screenLabel.Text = "WAITING FOR SCREEN...";
            _screenLabel.AddThemeFontSizeOverride("font_size", 10);
            _screenLabel.AddThemeColorOverride("font_color", ClrSub);
            titleVBox.AddChild(_screenLabel);

            // --- ARCHETYPE CHIP ---
            var chipPanel = new PanelContainer();
            chipPanel.AddThemeStyleboxOverride("panel", _sbChip);
            _archetypeLabel = new Label();
            _archetypeLabel.Text = "ANALYZING DECK...";
            _archetypeLabel.AddThemeFontSizeOverride("font_size", 11);
            _archetypeLabel.AddThemeColorOverride("font_color", ClrAccent);
            _archetypeLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _archetypeLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            chipPanel.AddChild(_archetypeLabel);
            mainVBox.AddChild(chipPanel);

            // --- SCROLLABLE CONTENT ---
            _scroll = new ScrollContainer();
            _scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
            var vScroll = _scroll.GetVScrollBar();
            vScroll.CustomMinimumSize = new Vector2(6, 0);
            mainVBox.AddChild(_scroll);

            _content = new VBoxContainer();
            _content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _content.AddThemeConstantOverride("separation", 8);
            _scroll.AddChild(_content);

            _layer.AddChild(_panel);

            // Input handler for F7/F8
            var inputHandler = new OverlayInputHandler(this);
            _layer.AddChild(inputHandler);

            tree.Root.CallDeferred("add_child", _layer);
            Plugin.Log("Overlay built and attached to scene tree.");
        }

        public void ShowCardAdvice(List<ScoredCard> cards, DeckAnalysis deckAnalysis = null)
        {
            _currentCards = cards; _currentRelics = null;
            _currentDeckAnalysis = deckAnalysis;
            _currentScreen = "CARD REWARD";
            Rebuild();
        }

        public void ShowRelicAdvice(List<ScoredRelic> relics, DeckAnalysis deckAnalysis = null)
        {
            _currentRelics = relics; _currentCards = null;
            _currentDeckAnalysis = deckAnalysis;
            _currentScreen = "RELIC REWARD";
            Rebuild();
        }

        public void ShowShopAdvice(List<ScoredCard> cards, List<ScoredRelic> relics, DeckAnalysis deckAnalysis = null)
        {
            _currentCards = cards; _currentRelics = relics;
            _currentDeckAnalysis = deckAnalysis;
            _currentScreen = "MERCHANT SHOP";
            Rebuild();
        }

        public void Clear()
        {
            _currentCards = null; _currentRelics = null;
            _currentDeckAnalysis = null;
            _currentScreen = "MAP / COMBAT";
            Rebuild();
        }

        public void ToggleVisible()
        {
            if (!EnsureOverlay()) return;
            _visible = !_visible;
            _panel.Visible = _visible;
            Plugin.Log($"Overlay {(_visible ? "shown" : "hidden")}");
        }

        public void ToggleTooltips()
        {
            _showTooltips = !_showTooltips;
            Rebuild();
            Plugin.Log($"Tooltips {(_showTooltips ? "shown" : "hidden")}");
        }

        public void HandleInput(InputEvent ev)
        {
            if (ev is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
            {
                if (keyEvent.Keycode == Key.F7)
                    ToggleVisible();
                else if (keyEvent.Keycode == Key.F8)
                    ToggleTooltips();
            }
        }

        private void Rebuild()
        {
            if (!EnsureOverlay()) return;

            if (_screenLabel != null && GodotObject.IsInstanceValid(_screenLabel))
                _screenLabel.Text = _currentScreen.ToUpper();

            UpdateArchetypeChip();

            // Safe cleanup: remove from tree first, then free
            foreach (var child in _content.GetChildren())
            {
                if (child is Node node)
                {
                    _content.RemoveChild(node);
                    node.QueueFree();
                }
            }

            bool hasCards = _currentCards != null && _currentCards.Count > 0;
            bool hasRelics = _currentRelics != null && _currentRelics.Count > 0;

            if (hasCards)
            {
                AddSectionHeader("CARD ANALYSIS");
                foreach (var card in _currentCards)
                    AddCardEntry(card);

                // SKIP recommendation when all offerings are weak
                if (_currentCards.All(c => c.FinalScore < 1.5f))
                    AddSkipEntry("SKIP CARDS", "All offerings are weak \u2014 keep your deck lean.");
            }

            if (hasRelics)
            {
                AddSectionHeader("RELIC ANALYSIS");
                foreach (var relic in _currentRelics)
                    AddRelicEntry(relic);
            }

            if (!hasCards && !hasRelics)
            {
                var label = new Label();
                label.Text = "Ready for decision screen.\n(Cards, Relics, or Shop)\n\nF7: Toggle | F8: Tooltips";
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.AddThemeColorOverride("font_color", ClrSub);
                label.AddThemeFontSizeOverride("font_size", 13);
                _content.AddChild(label);
            }

            // Scroll hint when many entries
            int entryCount = (_currentCards?.Count ?? 0) + (_currentRelics?.Count ?? 0);
            if (entryCount >= 4)
            {
                var hint = new Label();
                hint.Text = "scroll for more...";
                hint.HorizontalAlignment = HorizontalAlignment.Center;
                hint.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f, 0.7f));
                hint.AddThemeFontSizeOverride("font_size", 10);
                _content.AddChild(hint);
            }
        }

        private void UpdateArchetypeChip()
        {
            if (_archetypeLabel == null || !GodotObject.IsInstanceValid(_archetypeLabel))
                return;

            if (_currentDeckAnalysis == null || _currentDeckAnalysis.DetectedArchetypes.Count == 0)
            {
                _archetypeLabel.Text = "DECK: FLEXIBLE / BALANCED";
                return;
            }

            var parts = new List<string>();
            foreach (var match in _currentDeckAnalysis.DetectedArchetypes)
            {
                int pct = (int)(match.Strength * 100);
                parts.Add($"{match.Archetype.DisplayName.ToUpper()} {pct}%");
            }
            _archetypeLabel.Text = string.Join("  \u2022  ", parts);
        }

        private void AddSectionHeader(string text)
        {
            var label = new Label();
            label.Text = text;
            label.AddThemeColorOverride("font_color", ClrAccent);
            label.AddThemeFontSizeOverride("font_size", 12);
            _content.AddChild(label);
        }

        private void AddCardEntry(ScoredCard card)
        {
            var panel = CreateEntryPanel(card.IsBestPick);
            var hbox = new HBoxContainer();
            hbox.AddThemeConstantOverride("separation", 14);
            panel.AddChild(hbox);

            hbox.AddChild(CreateBadge(card.FinalGrade));

            var vbox = new VBoxContainer();
            vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            vbox.AddThemeConstantOverride("separation", 1);
            hbox.AddChild(vbox);

            var nameLabel = new Label();
            string displayName = card.Name ?? card.Id;
            nameLabel.Text = (card.IsBestPick ? "\u2605 " : "") + displayName;
            nameLabel.AddThemeColorOverride("font_color", card.IsBestPick ? ClrAccent : Colors.White);
            nameLabel.AddThemeFontSizeOverride("font_size", 15);
            vbox.AddChild(nameLabel);

            var meta = new Label();
            string typePart = !string.IsNullOrEmpty(card.Type) ? $"{card.Type.ToUpper()} \u2022 " : "";
            meta.Text = $"{typePart}COST: {card.Cost} \u2022 SCORE: {card.FinalScore:F1} [{card.BaseTier}]";
            meta.AddThemeColorOverride("font_color", ClrSub);
            meta.AddThemeFontSizeOverride("font_size", 10);
            vbox.AddChild(meta);

            if (_showTooltips)
            {
                string tooltip = SynergyTooltip.BuildCardTooltip(card);
                if (!string.IsNullOrEmpty(tooltip))
                {
                    var tipLabel = new Label();
                    tipLabel.Text = tooltip;
                    tipLabel.AddThemeColorOverride("font_color", ClrPositive);
                    tipLabel.AddThemeFontSizeOverride("font_size", 11);
                    tipLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    vbox.AddChild(tipLabel);
                }
            }

            _content.AddChild(panel);
        }

        private void AddRelicEntry(ScoredRelic relic)
        {
            var panel = CreateEntryPanel(relic.IsBestPick);
            var hbox = new HBoxContainer();
            hbox.AddThemeConstantOverride("separation", 14);
            panel.AddChild(hbox);

            hbox.AddChild(CreateBadge(relic.FinalGrade));

            var vbox = new VBoxContainer();
            vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            vbox.AddThemeConstantOverride("separation", 1);
            hbox.AddChild(vbox);

            var nameLabel = new Label();
            string displayName = relic.Name ?? relic.Id;
            nameLabel.Text = (relic.IsBestPick ? "\u2605 " : "") + displayName;
            nameLabel.AddThemeColorOverride("font_color", relic.IsBestPick ? ClrAccent : Colors.White);
            nameLabel.AddThemeFontSizeOverride("font_size", 15);
            vbox.AddChild(nameLabel);

            var meta = new Label();
            string rarityPart = !string.IsNullOrEmpty(relic.Rarity) ? $"{relic.Rarity.ToUpper()} \u2022 " : "";
            meta.Text = $"{rarityPart}SCORE: {relic.FinalScore:F1} [{relic.BaseTier}]";
            meta.AddThemeColorOverride("font_color", ClrSub);
            meta.AddThemeFontSizeOverride("font_size", 10);
            vbox.AddChild(meta);

            if (_showTooltips)
            {
                string tooltip = SynergyTooltip.BuildRelicTooltip(relic);
                if (!string.IsNullOrEmpty(tooltip))
                {
                    var tipLabel = new Label();
                    tipLabel.Text = tooltip;
                    tipLabel.AddThemeColorOverride("font_color", ClrPositive);
                    tipLabel.AddThemeFontSizeOverride("font_size", 11);
                    tipLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    vbox.AddChild(tipLabel);
                }
            }

            _content.AddChild(panel);
        }

        private void AddSkipEntry(string title, string reasoning)
        {
            var panel = CreateEntryPanel(false);
            var hbox = new HBoxContainer();
            hbox.AddThemeConstantOverride("separation", 14);
            panel.AddChild(hbox);

            hbox.AddChild(CreateBadge(TierGrade.C));

            var vbox = new VBoxContainer();
            vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hbox.AddChild(vbox);

            var nameLabel = new Label();
            nameLabel.Text = title;
            nameLabel.AddThemeColorOverride("font_color", ClrSkip);
            nameLabel.AddThemeFontSizeOverride("font_size", 14);
            vbox.AddChild(nameLabel);

            var reasonLabel = new Label();
            reasonLabel.Text = reasoning;
            reasonLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.8f));
            reasonLabel.AddThemeFontSizeOverride("font_size", 11);
            reasonLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            vbox.AddChild(reasonLabel);

            _content.AddChild(panel);
        }

        private PanelContainer CreateEntryPanel(bool isBest)
        {
            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", isBest ? _sbBest : _sbEntry);
            panel.MouseFilter = Control.MouseFilterEnum.Pass;

            var normalStyle = isBest ? _sbBest : _sbEntry;
            panel.Connect("mouse_entered", Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(panel))
                    panel.AddThemeStyleboxOverride("panel", _sbHover);
            }));
            panel.Connect("mouse_exited", Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(panel))
                    panel.AddThemeStyleboxOverride("panel", normalStyle);
            }));

            return panel;
        }

        private CenterContainer CreateBadge(TierGrade grade)
        {
            var center = new CenterContainer();
            center.CustomMinimumSize = new Vector2(44, 44);

            var badge = new PanelContainer();
            badge.CustomMinimumSize = new Vector2(40, 40);
            var style = new StyleBoxFlat();
            style.BgColor = TierBadge.GetGodotColor(grade);
            style.CornerRadiusTopLeft = style.CornerRadiusTopRight = 6;
            style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 6;
            badge.AddThemeStyleboxOverride("panel", style);

            var label = new Label();
            label.Text = grade.ToString();
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.AddThemeColorOverride("font_color", new Color(0.05f, 0.05f, 0.05f));
            label.AddThemeFontSizeOverride("font_size", 18);
            badge.AddChild(label);

            center.AddChild(badge);
            return center;
        }
    }
}
