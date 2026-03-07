# STS2 Advisor — Slay the Spire 2 Mod

## What This Is
A real-time card/relic tier advisor overlay for Slay the Spire 2. Shows tier badges (S/A/B/C/D/F), synergy analysis, and "best pick" recommendations on card reward, relic selection, and shop screens. Self-learning: local play data improves advice over time.

## Tech Stack
- **Game**: Slay the Spire 2 (Godot 4.5.1 + C# / .NET 9.0)
- **Mod injection**: `DOTNET_STARTUP_HOOKS` environment variable (no BepInEx)
- **Patching**: Harmony library for runtime method hooking
- **UI**: Godot CanvasLayer overlay (layer 100) with F7/F8 hotkeys
- **Database**: SQLite via Microsoft.Data.Sqlite for local run tracking
- **Serialization**: Newtonsoft.Json

## Project Structure
```
STS2Advisor/
  Plugin.cs              — Entry point (StartupHook.Initialize), Harmony patches, mod init
  Core/
    TierEngine.cs        — Loads JSON tier data, card/relic lookup with NormalizeId()
    SynergyScorer.cs     — Scores offerings using tier data + deck archetypes + adaptive data
    DeckAnalyzer.cs      — Detects deck archetypes from game tags + tier JSON synergy tags
    AdaptiveScorer.cs    — Blends static tiers with local win rate data (5-50 sample ramp)
    ArchetypeDefinitions.cs — Archetype patterns per character
  GameBridge/
    GameStateReader.cs   — Reads game state via reflection (RunManager, Player, Deck, etc.)
    CardData.cs          — CardInfo model
    RelicData.cs         — RelicInfo model
  Tracking/
    RunTracker.cs        — Run lifecycle tracking (start/end/decisions)
    RunDatabase.cs       — SQLite schema + CRUD for runs/decisions/community stats
    LocalStatsComputer.cs — Recomputes pick/win rates from local play data after each run
    Models.cs            — RunLog, DecisionEvent, CommunityCardStats, CommunityRelicStats
  UI/
    OverlayRenderer.cs   — Godot CanvasLayer overlay, panel, badges, tooltips
    SynergyTooltip.cs    — Tooltip text builder
    TierBadge.cs         — Tier-to-color mapping
  Data/
    CardTiers/*.json     — Tier data per character (ironclad, silent, defect, regent, necrobinder, colorless, deprived)
    RelicTiers/*.json    — Relic tier data per character + common pool
```

## Key Patterns

### ID Format
- Game uses UPPERCASE_SNAKE_CASE: `BODY_SLAM`, `FEEL_NO_PAIN`
- JSON tier data uses Title Case with spaces: `Body Slam`, `Feel No Pain`
- `TierEngine.NormalizeId()` handles this: replaces spaces with underscores, case-insensitive comparison
- Character IDs from game: `ironclad`, `silent`, `defect`, `regent`, `necrobinder`, `deprived` (lowercase)

### Harmony Patches (in Plugin.cs → GamePatches)
- `NCardRewardSelectionScreen.ShowScreen` (postfix) — card rewards after combat
- `NCardRewardSelectionScreen._Ready` (postfix) — overlay creation
- `NChooseARelicSelection.ShowScreen` (postfix) — relic selection
- `NMerchantInventory.Open` (postfix) — shop screen
- `RunManager.Launch` (postfix) — run start, extracts character + ascension
- `RunManager.OnEnded` (postfix) — run end, triggers LocalStats recompute
- `NCardRewardSelectionScreen.SelectCard` (manual patch) — tracks which card was picked
- `NChooseARelicSelection.SelectHolder` (manual patch) — tracks which relic was picked

### Self-Learning System
1. `RunTracker` records every card/relic offering + choice during play
2. On run end, `LocalStatsComputer.RecomputeAll()` runs SQL to compute pick rates + win rates
3. Results go into `community_card_stats` / `community_relic_stats` tables
4. `AdaptiveScorer` blends static JSON tiers with local data (confidence ramps from 5 to 50 samples)
5. More you play → smarter the advice gets

### Game Object Access (GameStateReader)
- `RunManager.Instance` → singleton
- `RunManager.State` (non-public) → `RunState` via reflection
- `RunState.Players[0]` → `Player`
- `Player.Character.Id.Entry` → character string
- `Player.Creature.CurrentHp / MaxHp` → HP
- `Player.Gold` → gold
- `Player.Deck.Cards` → deck
- `Player.Relics` → relics
- `RunState.TotalFloor` → current floor
- `RunState.CurrentActIndex + 1` → act number
- `CardModel.Id.Entry` → card ID, `CardModel.Title` → display name

## Build & Deploy
```bash
# Build
cd STS2Advisor
dotnet build -c Release

# Deploy (copy everything from bin/Release/net9.0/ to game mods folder)
# Target: "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\STS2Advisor\"
# Must include: DLL, deps.json, runtimeconfig.json, dependency DLLs, runtimes/ folder, Data/ folder

# Launch
# Use launch_modded.bat in game root (sets DOTNET_STARTUP_HOOKS=mods\STS2Advisor\STS2Advisor.dll)
```

## JSON Tier Data Format
```json
// CardTiers/<character>.json
{
  "character": "ironclad",
  "cards": [
    { "id": "Body Slam", "baseTier": "B", "synergies": ["block", "block_scaling"], "antiSynergies": [], "notes": "S-tier in block decks" }
  ]
}

// RelicTiers/<character>.json  (or common.json)
{
  "category": "ironclad",
  "relics": [
    { "id": "Red Skull", "baseTier": "B", "synergies": ["self_damage", "strength"], "antiSynergies": [], "notes": "+3 str below 50% HP" }
  ]
}
```
Tiers: S > A > B > C > D > F. Synergy tags must match archetype CoreTags/SupportTags in ArchetypeDefinitions.cs.

## Characters
- **Ironclad**: strength, exhaust, block, self_damage archetypes (87 cards)
- **Silent**: poison, shiv, discard, dexterity archetypes (88 cards)
- **Defect**: lightning, frost, dark, all_orbs, zero_cost archetypes (88 cards)
- **Regent**: stellar, authority, minion, cosmic_block archetypes (88 cards) — NEW in STS2
- **Necrobinder**: soul, summon, death, debuff archetypes (88 cards) — NEW in STS2
- **Colorless**: 64 cards (shared pool)
- **Deprived**: placeholder data — not confirmed in current EA build

## Important Notes
- All card/relic IDs extracted from game DLL via IL bytecode scanning (ModelDb.Card<T>/ModelDb.Relic<T> generic args)
- Tier assignments for Regent/Necrobinder are educated guesses — adaptive system auto-corrects with play data
- `deprived` character may not exist in current Early Access
- No external server/API — everything is local
- Mod version: 0.3.0
- Game references: `lib/0Harmony.dll`, `lib/GodotSharp.dll`, `lib/sts2.dll` (copied from game's data folder)
