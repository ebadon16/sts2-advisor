using System;
using System.Collections.Generic;
using System.Linq;
using QuestceSpire.GameBridge;

namespace QuestceSpire.Core;

public class SynergyScorer
{
	private const float SynergyBoostPerMatch = 0.5f;

	private const float StrongSynergyBoost = 0.8f;

	private const float AntiSynergyPenalty = 0.6f;

	private const float AntiSynergyCap = -1.2f;

	private const float EarlyFloorDamageBonus = 0.3f;

	private const float MidFloorBlockBonus = 0.2f;

	private const float LateFloorScalingBonus = 0.4f;

	private const float MissingPieceBonus = 0.5f;

	private const int ThinDeckThreshold = 15;

	private const int BloatedDeckThreshold = 30;

	private const float ThinDeckPenalty = -0.2f;

	private const float BloatedDeckPenalty = -0.4f;

	private const float UpgradeBonus = 0.4f;

	private static readonly HashSet<string> ScalingTags = new HashSet<string> { "strength", "dexterity", "focus", "poison_scaling", "scaling", "orb", "shiv_synergy" };

	public List<ScoredCard> ScoreOfferings(List<CardInfo> offerings, DeckAnalysis deckAnalysis, string character, int actNumber, int floorNumber, TierEngine tierEngine, AdaptiveScorer adaptiveScorer = null)
	{
		List<ScoredCard> list = new List<ScoredCard>();
		foreach (CardInfo offering in offerings)
		{
			CardTierEntry cardTier = tierEngine.GetCardTier(character, offering.Id);
			ScoredCard item = ScoreCard(offering, cardTier, deckAnalysis, actNumber, floorNumber, character, adaptiveScorer);
			item.Price = offering.Price;
			list.Add(item);
		}
		// Mark best pick without reordering — preserve game's card order for badge alignment
		if (list.Count > 0)
		{
			int bestIdx = 0;
			for (int i = 1; i < list.Count; i++)
			{
				if (list[i].FinalScore > list[bestIdx].FinalScore)
					bestIdx = i;
			}
			list[bestIdx].IsBestPick = true;
		}
		return list;
	}

	/// <summary>
	/// Rank deck cards for removal using simple priority buckets.
	/// Returns list sorted by removal priority (best to remove first).
	/// </summary>
	public List<ScoredCard> ScoreForRemoval(List<CardInfo> deck, DeckAnalysis deckAnalysis, string character, int actNumber, int floorNumber, TierEngine tierEngine, AdaptiveScorer adaptiveScorer = null)
	{
		List<ScoredCard> list = new List<ScoredCard>();
		foreach (CardInfo card in deck)
		{
			string type = card.Type?.ToLowerInvariant() ?? "";
			bool isCurse = type == "curse" || type == "status";
			bool isStrike = card.Tags != null && card.Tags.Contains("strike");
			bool isDefend = card.Tags != null && card.Tags.Contains("defend");

			float score;
			string reason;
			TierGrade grade;

			if (isCurse)
			{
				score = 5.0f; grade = TierGrade.S;
				reason = "Curse/Status — always remove first";
			}
			else if ((isStrike || isDefend) && !card.Upgraded)
			{
				score = 4.0f; grade = TierGrade.A;
				reason = isStrike ? "Basic Strike — safe removal to thin deck" : "Basic Defend — safe removal to thin deck";
			}
			else if ((isStrike || isDefend) && card.Upgraded)
			{
				score = 3.0f; grade = TierGrade.B;
				reason = "Upgraded basic — still worth removing in lean decks";
			}
			else
			{
				// Non-basic: use tier data to find genuinely bad cards
				CardTierEntry cardTier = tierEngine.GetCardTier(character, card.Id);
				TierGrade cardGrade = cardTier != null ? TierEngine.ParseGrade(cardTier.BaseTier) : TierGrade.C;
				// Invert: bad cards get high removal score
				score = Math.Max(0f, 3.0f - (float)cardGrade);
				grade = TierEngine.ScoreToGrade(score);
				reason = cardGrade <= TierGrade.D ? "Low-tier card — consider removing" : "Decent card — probably keep";
			}

			list.Add(new ScoredCard
			{
				Id = card.Id,
				Name = card.Name ?? card.Id,
				Type = card.Type,
				Cost = card.Cost,
				BaseTier = grade,
				FinalScore = score,
				FinalGrade = grade,
				SynergyReasons = new List<string> { reason },
				AntiSynergyReasons = new List<string>(),
				Notes = "",
				Upgraded = card.Upgraded,
				ScoreSource = "removal"
			});
		}
		list.Sort((ScoredCard a, ScoredCard b) => b.FinalScore.CompareTo(a.FinalScore));
		if (list.Count > 0)
			list[0].IsBestPick = true;
		return list;
	}

