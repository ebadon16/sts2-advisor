# STS2 Advisor - Setup Guide

## Game Info

- **Engine:** Godot 4.5.1 with C# (.NET 9.0)
- **Game assembly:** `sts2.dll` (in `data_sts2_windows_x86_64/`)
- **Harmony:** 0Harmony 2.4.2 ships with the game
- **Mod injection:** .NET Startup Hooks (`DOTNET_STARTUP_HOOKS`)

## Prerequisites

- **Slay the Spire 2** installed on Windows (Steam)
- **.NET SDK 9.0** ([download](https://dotnet.microsoft.com/download/dotnet/9.0))
- **dnSpy**, **ILSpy**, or **dotPeek** for decompiling sts2.dll

## Step 1: Copy Reference DLLs

Create a `lib/` folder in the project root and copy these from
`<STS2 install>/data_sts2_windows_x86_64/`:

```
lib/0Harmony.dll
lib/GodotSharp.dll
lib/sts2.dll
```

## Step 2: Find Game Classes with dnSpy

Open `sts2.dll` in dnSpy (or ILSpy/dotPeek). This is the game's main assembly
containing all C# game logic.

### 2a: Game Initialization (for overlay creation)

Search for the main game manager or autoload that runs at startup:
- Look for classes with `_Ready()` methods
- Search for "GameManager", "Main", "GameController"
- We need a method that runs after the SceneTree is ready

### 2b: Card Reward Screen

Search: `CardReward`, `RewardScreen`, `CardChoice`, `CombatReward`

Look for:
- A class that manages card selection after combat
- A method that opens/shows/populates the reward cards
- The collection of offered card objects

### 2c: Relic Selection Screen

Search: `RelicReward`, `BossRelic`, `RelicChoice`, `RelicSelect`

### 2d: Shop Screen

Search: `ShopScreen`, `Merchant`, `Store`, `ShopItem`

### 2e: Player/Deck State

Search: `Player`, `Deck`, `MasterDeck`, `Character`, `RunState`

Look for:
- Current character (enum, string, or class)
- Deck contents (List/Array of card objects)
- Current relics
- Act/floor number
- HP, gold

### 2f: Card Object

Search for the card data class. Note field names for:
- Card ID/name
- Energy cost
- Card type (Attack/Skill/Power)
- Upgraded status
- Keywords/tags

### Godot-Specific Patterns

STS2 uses Godot + C#, so expect:
- `_Ready()` instead of `Awake()`/`Start()`
- `_Process(double delta)` instead of `Update()`
- Signals instead of Unity events
- Node tree instead of GameObject hierarchy
- `GetNode<T>()` for references

## Step 3: Update Code

### 3a: GameStateReader.cs
Replace all placeholder methods with real reads using the class/field names
found in Step 2.

### 3b: Plugin.cs
Replace `PlaceholderCardRewardScreen`, `PlaceholderRelicRewardScreen`, and
`PlaceholderShopScreen` with real game classes. Add the game init patch
for overlay creation.

### 3c: Delete Placeholders
Remove the `#if !STS2_REAL_HOOKS` placeholder classes at the bottom of Plugin.cs.

## Step 4: Build

```bash
dotnet build -c Release
```

Output: `STS2Advisor/bin/Release/net9.0/`

## Step 5: Install

### Option A: Startup Hook (Recommended)

1. Copy `STS2Advisor.dll` + `Data/` folder to a mod directory:
   ```
   <STS2 install>/mods/STS2Advisor/
     STS2Advisor.dll
     Newtonsoft.Json.dll
     Microsoft.Data.Sqlite.dll
     SQLitePCLRaw.core.dll
     SQLitePCLRaw.provider.e_sqlite3.dll
     Data/
       CardTiers/*.json
       RelicTiers/*.json
   ```

2. Set the startup hook environment variable. Create/edit a batch file:
   ```batch
   @echo off
   set DOTNET_STARTUP_HOOKS=mods\STS2Advisor\STS2Advisor.dll
   SlayTheSpire2.exe
   ```
   Save as `launch_modded.bat` in the game folder.

3. Run `launch_modded.bat` instead of launching from Steam.

### Option B: Steam Launch Options

Right-click STS2 in Steam → Properties → Launch Options:
```
DOTNET_STARTUP_HOOKS=data_sts2_windows_x86_64\..\mods\STS2Advisor\STS2Advisor.dll %command%
```

Note: The exact path may need adjustment. The startup hook path must be
relative to the working directory or absolute.

## Step 6: Verify

1. Launch game via the modded launcher
2. Check `mods/STS2Advisor/sts2advisor.log` for:
   ```
   [STS2 Advisor] STS2 Advisor v0.2.0 initialized successfully.
   ```
3. Start a run and open a card reward screen
4. Tier badges should appear on the right side
5. **F7** to toggle overlay, **F8** for tooltips

## Troubleshooting

### Mod doesn't load (no log file)
- Verify the `DOTNET_STARTUP_HOOKS` path is correct
- The path must point exactly to `STS2Advisor.dll`
- Try an absolute path: `C:\...\mods\STS2Advisor\STS2Advisor.dll`

### "Method not found" or "Type not found" in log
- Harmony patches target wrong class/method names
- Re-check with dnSpy and update Plugin.cs

### Overlay doesn't appear
- The game init patch may be targeting the wrong _Ready() method
- Check the log for "Overlay created" or "SceneTree not ready"

### Cards show as C-tier (not recognized)
- Card IDs in JSON must match sts2.dll's internal card IDs exactly
- Open dnSpy, find a card class, note the exact ID format
- Update JSON files to match

### Game crashes on launch
- Target framework mismatch: ensure you're building for net9.0
- Missing dependency: copy all required DLLs to the mod folder
- Check sts2advisor.log for the error before crash
