using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace STS2Advisor.GameBridge
{
    public class GameState
    {
        public string Character { get; set; }
        public int ActNumber { get; set; }
        public int Floor { get; set; }
        public int CurrentHP { get; set; }
        public int MaxHP { get; set; }
        public int Gold { get; set; }
        public int AscensionLevel { get; set; }
        public List<CardInfo> DeckCards { get; set; } = new List<CardInfo>();
        public List<RelicInfo> CurrentRelics { get; set; } = new List<RelicInfo>();
        public List<CardInfo> OfferedCards { get; set; } = new List<CardInfo>();
        public List<RelicInfo> OfferedRelics { get; set; } = new List<RelicInfo>();
        public List<CardInfo> ShopCards { get; set; } = new List<CardInfo>();
        public List<RelicInfo> ShopRelics { get; set; } = new List<RelicInfo>();
    }

    public static class GameStateReader
    {
        public static GameState ReadCurrentState()
        {
            try
            {
                var runManager = RunManager.Instance;
                if (runManager == null)
                {
                    Plugin.Log("RunManager is null — not in a run.");
                    return null;
                }

                var runState = GetRunState(runManager);
                if (runState == null)
                {
                    Plugin.Log("RunState is null — not in a run.");
                    return null;
                }
                var player = runState.Players?.FirstOrDefault();
                if (player == null)
                {
                    Plugin.Log("No player found in RunState.");
                    return null;
                }

                var state = new GameState
                {
                    Character = ReadCharacter(player),
                    ActNumber = runState.CurrentActIndex + 1,
                    Floor = runState.TotalFloor,
                    CurrentHP = ReadCurrentHP(player),
                    MaxHP = ReadMaxHP(player),
                    Gold = ReadGold(player),
                    AscensionLevel = runState.AscensionLevel,
                    DeckCards = ReadDeck(player),
                    CurrentRelics = ReadRelics(player),
                    OfferedCards = ReadOfferedCards(),
                    OfferedRelics = ReadOfferedRelics(),
                    ShopCards = ReadShopCards(player),
                    ShopRelics = ReadShopRelics(player)
                };

                return state;
            }
            catch (Exception ex)
            {
                Plugin.Log($"Failed to read game state: {ex.Message}");
                return null;
            }
        }

        private static string ReadCharacter(Player player)
        {
            try
            {
                var charModel = player.Character;
                if (charModel?.Id != null)
                    return charModel.Id.Entry?.ToLowerInvariant() ?? "unknown";
                return "unknown";
            }
            catch (Exception ex)
            {
                Plugin.Log($"ReadCharacter error: {ex.Message}");
                return "unknown";
            }
        }

        private static int ReadCurrentHP(Player player)
        {
            try { return player.Creature?.CurrentHp ?? 0; }
            catch { return 0; }
        }

        private static int ReadMaxHP(Player player)
        {
            try { return player.Creature?.MaxHp ?? 0; }
            catch { return 0; }
        }

        private static int ReadGold(Player player)
        {
            try { return player.Gold; }
            catch { return 0; }
        }

        private static List<CardInfo> ReadDeck(Player player)
        {
            var result = new List<CardInfo>();
            try
            {
                var deck = player.Deck;
                if (deck?.Cards == null) return result;

                foreach (var card in deck.Cards)
                {
                    result.Add(CardModelToInfo(card));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log($"ReadDeck error: {ex.Message}");
            }
            return result;
        }

        private static List<RelicInfo> ReadRelics(Player player)
        {
            var result = new List<RelicInfo>();
            try
            {
                var relics = player.Relics;
                if (relics == null) return result;

                foreach (var relic in relics)
                {
                    result.Add(RelicModelToInfo(relic));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log($"ReadRelics error: {ex.Message}");
            }
            return result;
        }

        private static List<CardInfo> ReadOfferedCards()
        {
            // Card reward options are read from the active NCardRewardSelectionScreen
            // via the Harmony patch that intercepts ShowScreen. The patch stores them
            // in _lastCardOptions before calling our analysis.
            var result = new List<CardInfo>();
            try
            {
                if (_lastCardOptions != null)
                {
                    foreach (var option in _lastCardOptions)
                    {
                        var card = option.Card;
                        if (card != null)
                            result.Add(CardModelToInfo(card));
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log($"ReadOfferedCards error: {ex.Message}");
            }
            return result;
        }

        private static List<RelicInfo> ReadOfferedRelics()
        {
            var result = new List<RelicInfo>();
            try
            {
                if (_lastRelicOptions != null)
                {
                    foreach (var relic in _lastRelicOptions)
                    {
                        result.Add(RelicModelToInfo(relic));
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log($"ReadOfferedRelics error: {ex.Message}");
            }
            return result;
        }

        private static List<CardInfo> ReadShopCards(Player player)
        {
            var result = new List<CardInfo>();
            try
            {
                if (_lastMerchantInventory == null) return result;

                var charEntries = _lastMerchantInventory.CharacterCardEntries
                    ?? Enumerable.Empty<MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardEntry>();
                var colorlessEntries = _lastMerchantInventory.ColorlessCardEntries
                    ?? Enumerable.Empty<MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardEntry>();
                var entries = charEntries.Concat(colorlessEntries);

                foreach (var entry in entries)
                {
                    if (!entry.IsStocked) continue;
                    var card = entry.CreationResult?.Card;
                    if (card != null)
                        result.Add(CardModelToInfo(card));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log($"ReadShopCards error: {ex.Message}");
            }
            return result;
        }

        private static List<RelicInfo> ReadShopRelics(Player player)
        {
            var result = new List<RelicInfo>();
            try
            {
                if (_lastMerchantInventory == null) return result;

                var relicEntries = _lastMerchantInventory.RelicEntries;
                if (relicEntries == null) return result;
                foreach (var entry in relicEntries)
                {
                    if (!entry.IsStocked) continue;
                    var relic = entry.Model;
                    if (relic != null)
                        result.Add(RelicModelToInfo(relic));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log($"ReadShopRelics error: {ex.Message}");
            }
            return result;
        }

        // =================================================================
        // RunManager.State accessor (non-public getter)
        // =================================================================

        private static readonly PropertyInfo _stateProperty =
            typeof(RunManager).GetProperty("State",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static RunState GetRunState(RunManager runManager)
        {
            return _stateProperty?.GetValue(runManager) as RunState;
        }

        // =================================================================
        // Conversion helpers
        // =================================================================

        private static CardInfo CardModelToInfo(CardModel card)
        {
            var info = new CardInfo
            {
                Id = card.Id?.Entry ?? "unknown",
                Name = card.Title ?? card.Id?.Entry ?? "unknown",
                Cost = card.EnergyCost?.Canonical ?? 0,
                Type = card.Type.ToString(),
                Rarity = card.Rarity.ToString(),
                Upgraded = card.Id?.Entry?.EndsWith("+") == true,
                Tags = new List<string>()
            };

            // Extract keywords as tags
            try
            {
                var keywords = card.GetType().GetField("_keywords",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (keywords?.GetValue(card) is HashSet<CardKeyword> kws)
                {
                    foreach (var kw in kws)
                    {
                        if (kw != CardKeyword.None)
                            info.Tags.Add(kw.ToString().ToLowerInvariant());
                    }
                }
            }
            catch { }

            return info;
        }

        private static RelicInfo RelicModelToInfo(RelicModel relic)
        {
            return new RelicInfo
            {
                Id = relic.Id?.Entry ?? "unknown",
                Name = relic.Title?.ToString() ?? relic.Id?.Entry ?? "unknown",
                Rarity = relic.Rarity.ToString()
            };
        }

        // =================================================================
        // State captured by Harmony patches
        // =================================================================

        internal static IReadOnlyList<CardCreationResult> _lastCardOptions;
        internal static IReadOnlyList<RelicModel> _lastRelicOptions;
        internal static MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory _lastMerchantInventory;
    }
}
