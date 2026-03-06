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

            // STS2 new characters — archetypes are speculative, update as you learn them
            ["regent"] = new List<Archetype>
            {
                new Archetype
                {
                    Id = "command",
                    DisplayName = "Command / Authority",
                    CoreTags = new List<string> { "command", "authority" },
                    SupportTags = new List<string> { "summon", "buff" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "summon",
                    DisplayName = "Summon / Minions",
                    CoreTags = new List<string> { "summon", "minion" },
                    SupportTags = new List<string> { "buff", "sacrifice" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "decree",
                    DisplayName = "Decree / Edict",
                    CoreTags = new List<string> { "decree", "edict" },
                    SupportTags = new List<string> { "retain", "status" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                }
            },

            ["necromancer"] = new List<Archetype>
            {
                new Archetype
                {
                    Id = "undead",
                    DisplayName = "Undead / Raise",
                    CoreTags = new List<string> { "undead", "raise" },
                    SupportTags = new List<string> { "exhaust", "sacrifice" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "curse",
                    DisplayName = "Curse Synergy",
                    CoreTags = new List<string> { "curse", "curse_synergy" },
                    SupportTags = new List<string> { "exhaust", "dark" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                },
                new Archetype
                {
                    Id = "sacrifice",
                    DisplayName = "Sacrifice / Life Tap",
                    CoreTags = new List<string> { "sacrifice", "hp_loss" },
                    SupportTags = new List<string> { "heal", "undead" },
                    CoreThreshold = 3,
                    SupportThreshold = 2
                }
            }
        };
    }
}
