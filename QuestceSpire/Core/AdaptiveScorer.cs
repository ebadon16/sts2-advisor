using System;
using System.Collections.Generic;
using QuestceSpire.Tracking;

namespace QuestceSpire.Core;

public class AdaptiveScorer
{
	private const int MinSampleSize = 5;

	private const int FullConfidenceSampleSize = 50;

	private const float WinRateForS = 0.58f;

	private const float WinRateForF = 0.35f;

	private readonly RunDatabase _db;

	public AdaptiveScorer(RunDatabase db)
	{
		_db = db;
	}

	public float GetAdaptiveCardScore(string character, string cardId, float staticScore, DeckAnalysis deckAnalysis)
	{
		float num = staticScore;
		CommunityCardStats communityCardStats = _db?.GetCommunityCardStats(character, cardId);
		if (communityCardStats == null || communityCardStats.SampleSize < 5)
		{
			return num;
		}
		float num2 = WinRateToScore(communityCardStats.WinRateWhenPicked);
		float contextScore = GetContextScore(communityCardStats.ArchetypeContext, deckAnalysis);
		if (contextScore >= 0f)
		{
			num2 = num2 * 0.4f + contextScore * 0.6f;
		}
		float val = (communityCardStats.PickRate - 0.33f) * 0.9f;
		val = Math.Max(-0.3f, Math.Min(0.3f, val));
		num2 += val;
		float num3 = communityCardStats.WinRateWhenPicked - communityCardStats.WinRateWhenSkipped;
		if (Math.Abs(num3) > 0.03f)
		{
			num2 += num3 * 2f;
		}
		num2 = Math.Max(0f, Math.Min(5f, num2));
		float confidence = GetConfidence(communityCardStats.SampleSize);
		float val2 = num * (1f - confidence) + num2 * confidence;
		return Math.Max(0f, Math.Min(5f, val2));
	}

	public float GetAdaptiveRelicScore(string character, string relicId, float staticScore, DeckAnalysis deckAnalysis)
	{
		float num = staticScore;
		CommunityRelicStats communityRelicStats = _db?.GetCommunityRelicStats(character, relicId);
		if (communityRelicStats == null || communityRelicStats.SampleSize < 5)
		{
			return num;
		}
		float num2 = WinRateToScore(communityRelicStats.WinRateWhenPicked);
		float val = (communityRelicStats.PickRate - 0.33f) * 0.9f;
		val = Math.Max(-0.3f, Math.Min(0.3f, val));
		num2 += val;
		float num3 = communityRelicStats.WinRateWhenPicked - communityRelicStats.WinRateWhenSkipped;
		if (Math.Abs(num3) > 0.03f)
		{
			num2 += num3 * 2f;
		}
		num2 = Math.Max(0f, Math.Min(5f, num2));
		float confidence = GetConfidence(communityRelicStats.SampleSize);
		float val2 = num * (1f - confidence) + num2 * confidence;
		return Math.Max(0f, Math.Min(5f, val2));
	}

	private float GetConfidence(int sampleSize)
	{
		if (sampleSize < MinSampleSize)
		{
			return 0f;
		}
		if (sampleSize >= FullConfidenceSampleSize)
		{
			return 1f;
		}
		return (float)(sampleSize - MinSampleSize) / (float)(FullConfidenceSampleSize - MinSampleSize);
	}

	private float WinRateToScore(float winRate)
	{
		float num = (winRate - WinRateForF) / (WinRateForS - WinRateForF);
		return Math.Max(0f, Math.Min(5f, num * 5f));
	}

	private float GetContextScore(Dictionary<string, float> contextStats, DeckAnalysis deckAnalysis)
	{
		if (contextStats == null || contextStats.Count == 0 || deckAnalysis == null)
		{
			return -1f;
		}
		float result = -1f;
		float bestStrength = 0f;
		foreach (ArchetypeMatch detectedArchetype in deckAnalysis.DetectedArchetypes)
		{
			foreach (string coreTag in detectedArchetype.Archetype.CoreTags)
			{
				// Match the context key with the highest count for this core tag
				// e.g., if deck has 7 poison cards, prefer "poison_7+" over "poison_3+"
				float bestWinRate = -1f;
				int bestCount = 0;
				foreach (var kvp in contextStats)
				{
					if (!kvp.Key.StartsWith(coreTag + "_", StringComparison.OrdinalIgnoreCase))
						continue;
					// Parse count from key like "poison_5+"
					int count = 0;
					string suffix = kvp.Key.Substring(coreTag.Length + 1).TrimEnd('+');
					int.TryParse(suffix, out count);
					if (count > bestCount)
					{
						bestCount = count;
						bestWinRate = kvp.Value;
					}
				}
				if (bestWinRate >= 0f && detectedArchetype.Strength > bestStrength)
				{
					result = WinRateToScore(bestWinRate);
					bestStrength = detectedArchetype.Strength;
				}
			}
		}
		return result;
	}
}
