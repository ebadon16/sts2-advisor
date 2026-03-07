# Qu'est-ce Spire? — Slay the Spire 2 Mod

## What This Is
A real-time card/relic tier advisor overlay for Slay the Spire 2. Shows tier badges (S/A/B/C/D/F), synergy analysis, and "best pick" recommendations on card reward, relic selection, and shop screens. Self-learning: local play data improves advice over time.

## Tech Stack
- **Game**: Slay the Spire 2 (Godot 4.5.1 + C# / .NET 9.0)
- **Mod injection**: PCK file + DLL loaded by game's mod system
- **Patching**: Harmony library for runtime method hooking
- **UI**: Godot CanvasLayer overlay (layer 100) with F7-F11 hotkeys
- **Database**: SQLite via Microsoft.Data.Sqlite for local run tracking
- **Serialization**: Newtonsoft.Json

## Project Structure
```
QuestceSpire/
  Plugin.cs              — Entry point, Harmony patches, mod init
  GamePatches.cs         — Harmony patch definitions
  Core/
    TierEngine.cs        — Loads JSON tier data, card/relic lookup with NormalizeId()
    SynergyScorer.cs     — Scores offerings using tier data + deck archetypes + adaptive data
    DeckAnalyzer.cs      — Detects deck archetypes from game tags + tier JSON synergy tags
    AdaptiveScorer.cs    — Blends static tiers with local win rate data (5-50 sample ramp)
    ArchetypeDefinitions.cs — Archetype patterns per character
    DeckAnalysis.cs      — Deck analysis result model
    OverlaySettings.cs   — Persisted overlay position/state settings
  GameBridge/
    GameStateReader.cs   — Reads game state via reflection (RunManager, Player, Deck, etc.)
    CardInfo.cs          — CardInfo model
    RelicInfo.cs         — RelicInfo model
    GameState.cs         — Game state snapshot model
  Tracking/
    RunTracker.cs        — Run lifecycle tracking (start/end/decisions)
    RunDatabase.cs       — SQLite schema + CRUD for runs/decisions/community stats
    LocalStatsComputer.cs — Recomputes pick/win rates from local play data after each run
    DecisionEvent.cs     — Decision event model
    CommunityCardStats.cs — Community card stats model
    CommunityRelicStats.cs — Community relic stats model
    GameDataImporter.cs  — Imports game data
  UI/
    OverlayManager.cs    — Godot CanvasLayer overlay, panel, badges, tooltips, deck viz
    OverlayInputHandler.cs — Keyboard shortcut handling
    TierBadge.cs         — Tier-to-color mapping
  Data/
    CardTiers/*.json     — Tier data per character (ironclad, silent, defect, regent, necrobinder, colorless)
    RelicTiers/*.json    — Relic tier data per character + common pool
```

## Key Patterns

### ID Format
- Game uses UPPERCASE_SNAKE_CASE: `BODY_SLAM`, `FEEL_NO_PAIN`
- JSON tier data uses Title Case with spaces: `Body Slam`, `Feel No Pain`
- `TierEngine.NormalizeId()` handles this: replaces spaces with underscores, case-insensitive comparison
- Character IDs from game: `ironclad`, `silent`, `defect`, `regent`, `necrobinder` (lowercase)

### Harmony Patches (in GamePatches.cs)
- `NCardRewardSelectionScreen.ShowScreen` (static postfix) — card rewards after combat
- `NChooseARelicSelection.ShowScreen` (static postfix) — relic selection
- `NMerchantInventory.Open` (postfix) — shop screen
- `RunManager.Launch` (postfix) — run start, extracts character + ascension
- `RunManager.OnEnded` (postfix) — run end (returns SerializableRun), triggers LocalStats recompute
- Use manual patching (AccessTools.Method + harmony.Patch) for reliability

### Self-Learning System
1. `RunTracker` records every card/relic offering + choice during play
2. On run end, `LocalStatsComputer.RecomputeAll()` runs SQL to compute pick rates + win rates
3. Results go into `community_card_stats` / `community_relic_stats` tables
4. `AdaptiveScorer` blends static JSON tiers with local data (confidence ramps from 5 to 50 samples)
5. More you play → smarter the advice gets

## Build & Deploy
```bash
# Build
cd QuestceSpire
dotnet build -c Release

# Deploy (copy everything from bin/Release/net9.0/ to game mods folder)
# Target: "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\QuestceSpire\"
# Must include: DLL, deps.json, runtimeconfig.json, runtimes/ folder, Data/ folder
# PCK file required: generated with custom tool (Godot 4.5 PCK v2 format)
# Close game before deploying — DLL gets locked by the game process
```

## Characters
- **Ironclad**: strength, exhaust, block, self_damage archetypes (87 cards)
- **Silent**: poison, shiv, discard, dexterity archetypes (88 cards)
- **Defect**: lightning, frost, dark, all_orbs, zero_cost archetypes (88 cards)
- **Regent**: stellar, authority, minion, cosmic_block archetypes (88 cards)
- **Necrobinder**: soul, summon, death, debuff archetypes (88 cards)
- **Colorless**: 64 cards (shared pool)

## Important Notes
- All card/relic IDs extracted from game DLL via IL bytecode scanning
- Tier assignments for Regent/Necrobinder are educated guesses — adaptive system auto-corrects with play data
- No external server/API — everything is local
- Mod version: 0.6.2
- Game references: `lib/0Harmony.dll`, `lib/GodotSharp.dll`, `lib/sts2.dll` (copied from game's data folder)
