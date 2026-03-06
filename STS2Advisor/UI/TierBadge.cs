using STS2Advisor.Core;
using UnityEngine;

namespace STS2Advisor.UI
{
    public static class TierBadge
    {
        // S = Gold, A = Green, B = Blue, C = Gray, D = Orange, F = Red
        private static readonly Color ColorS = new Color(1.0f, 0.84f, 0.0f);    // Gold
        private static readonly Color ColorA = new Color(0.2f, 0.8f, 0.2f);     // Green
        private static readonly Color ColorB = new Color(0.3f, 0.5f, 1.0f);     // Blue
        private static readonly Color ColorC = new Color(0.6f, 0.6f, 0.6f);     // Gray
        private static readonly Color ColorD = new Color(1.0f, 0.6f, 0.2f);     // Orange
        private static readonly Color ColorF = new Color(0.9f, 0.2f, 0.2f);     // Red

        public static Color GetTierColor(TierGrade grade)
        {
            switch (grade)
            {
                case TierGrade.S: return ColorS;
                case TierGrade.A: return ColorA;
                case TierGrade.B: return ColorB;
                case TierGrade.C: return ColorC;
                case TierGrade.D: return ColorD;
                case TierGrade.F: return ColorF;
                default: return ColorC;
            }
        }
    }
}
