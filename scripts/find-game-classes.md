# dnSpy Class Discovery Cheat Sheet

Open `data_sts2_windows_x86_64/sts2.dll` in dnSpy (or ILSpy/dotPeek).

This is a Godot 4.5.1 + C# (.NET 9.0) game. Classes extend Godot.Node,
use _Ready()/_Process(), and communicate via signals.

## Quick Search (Ctrl+Shift+K in dnSpy)

### Game Init / Main Manager
```
GameManager
Main
_Ready
Autoload
AppManager
```
You want: the first _Ready() that fires when the game starts.
We patch this to create our overlay CanvasLayer.

### Card Reward Screen
```
CardReward
RewardScreen
CardChoice
CombatReward
PostCombat
```
You want: the class + method that opens card selection after combat.

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

### Player / Run State
```
Player
RunState
RunData
CharacterData
MasterDeck
```
Fields: character, deck (List<>), relics (List<>), currentAct, currentFloor, hp, gold

### Card Class
```
Card
CardData
CardDefinition
BaseCard
```
Fields: id/cardId, name, cost, type, upgraded, keywords/tags

### Relic Class
```
Relic
RelicData
RelicDefinition
```
Fields: id/relicId, name, rarity

## Godot C# Patterns to Recognize

```csharp
// Godot lifecycle
public override void _Ready() { }           // Called when node enters tree
public override void _Process(double delta) { } // Called every frame

// Signals (events)
[Signal] public delegate void CardSelectedEventHandler(int index);
EmitSignal(SignalName.CardSelected, index);

// Node references
var deck = GetNode<DeckManager>("/root/DeckManager");
var card = GetChild<CardUI>(0);

// Singletons / Autoloads
var gm = GetNode<GameManager>("/root/GameManager");
```

## What to Write Down

For each class found, note:
1. Full namespace + class name
2. How to access it (autoload path, singleton, node path)
3. Key method names (_Ready, Show, Open, PopulateCards)
4. Key field names (cards, offerings, deck, relics)
5. Field types (List<Card>, Godot.Collections.Array, etc.)
6. Signals that fire on screen open/close

## Example Updates

Once found, update GameStateReader.cs:

```csharp
// Instead of placeholder:
private static string ReadCharacter()
{
    // Access the game's run state autoload
    var runState = ((SceneTree)Engine.GetMainLoop()).Root
        .GetNode<RunState>("/root/RunState");
    return runState.Character.ToString().ToLowerInvariant();
}
```

Update Plugin.cs Harmony patches:

```csharp
// Instead of PlaceholderCardRewardScreen:
[HarmonyPatch(typeof(ActualNamespace.CardRewardScreen), "ShowRewards")]
[HarmonyPostfix]
public static void OnCardRewardOpened() { ... }
```

## Tips

- sts2.dll is 8.6MB — expect many classes. Use the namespace tree in dnSpy.
- Look for namespaces like `STS2.Cards`, `STS2.Relics`, `STS2.UI`, `STS2.Combat`
- Card IDs in our JSON must match the game's exact string IDs
- The game likely loads card definitions from .tres or .json resource files
  packed inside SlayTheSpire2.pck
