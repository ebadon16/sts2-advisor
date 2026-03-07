using System.Collections.Generic;
using Godot;
using STS2Advisor.Core;

namespace STS2Advisor.UI
{
    /// <summary>
    /// Manages the Godot CanvasLayer overlay that displays tier badges and advice.
    ///
    /// Creates a CanvasLayer on layer 100 (above all game UI) with Control nodes
    /// for the advisor panel. Toggle with F7, tooltips with F8.
    ///
    /// IMPORTANT: This must be instantiated after the SceneTree is available.
    /// It's created from a Harmony patch on a game init method.
    /// </summary>
    public class OverlayManager
    {
        private CanvasLayer _layer;
        private PanelContainer _panel;
        private VBoxContainer _content;
        private bool _visible = true;
        private bool _showTooltips = true;

        private List<ScoredCard> _currentCards;
        private List<ScoredRelic> _currentRelics;

        public OverlayManager()
        {
            BuildOverlay();
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
            _layer.Layer = 100; // Above everything

            // Main panel — right side of screen
            _panel = new PanelContainer();
            _panel.AnchorLeft = 1.0f;
            _panel.AnchorRight = 1.0f;
            _panel.AnchorTop = 0.0f;
            _panel.AnchorBottom = 0.0f;
            _panel.OffsetLeft = -280;
            _panel.OffsetRight = -10;
            _panel.OffsetTop = 50;
            _panel.OffsetBottom = 600;

            // Semi-transparent dark background
            var styleBox = new StyleBoxFlat();
            styleBox.BgColor = new Color(0.05f, 0.05f, 0.1f, 0.85f);
            styleBox.CornerRadiusTopLeft = 8;
            styleBox.CornerRadiusTopRight = 8;
            styleBox.CornerRadiusBottomLeft = 8;
            styleBox.CornerRadiusBottomRight = 8;
            styleBox.ContentMarginLeft = 12;
            styleBox.ContentMarginRight = 12;
            styleBox.ContentMarginTop = 12;
            styleBox.ContentMarginBottom = 12;
            _panel.AddThemeStyleboxOverride("panel", styleBox);

            // Scrollable content
            var scroll = new ScrollContainer();
            scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _panel.AddChild(scroll);

            _content = new VBoxContainer();
            _content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            scroll.AddChild(_content);

            _layer.AddChild(_panel);
            tree.Root.CallDeferred("add_child", _layer);

            // Input handling via process
            _layer.SetProcessInput(true);

            Plugin.Log("Overlay built and attached to scene tree.");
        }

        public void ShowCardAdvice(List<ScoredCard> cards)
        {
            _currentCards = cards;
            _currentRelics = null;
            Rebuild();
        }

        public void ShowRelicAdvice(List<ScoredRelic> relics)
        {
            _currentRelics = relics;
            _currentCards = null;
            Rebuild();
        }

        public void ShowShopAdvice(List<ScoredCard> cards, List<ScoredRelic> relics)
        {
            _currentCards = cards;
            _currentRelics = relics;
            Rebuild();
        }

        public void Clear()
        {
            _currentCards = null;
            _currentRelics = null;
            Rebuild();
        }

        public void ToggleVisible()
        {
            _visible = !_visible;
            if (_panel != null)
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
            if (_content == null) return;

            // Clear existing children
            foreach (var child in _content.GetChildren())
            {
                if (child is Node node)
                    node.QueueFree();
            }

            if (_currentCards != null && _currentCards.Count > 0)
            {
                AddHeader("Card Advisor");
                foreach (var card in _currentCards)
                    AddCardEntry(card);
            }

            if (_currentRelics != null && _currentRelics.Count > 0)
            {
                if (_currentCards != null && _currentCards.Count > 0)
                    AddSeparator();
                AddHeader("Relic Advisor");
                foreach (var relic in _currentRelics)
                    AddRelicEntry(relic);
            }

            if ((_currentCards == null || _currentCards.Count == 0) &&
                (_currentRelics == null || _currentRelics.Count == 0))
            {
                var label = new Label();
                label.Text = "STS2 Advisor | F7: Toggle | F8: Tooltips";
                label.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
                _content.AddChild(label);
            }
        }

