using System.Collections.Generic;

namespace QuestceSpire.Core;

public class ScoredRelic
{
	public string Id { get; set; }

	public string Name { get; set; }

	public string Rarity { get; set; }

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

	public int Price { get; set; }

	public string ScoreSource { get; set; } = "static";
}
