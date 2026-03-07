using System;
using System.Collections.Generic;
using QuestceSpire.GameBridge;

namespace QuestceSpire.Core;

public class DeckAnalyzer
{
	public DeckAnalysis Analyze(string character, List<CardInfo> deck, TierEngine tierEngine = null)
	{
		DeckAnalysis deckAnalysis = new DeckAnalysis
		{
			Character = character,
			TotalCards = deck.Count
		};
		foreach (CardInfo item3 in deck)
		{
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string tag in item3.Tags)
			{
				string item = tag.ToLowerInvariant();
				hashSet.Add(item);
			}
			if (tierEngine != null)
			{
				CardTierEntry cardTier = tierEngine.GetCardTier(character, item3.Id);
				if (cardTier?.Synergies != null)
				{
					foreach (string synergy in cardTier.Synergies)
					{
						string item2 = synergy.ToLowerInvariant();
						hashSet.Add(item2);
					}
				}
			}
			foreach (string item4 in hashSet)
			{
				IncrementTag(deckAnalysis.TagCounts, item4);
			}
			// Energy curve bucketing (0-5+)
			int costBucket = Math.Min(item3.Cost, 5);
			if (deckAnalysis.EnergyCurve.ContainsKey(costBucket))
				deckAnalysis.EnergyCurve[costBucket]++;
			else
				deckAnalysis.EnergyCurve[costBucket] = 1;
			// Type counting
			string typeLower = item3.Type?.ToLowerInvariant() ?? "";
			if (typeLower == "attack") deckAnalysis.AttackCount++;
			else if (typeLower == "skill") deckAnalysis.SkillCount++;
			else if (typeLower == "power") deckAnalysis.PowerCount++;
		}
		string key = character?.ToLowerInvariant() ?? "";
		if (!ArchetypeDefinitions.ByCharacter.TryGetValue(key, out var value))
		{
			return deckAnalysis;
		}
		foreach (Archetype item5 in value)
		{
			int num = 0;
			foreach (string coreTag in item5.CoreTags)
			{
				if (deckAnalysis.TagCounts.TryGetValue(coreTag, out var value2))
				{
					num += value2;
				}
			}
			int num2 = 0;
			foreach (string supportTag in item5.SupportTags)
			{
				if (deckAnalysis.TagCounts.TryGetValue(supportTag, out var value3))
				{
					num2 += value3;
				}
			}
			bool num3 = num >= item5.CoreThreshold;
			bool flag = num2 >= item5.SupportThreshold;
			if (num3 || (num >= 2 && flag))
			{
				float num4 = (float)num / ((float)item5.CoreThreshold * 2f);
				if (flag)
				{
					num4 += 0.2f;
				}
				// Density normalization: scale by how concentrated the archetype is
				// A "normal" deck is ~20 cards. Larger decks dilute archetypes.
				if (deckAnalysis.TotalCards > 0)
				{
					float densityFactor = 20f / Math.Max(deckAnalysis.TotalCards, 10f);
					densityFactor = Math.Max(0.7f, Math.Min(1.3f, densityFactor));
					num4 *= densityFactor;
				}
				num4 = Math.Min(num4, 1f);
				deckAnalysis.DetectedArchetypes.Add(new ArchetypeMatch
				{
					Archetype = item5,
					Strength = num4,
					CoreCount = num,
					SupportCount = num2
				});
			}
		}
		deckAnalysis.DetectedArchetypes.Sort((ArchetypeMatch a, ArchetypeMatch b) => b.Strength.CompareTo(a.Strength));
		return deckAnalysis;
	}

	private static void IncrementTag(Dictionary<string, int> tagCounts, string key)
	{
		if (tagCounts.ContainsKey(key))
		{
			tagCounts[key]++;
		}
		else
		{
			tagCounts[key] = 1;
		}
	}
}
