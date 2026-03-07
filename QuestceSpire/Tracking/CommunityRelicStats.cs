using System.Collections.Generic;

namespace QuestceSpire.Tracking;

public class CommunityRelicStats
{
	public string RelicId { get; set; }

	public string Character { get; set; }

	public float PickRate { get; set; }

	public float WinRateWhenPicked { get; set; }

	public float WinRateWhenSkipped { get; set; }

	public int SampleSize { get; set; }

	public float AvgFloorPicked { get; set; }

	public Dictionary<string, float> ArchetypeContext { get; set; } = new Dictionary<string, float>();
}
