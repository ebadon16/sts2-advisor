using System;

namespace QuestceSpire.Tracking;

public class RunLog
{
	public string RunId { get; set; }

	public string PlayerId { get; set; }

	public string Character { get; set; }

	public string Seed { get; set; }

	public DateTime StartTime { get; set; }

	public DateTime? EndTime { get; set; }

	public RunOutcome? Outcome { get; set; }

	public int? FinalFloor { get; set; }

	public int? FinalAct { get; set; }

	public int AscensionLevel { get; set; }

	public bool Synced { get; set; }
}
