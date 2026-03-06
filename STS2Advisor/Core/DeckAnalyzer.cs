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
        public DeckAnalysis Analyze(string character, List<CardInfo> deck)
        {
            var analysis = new DeckAnalysis
            {
                Character = character,
                TotalCards = deck.Count
            };

            // Count all tags in the deck
            foreach (var card in deck)
            {
                foreach (string tag in card.Tags)
                {
                    string key = tag.ToLowerInvariant();
                    if (analysis.TagCounts.ContainsKey(key))
                        analysis.TagCounts[key]++;
                    else
                        analysis.TagCounts[key] = 1;
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
    }
}
