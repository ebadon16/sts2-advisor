using System;
using System.Collections.Generic;
using System.Linq;
using STS2Advisor.GameBridge;

namespace STS2Advisor.Core
{
    public class SynergyScorer
    {
        private const float SynergyBoostPerMatch = 0.5f;
        private const float StrongSynergyBoost = 0.8f;      // When archetype strength > 0.5
        private const float AntiSynergyPenalty = 0.6f;
        private const float EarlyGameFlexBonus = 0.3f;       // Bonus for flexible cards in Act 1
        private const float LateGameScalingBonus = 0.4f;     // Bonus for scaling in Act 3+
        private const float MissingPieceBonus = 0.5f;        // Bonus for cards that fill a gap

        private static readonly HashSet<string> ScalingTags = new HashSet<string>
        {
            "strength", "dexterity", "focus", "poison_scaling",
            "scaling", "orb", "shiv_synergy"
        };

        public List<ScoredCard> ScoreOfferings(
            List<CardInfo> offerings,
            DeckAnalysis deckAnalysis,
            string character,
            int actNumber,
            TierEngine tierEngine,
            AdaptiveScorer adaptiveScorer = null)
        {
            var results = new List<ScoredCard>();

            foreach (var card in offerings)
            {
                var tierEntry = tierEngine.GetCardTier(character, card.Id);
                var scored = ScoreCard(card, tierEntry, deckAnalysis, actNumber,
                    character, adaptiveScorer);
                results.Add(scored);
            }

            // Sort by final score descending
            results.Sort((a, b) => b.FinalScore.CompareTo(a.FinalScore));

            // Mark best pick
            if (results.Count > 0)
                results[0].IsBestPick = true;

            return results;
        }

        public List<ScoredRelic> ScoreRelicOfferings(
            List<RelicInfo> offerings,
            DeckAnalysis deckAnalysis,
            string character,
            int actNumber,
            TierEngine tierEngine,
            AdaptiveScorer adaptiveScorer = null)
        {
            var results = new List<ScoredRelic>();

            foreach (var relic in offerings)
            {
                var tierEntry = tierEngine.GetRelicTier(character, relic.Id);
                var scored = ScoreRelic(relic, tierEntry, deckAnalysis, actNumber,
                    character, adaptiveScorer);
                results.Add(scored);
            }

            results.Sort((a, b) => b.FinalScore.CompareTo(a.FinalScore));

            if (results.Count > 0)
                results[0].IsBestPick = true;

            return results;
        }

