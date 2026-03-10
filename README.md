# Qu'est-ce Spire? — AI Card Advisor for Slay the Spire 2

A real-time overlay mod that grades every card and relic offering, powered by community win-rate data. Think SpireLogs meets an in-game coach.

![Overlay Demo](docs/overlay-demo.png)

## What It Does

When you see a card reward, shop, or relic selection, the overlay instantly shows:

- **Tier grades** (S/A/B/C/D/F) for every option
- **Win-delta scores** — how much picking this card improves your chance of winning
- **Synergy analysis** — grades shift based on your current deck and archetype
- **"Best Pick" recommendation** with explanation

The advice gets smarter over time. Every run you play feeds back into the scoring system — your local data blends with community-wide stats from thousands of runs.

## How It Works

1. **Static tier data** — expert-curated base ratings for all 400+ cards and 150+ relics
2. **Deck analysis** — detects your archetype (poison, strength, frost, etc.) and adjusts grades for synergy
3. **Local learning** — tracks your picks and outcomes, computes personal win rates after each run
4. **Community stats** — anonymously uploads run data to a shared server, downloads aggregated pick/win rates from all players
5. **Adaptive blending** — as sample sizes grow, community data gradually overrides static tiers

## Supported Characters

| Character | Cards | Archetypes |
|-----------|-------|------------|
| Ironclad | 87 | Strength, Exhaust, Block, Self-Damage |
| Silent | 88 | Poison, Shiv, Discard, Dexterity |
| Defect | 88 | Lightning, Frost, Dark, All Orbs, Zero-Cost |
| Regent | 88 | Stellar, Authority, Minion, Cosmic Block |
| Necrobinder | 88 | Soul, Summon, Death, Debuff |
| Colorless | 64 | — |

## Installation

### Steam Workshop (Recommended)
Subscribe on the [Steam Workshop page](#) — the mod auto-installs and updates.

### Manual Install
1. Download the [latest release](https://github.com/ebadon16/sts2-advisor/releases)
2. Extract to: `Slay the Spire 2/mods/QuestceSpire/`
3. The folder should contain: `QuestceSpire.pck`, `QuestceSpire.dll`, `Data/`, and runtime files
4. Launch the game — the overlay appears automatically on card/relic screens

## Controls

| Key | Action |
|-----|--------|
| F7 | Toggle overlay visibility |
| F8 | Toggle in-game grade badges |
| F9 | Cycle panel position (left/right) |
| F10 | Toggle deck breakdown |
| F11 | Toggle draw probability |

## Community Stats

Run data is anonymously synced to a shared server. You can browse community-wide pick rates and win deltas at:

**[questcespire-api.questcespire.workers.dev](https://questcespire-api.questcespire.workers.dev)**

Opt out anytime by setting `CloudSyncEnabled: false` in `overlay_settings.json`.

## Community Stats API

```
GET  /api/stats?character=silent&min_samples=5
POST /api/upload   — used by the mod automatically
POST /api/aggregate — recomputes stats (runs every 6 hours)
```

## Building from Source

```bash
# Build the mod
cd QuestceSpire
dotnet build -c Release

# Deploy to game (close the game first!)
cp -r bin/Release/net9.0/* "Slay the Spire 2/mods/QuestceSpire/"

# Run the API locally
cd questcespire-api
npm install
npx wrangler dev
```

Requires: .NET 9.0 SDK, game DLLs in `lib/` (0Harmony.dll, GodotSharp.dll, sts2.dll)

## Tech Stack

- **Mod**: C# / .NET 9.0, Harmony for runtime patching, Godot CanvasLayer overlay, SQLite for local tracking
- **API**: Cloudflare Worker (TypeScript) + D1 (SQLite), free tier
- **Game**: Slay the Spire 2 (Godot 4.5.1)

## Support

If this mod helps your runs, consider buying me a coffee:

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/redfred)

## Contributing

PRs welcome. The tier data for Regent and Necrobinder is mostly educated guesses — if you have strong opinions backed by high-ascension experience, open an issue or PR against the JSON files in `Data/CardTiers/`.

## License

MIT
