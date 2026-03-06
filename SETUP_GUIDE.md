# STS2 Advisor - Setup Guide

## Prerequisites

- **Slay the Spire 2** installed on Windows (Steam)
- **.NET SDK 8.0** ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- **dnSpy** or **ILSpy** for decompiling game assemblies

## Step 1: Install BepInEx

1. Download **BepInEx 5.x** (check if STS2 is Mono or IL2CPP):
   - Mono: [BepInEx 5 Stable](https://github.com/BepInEx/BepInEx/releases)
   - IL2CPP: [BepInEx 6 Bleeding Edge](https://builds.bepinex.dev/projects/bepinex_be)
2. Extract into the STS2 game folder (where `Slay the Spire 2.exe` lives)
3. Run the game once, then close it
4. Verify `BepInEx/` folder now contains `config/`, `plugins/`, etc.

### How to check Mono vs IL2CPP:
- Look in `Slay the Spire 2_Data/Managed/`
- If you see `Assembly-CSharp.dll` → **Mono** (use BepInEx 5)
- If you see `GameAssembly.dll` instead → **IL2CPP** (use BepInEx 6, requires different setup)

## Step 2: Copy Reference DLLs

Create a `lib/` folder in the project root and copy these files:

**From BepInEx/core/:**
```
lib/BepInEx.dll
lib/0Harmony.dll
```

**From Slay the Spire 2_Data/Managed/:**
```
lib/UnityEngine.dll
lib/UnityEngine.CoreModule.dll
lib/UnityEngine.IMGUIModule.dll
lib/UnityEngine.InputLegacyModule.dll
lib/Assembly-CSharp.dll
```

## Step 3: Find Game Classes with dnSpy

This is the critical step. Open `Assembly-CSharp.dll` in dnSpy and find the real class/method names for:

### 3a: Card Reward Screen

Search for keywords: `CardReward`, `RewardScreen`, `CombatReward`

What you're looking for:
- A class that manages the post-combat card selection screen
- A method that opens/shows this screen (this is what we'll patch)
- A field/property containing the list of offered cards

Once found, update `Plugin.cs`:
```csharp
// Replace PlaceholderCardRewardScreen with the real class
// Replace "OnOpen" with the real method name
[HarmonyPatch(typeof(RealCardRewardScreenClass), "RealMethodName")]
```

### 3b: Relic Selection Screen

Search for: `RelicReward`, `BossRelic`, `RelicChoice`

Look for:
- Boss relic choice screen
- Regular relic reward screen
- The list of offered relics

### 3c: Shop Screen

Search for: `ShopScreen`, `Merchant`, `StoreScreen`

Look for:
- The shop inventory class
- Cards for sale list
- Relics for sale list

### 3d: Player/Deck State

Search for: `AbstractPlayer`, `PlayerManager`, `MasterDeck`, `CharacterType`

Look for:
- Current character (enum or string)
- Master deck (List of card objects)
- Current relics (List of relic objects)
- Act number

### 3e: Card Object Fields

When you find the card class, note the field names for:
- Card ID/name
- Energy cost
- Card type (Attack/Skill/Power)
- Whether it's upgraded
- Keywords or tags

## Step 4: Update GameStateReader.cs

Replace all placeholder methods in `GameBridge/GameStateReader.cs` with real reads using the class/field names you found in Step 3.

## Step 5: Update Plugin.cs Harmony Patches

Replace the `PlaceholderCardRewardScreen`, `PlaceholderRelicRewardScreen`, and `PlaceholderShopScreen` references with the real game classes.

Then delete or `#if` out the placeholder classes at the bottom of Plugin.cs.

## Step 6: Build

```bash
dotnet build -c Release
```

The output DLL will be in `STS2Advisor/bin/Release/netstandard2.1/`.

## Step 7: Install

1. Copy `STS2Advisor.dll` to `BepInEx/plugins/`
2. Copy the `Data/` folder (with all JSON files) to `BepInEx/plugins/Data/`
3. Also copy `Newtonsoft.Json.dll` from the build output to `BepInEx/plugins/`

Your plugins folder should look like:
```
BepInEx/plugins/
  STS2Advisor.dll
  Newtonsoft.Json.dll
  Data/
    CardTiers/
      ironclad.json
      silent.json
      defect.json
      regent.json
      necromancer.json
      colorless.json
    RelicTiers/
      common.json
      ironclad.json
      silent.json
      defect.json
      regent.json
      necromancer.json
```

## Step 8: Launch and Verify

1. Start STS2
2. Check `BepInEx/LogOutput.log` for: `STS2 Advisor v0.1.0 loaded successfully.`
3. Start a run and open a card reward screen
4. Tier badges should appear. Press **F7** to toggle, **F8** for tooltips.

## Troubleshooting

### "Method not found" errors in log
The Harmony patches are targeting wrong method names. Re-check with dnSpy.

### Overlay doesn't appear
- Check the log for GameStateReader warnings (placeholder methods)
- Verify screen detection is triggering (card reward patch firing)

### Cards not recognized (all showing C-tier)
- Card IDs in the JSON files must match the game's internal card IDs exactly
- Open dnSpy, find a card, note its exact `cardID` field value
- Update JSON files to match

### IL2CPP game (no Assembly-CSharp.dll)
If STS2 uses IL2CPP, you'll need BepInEx 6 with Il2CppInterop. The patching approach is similar but uses `Il2CppInterop.Runtime` instead of direct type references. This requires more setup — see the BepInEx 6 docs.
