using System;
using System.Collections.Generic;
using System.Linq;

namespace QuestceSpire.Core;

public class DeckAnalysis
{
	public string Character { get; set; }

	public List<ArchetypeMatch> DetectedArchetypes { get; set; } = new List<ArchetypeMatch>();

	public int TotalCards { get; set; }

	public Dictionary<string, int> TagCounts { get; set; } = new Dictionary<string, int>();

	public Dictionary<int, int> EnergyCurve { get; set; } = new Dictionary<int, int>();

	public int AttackCount { get; set; }

	public int SkillCount { get; set; }

	public int PowerCount { get; set; }

	/// <summary>Average energy cost of cards in deck.</summary>
	public float AverageCost { get; set; }

	/// <summary>
	/// Deck "job" coverage: how well the deck fills each functional role (0.0 = missing, 1.0 = fully covered).
	/// Jobs: frontloaded_damage, aoe, block, scaling, draw
	/// </summary>
	public Dictionary<string, float> JobCoverage { get; set; } = new Dictionary<string, float>();

	public bool IsUndefined
	{
		get
		{
			if (DetectedArchetypes.Count != 0)
			{
				return DetectedArchetypes.All((ArchetypeMatch a) => a.Strength < 0.3f);
			}
			return true;
		}
	}

	public bool HasArchetype(string archetypeId)
	{
		return DetectedArchetypes.Any((ArchetypeMatch a) => a.Archetype.Id == archetypeId);
	}

	public float ArchetypeStrength(string archetypeId)
	{
		return DetectedArchetypes.FirstOrDefault((ArchetypeMatch a) => a.Archetype.Id == archetypeId)?.Strength ?? 0f;
	}

	/// <summary>Returns 1.0 - coverage for the given job (how badly the deck needs it).</summary>
	public float JobGap(string job)
	{
		if (JobCoverage.TryGetValue(job, out float coverage))
			return Math.Max(0f, 1f - coverage);
		return 1f;
	}
}
