using System;
using System.Collections.Generic;
using System.Linq;
using QuestceSpire.GameBridge;

namespace QuestceSpire.Core;

public class SynergyScorer
{
	// --- Synergy scoring (graduated, replaces flat 0.5/0.8) ---
	// Base synergy value = 0.3 + archetype.Strength * 0.5 (ranges 0.3–0.8)
	// Diminishing per match: 1st=100%, 2nd=60%, 3rd=30%
	private static readonly float[] SynergyDiminishing = { 1.0f, 0.6f, 0.3f };

	// Saturation: reduce synergy bonus when deck already has many cards with the tag
	// <4 cards = full, 4-6 = 70%, 7+ = 40%
	private const int SaturationSoftCap = 4;
	private const int SaturationHardCap = 7;
	private const float SaturationSoftMult = 0.7f;
	private const float SaturationHardMult = 0.4f;

	private const float AntiSynergyPenalty = 0.6f;

	private const float AntiSynergyCap = -1.2f;

	private const float EarlyFloorDamageBonus = 0.3f;

	private const float MidFloorBlockBonus = 0.2f;

	private const float LateFloorScalingBonus = 0.4f;

	private const float MissingPieceBonus = 0.5f;

	// Deck size thresholds (aligned with community consensus: 20-25 ideal)
	private const int ThinDeckThreshold = 18;

	private const int BloatedDeckThreshold = 25;

	private const float ThinDeckPenalty = -0.2f;

	private const float BloatedDeckPenalty = -0.4f;

	private const float UpgradeBonus = 0.4f;

	// Energy curve: penalize expensive cards in expensive decks
	private const float ExpensiveCardPenalty = -0.3f;
	private const float VeryExpensiveCardPenalty = -0.5f;
	private const float CheapCardBonus = 0.15f;

	// Job gap: bonus for filling a missing functional role
	private const float JobGapMaxBonus = 0.4f;

	// Card type balance
	private const float PowerGapBonus = 0.2f;
	private const float PowerGlutPenalty = -0.2f;
	private const float AoEGapBonus = 0.3f;

	// Job-to-tag mapping for card evaluation
	private static readonly Dictionary<string, string[]> JobTags = new()
	{
		["frontloaded_damage"] = new[] { "damage", "multi_hit", "vulnerable" },
		["aoe"] = new[] { "aoe" },
		["block"] = new[] { "block", "dexterity", "weak" },
		["scaling"] = new[] { "strength", "dexterity", "focus", "poison_scaling", "scaling", "orb", "shiv_synergy" },
		["draw"] = new[] { "draw", "discard" },
	};

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
				// Non-basic: use adaptive data if available, else static tier
				CardTierEntry cardTier = tierEngine.GetCardTier(character, card.Id);
				TierGrade cardGrade = cardTier != null ? TierEngine.ParseGrade(cardTier.BaseTier) : TierGrade.C;
				float cardScore = (float)cardGrade;
				if (adaptiveScorer != null && character != null)
				{
					cardScore = adaptiveScorer.GetAdaptiveCardScore(character, card.Id, cardScore, deckAnalysis);
				}
				// Invert: bad cards get high removal score (5 - score, so F=5→S removal, S=0→F removal)
				score = Math.Max(0f, 5.0f - cardScore);
				grade = TierEngine.ScoreToGrade(score);
				reason = cardScore < 2.0f ? "Weak card — strong removal candidate"
					: cardScore < 3.0f ? "Below average — consider removing"
					: "Decent card — probably keep";
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

