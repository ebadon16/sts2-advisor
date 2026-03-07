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

	public float GetAdaptiveCardScore(string character, string cardId, TierGrade staticTier, DeckAnalysis deckAnalysis)
	{
		float num = (float)staticTier;
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
		return Math.Max(0f, Math.Min(5.5f, val2));
	}

	public float GetAdaptiveRelicScore(string character, string relicId, TierGrade staticTier, DeckAnalysis deckAnalysis)
	{
		float num = (float)staticTier;
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
		return Math.Max(0f, Math.Min(5.5f, val2));
	}

	private float GetConfidence(int sampleSize)
	{
		if (sampleSize <= 5)
		{
			return 0f;
		}
		if (sampleSize >= 50)
		{
			return 1f;
		}
		return (float)(sampleSize - 5) / 45f;
	}

	private float WinRateToScore(float winRate)
	{
		float num = (winRate - 0.35f) / 0.22999999f;
		return Math.Max(0f, Math.Min(5f, num * 5f));
	}

	private float GetContextScore(Dictionary<string, float> contextStats, DeckAnalysis deckAnalysis)
	{
		if (contextStats == null || contextStats.Count == 0 || deckAnalysis == null)
		{
			return -1f;
		}
		float result = -1f;
		float num = 0f;
		foreach (ArchetypeMatch detectedArchetype in deckAnalysis.DetectedArchetypes)
		{
			foreach (string coreTag in detectedArchetype.Archetype.CoreTags)
			{
				string key = coreTag + "_3+";
				if (contextStats.TryGetValue(key, out var value) && detectedArchetype.Strength > num)
				{
					result = WinRateToScore(value);
					num = detectedArchetype.Strength;
				}
			}
		}
		return result;
	}
}
