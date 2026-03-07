using System;
using System.Collections.Generic;
using System.Linq;
using STS2Advisor.GameBridge;

namespace STS2Advisor.Core
{
    public class ArchetypeMatch
    {
        public Archetype Archetype { get; set; }
        public float Strength { get; set; }     // 0.0 to 1.0+ — how committed the deck is
        public int CoreCount { get; set; }
        public int SupportCount { get; set; }
    }

    public class DeckAnalysis
    {
        public string Character { get; set; }
        public List<ArchetypeMatch> DetectedArchetypes { get; set; } = new List<ArchetypeMatch>();
        public int TotalCards { get; set; }
        public Dictionary<string, int> TagCounts { get; set; } = new Dictionary<string, int>();

        public bool HasArchetype(string archetypeId)
        {
            return DetectedArchetypes.Any(a => a.Archetype.Id == archetypeId);
        }

        public float ArchetypeStrength(string archetypeId)
        {
            var match = DetectedArchetypes.FirstOrDefault(a => a.Archetype.Id == archetypeId);
            return match?.Strength ?? 0f;
        }

        /// <summary>
        /// Returns true if deck has no strong archetype yet (early game / unfocused).
        /// </summary>
        public bool IsUndefined => DetectedArchetypes.Count == 0 ||
                                    DetectedArchetypes.All(a => a.Strength < 0.3f);
    }

    public class DeckAnalyzer
    {
        /// <summary>
        /// Analyzes a deck to detect archetype commitment.
        ///
        /// Tags come from TWO sources:
        /// 1. CardKeyword tags from the game (Exhaust, Ethereal, etc.) — only 8 values
        /// 2. Synergy tags from tier JSON data (strength, poison, shiv, etc.) — the main source
        ///
        /// Without tierEngine, archetype detection is very limited since game card tags
        /// only have a few keyword values, not archetype-relevant tags like "strength" or "poison".
        /// </summary>
        public DeckAnalysis Analyze(string character, List<CardInfo> deck, TierEngine tierEngine = null)
        {
            var analysis = new DeckAnalysis
            {
                Character = character,
                TotalCards = deck.Count
            };

            // Count tags from both game card data and tier JSON synergies
            // Use a HashSet per card to avoid double-counting when a tag appears
            // in both game keywords and tier JSON synergies
            foreach (var card in deck)
            {
                var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Source 1: Game card tags (CardKeyword: Exhaust, Ethereal, etc.)
                foreach (string tag in card.Tags)
                {
                    string key = tag.ToLowerInvariant();
                    seenTags.Add(key);
                }

                // Source 2: Tier JSON synergy tags (strength, poison, shiv, orb, etc.)
                // This is the primary source of archetype-relevant tags
                if (tierEngine != null)
                {
                    var tierEntry = tierEngine.GetCardTier(character, card.Id);
                    if (tierEntry?.Synergies != null)
                    {
                        foreach (string syn in tierEntry.Synergies)
                        {
                            string key = syn.ToLowerInvariant();
                            seenTags.Add(key);
                        }
                    }
                }

                // Increment each unique tag once per card
                foreach (string key in seenTags)
                {
                    IncrementTag(analysis.TagCounts, key);
                }
            }

            // Check each archetype for this character
            string charKey = character?.ToLowerInvariant() ?? "";
            if (!ArchetypeDefinitions.ByCharacter.TryGetValue(charKey, out var archetypes))
                return analysis;

            foreach (var archetype in archetypes)
            {
                int coreCount = 0;
                foreach (string coreTag in archetype.CoreTags)
                {
                    if (analysis.TagCounts.TryGetValue(coreTag, out int count))
                        coreCount += count;
                }

                int supportCount = 0;
                foreach (string supportTag in archetype.SupportTags)
                {
                    if (analysis.TagCounts.TryGetValue(supportTag, out int count))
                        supportCount += count;
                }

                bool coreMetThreshold = coreCount >= archetype.CoreThreshold;
                bool supportMetThreshold = supportCount >= archetype.SupportThreshold;

                if (coreMetThreshold || (coreCount >= 2 && supportMetThreshold))
                {
                    float strength = (float)coreCount / (archetype.CoreThreshold * 2f);
                    if (supportMetThreshold)
                        strength += 0.2f;

                    strength = System.Math.Min(strength, 1.0f);

                    analysis.DetectedArchetypes.Add(new ArchetypeMatch
                    {
                        Archetype = archetype,
                        Strength = strength,
                        CoreCount = coreCount,
                        SupportCount = supportCount
                    });
                }
            }

            // Sort by strength descending
            analysis.DetectedArchetypes.Sort((a, b) => b.Strength.CompareTo(a.Strength));

            return analysis;
        }

        private static void IncrementTag(Dictionary<string, int> tagCounts, string key)
        {
            if (tagCounts.ContainsKey(key))
                tagCounts[key]++;
            else
                tagCounts[key] = 1;
        }
    }
}
