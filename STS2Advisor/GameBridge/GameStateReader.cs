using System.Collections.Generic;

namespace STS2Advisor.GameBridge
{
    public class GameState
    {
        public string Character { get; set; }
        public int ActNumber { get; set; }
        public List<CardInfo> DeckCards { get; set; } = new List<CardInfo>();
        public List<RelicInfo> CurrentRelics { get; set; } = new List<RelicInfo>();

        // Current screen offerings
        public List<CardInfo> OfferedCards { get; set; } = new List<CardInfo>();
        public List<RelicInfo> OfferedRelics { get; set; } = new List<RelicInfo>();

        // Shop-specific
        public List<CardInfo> ShopCards { get; set; } = new List<CardInfo>();
        public List<RelicInfo> ShopRelics { get; set; } = new List<RelicInfo>();
    }

    /// <summary>
    /// Reads the current game state from STS2's runtime objects.
    ///
    /// !! PLACEHOLDER IMPLEMENTATION !!
    ///
    /// Every method here returns dummy data. Once you have Assembly-CSharp.dll
    /// and have identified the game's internal classes via dnSpy/ILSpy, replace
    /// these with real reads from the game's singletons/managers.
    ///
    /// WHAT TO LOOK FOR IN dnSpy:
    ///
    /// 1. Character class:
    ///    - Search for "AbstractPlayer" or "PlayerManager" or "CharacterType"
    ///    - There's likely an enum or string field for the current character
    ///
    /// 2. Deck:
    ///    - Search for "MasterDeck" or "masterDeck" — usually a List of card objects
    ///    - Each card object has: cardID/name, cost, type, upgraded flag
    ///    - Keywords/tags might be on the card or derived from its effects
    ///
    /// 3. Relics:
    ///    - Search for "RelicManager" or "relics" — usually a List of relic objects
    ///    - Each relic has: relicId, name, rarity
    ///
    /// 4. Card reward screen:
    ///    - Search for "CardReward" or "RewardScreen"
    ///    - The offered cards are usually stored in a List on that screen object
    ///    - You need their screen positions for overlay placement
    ///
    /// 5. Act number:
    ///    - Search for "actNum" or "currentAct" or "AbstractDungeon"
    ///    - Usually an int field on the dungeon/run manager
    ///
    /// 6. Shop:
    ///    - Search for "ShopScreen" or "Merchant"
    ///    - Cards and relics for sale are stored in separate lists
    ///
    /// COMMON STS2 PATTERNS:
    ///    - Singleton access: ClassName.instance or ClassName.Instance
    ///    - Card ID: card.cardID or card.name
    ///    - Tags may need to be inferred from card keywords/description
    /// </summary>
    public static class GameStateReader
    {
        public static GameState ReadCurrentState()
        {
            try
            {
                var state = new GameState
                {
                    Character = ReadCharacter(),
                    ActNumber = ReadActNumber(),
                    DeckCards = ReadDeck(),
                    CurrentRelics = ReadRelics(),
                    OfferedCards = ReadOfferedCards(),
                    OfferedRelics = ReadOfferedRelics(),
                    ShopCards = ReadShopCards(),
                    ShopRelics = ReadShopRelics()
                };

                return state;
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"Failed to read game state: {ex.Message}");
                return null;
            }
        }

        // =================================================================
        // REPLACE EACH METHOD BELOW WITH REAL GAME READS
        // =================================================================

        private static string ReadCharacter()
        {
            // TODO: Replace with real read.
            // Example: return PlayerManager.Instance.currentCharacter.name.ToLowerInvariant();
            Plugin.Log?.LogWarning("GameStateReader.ReadCharacter() is using placeholder!");
            return "ironclad";
        }

        private static int ReadActNumber()
        {
            // TODO: Replace with real read.
            // Example: return AbstractDungeon.actNum;
            Plugin.Log?.LogWarning("GameStateReader.ReadActNumber() is using placeholder!");
            return 1;
        }

        private static List<CardInfo> ReadDeck()
        {
            // TODO: Replace with real read.
            // Example:
            //   var deck = new List<CardInfo>();
            //   foreach (var card in PlayerManager.Instance.masterDeck)
            //   {
            //       deck.Add(new CardInfo
            //       {
            //           Id = card.cardID,
            //           Name = card.name,
            //           Cost = card.cost,
            //           Type = card.type.ToString(),
            //           Upgraded = card.upgraded,
            //           Tags = ExtractTags(card)
            //       });
            //   }
            //   return deck;
            Plugin.Log?.LogWarning("GameStateReader.ReadDeck() is using placeholder!");
            return new List<CardInfo>();
        }

        private static List<RelicInfo> ReadRelics()
        {
            // TODO: Replace with real read.
            Plugin.Log?.LogWarning("GameStateReader.ReadRelics() is using placeholder!");
            return new List<RelicInfo>();
        }

        private static List<CardInfo> ReadOfferedCards()
        {
            // TODO: Replace with real read from the card reward screen.
            // You'll also need each card's screen position for overlay placement.
            // Example:
            //   var screen = CardRewardScreen.Instance;
            //   if (screen == null || !screen.isActive) return new List<CardInfo>();
            //   var cards = new List<CardInfo>();
            //   foreach (var cardObj in screen.rewardCards)
            //   {
            //       var worldPos = cardObj.transform.position;
            //       var screenPos = Camera.main.WorldToScreenPoint(worldPos);
            //       cards.Add(new CardInfo
            //       {
            //           Id = cardObj.cardID,
            //           Name = cardObj.name,
            //           ScreenX = screenPos.x,
            //           ScreenY = Screen.height - screenPos.y, // Flip Y for IMGUI
            //           Tags = ExtractTags(cardObj)
            //       });
            //   }
            //   return cards;
            Plugin.Log?.LogWarning("GameStateReader.ReadOfferedCards() is using placeholder!");
            return new List<CardInfo>();
        }

        private static List<RelicInfo> ReadOfferedRelics()
        {
            Plugin.Log?.LogWarning("GameStateReader.ReadOfferedRelics() is using placeholder!");
            return new List<RelicInfo>();
        }

        private static List<CardInfo> ReadShopCards()
        {
            Plugin.Log?.LogWarning("GameStateReader.ReadShopCards() is using placeholder!");
            return new List<CardInfo>();
        }

        private static List<RelicInfo> ReadShopRelics()
        {
            Plugin.Log?.LogWarning("GameStateReader.ReadShopRelics() is using placeholder!");
            return new List<RelicInfo>();
        }

        // =================================================================
        // HELPER: Extract tags from a card's keywords/description.
        // You'll need to build this mapping based on what STS2 exposes.
        // =================================================================
        //
        // private static List<string> ExtractTags(AbstractCard card)
        // {
        //     var tags = new List<string>();
        //
        //     // Option A: Card has a keywords list
        //     // foreach (var kw in card.keywords) tags.Add(kw.ToLowerInvariant());
        //
        //     // Option B: Infer from card properties
        //     // if (card.baseDamage > 0 && card.hitCount > 1) tags.Add("multi_hit");
        //     // if (card.description.Contains("Poison")) tags.Add("poison");
        //     // if (card.description.Contains("Strength")) tags.Add("strength");
        //     // if (card.cost == 0) tags.Add("zero_cost");
        //     // if (card.exhaust) tags.Add("exhaust");
        //
        //     // Option C: Maintain a manual map (most reliable)
        //     // if (CardTagMap.TryGetValue(card.cardID, out var mappedTags))
        //     //     tags.AddRange(mappedTags);
        //
        //     return tags;
        // }
    }
}
