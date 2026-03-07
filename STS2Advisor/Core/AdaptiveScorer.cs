using System;
using System.Collections.Generic;
using STS2Advisor.Tracking;

namespace STS2Advisor.Core
{
    /// <summary>
    /// Blends static JSON tier data with local play statistics.
    ///
    /// When sample size is low, static tiers dominate.
    /// As the player plays more runs, local win rate data takes over.
    /// For characters with no static tier data (Regent, Necrobinder, Deprived),
    /// scores start at C-tier but improve as the player accumulates data.
    /// </summary>
    public class AdaptiveScorer
    {
        // How many times a card must be offered before local data
        // starts influencing the score. Below this, static tiers dominate.
        private const int MinSampleSize = 5;

        // At this sample size, local data is weighted at 100%.
        // Between Min and Full, it's linearly interpolated.
        private const int FullConfidenceSampleSize = 50;

        // Community win rate is converted to a 0-5 scale to blend with static tiers.
        // These define the mapping: 60% win rate = S-tier equivalent, etc.
        private const float WinRateForS = 0.58f;
        private const float WinRateForF = 0.35f;

        private readonly RunDatabase _db;

        public AdaptiveScorer(RunDatabase db)
        {
            _db = db;
        }

        /// <summary>
        /// Returns an adjusted base score for a card, blending static tier with
        /// community win rate data. Call this instead of raw TierEngine.ParseGrade()
        /// when community data is available.
        /// </summary>
        public float GetAdaptiveCardScore(string character, string cardId,
            TierGrade staticTier, DeckAnalysis deckAnalysis)
        {
            float staticScore = (float)staticTier;

            var stats = _db?.GetCommunityCardStats(character, cardId);
            if (stats == null || stats.SampleSize < MinSampleSize)
                return staticScore;

            // Base community score from overall win rate when picked
            float communityScore = WinRateToScore(stats.WinRateWhenPicked);

            // Check for archetype-specific context stats
            float contextScore = GetContextScore(stats.ArchetypeContext, deckAnalysis);
            if (contextScore >= 0)
            {
                // Blend overall community score with context-specific score
                // Context is more specific, so weight it higher when available
                communityScore = communityScore * 0.4f + contextScore * 0.6f;
            }

            // Pick rate as a signal: if everyone picks it, it's probably good.
            // Slight nudge (max +0.3) based on pick rate.
            float pickRateNudge = (stats.PickRate - 0.33f) * 0.9f; // 0.33 = random chance with 3 offerings
            pickRateNudge = Math.Max(-0.3f, Math.Min(0.3f, pickRateNudge));
            communityScore += pickRateNudge;

            // Win rate differential: if win rate when picked >> when skipped, strong signal
            float winDiff = stats.WinRateWhenPicked - stats.WinRateWhenSkipped;
            if (Math.Abs(winDiff) > 0.03f) // Only if meaningful difference
                communityScore += winDiff * 2f; // Scale up the signal

            // Clamp community score consistently after all adjustments
            communityScore = Math.Max(0f, Math.Min(5f, communityScore));

            // Blend static and community based on confidence
            float confidence = GetConfidence(stats.SampleSize);
            float blended = staticScore * (1f - confidence) + communityScore * confidence;

            return Math.Max(0f, Math.Min(5.5f, blended));
        }

        /// <summary>
        /// Same as GetAdaptiveCardScore but for relics.
        /// </summary>
        public float GetAdaptiveRelicScore(string character, string relicId,
            TierGrade staticTier, DeckAnalysis deckAnalysis)
        {
            float staticScore = (float)staticTier;

            var stats = _db?.GetCommunityRelicStats(character, relicId);
            if (stats == null || stats.SampleSize < MinSampleSize)
                return staticScore;

            float communityScore = WinRateToScore(stats.WinRateWhenPicked);

            float pickRateNudge = (stats.PickRate - 0.33f) * 0.9f;
            pickRateNudge = Math.Max(-0.3f, Math.Min(0.3f, pickRateNudge));
            communityScore += pickRateNudge;

            float winDiff = stats.WinRateWhenPicked - stats.WinRateWhenSkipped;
            if (Math.Abs(winDiff) > 0.03f)
                communityScore += winDiff * 2f;

            communityScore = Math.Max(0f, Math.Min(5f, communityScore));

            float confidence = GetConfidence(stats.SampleSize);
            float blended = staticScore * (1f - confidence) + communityScore * confidence;

            return Math.Max(0f, Math.Min(5.5f, blended));
        }

        /// <summary>
        /// Confidence ramps linearly from 0 at MinSampleSize to 1 at FullConfidenceSampleSize.
        /// </summary>
        private float GetConfidence(int sampleSize)
        {
            if (sampleSize <= MinSampleSize) return 0f;
            if (sampleSize >= FullConfidenceSampleSize) return 1f;

            return (float)(sampleSize - MinSampleSize) /
                   (FullConfidenceSampleSize - MinSampleSize);
        }

        /// <summary>
        /// Converts a win rate (0.0-1.0) to a 0-5 tier score.
        /// Maps WinRateForF → 0 and WinRateForS → 5, linear interpolation.
        /// </summary>
        private float WinRateToScore(float winRate)
        {
            float normalized = (winRate - WinRateForF) / (WinRateForS - WinRateForF);
            return Math.Max(0f, Math.Min(5f, normalized * 5f));
        }

        /// <summary>
        /// Checks if any archetype context matches the current deck analysis
        /// and returns the context-specific win rate as a score.
        /// Returns -1 if no matching context found.
        /// </summary>
        private float GetContextScore(Dictionary<string, float> contextStats,
            DeckAnalysis deckAnalysis)
        {
            if (contextStats == null || contextStats.Count == 0 || deckAnalysis == null)
                return -1f;

            // Find the best matching context from the deck's detected archetypes
            float bestScore = -1f;
            float bestStrength = 0f;

            foreach (var archMatch in deckAnalysis.DetectedArchetypes)
            {
                // Try context keys like "poison_3+", "strength_3+", etc.
                foreach (string tag in archMatch.Archetype.CoreTags)
                {
                    string contextKey = $"{tag}_3+";
                    if (contextStats.TryGetValue(contextKey, out float contextWinRate))
                    {
                        // Prefer the context from the strongest archetype match
                        if (archMatch.Strength > bestStrength)
                        {
                            bestScore = WinRateToScore(contextWinRate);
                            bestStrength = archMatch.Strength;
                        }
                    }
                }
            }

            return bestScore;
        }
    }
}
