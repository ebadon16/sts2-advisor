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
        private DeckAnalysis _currentDeckAnalysis;

        public OverlayManager()
        {
            BuildOverlay();
        }

        /// <summary>
        /// Returns true if all Godot nodes are still alive and usable.
        /// </summary>
        private bool IsOverlayValid()
        {
            return _layer != null && GodotObject.IsInstanceValid(_layer)
                && _panel != null && GodotObject.IsInstanceValid(_panel)
                && _content != null && GodotObject.IsInstanceValid(_content);
        }

        /// <summary>
        /// Ensures the overlay exists and is valid. Rebuilds if nodes were freed
        /// (e.g. after a scene transition).
        /// </summary>
        private bool EnsureOverlay()
        {
            if (IsOverlayValid()) return true;

            // Nodes were freed or never created — try to rebuild
            _layer = null;
            _panel = null;
            _content = null;
            BuildOverlay();
            return IsOverlayValid();
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
            _content.AddThemeConstantOverride("separation", 6);
            scroll.AddChild(_content);

            _layer.AddChild(_panel);

            // Input handler node to capture F7/F8 keypresses
            var inputHandler = new OverlayInputHandler(this);
            _layer.AddChild(inputHandler);

            tree.Root.CallDeferred("add_child", _layer);

            Plugin.Log("Overlay built and attached to scene tree.");
        }

        public void ShowCardAdvice(List<ScoredCard> cards, DeckAnalysis deckAnalysis = null)
        {
            _currentCards = cards;
            _currentRelics = null;
            _currentDeckAnalysis = deckAnalysis;
            Rebuild();
        }

        public void ShowRelicAdvice(List<ScoredRelic> relics, DeckAnalysis deckAnalysis = null)
        {
            _currentRelics = relics;
            _currentCards = null;
            _currentDeckAnalysis = deckAnalysis;
            Rebuild();
        }

        public void ShowShopAdvice(List<ScoredCard> cards, List<ScoredRelic> relics, DeckAnalysis deckAnalysis = null)
        {
            _currentCards = cards;
            _currentRelics = relics;
            _currentDeckAnalysis = deckAnalysis;
            Rebuild();
        }

        public void Clear()
        {
            _currentCards = null;
            _currentRelics = null;
            _currentDeckAnalysis = null;
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

            // Clear existing children — remove from tree first, then free
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

            // Archetype summary at the top when we have deck analysis
            if ((hasCards || hasRelics) && _currentDeckAnalysis != null)
                AddArchetypeSummary(_currentDeckAnalysis);

            if (hasCards)
            {
                AddHeader("Card Advisor");
                foreach (var card in _currentCards)
                    AddCardEntry(card);

                // SKIP recommendation when all card scores are low
                if (_currentCards.All(c => c.FinalScore < 1.5f))
                    AddSkipHint("Consider SKIP — all offerings are weak");
                else if (_currentCards.Count > 1 &&
                         _currentCards[0].FinalScore - _currentCards[1].FinalScore > 2.0f)
                    AddSkipHint($"Strong pick: {_currentCards[0].Name}");
            }

            if (hasRelics)
            {
                if (hasCards)
                    AddSeparator();
                AddHeader("Relic Advisor");
                foreach (var relic in _currentRelics)
                    AddRelicEntry(relic);
            }

            if (!hasCards && !hasRelics)
            {
                var label = new Label();
                label.Text = "STS2 Advisor | F7: Toggle | F8: Tooltips";
                label.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
                _content.AddChild(label);
            }

            // Scroll hint if content is tall
            AddScrollHint();
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
            string cardDisplayName = card.Name ?? card.Id;
            nameLabel.Text = card.IsBestPick ? $"{cardDisplayName}  ★ BEST PICK" : cardDisplayName;
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
            string relicDisplayName = relic.Name ?? relic.Id;
            nameLabel.Text = relic.IsBestPick ? $"{relicDisplayName}  ★ BEST PICK" : relicDisplayName;
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

        private CenterContainer CreateBadge(TierGrade grade)
        {
            // Wrap badge in CenterContainer so it vertically centers in the row
            var center = new CenterContainer();
            center.CustomMinimumSize = new Vector2(40, 40);

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

            center.AddChild(badge);
            return center;
        }

        private void AddArchetypeSummary(DeckAnalysis analysis)
        {
            if (analysis.DetectedArchetypes.Count == 0)
            {
                var label = new Label();
                label.Text = "Deck: No archetype yet";
                label.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.7f));
                label.AddThemeFontSizeOverride("font_size", 12);
                _content.AddChild(label);
                return;
            }

            var archLabel = new Label();
            var parts = new List<string>();
            foreach (var match in analysis.DetectedArchetypes)
            {
                int pct = (int)(match.Strength * 100);
                parts.Add($"{match.Archetype.DisplayName} ({pct}%)");
            }
            archLabel.Text = "Deck: " + string.Join(", ", parts);
            archLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 1.0f));
            archLabel.AddThemeFontSizeOverride("font_size", 12);
            archLabel.AutowrapMode = TextServer.AutowrapMode.Word;
            _content.AddChild(archLabel);

            AddSeparator();
        }

        private void AddSkipHint(string text)
        {
            var label = new Label();
            label.Text = text;
            label.AddThemeColorOverride("font_color", new Color(1.0f, 0.5f, 0.3f));
            label.AddThemeFontSizeOverride("font_size", 12);
            _content.AddChild(label);
        }

        private void AddScrollHint()
        {
            // Add a scroll hint at the bottom — always visible when there are 4+ entries
            // (at that point content almost certainly overflows the 550px panel)
            int entryCount = (_currentCards?.Count ?? 0) + (_currentRelics?.Count ?? 0);
            if (entryCount < 4) return;

            var hint = new Label();
            hint.Text = "scroll for more...";
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f, 0.7f));
            hint.AddThemeFontSizeOverride("font_size", 10);
            _content.AddChild(hint);
        }
    }
}
