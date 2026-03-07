using Godot;
using STS2Advisor.Core;

namespace STS2Advisor.UI
{
    public static class TierBadge
    {
        // S = Gold, A = Green, B = Blue, C = Gray, D = Orange, F = Red
        public static Color GetGodotColor(TierGrade grade)
        {
            switch (grade)
            {
                case TierGrade.S: return new Color(1.0f, 0.84f, 0.0f);    // Gold
                case TierGrade.A: return new Color(0.2f, 0.8f, 0.2f);     // Green
                case TierGrade.B: return new Color(0.3f, 0.5f, 1.0f);     // Blue
                case TierGrade.C: return new Color(0.6f, 0.6f, 0.6f);     // Gray
                case TierGrade.D: return new Color(1.0f, 0.6f, 0.2f);     // Orange
                case TierGrade.F: return new Color(0.9f, 0.2f, 0.2f);     // Red
                default: return new Color(0.6f, 0.6f, 0.6f);
            }
        }
    }
}