        private void AddHeader(string text)
        {
            var label = new Label();
            label.Text = text;
            label.AddThemeColorOverride("font_color", new Color(1f, 0.84f, 0f)); // Gold
            label.AddThemeFontSizeOverride("font_size", 18);
            _content.AddChild(label);
        }

        private void AddSeparator()
        {
            var sep = new HSeparator();
            sep.CustomMinimumSize = new Vector2(0, 10);
            _content.AddChild(sep);
        }

        private void AddCardEntry(ScoredCard card)
        {
            var hbox = new HBoxContainer();
            hbox.CustomMinimumSize = new Vector2(0, 50);

            // Tier badge
            var badge = CreateBadge(card.FinalGrade);
            hbox.AddChild(badge);

            // Info column
            var vbox = new VBoxContainer();
            vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            var nameLabel = new Label();
            nameLabel.Text = card.IsBestPick ? $"{card.Id}  ★ BEST PICK" : card.Id;
            nameLabel.AddThemeColorOverride("font_color",
                card.IsBestPick ? new Color(1f, 1f, 0f) : Colors.White);
            vbox.AddChild(nameLabel);

            var scoreLabel = new Label();
            scoreLabel.Text = $"Score: {card.FinalScore:F1} (base {card.BaseTier})";
            scoreLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            scoreLabel.AddThemeFontSizeOverride("font_size", 12);
            vbox.AddChild(scoreLabel);

            if (_showTooltips)
            {
                string tooltip = SynergyTooltip.BuildCardTooltip(card);
                if (!string.IsNullOrEmpty(tooltip))
                {
                    var tipLabel = new Label();
                    tipLabel.Text = tooltip;
                    tipLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.8f, 0.5f));
                    tipLabel.AddThemeFontSizeOverride("font_size", 11);
                    tipLabel.AutowrapMode = TextServer.AutowrapMode.Word;
                    vbox.AddChild(tipLabel);
                }
            }

            hbox.AddChild(vbox);
            _content.AddChild(hbox);
        }

        private void AddRelicEntry(ScoredRelic relic)
        {
            var hbox = new HBoxContainer();
            hbox.CustomMinimumSize = new Vector2(0, 50);

            var badge = CreateBadge(relic.FinalGrade);
            hbox.AddChild(badge);

            var vbox = new VBoxContainer();
            vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            var nameLabel = new Label();
            nameLabel.Text = relic.IsBestPick ? $"{relic.Id}  ★ BEST PICK" : relic.Id;
            nameLabel.AddThemeColorOverride("font_color",
                relic.IsBestPick ? new Color(1f, 1f, 0f) : Colors.White);
            vbox.AddChild(nameLabel);

            var scoreLabel = new Label();
            scoreLabel.Text = $"Score: {relic.FinalScore:F1} (base {relic.BaseTier})";
            scoreLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            scoreLabel.AddThemeFontSizeOverride("font_size", 12);
            vbox.AddChild(scoreLabel);

            if (_showTooltips)
            {
                string tooltip = SynergyTooltip.BuildRelicTooltip(relic);
                if (!string.IsNullOrEmpty(tooltip))
                {
                    var tipLabel = new Label();
                    tipLabel.Text = tooltip;
                    tipLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.8f, 0.5f));
                    tipLabel.AddThemeFontSizeOverride("font_size", 11);
                    tipLabel.AutowrapMode = TextServer.AutowrapMode.Word;
                    vbox.AddChild(tipLabel);
                }
            }

            hbox.AddChild(vbox);
            _content.AddChild(hbox);
        }

        private PanelContainer CreateBadge(TierGrade grade)
        {
            var badge = new PanelContainer();
            badge.CustomMinimumSize = new Vector2(36, 36);

            var style = new StyleBoxFlat();
            style.BgColor = TierBadge.GetGodotColor(grade);
            style.CornerRadiusTopLeft = 4;
            style.CornerRadiusTopRight = 4;
            style.CornerRadiusBottomLeft = 4;
            style.CornerRadiusBottomRight = 4;
            badge.AddThemeStyleboxOverride("panel", style);

            var label = new Label();
            label.Text = grade.ToString();
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.AddThemeColorOverride("font_color", new Color(0.05f, 0.05f, 0.05f));
            label.AddThemeFontSizeOverride("font_size", 16);
            badge.AddChild(label);

            return badge;
        }
    }
}
