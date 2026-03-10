using System.Collections.Generic;

namespace QuestceSpire.Core;

public class ScoredCard
{
	public string Id { get; set; }

	public string Name { get; set; }

	public string Type { get; set; }

	public int Cost { get; set; }

	public TierGrade BaseTier { get; set; }

	public float FinalScore { get; set; }

	public TierGrade FinalGrade { get; set; }

	public bool IsBestPick { get; set; }

	public List<string> SynergyReasons { get; set; } = new List<string>();

	public List<string> AntiSynergyReasons { get; set; } = new List<string>();

	public string Notes { get; set; }

	public float BaseScore { get; set; }

	public float SynergyDelta { get; set; }

	public float FloorAdjust { get; set; }

	public float DeckSizeAdjust { get; set; }

	public bool Upgraded { get; set; }

	public float UpgradeAdjust { get; set; }

	/// <summary>Score delta from upgrading this card (upgraded score - current score). Only set on upgrade screens.</summary>
	public float UpgradeDelta { get; set; }

	public int Price { get; set; }

	public string ScoreSource { get; set; } = "static";
}
