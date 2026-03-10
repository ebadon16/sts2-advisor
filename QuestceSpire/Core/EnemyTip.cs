using System.Collections.Generic;

namespace QuestceSpire.Core;

public class EnemyTipData
{
	public List<EnemyTipEntry> Enemies { get; set; } = new();
}

public class EnemyTipEntry
{
	public string EnemyId { get; set; }
	public string EnemyName { get; set; }
	public List<string> Tips { get; set; } = new();
	public string DangerLevel { get; set; } // "low", "medium", "high", "extreme"
	public int Priority { get; set; } // Higher = show first
}