        private ScoredCard ScoreCard(CardInfo card, CardTierEntry tierEntry,
            DeckAnalysis deckAnalysis, int actNumber,
            string character = null, AdaptiveScorer adaptiveScorer = null)
        {
            TierGrade baseTier = tierEntry != null
                ? TierEngine.ParseGrade(tierEntry.BaseTier)
                : TierGrade.C;

            // Use adaptive score (blended static + community data) when available
            float score;
            if (adaptiveScorer != null && character != null)
                score = adaptiveScorer.GetAdaptiveCardScore(character, card.Id, baseTier, deckAnalysis);
            else
                score = (float)baseTier;
            var synergyReasons = new List<string>();
            var antiSynergyReasons = new List<string>();

            List<string> synergies = tierEntry?.Synergies ?? card.Tags;
            List<string> antiSynergies = tierEntry?.AntiSynergies ?? new List<string>();

            // Synergy bonuses
            foreach (var archMatch in deckAnalysis.DetectedArchetypes)
            {
                foreach (string syn in synergies)
                {
                    if (archMatch.Archetype.CoreTags.Contains(syn) ||
                        archMatch.Archetype.SupportTags.Contains(syn))
                    {
                        float boost = archMatch.Strength > 0.5f
                            ? StrongSynergyBoost
                            : SynergyBoostPerMatch;
                        score += boost;
                        synergyReasons.Add($"+{boost:F1} synergy with {archMatch.Archetype.DisplayName}");
                        break; // One bonus per archetype
                    }
                }
            }

            // Anti-synergy penalties
            foreach (var archMatch in deckAnalysis.DetectedArchetypes)
            {
                foreach (string anti in antiSynergies)
                {
                    if (archMatch.Archetype.CoreTags.Contains(anti) ||
                        archMatch.Archetype.Id == anti)
                    {
                        score -= AntiSynergyPenalty;
                        antiSynergyReasons.Add($"-{AntiSynergyPenalty:F1} conflicts with {archMatch.Archetype.DisplayName}");
                        break;
                    }
                }
            }

            // Act-based adjustments
            if (actNumber <= 1 && deckAnalysis.IsUndefined)
            {
                // Early game: favor flexible, high-floor cards
                if (synergies.Count >= 2)
                {
                    score += EarlyGameFlexBonus;
                    synergyReasons.Add($"+{EarlyGameFlexBonus:F1} flexible (early game)");
                }
            }
            else if (actNumber >= 3)
            {
                // Late game: favor scaling
                bool hasScaling = synergies.Any(s => ScalingTags.Contains(s));
                if (hasScaling)
                {
                    score += LateGameScalingBonus;
                    synergyReasons.Add($"+{LateGameScalingBonus:F1} scaling (late game)");
                }
            }

            // Missing piece detection: if deck has an archetype but lacks scaling
            foreach (var archMatch in deckAnalysis.DetectedArchetypes)
            {
                if (archMatch.Strength > 0.3f && archMatch.Strength < 0.7f)
                {
                    // Check if this card provides a support tag the deck is missing
                    foreach (string syn in synergies)
                    {
                        if (archMatch.Archetype.SupportTags.Contains(syn))
                        {
                            string tagKey = syn.ToLowerInvariant();
                            int deckCount = deckAnalysis.TagCounts.TryGetValue(tagKey, out int c) ? c : 0;
                            if (deckCount == 0)
                            {
                                score += MissingPieceBonus;
                                synergyReasons.Add($"+{MissingPieceBonus:F1} fills gap: {syn}");
                                break;
                            }
                        }
                    }
                }
            }

            score = Math.Max(score, 0f);

            return new ScoredCard
            {
                Id = card.Id,
                BaseTier = baseTier,
                FinalScore = score,
                FinalGrade = TierEngine.ScoreToGrade(score),
                SynergyReasons = synergyReasons,
                AntiSynergyReasons = antiSynergyReasons,
                Notes = tierEntry?.Notes ?? ""
            };
        }

        private ScoredRelic ScoreRelic(RelicInfo relic, RelicTierEntry tierEntry,
            DeckAnalysis deckAnalysis, int actNumber,
            string character = null, AdaptiveScorer adaptiveScorer = null)
        {
            TierGrade baseTier = tierEntry != null
                ? TierEngine.ParseGrade(tierEntry.BaseTier)
                : TierGrade.C;

            float score;
            if (adaptiveScorer != null && character != null)
                score = adaptiveScorer.GetAdaptiveRelicScore(character, relic.Id, baseTier, deckAnalysis);
            else
                score = (float)baseTier;
            var synergyReasons = new List<string>();
            var antiSynergyReasons = new List<string>();

            List<string> synergies = tierEntry?.Synergies ?? new List<string>();
            List<string> antiSynergies = tierEntry?.AntiSynergies ?? new List<string>();

            foreach (var archMatch in deckAnalysis.DetectedArchetypes)
            {
                foreach (string syn in synergies)
                {
                    if (archMatch.Archetype.CoreTags.Contains(syn) ||
                        archMatch.Archetype.SupportTags.Contains(syn) ||
                        archMatch.Archetype.Id == syn)
                    {
                        float boost = archMatch.Strength > 0.5f
                            ? StrongSynergyBoost
                            : SynergyBoostPerMatch;
                        score += boost;
                        synergyReasons.Add($"+{boost:F1} synergy with {archMatch.Archetype.DisplayName}");
                        break;
                    }
                }
            }

            foreach (var archMatch in deckAnalysis.DetectedArchetypes)
            {
                foreach (string anti in antiSynergies)
                {
                    if (archMatch.Archetype.CoreTags.Contains(anti) ||
                        archMatch.Archetype.Id == anti)
                    {
                        score -= AntiSynergyPenalty;
                        antiSynergyReasons.Add($"-{AntiSynergyPenalty:F1} conflicts with {archMatch.Archetype.DisplayName}");
                        break;
                    }
                }
            }

            score = Math.Max(score, 0f);

            return new ScoredRelic
            {
                Id = relic.Id,
                BaseTier = baseTier,
                FinalScore = score,
                FinalGrade = TierEngine.ScoreToGrade(score),
                SynergyReasons = synergyReasons,
                AntiSynergyReasons = antiSynergyReasons,
                Notes = tierEntry?.Notes ?? ""
            };
        }
    }
}
