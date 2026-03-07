using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace STS2Advisor.Core
{
    public enum TierGrade { S = 5, A = 4, B = 3, C = 2, D = 1, F = 0 }

    public class CardTierEntry
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("baseTier")] public string BaseTier { get; set; }
        [JsonProperty("synergies")] public List<string> Synergies { get; set; } = new List<string>();
        [JsonProperty("antiSynergies")] public List<string> AntiSynergies { get; set; } = new List<string>();
        [JsonProperty("notes")] public string Notes { get; set; }
    }

    public class RelicTierEntry
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("baseTier")] public string BaseTier { get; set; }
        [JsonProperty("synergies")] public List<string> Synergies { get; set; } = new List<string>();
        [JsonProperty("antiSynergies")] public List<string> AntiSynergies { get; set; } = new List<string>();
        [JsonProperty("notes")] public string Notes { get; set; }
    }

    public class CharacterCardTiers
    {
        [JsonProperty("character")] public string Character { get; set; }
        [JsonProperty("cards")] public List<CardTierEntry> Cards { get; set; } = new List<CardTierEntry>();
    }

    public class RelicTierFile
    {
        [JsonProperty("category")] public string Category { get; set; }
        [JsonProperty("relics")] public List<RelicTierEntry> Relics { get; set; } = new List<RelicTierEntry>();
    }

    public class ScoredCard
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public TierGrade BaseTier { get; set; }
        public float FinalScore { get; set; }
        public TierGrade FinalGrade { get; set; }
        public bool IsBestPick { get; set; }
        public List<string> SynergyReasons { get; set; } = new List<string>();
        public List<string> AntiSynergyReasons { get; set; } = new List<string>();
        public string Notes { get; set; }
    }

    public class ScoredRelic
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public TierGrade BaseTier { get; set; }
        public float FinalScore { get; set; }
        public TierGrade FinalGrade { get; set; }
        public bool IsBestPick { get; set; }
        public List<string> SynergyReasons { get; set; } = new List<string>();
        public List<string> AntiSynergyReasons { get; set; } = new List<string>();
        public string Notes { get; set; }
    }

    public class TierEngine
    {
        private readonly Dictionary<string, CharacterCardTiers> _cardTiers = new Dictionary<string, CharacterCardTiers>();
        private readonly Dictionary<string, RelicTierFile> _relicTiers = new Dictionary<string, RelicTierFile>();

        public TierEngine(string dataPath)
        {
            LoadCardTiers(Path.Combine(dataPath, "CardTiers"));
            LoadRelicTiers(Path.Combine(dataPath, "RelicTiers"));
        }

        private void LoadCardTiers(string folder)
        {
            if (!Directory.Exists(folder))
            {
                Plugin.Log($"Card tier folder not found: {folder}");
                return;
            }

            foreach (string file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var tiers = JsonConvert.DeserializeObject<CharacterCardTiers>(json);
                    if (tiers?.Character != null)
                    {
                        _cardTiers[tiers.Character.ToLowerInvariant()] = tiers;
                        Plugin.Log($"Loaded {tiers.Cards?.Count ?? 0} card tiers for {tiers.Character}");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log($"Failed to load card tiers from {file}: {ex.Message}");
                }
            }
        }

        private void LoadRelicTiers(string folder)
        {
            if (!Directory.Exists(folder))
            {
                Plugin.Log($"Relic tier folder not found: {folder}");
                return;
            }

            foreach (string file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var tiers = JsonConvert.DeserializeObject<RelicTierFile>(json);
                    if (tiers?.Category != null)
                    {
                        _relicTiers[tiers.Category.ToLowerInvariant()] = tiers;
                        Plugin.Log($"Loaded {tiers.Relics.Count} relic tiers for {tiers.Category}");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log($"Failed to load relic tiers from {file}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Normalizes an ID for comparison: replaces spaces with underscores.
        /// JSON tier data uses "Body Slam" but game uses "BODY_SLAM".
        /// </summary>
        private static string NormalizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            return id.Replace(' ', '_').Replace('-', '_').Replace("'", "");
        }

        public CardTierEntry GetCardTier(string character, string cardId)
        {
            string normalizedCardId = NormalizeId(cardId);
            string key = character?.ToLowerInvariant();
            if (key != null && _cardTiers.TryGetValue(key, out var charTiers))
            {
                foreach (var entry in charTiers.Cards)
                {
                    if (string.Equals(NormalizeId(entry.Id), normalizedCardId, StringComparison.OrdinalIgnoreCase))
                        return entry;
                }
            }

            // Also check colorless
            if (_cardTiers.TryGetValue("colorless", out var colorless))
            {
                foreach (var entry in colorless.Cards)
                {
                    if (string.Equals(NormalizeId(entry.Id), normalizedCardId, StringComparison.OrdinalIgnoreCase))
                        return entry;
                }
            }

            return null;
        }

        public RelicTierEntry GetRelicTier(string character, string relicId)
        {
            string normalizedRelicId = NormalizeId(relicId);
            // Check character-specific first, then common
            string[] categories = { character?.ToLowerInvariant(), "common" };

            foreach (string cat in categories)
            {
                if (cat != null && _relicTiers.TryGetValue(cat, out var relicFile))
                {
                    foreach (var entry in relicFile.Relics)
                    {
                        if (string.Equals(NormalizeId(entry.Id), normalizedRelicId, StringComparison.OrdinalIgnoreCase))
                            return entry;
                    }
                }
            }

            return null;
        }

        public static TierGrade ParseGrade(string grade)
        {
            if (string.IsNullOrEmpty(grade)) return TierGrade.C;

            switch (grade.Trim().ToUpperInvariant())
            {
                case "S": return TierGrade.S;
                case "A": return TierGrade.A;
                case "B": return TierGrade.B;
                case "C": return TierGrade.C;
                case "D": return TierGrade.D;
                case "F": return TierGrade.F;
                default: return TierGrade.C;
            }
        }

        public static TierGrade ScoreToGrade(float score)
        {
            if (score >= 4.5f) return TierGrade.S;
            if (score >= 3.5f) return TierGrade.A;
            if (score >= 2.5f) return TierGrade.B;
            if (score >= 1.5f) return TierGrade.C;
            if (score >= 0.5f) return TierGrade.D;
            return TierGrade.F;
        }
    }
}