	/// <summary>
	/// Score cards for upgrade value by computing the delta between unupgraded and upgraded scores.
	/// Returns list sorted by upgrade delta (best upgrade first).
	/// </summary>
	public List<ScoredCard> ScoreForUpgrade(List<CardInfo> candidates, DeckAnalysis deckAnalysis, string character, int actNumber, int floorNumber, TierEngine tierEngine, AdaptiveScorer adaptiveScorer = null)
	{
		var list = new List<ScoredCard>();
		foreach (var card in candidates)
		{
			string baseId = card.Id.EndsWith("+") ? card.Id.Substring(0, card.Id.Length - 1) : card.Id;

			// Skip already-upgraded cards — can't upgrade them again
			if (card.Upgraded)
			{
				var skip = new ScoredCard
				{
					Id = baseId, Name = card.Name ?? baseId, Type = card.Type, Cost = card.Cost,
					Upgraded = true, FinalScore = 0f, FinalGrade = TierGrade.F, UpgradeDelta = 0f,
					SynergyReasons = new List<string> { "Already upgraded" },
					AntiSynergyReasons = new List<string>(), Notes = "", ScoreSource = "static"
				};
				list.Add(skip);
				continue;
			}

			// Score the card as-is (unupgraded) to get its base strength
			var currentCard = new CardInfo
			{
				Id = baseId, Name = card.Name, Cost = card.Cost,
				Type = card.Type, Rarity = card.Rarity, Upgraded = false, Tags = card.Tags
			};
			CardTierEntry currentTier = tierEngine.GetCardTier(character, baseId);
			ScoredCard currentScored = ScoreCard(currentCard, currentTier, deckAnalysis, actNumber, floorNumber, character, adaptiveScorer);

			// Score the upgraded version
			var upgradedCard = new CardInfo
			{
				Id = baseId + "+", Name = (card.Name ?? baseId) + "+", Cost = card.Cost,
				Type = card.Type, Rarity = card.Rarity, Upgraded = true, Tags = card.Tags
			};
			CardTierEntry upgradedTier = tierEngine.GetCardTier(character, baseId + "+") ?? currentTier;
			ScoredCard upgradedScored = ScoreCard(upgradedCard, upgradedTier, deckAnalysis, actNumber, floorNumber, character, adaptiveScorer);

			float delta = upgradedScored.FinalScore - currentScored.FinalScore;

			// Upgrade priority = base card strength (better cards benefit more from upgrades)
			// Use the unupgraded score as the primary signal since delta is usually flat
			float baseStrength = currentScored.FinalScore;
			// Bonus for cards that have actual separate upgrade tier entries
			bool hasSeparateUpgradeTier = tierEngine.GetCardTier(character, baseId + "+") != null;
			float tierDeltaBonus = hasSeparateUpgradeTier ? delta * 2f : 0f;
			// Final upgrade priority score: base strength + any real tier delta
			float upgradePriority = Math.Min(5.5f, baseStrength + tierDeltaBonus);

			upgradedScored.UpgradeDelta = delta;
			upgradedScored.Id = baseId;
			upgradedScored.Name = card.Name ?? baseId;
			upgradedScored.Upgraded = false;
			upgradedScored.FinalScore = Math.Max(0f, upgradePriority);
			upgradedScored.FinalGrade = TierEngine.ScoreToGrade(upgradedScored.FinalScore);
			upgradedScored.ScoreSource = currentScored.ScoreSource;
			upgradedScored.SynergyReasons.Insert(0, $"Base strength {baseStrength:F1} — upgrade your best cards first");
			list.Add(upgradedScored);
		}
		list.Sort((a, b) => b.FinalScore.CompareTo(a.FinalScore));
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

	/// <summary>Returns saturation multiplier based on how many cards already have this tag.</summary>
	private static float GetSaturationMult(DeckAnalysis deck, string tag)
	{
		if (!deck.TagCounts.TryGetValue(tag, out int count)) return 1f;
		if (count >= SaturationHardCap) return SaturationHardMult;
		if (count >= SaturationSoftCap) return SaturationSoftMult;
		return 1f;
	}

	/// <summary>Checks if a card's synergy tags fill any of the 5 functional "jobs".</summary>
	private static float ComputeJobGapBonus(List<string> cardSynTags, DeckAnalysis deck, List<string> reasons)
	{
		float bestBonus = 0f;
		string bestJob = null;
		foreach (var (jobName, jobTags) in JobTags)
		{
			bool fills = false;
			foreach (string tag in cardSynTags)
			{
				if (Array.IndexOf(jobTags, tag) >= 0) { fills = true; break; }
			}
			if (!fills) continue;
			float gap = deck.JobGap(jobName);
			if (gap <= 0.1f) continue; // job already covered
			float bonus = gap * JobGapMaxBonus;
			if (bonus > bestBonus) { bestBonus = bonus; bestJob = jobName; }
		}
		if (bestBonus > 0.05f && bestJob != null)
		{
			reasons.Add($"+{bestBonus:F1} fills {bestJob.Replace('_', ' ')} gap");
		}
		return bestBonus;
	}

	private ScoredCard ScoreCard(CardInfo card, CardTierEntry tierEntry, DeckAnalysis deckAnalysis, int actNumber, int floorNumber, string character = null, AdaptiveScorer adaptiveScorer = null)
	{
		TierGrade tierGrade;
		float rawScore;
		List<string> computedSynTags = null;
		string scoreSource;
		if (tierEntry != null)
		{
			tierGrade = TierEngine.ParseGrade(tierEntry.BaseTier);
			rawScore = (float)tierGrade;
			scoreSource = "static";
		}
		else if (Plugin.CardPropertyScorer != null)
		{
			var computed = Plugin.CardPropertyScorer.ComputeScore(card.Id);
			rawScore = computed.Score;
			tierGrade = TierEngine.ScoreToGrade(rawScore);
			computedSynTags = computed.SynergyTags;
			scoreSource = "computed";
		}
		else
		{
			tierGrade = TierGrade.C;
			rawScore = (float)tierGrade;
			scoreSource = "default";
		}
		bool usedAdaptive = adaptiveScorer != null && character != null;
		float baseScore = usedAdaptive ? adaptiveScorer.GetAdaptiveCardScore(character, card.Id, rawScore, deckAnalysis) : rawScore;
		if (usedAdaptive) scoreSource = "adaptive";
		float num = baseScore;
		float synergyDelta = 0f;
		float floorAdjust = 0f;
		float deckSizeAdjust = 0f;
		List<string> synReasons = new List<string>();
		List<string> antiReasons = new List<string>();
		List<string> cardSynTags = (tierEntry?.Synergies != null && tierEntry.Synergies.Count > 0)
			? tierEntry.Synergies
			: computedSynTags
			?? (card.Tags != null ? card.Tags.ConvertAll((string t) => t.ToLowerInvariant()) : new List<string>());
		List<string> cardAntiTags = tierEntry?.AntiSynergies ?? new List<string>();

		// === GRADUATED SYNERGY with diminishing returns + saturation ===
		int matchCount = 0;
		foreach (ArchetypeMatch arch in deckAnalysis.DetectedArchetypes)
		{
			if (matchCount >= SynergyDiminishing.Length) break;
			foreach (string tag in cardSynTags)
			{
				if (arch.Archetype.CoreTags.Contains(tag) || arch.Archetype.SupportTags.Contains(tag) || arch.Archetype.Id == tag)
				{
					// Graduated: scales continuously with archetype strength (0.3 at 0.0 → 0.8 at 1.0)
					float baseBoost = 0.3f + arch.Strength * 0.5f;
					// Diminishing per match
					float dimMult = matchCount < SynergyDiminishing.Length ? SynergyDiminishing[matchCount] : 0f;
					// Saturation: reduce if deck already has many of this tag
					float satMult = GetSaturationMult(deckAnalysis, tag);
					float boost = baseBoost * dimMult * satMult;
					num += boost;
					synergyDelta += boost;
					string satNote = satMult < 1f ? $" (sat {satMult:P0})" : "";
					synReasons.Add($"+{boost:F2} {arch.Archetype.DisplayName}{satNote}");
					matchCount++;
					break;
				}
			}
		}

		// === ANTI-SYNERGY — checks both CoreTags AND SupportTags ===
		foreach (ArchetypeMatch arch in deckAnalysis.DetectedArchetypes)
		{
			foreach (string tag in cardAntiTags)
			{
				if (arch.Archetype.CoreTags.Contains(tag) || arch.Archetype.SupportTags.Contains(tag) || arch.Archetype.Id == tag)
				{
					num -= AntiSynergyPenalty;
					synergyDelta -= AntiSynergyPenalty;
					antiReasons.Add($"-{AntiSynergyPenalty:F1} conflicts with {arch.Archetype.DisplayName}");
					break;
				}
			}
		}
		// Cap anti-synergy penalty to prevent excessive stacking
		if (synergyDelta < AntiSynergyCap)
		{
			float excess = AntiSynergyCap - synergyDelta;
			num += excess;
			synergyDelta = AntiSynergyCap;
		}

		// === JOB GAP BONUS — fills a missing functional role ===
		float jobBonus = ComputeJobGapBonus(cardSynTags, deckAnalysis, synReasons);
		num += jobBonus;
		synergyDelta += jobBonus;

		// === ENERGY CURVE — penalize expensive cards in expensive decks ===
		float energyAdjust = 0f;
		if (card.Cost >= 3 && deckAnalysis.AverageCost > 2.2f)
		{
			energyAdjust = VeryExpensiveCardPenalty;
			antiReasons.Add($"{VeryExpensiveCardPenalty:F1} too expensive (avg cost {deckAnalysis.AverageCost:F1})");
		}
		else if (card.Cost >= 3 && deckAnalysis.AverageCost > 1.8f)
		{
			energyAdjust = ExpensiveCardPenalty;
			antiReasons.Add($"{ExpensiveCardPenalty:F1} expensive (avg cost {deckAnalysis.AverageCost:F1})");
		}
		else if (card.Cost == 0 && deckAnalysis.AverageCost > 1.5f)
		{
			energyAdjust = CheapCardBonus;
			synReasons.Add($"+{CheapCardBonus:F2} 0-cost helps energy curve");
		}
		num += energyAdjust;

		// === CARD TYPE BALANCE ===
		string cardType = card.Type?.ToLowerInvariant() ?? "";
		if (cardType == "power" && deckAnalysis.PowerCount == 0 && floorNumber > 6)
		{
			num += PowerGapBonus;
			synReasons.Add($"+{PowerGapBonus:F1} first power (thins draw pool)");
		}
		else if (cardType == "power" && deckAnalysis.PowerCount >= 4)
		{
			num += PowerGlutPenalty;
			antiReasons.Add($"{PowerGlutPenalty:F1} too many powers already");
		}

		// === AoE BONUS (STS2: harsher on single-target-only decks) ===
		bool cardHasAoE = cardSynTags.Contains("aoe");
		if (cardHasAoE)
		{
			int deckAoE = 0;
			deckAnalysis.TagCounts.TryGetValue("aoe", out deckAoE);
			if (deckAoE == 0)
			{
				num += AoEGapBonus;
				synReasons.Add($"+{AoEGapBonus:F1} deck needs AoE");
			}
			else if (deckAoE == 1)
			{
				float smallAoE = 0.1f;
				num += smallAoE;
				synReasons.Add($"+{smallAoE:F1} backup AoE");
			}
		}

		// === FLOOR-AWARE SCORING ===
		bool hasScaling = cardSynTags.Any((string s) => ScalingTags.Contains(s));
		bool hasDefense = cardSynTags.Any((string s) => s == "block" || s == "dexterity" || s == "weak");
		if (floorNumber <= 6 && deckAnalysis.IsUndefined)
		{
			if (cardSynTags.Count >= 2)
			{
				num += EarlyFloorDamageBonus;
				floorAdjust += EarlyFloorDamageBonus;
				synReasons.Add($"+{EarlyFloorDamageBonus:F1} flexible (early floors)");
			}
		}
		else if (floorNumber >= 19 && hasScaling)
		{
			num += LateFloorScalingBonus;
			floorAdjust += LateFloorScalingBonus;
			synReasons.Add($"+{LateFloorScalingBonus:F1} scaling (late floors)");
		}
		if (floorNumber >= 7 && !deckAnalysis.IsUndefined && hasDefense && !(floorNumber >= 19 && hasScaling))
		{
			num += MidFloorBlockBonus;
			floorAdjust += MidFloorBlockBonus;
			synReasons.Add($"+{MidFloorBlockBonus:F1} defense (mid floors)");
		}

		// === MISSING PIECE BONUS ===
		bool foundMissing = false;
		foreach (ArchetypeMatch arch in deckAnalysis.DetectedArchetypes)
		{
			if (foundMissing) break;
			if (!(arch.Strength > 0.3f) || !(arch.Strength < 0.7f)) continue;
			foreach (string tag in cardSynTags)
			{
				if (arch.Archetype.SupportTags.Contains(tag))
				{
					string key = tag.ToLowerInvariant();
					if (!deckAnalysis.TagCounts.TryGetValue(key, out var val) || val == 0)
					{
						num += MissingPieceBonus;
						synergyDelta += MissingPieceBonus;
						synReasons.Add($"+{MissingPieceBonus:F1} fills gap: {tag}");
						foundMissing = true;
						break;
					}
				}
			}
		}

		// === DECK SIZE (tighter thresholds: 18 thin, 25 bloated) ===
		int deckSize = deckAnalysis.TotalCards;
		if (deckSize <= ThinDeckThreshold && num < 2.5f)
		{
			num += ThinDeckPenalty;
			deckSizeAdjust += ThinDeckPenalty;
			antiReasons.Add($"{ThinDeckPenalty:F1} be selective (thin deck)");
		}
		else if (deckSize >= BloatedDeckThreshold && num < 3.5f)
		{
			num += BloatedDeckPenalty;
			deckSizeAdjust += BloatedDeckPenalty;
			antiReasons.Add($"{BloatedDeckPenalty:F1} only take great cards (bloated deck)");
		}

		// === UPGRADE BONUS ===
		float upgradeAdjust = 0f;
		if (card.Upgraded)
		{
			num += UpgradeBonus;
			upgradeAdjust += UpgradeBonus;
			synReasons.Add($"+{UpgradeBonus:F1} upgraded");
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
			SynergyReasons = synReasons,
			AntiSynergyReasons = antiReasons,
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
		float baseScore = usedAdaptive ? adaptiveScorer.GetAdaptiveRelicScore(character, relic.Id, (float)tierGrade, deckAnalysis) : (float)tierGrade;
		string scoreSource = tierEntry == null ? "default" : usedAdaptive ? "adaptive" : "static";
		float num = baseScore;
		float synergyDelta = 0f;
		float floorAdjust = 0f;
		List<string> synReasons = new List<string>();
		List<string> antiReasons = new List<string>();
		List<string> relicSynTags = tierEntry?.Synergies ?? new List<string>();
		List<string> relicAntiTags = tierEntry?.AntiSynergies ?? new List<string>();
		// Graduated synergy with diminishing returns (same as cards)
		int matchCount = 0;
		foreach (ArchetypeMatch arch in deckAnalysis.DetectedArchetypes)
		{
			if (matchCount >= SynergyDiminishing.Length) break;
			foreach (string tag in relicSynTags)
			{
				if (arch.Archetype.CoreTags.Contains(tag) || arch.Archetype.SupportTags.Contains(tag) || arch.Archetype.Id == tag)
				{
					float baseBoost = 0.3f + arch.Strength * 0.5f;
					float dimMult = matchCount < SynergyDiminishing.Length ? SynergyDiminishing[matchCount] : 0f;
					float boost = baseBoost * dimMult;
					num += boost;
					synergyDelta += boost;
					synReasons.Add($"+{boost:F2} {arch.Archetype.DisplayName}");
					matchCount++;
					break;
				}
			}
		}
		// Anti-synergy — checks both CoreTags AND SupportTags
		foreach (ArchetypeMatch arch in deckAnalysis.DetectedArchetypes)
		{
			foreach (string tag in relicAntiTags)
			{
				if (arch.Archetype.CoreTags.Contains(tag) || arch.Archetype.SupportTags.Contains(tag) || arch.Archetype.Id == tag)
				{
					num -= AntiSynergyPenalty;
					synergyDelta -= AntiSynergyPenalty;
					antiReasons.Add($"-{AntiSynergyPenalty:F1} conflicts with {arch.Archetype.DisplayName}");
					break;
				}
			}
		}
		// Cap anti-synergy penalty to prevent excessive stacking
		if (synergyDelta < AntiSynergyCap)
		{
			float excess = AntiSynergyCap - synergyDelta;
			num += excess;
			synergyDelta = AntiSynergyCap;
		}
		// Floor-aware: late-game scaling bonus for relics too
		if (floorNumber >= 19 && relicSynTags.Any((string s) => ScalingTags.Contains(s)))
		{
			num += LateFloorScalingBonus;
			floorAdjust += LateFloorScalingBonus;
			synReasons.Add($"+{LateFloorScalingBonus:F1} scaling (late floors)");
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
			SynergyReasons = synReasons,
			AntiSynergyReasons = antiReasons,
			Notes = (tierEntry?.Notes ?? ""),
			BaseScore = baseScore,
			SynergyDelta = synergyDelta,
			FloorAdjust = floorAdjust,
			DeckSizeAdjust = 0f,
			ScoreSource = scoreSource
		};
	}
}
