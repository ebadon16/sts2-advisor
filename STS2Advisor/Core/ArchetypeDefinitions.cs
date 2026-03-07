using System.Collections.Generic;

namespace STS2Advisor.Core
{
    public class Archetype
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public List<string> CoreTags { get; set; } = new List<string>();
        public List<string> SupportTags { get; set; } = new List<string>();
        public int CoreThreshold { get; set; } = 3;
        public int SupportThreshold { get; set; } = 2;
    }

    public static class ArchetypeDefinitions
    {
        public static readonly Dictionary<string, List<Archetype>> ByCharacter =
            new Dictionary<string, List<Archetype>>
        {
            ["ironclad"] = new List<Archetype>
            {
                new Archetype
                {
                    Id = "strength",
                    DisplayName = "Strength",
                    CoreTags = new List<string> { "strength", "scaling" },
                    SupportTags = new List<string> { "exhaust", "multi_hit" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "exhaust",
                    DisplayName = "Exhaust",
                    CoreTags = new List<string> { "exhaust", "exhaust_synergy" },
                    SupportTags = new List<string> { "draw", "self_damage" },
                    CoreThreshold = 4,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "block",
                    DisplayName = "Barricade / Block",
                    CoreTags = new List<string> { "block", "block_scaling" },
                    SupportTags = new List<string> { "body_slam", "entrench" },
                    CoreThreshold = 4,
                    SupportThreshold = 1
                },
                new Archetype
                {
                    Id = "self_damage",
                    DisplayName = "Self-Damage / Corruption",
                    CoreTags = new List<string> { "self_damage", "hp_loss" },
                    SupportTags = new List<string> { "heal", "strength" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                }
            },

            ["silent"] = new List<Archetype>
            {
                new Archetype
                {
                    Id = "poison",
                    DisplayName = "Poison",
                    CoreTags = new List<string> { "poison", "poison_scaling" },
                    SupportTags = new List<string> { "catalyst", "weak" },
                    CoreThreshold = 3,
                    SupportThreshold = 1
                },
                new Archetype
                {
                    Id = "shiv",
                    DisplayName = "Shiv",
                    CoreTags = new List<string> { "shiv", "shiv_synergy" },
                    SupportTags = new List<string> { "dexterity", "draw" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "discard",
                    DisplayName = "Discard",
                    CoreTags = new List<string> { "discard", "discard_synergy" },
                    SupportTags = new List<string> { "draw", "retain" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "dexterity",
                    DisplayName = "Dexterity / Block",
                    CoreTags = new List<string> { "dexterity", "block" },
                    SupportTags = new List<string> { "weak", "draw" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                }
            },

            ["defect"] = new List<Archetype>
            {
                new Archetype
                {
                    Id = "lightning",
                    DisplayName = "Lightning Orbs",
                    CoreTags = new List<string> { "lightning", "orb" },
                    SupportTags = new List<string> { "focus", "evoke" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "frost",
                    DisplayName = "Frost / Focus",
                    CoreTags = new List<string> { "frost", "focus" },
                    SupportTags = new List<string> { "orb", "block" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "dark",
                    DisplayName = "Dark Orbs",
                    CoreTags = new List<string> { "dark", "orb" },
                    SupportTags = new List<string> { "focus", "evoke" },
                    CoreThreshold = 2,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "all_orbs",
                    DisplayName = "All Orbs / Focus",
                    CoreTags = new List<string> { "focus", "orb" },
                    SupportTags = new List<string> { "lightning", "frost", "dark", "plasma" },
                    CoreThreshold = 3,
                    SupportThreshold = 3
                },
                new Archetype
                {
                    Id = "zero_cost",
                    DisplayName = "0-Cost",
                    CoreTags = new List<string> { "zero_cost", "zero_synergy" },
                    SupportTags = new List<string> { "draw", "all_for_one" },
                    CoreThreshold = 4,
                    SupportThreshold = 1
                }
            },

            // STS2 new characters — archetypes from community guides (Mobalytics, GameRant, NeonLightsMedia)
            ["regent"] = new List<Archetype>
            {
                new Archetype
                {
                    Id = "stars",
                    DisplayName = "Stars / Energy",
                    CoreTags = new List<string> { "stars", "star_gen" },
                    SupportTags = new List<string> { "draw", "scaling" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "forge",
                    DisplayName = "Sovereign Blade / Forge",
                    CoreTags = new List<string> { "forge", "blade" },
                    SupportTags = new List<string> { "stars", "damage" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "cosmic",
                    DisplayName = "Cosmic Damage",
                    CoreTags = new List<string> { "cosmic", "aoe" },
                    SupportTags = new List<string> { "stars", "scaling" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "star_block",
                    DisplayName = "Star Defense",
                    CoreTags = new List<string> { "block", "stars" },
                    SupportTags = new List<string> { "forge", "scaling" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                }
            },

            ["necrobinder"] = new List<Archetype>
            {
                new Archetype
                {
                    Id = "doom",
                    DisplayName = "Doom / Debuff Stacking",
                    CoreTags = new List<string> { "doom", "debuff" },
                    SupportTags = new List<string> { "block", "scaling" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "soul",
                    DisplayName = "Soul / Exhaust Cycling",
                    CoreTags = new List<string> { "soul", "exhaust" },
                    SupportTags = new List<string> { "draw", "scaling" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "osty",
                    DisplayName = "Osty / Minion",
                    CoreTags = new List<string> { "osty", "minion" },
                    SupportTags = new List<string> { "damage", "scaling" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                }
            },

            // Deprived — speculative archetypes, update as you learn them
            ["deprived"] = new List<Archetype>
            {
                new Archetype
                {
                    Id = "deprivation",
                    DisplayName = "Deprivation / Scarcity",
                    CoreTags = new List<string> { "deprivation", "scarcity" },
                    SupportTags = new List<string> { "exhaust", "draw" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "survival",
                    DisplayName = "Survival / Endurance",
                    CoreTags = new List<string> { "survival", "block" },
                    SupportTags = new List<string> { "heal", "retain" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                }
            }
        };
    }
}