	public List<ScoredRelic> ScoreRelicOfferings(List<RelicInfo> offerings, DeckAnalysis deckAnalysis, string character, int actNumber, int floorNumber, TierEngine tierEngine, AdaptiveScorer adaptiveScorer = null)
	{
		List<ScoredRelic> list = new List<ScoredRelic>();
		foreach (RelicInfo offering in offerings)
		{
			RelicTierEntry relicTier = tierEngine.GetRelicTier(character, offering.Id);
			ScoredRelic item = ScoreRelic(offering, relicTier, deckAnalysis, actNumber, floorNumber, character, adaptiveScorer);
			item.Price = offering.Price;
			list.Add(item);
		}
		// Mark best pick without reordering — preserve game's order for badge alignment
		if (list.Count > 0)
		{
			int bestIdx = 0;
			for (int i = 1; i < list.Count; i++)
			{
				if (list[i].FinalScore > list[bestIdx].FinalScore)
					bestIdx = i;
			}
			list[bestIdx].IsBestPick = true;
		}
		return list;
	}

	private ScoredCard ScoreCard(CardInfo card, CardTierEntry tierEntry, DeckAnalysis deckAnalysis, int actNumber, int floorNumber, string character = null, AdaptiveScorer adaptiveScorer = null)
	{
		TierGrade tierGrade;
		List<string> computedSynTags = null;
		string scoreSource;
		if (tierEntry != null)
		{
			tierGrade = TierEngine.ParseGrade(tierEntry.BaseTier);
			scoreSource = "static";
		}
		else if (Plugin.CardPropertyScorer != null)
		{
			var computed = Plugin.CardPropertyScorer.ComputeScore(card.Id);
			tierGrade = TierEngine.ScoreToGrade(computed.Score);
			computedSynTags = computed.SynergyTags;
			scoreSource = "computed";
		}
		else
		{
			tierGrade = TierGrade.C;
			scoreSource = "default";
		}
		bool usedAdaptive = adaptiveScorer != null && character != null;
		float baseScore = usedAdaptive ? adaptiveScorer.GetAdaptiveCardScore(character, card.Id, tierGrade, deckAnalysis) : (float)tierGrade;
		if (usedAdaptive) scoreSource = "adaptive";
		float num = baseScore;
		float synergyDelta = 0f;
		float floorAdjust = 0f;
		float deckSizeAdjust = 0f;
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		List<string> list3 = tierEntry?.Synergies
			?? computedSynTags
			?? (card.Tags != null ? card.Tags.ConvertAll((string t) => t.ToLowerInvariant()) : new List<string>());
		List<string> list4 = tierEntry?.AntiSynergies ?? new List<string>();
		int num2 = 0;
		foreach (ArchetypeMatch detectedArchetype in deckAnalysis.DetectedArchetypes)
		{
			if (num2 >= 2)
			{
				break;
			}
			foreach (string item in list3)
			{
				if (detectedArchetype.Archetype.CoreTags.Contains(item) || detectedArchetype.Archetype.SupportTags.Contains(item) || detectedArchetype.Archetype.Id == item)
				{
					float num3 = ((detectedArchetype.Strength > 0.5f) ? StrongSynergyBoost : SynergyBoostPerMatch);
					num += num3;
					synergyDelta += num3;
					list.Add($"+{num3:F1} synergy with {detectedArchetype.Archetype.DisplayName}");
					num2++;
					break;
				}
			}
		}
		foreach (ArchetypeMatch detectedArchetype2 in deckAnalysis.DetectedArchetypes)
		{
			foreach (string item2 in list4)
			{
				if (detectedArchetype2.Archetype.CoreTags.Contains(item2) || detectedArchetype2.Archetype.Id == item2)
				{
					num -= AntiSynergyPenalty;
					synergyDelta -= AntiSynergyPenalty;
					list2.Add($"-{AntiSynergyPenalty:F1} conflicts with {detectedArchetype2.Archetype.DisplayName}");
					break;
				}
			}
		}
		// Cap anti-synergy penalty to prevent excessive stacking
		float antiTotal = list2.Count * -AntiSynergyPenalty;
		if (antiTotal < AntiSynergyCap)
		{
			float excess = AntiSynergyCap - antiTotal;
			num += excess;
			synergyDelta += excess;
		}
		// Floor-aware scoring (replaces act-based logic)
		if (floorNumber <= 6 && deckAnalysis.IsUndefined)
		{
			if (list3.Count >= 2)
			{
				num += EarlyFloorDamageBonus;
				floorAdjust += EarlyFloorDamageBonus;
				list.Add($"+{EarlyFloorDamageBonus:F1} flexible (early floors)");
			}
		}
		else if (floorNumber >= 19 && list3.Any((string s) => ScalingTags.Contains(s)))
		{
			num += LateFloorScalingBonus;
			floorAdjust += LateFloorScalingBonus;
			list.Add($"+{LateFloorScalingBonus:F1} scaling (late floors)");
		}
		else if (floorNumber >= 7 && list3.Any((string s) => s == "block" || s == "dexterity" || s == "weak"))
		{
			num += MidFloorBlockBonus;
			floorAdjust += MidFloorBlockBonus;
			list.Add($"+{MidFloorBlockBonus:F1} defense (mid floors)");
		}
		bool flag = false;
		foreach (ArchetypeMatch detectedArchetype3 in deckAnalysis.DetectedArchetypes)
		{
			if (flag)
			{
				break;
			}
			if (!(detectedArchetype3.Strength > 0.3f) || !(detectedArchetype3.Strength < 0.7f))
			{
				continue;
			}
			foreach (string item3 in list3)
			{
				if (detectedArchetype3.Archetype.SupportTags.Contains(item3))
				{
					string key = item3.ToLowerInvariant();
					if (!deckAnalysis.TagCounts.TryGetValue(key, out var value) || value == 0)
					{
						num += 0.5f;
						synergyDelta += 0.5f;
						list.Add($"+{0.5f:F1} fills gap: {item3}");
						flag = true;
						break;
					}
				}
			}
		}
		// Deck size awareness
		int deckSize = deckAnalysis.TotalCards;
		if (deckSize <= ThinDeckThreshold && tierGrade <= TierGrade.C)
		{
			num += ThinDeckPenalty;
			deckSizeAdjust += ThinDeckPenalty;
			list2.Add($"{ThinDeckPenalty:F1} be selective (thin deck)");
		}
		else if (deckSize >= BloatedDeckThreshold && tierGrade <= TierGrade.B)
		{
			num += BloatedDeckPenalty;
			deckSizeAdjust += BloatedDeckPenalty;
			list2.Add($"{BloatedDeckPenalty:F1} only take great cards (bloated deck)");
		}
		// Upgrade bonus
		float upgradeAdjust = 0f;
		if (card.Upgraded)
		{
			num += UpgradeBonus;
			upgradeAdjust += UpgradeBonus;
			list.Add($"+{UpgradeBonus:F1} upgraded");
		}
		num = Math.Max(0f, Math.Min(6.0f, num));
		return new ScoredCard
		{
			Id = card.Id,
			Name = (card.Name ?? card.Id),
			Type = card.Type,
			Cost = card.Cost,
			BaseTier = tierGrade,
			FinalScore = num,
			FinalGrade = TierEngine.ScoreToGrade(num),
			SynergyReasons = list,
			AntiSynergyReasons = list2,
			Notes = (tierEntry?.Notes ?? ""),
			BaseScore = baseScore,
			SynergyDelta = synergyDelta,
			FloorAdjust = floorAdjust,
			DeckSizeAdjust = deckSizeAdjust,
			Upgraded = card.Upgraded,
			UpgradeAdjust = upgradeAdjust,
			ScoreSource = scoreSource
		};
	}

