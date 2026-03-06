using System.Collections.Generic;
using STS2Advisor.Core;
using UnityEngine;

namespace STS2Advisor.UI
{
    public class OverlayRenderer : MonoBehaviour
    {
        public static OverlayRenderer Instance { get; private set; }

        private bool _visible = true;
        private bool _showTooltips = true;
        private List<ScoredCard> _currentCardAdvice;
        private List<ScoredRelic> _currentRelicAdvice;
        private List<ScoredCard> _shopCardAdvice;
        private List<ScoredRelic> _shopRelicAdvice;
        private string _activeScreen; // "card", "relic", "shop", or null

        private GUIStyle _badgeStyle;
        private GUIStyle _bestPickStyle;
        private GUIStyle _tooltipStyle;
        private GUIStyle _headerStyle;
        private bool _stylesInitialized;

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            // F7 to toggle overlay visibility
            if (Input.GetKeyDown(KeyCode.F7))
            {
                _visible = !_visible;
                Plugin.Log.LogInfo($"Advisor overlay {(_visible ? "shown" : "hidden")}");
            }

            // F8 to toggle tooltips
            if (Input.GetKeyDown(KeyCode.F8))
            {
                _showTooltips = !_showTooltips;
                Plugin.Log.LogInfo($"Advisor tooltips {(_showTooltips ? "shown" : "hidden")}");
            }
        }

        public void ShowCardAdvice(List<ScoredCard> scored)
        {
            _currentCardAdvice = scored;
            _activeScreen = "card";
        }

        public void ShowRelicAdvice(List<ScoredRelic> scored)
        {
            _currentRelicAdvice = scored;
            _activeScreen = "relic";
        }

        public void ShowShopAdvice(List<ScoredCard> scoredCards, List<ScoredRelic> scoredRelics)
        {
            _shopCardAdvice = scoredCards;
            _shopRelicAdvice = scoredRelics;
            _activeScreen = "shop";
        }

        public void ClearAdvice()
        {
            _currentCardAdvice = null;
            _currentRelicAdvice = null;
            _shopCardAdvice = null;
            _shopRelicAdvice = null;
            _activeScreen = null;
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _badgeStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(6, 6, 2, 2)
            };

            _bestPickStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _bestPickStyle.normal.textColor = Color.yellow;

            _tooltipStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                padding = new RectOffset(8, 8, 6, 6)
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            _headerStyle.normal.textColor = new Color(1f, 0.84f, 0f);

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (!_visible || _activeScreen == null) return;

            InitStyles();

            switch (_activeScreen)
            {
                case "card":
                    DrawCardAdvice(_currentCardAdvice);
                    break;
                case "relic":
                    DrawRelicAdvice(_currentRelicAdvice);
                    break;
                case "shop":
                    DrawCardAdvice(_shopCardAdvice);
                    DrawRelicAdvice(_shopRelicAdvice);
                    break;
            }

            // Draw toggle hint
            GUI.Label(
                new Rect(10, Screen.height - 30, 300, 25),
                "STS2 Advisor | F7: Toggle | F8: Tooltips",
                GUI.skin.label
            );
        }

        private void DrawCardAdvice(List<ScoredCard> cards)
        {
            if (cards == null || cards.Count == 0) return;

            // If we have screen positions from the game, draw badges on the cards.
            // Otherwise, fall back to a summary panel.
            bool hasPositions = cards.Count > 0 && (cards[0] is ScoredCard);

            // Summary panel on the right side
            float panelX = Screen.width - 280;
            float panelY = 60;

            GUI.Box(new Rect(panelX - 10, panelY - 10, 270, 40 + cards.Count * 80), "");
            GUI.Label(new Rect(panelX, panelY, 250, 25), "Card Advisor", _headerStyle);
            panelY += 35;

            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                DrawScoredCardEntry(panelX, panelY, card);
                panelY += 75;
            }
        }

        private void DrawRelicAdvice(List<ScoredRelic> relics)
        {
            if (relics == null || relics.Count == 0) return;

            float panelX = Screen.width - 280;
            float panelY = _currentCardAdvice != null
                ? 60 + 40 + _currentCardAdvice.Count * 80 + 20
                : 60;

            GUI.Box(new Rect(panelX - 10, panelY - 10, 270, 40 + relics.Count * 80), "");
            GUI.Label(new Rect(panelX, panelY, 250, 25), "Relic Advisor", _headerStyle);
            panelY += 35;

            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                DrawScoredRelicEntry(panelX, panelY, relic);
                panelY += 75;
            }
        }

        private void DrawScoredCardEntry(float x, float y, ScoredCard card)
        {
            // Badge
            Color badgeColor = TierBadge.GetTierColor(card.FinalGrade);
            GUI.backgroundColor = badgeColor;
            GUI.Box(new Rect(x, y, 36, 36), card.FinalGrade.ToString(), _badgeStyle);
            GUI.backgroundColor = Color.white;

            // Card name + score
            string label = card.IsBestPick
                ? $"<b>{card.Id}</b>  ★ BEST PICK"
                : card.Id;
            GUI.Label(new Rect(x + 44, y + 2, 200, 20), label);
            GUI.Label(new Rect(x + 44, y + 20, 200, 18),
                $"Score: {card.FinalScore:F1} (base {card.BaseTier})");

            // Tooltip
            if (_showTooltips && (card.SynergyReasons.Count > 0 || card.AntiSynergyReasons.Count > 0))
            {
                string tooltip = SynergyTooltip.BuildCardTooltip(card);
                GUI.Label(new Rect(x + 44, y + 38, 210, 32), tooltip, _tooltipStyle);
            }
        }

        private void DrawScoredRelicEntry(float x, float y, ScoredRelic relic)
        {
            Color badgeColor = TierBadge.GetTierColor(relic.FinalGrade);
            GUI.backgroundColor = badgeColor;
            GUI.Box(new Rect(x, y, 36, 36), relic.FinalGrade.ToString(), _badgeStyle);
            GUI.backgroundColor = Color.white;

            string label = relic.IsBestPick
                ? $"<b>{relic.Id}</b>  ★ BEST PICK"
                : relic.Id;
            GUI.Label(new Rect(x + 44, y + 2, 200, 20), label);
            GUI.Label(new Rect(x + 44, y + 20, 200, 18),
                $"Score: {relic.FinalScore:F1} (base {relic.BaseTier})");

            if (_showTooltips && (relic.SynergyReasons.Count > 0 || relic.AntiSynergyReasons.Count > 0))
            {
                string tooltip = SynergyTooltip.BuildRelicTooltip(relic);
                GUI.Label(new Rect(x + 44, y + 38, 210, 32), tooltip, _tooltipStyle);
            }
        }
    }
}
