# dnSpy Class Discovery Cheat Sheet

Open `Slay the Spire 2_Data/Managed/Assembly-CSharp.dll` in dnSpy.

## Quick Search (Ctrl+Shift+K)

### Card Reward Screen
Search these terms and note which class handles card selection:
```
CardReward
RewardScreen
CardChoice
CombatReward
PostCombat
```
You want: the class that opens when you pick cards after a fight.
Note: class name, the "open/show" method, and the field holding offered cards.

### Relic Reward
```
RelicReward
BossRelic
RelicChoice
RelicSelect
```

### Shop
```
ShopScreen
Merchant
Store
ShopItem
```

### Player State
```
AbstractPlayer
PlayerManager
CharacterData
CharacterType
MasterDeck
masterDeck
```

### Card Class
```
AbstractCard
CardData
BaseCard
```
Fields to note: cardID, name, cost, type, upgraded, keywords

### Relic Class
```
AbstractRelic
RelicData
BaseRelic
```
Fields to note: relicId, name, rarity

### Act/Dungeon
```
AbstractDungeon
DungeonManager
actNum
currentAct
FloorManager
```

## What to Write Down

For each class found, note:
1. Full namespace + class name (e.g., `MegaCrit.GameLogic.CardRewardScreen`)
2. Singleton access pattern (e.g., `.instance`, `.Instance`, `FindObjectOfType<>()`)
3. Key method names (Open, Show, Display, Initialize)
4. Key field names (cards, offerings, inventory, deck)
5. Field types (List<AbstractCard>, AbstractCard[], etc.)

## Example Mapping

Once found, the GameStateReader.cs updates look like:

```csharp
// Instead of:
// Plugin.Log?.LogWarning("GameStateReader.ReadCharacter() is using placeholder!");
// return "ironclad";

// You write:
return PlayerManager.Instance.currentCharacter.characterName.ToLowerInvariant();
```

```csharp
// Instead of placeholder ReadDeck():
var deck = new List<CardInfo>();
foreach (var card in PlayerManager.Instance.masterDeck.cards)
{
    deck.Add(new CardInfo
    {
        Id = card.cardID,
        Name = card.displayName,
        Cost = card.baseCost,
        Type = card.cardType.ToString(),
        Upgraded = card.timesUpgraded > 0,
        Tags = ExtractTags(card)
    });
}
return deck;
```

## IL2CPP Warning

If the game folder has `GameAssembly.dll` instead of `Assembly-CSharp.dll`:
1. Use Il2CppDumper to generate dummy DLLs
2. Open the generated DLLs in dnSpy instead
3. Switch to BepInEx 6 with Il2CppInterop
4. Patching syntax changes slightly (use `Il2CppInterop.Runtime` types)