	private ScoredRelic ScoreRelic(RelicInfo relic, RelicTierEntry tierEntry, DeckAnalysis deckAnalysis, int actNumber, int floorNumber, string character = null, AdaptiveScorer adaptiveScorer = null)
	{
		TierGrade tierGrade = ((tierEntry != null) ? TierEngine.ParseGrade(tierEntry.BaseTier) : TierGrade.C);
		bool usedAdaptive = adaptiveScorer != null && character != null;
		float baseScore = usedAdaptive ? adaptiveScorer.GetAdaptiveRelicScore(character, relic.Id, tierGrade, deckAnalysis) : (float)tierGrade;
		string scoreSource = tierEntry == null ? "default" : usedAdaptive ? "adaptive" : "static";
		float num = baseScore;
		float synergyDelta = 0f;
		float floorAdjust = 0f;
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		List<string> list3 = tierEntry?.Synergies ?? new List<string>();
		List<string> list4 = tierEntry?.AntiSynergies ?? new List<string>();
		int archetypeBonuses = 0;
		foreach (ArchetypeMatch detectedArchetype in deckAnalysis.DetectedArchetypes)
		{
			if (archetypeBonuses >= 2) break;
			foreach (string item in list3)
			{
				if (detectedArchetype.Archetype.CoreTags.Contains(item) || detectedArchetype.Archetype.SupportTags.Contains(item) || detectedArchetype.Archetype.Id == item)
				{
					float num2 = ((detectedArchetype.Strength > 0.5f) ? StrongSynergyBoost : SynergyBoostPerMatch);
					num += num2;
					synergyDelta += num2;
					list.Add($"+{num2:F1} synergy with {detectedArchetype.Archetype.DisplayName}");
					archetypeBonuses++;
					break;
				}
			}
		}
		foreach (ArchetypeMatch detectedArchetype2 in deckAnalysis.DetectedArchetypes)
		{
			foreach (string item2 in list4)
			{
				if (detectedArchetype2.Archetype.CoreTags.Contains(item2) || detectedArchetype2.Archetype.Id == item2)
				{
					num -= AntiSynergyPenalty;
					synergyDelta -= AntiSynergyPenalty;
					list2.Add($"-{AntiSynergyPenalty:F1} conflicts with {detectedArchetype2.Archetype.DisplayName}");
					break;
				}
			}
		}
		// Cap anti-synergy penalty to prevent excessive stacking
		float antiTotal = list2.Count * -AntiSynergyPenalty;
		if (antiTotal < AntiSynergyCap)
		{
			float excess = AntiSynergyCap - antiTotal;
			num += excess;
			synergyDelta += excess;
		}
		// Floor-aware: late-game scaling bonus for relics too
		if (floorNumber >= 19 && list3.Any((string s) => ScalingTags.Contains(s)))
		{
			num += LateFloorScalingBonus;
			floorAdjust += LateFloorScalingBonus;
			list.Add($"+{LateFloorScalingBonus:F1} scaling (late floors)");
		}
		num = Math.Max(0f, Math.Min(6.0f, num));
		return new ScoredRelic
		{
			Id = relic.Id,
			Name = (relic.Name ?? relic.Id),
			Rarity = relic.Rarity,
			BaseTier = tierGrade,
			FinalScore = num,
			FinalGrade = TierEngine.ScoreToGrade(num),
			SynergyReasons = list,
			AntiSynergyReasons = list2,
			Notes = (tierEntry?.Notes ?? ""),
			BaseScore = baseScore,
			SynergyDelta = synergyDelta,
			FloorAdjust = floorAdjust,
			DeckSizeAdjust = 0f,
			ScoreSource = scoreSource
		};
	}
}
