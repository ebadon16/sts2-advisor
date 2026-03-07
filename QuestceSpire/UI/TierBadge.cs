using Godot;
using QuestceSpire.Core;

namespace QuestceSpire.UI;

public static class TierBadge
{
	public static Color GetGodotColor(TierGrade grade)
	{
		return grade switch
		{
			TierGrade.S => new Color(1f, 0.84f, 0f),
			TierGrade.A => new Color(0.2f, 0.8f, 0.2f),
			TierGrade.B => new Color(0.3f, 0.5f, 1f),
			TierGrade.C => new Color(0.6f, 0.6f, 0.6f),
			TierGrade.D => new Color(0.8f, 0.4f, 0.2f),
			TierGrade.F => new Color(0.9f, 0.2f, 0.2f),
			_ => new Color(0.6f, 0.6f, 0.6f),
		};
	}

	public static Color GetTextColor(TierGrade grade)
	{
		return grade switch
		{
			TierGrade.S => new Color(0.05f, 0.05f, 0.05f),
			TierGrade.A => new Color(0.05f, 0.05f, 0.05f),
			_ => new Color(0.95f, 0.95f, 0.95f),
		};
	}
}
