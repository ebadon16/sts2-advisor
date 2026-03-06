using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using STS2Advisor.API.Data;
using STS2Advisor.API.Models;

namespace STS2Advisor.API.Services;

public class StatsEngine
{
    private readonly AppDbContext _db;

    // Tags used for archetype context detection.
    // If a deck snapshot contains 3+ cards matching any tag, that becomes a context label.
    private static readonly string[] ContextTags =
    {
        "poison", "shiv", "strength", "exhaust", "block", "draw", "discard",
        "focus", "frost", "lightning", "dark", "orb", "zero_cost",
        "command", "summon", "decree", "undead", "curse", "sacrifice"
    };

    private const int ContextThreshold = 3;

    public StatsEngine(AppDbContext db)
    {
        _db = db;
    }

    // ─── Card Stats ───

    public async Task<CardStatsResponse> ComputeCardStats(string character)
    {
        character = character.ToLowerInvariant();

        // Pull all card-related decisions for this character in one query,
        // joining with runs to get outcome. Only completed runs.
        var decisions = await _db.Decisions
            .AsNoTracking()
            .Where(d => d.Run!.Character == character
                && (d.EventType == "CardReward" || d.EventType == "Shop" || d.EventType == "CardTransform")
                && (d.Run.Outcome == "win" || d.Run.Outcome == "loss"))
            .Select(d => new
            {
                d.OfferedIds,
                d.ChosenId,
                d.DeckSnapshot,
                d.Floor,
                RunOutcome = d.Run!.Outcome
            })
            .ToListAsync();

        // Parse and aggregate in memory
        var cardAgg = new Dictionary<string, CardAggregator>();

        foreach (var d in decisions)
        {
            var offered = ParseJsonArray(d.OfferedIds);
            var deck = ParseJsonArray(d.DeckSnapshot);
            var contexts = DetectContexts(deck);
            bool isWin = d.RunOutcome == "win";

            foreach (var cardId in offered)
            {
                if (!cardAgg.TryGetValue(cardId, out var agg))
                {
                    agg = new CardAggregator();
                    cardAgg[cardId] = agg;
                }

                agg.TimesOffered++;
                bool picked = cardId == d.ChosenId;

                if (picked)
                {
                    agg.TimesPicked++;
                    agg.FloorSum += d.Floor;
                    if (isWin) agg.WinsWhenPicked++;
                }
                else
                {
                    agg.TimesSkipped++;
                    if (isWin) agg.WinsWhenSkipped++;
                }

                // Context stats: only for the chosen card
                if (picked)
                {
                    foreach (var ctx in contexts)
                    {
                        if (!agg.ContextPicked.ContainsKey(ctx))
                        {
                            agg.ContextPicked[ctx] = 0;
                            agg.ContextWins[ctx] = 0;
                        }
                        agg.ContextPicked[ctx]++;
                        if (isWin) agg.ContextWins[ctx]++;
                    }
                }
            }
        }

        var entries = cardAgg
            .Where(kv => kv.Value.TimesOffered >= 5) // minimum sample
            .Select(kv =>
            {
                var a = kv.Value;
                var contextStats = new Dictionary<string, float>();
                foreach (var ctx in a.ContextPicked.Keys)
                {
                    int picks = a.ContextPicked[ctx];
                    if (picks >= 3) // minimum context sample
                    {
                        contextStats[ctx] = (float)a.ContextWins[ctx] / picks;
                    }
                }

                return new CardStatEntry
                {
                    CardId = kv.Key,
                    PickRate = (float)a.TimesPicked / a.TimesOffered,
                    WinRateWhenPicked = a.TimesPicked > 0 ? (float)a.WinsWhenPicked / a.TimesPicked : 0f,
                    WinRateWhenSkipped = a.TimesSkipped > 0 ? (float)a.WinsWhenSkipped / a.TimesSkipped : 0f,
                    SampleSize = a.TimesOffered,
                    AvgFloorPicked = a.TimesPicked > 0 ? (float)a.FloorSum / a.TimesPicked : 0f,
                    ContextStats = contextStats
                };
            })
            .OrderByDescending(e => e.SampleSize)
            .ToList();

        return new CardStatsResponse
        {
            Character = character,
            Cards = entries,
            GeneratedAt = DateTime.UtcNow
        };
    }

    // ─── Relic Stats ───

    public async Task<RelicStatsResponse> ComputeRelicStats(string character)
    {
        character = character.ToLowerInvariant();

        var decisions = await _db.Decisions
            .AsNoTracking()
            .Where(d => d.Run!.Character == character
                && (d.EventType == "RelicReward" || d.EventType == "BossRelic")
                && (d.Run.Outcome == "win" || d.Run.Outcome == "loss"))
            .Select(d => new
            {
                d.OfferedIds,
                d.ChosenId,
                d.DeckSnapshot,
                d.Floor,
                RunOutcome = d.Run!.Outcome
            })
            .ToListAsync();

        var relicAgg = new Dictionary<string, CardAggregator>(); // reuse same shape

        foreach (var d in decisions)
        {
            var offered = ParseJsonArray(d.OfferedIds);
            var deck = ParseJsonArray(d.DeckSnapshot);
            var contexts = DetectContexts(deck);
            bool isWin = d.RunOutcome == "win";

            foreach (var relicId in offered)
            {
                if (!relicAgg.TryGetValue(relicId, out var agg))
                {
                    agg = new CardAggregator();
                    relicAgg[relicId] = agg;
                }

                agg.TimesOffered++;
                bool picked = relicId == d.ChosenId;

                if (picked)
                {
                    agg.TimesPicked++;
                    agg.FloorSum += d.Floor;
                    if (isWin) agg.WinsWhenPicked++;
                }
                else
                {
                    agg.TimesSkipped++;
                    if (isWin) agg.WinsWhenSkipped++;
                }

                if (picked)
                {
                    foreach (var ctx in contexts)
                    {
                        if (!agg.ContextPicked.ContainsKey(ctx))
                        {
                            agg.ContextPicked[ctx] = 0;
                            agg.ContextWins[ctx] = 0;
                        }
                        agg.ContextPicked[ctx]++;
                        if (isWin) agg.ContextWins[ctx]++;
                    }
                }
            }
        }

        var entries = relicAgg
            .Where(kv => kv.Value.TimesOffered >= 5)
            .Select(kv =>
            {
                var a = kv.Value;
                var contextStats = new Dictionary<string, float>();
                foreach (var ctx in a.ContextPicked.Keys)
                {
                    int picks = a.ContextPicked[ctx];
                    if (picks >= 3)
                    {
                        contextStats[ctx] = (float)a.ContextWins[ctx] / picks;
                    }
                }

                return new RelicStatEntry
                {
                    RelicId = kv.Key,
                    PickRate = (float)a.TimesPicked / a.TimesOffered,
                    WinRateWhenPicked = a.TimesPicked > 0 ? (float)a.WinsWhenPicked / a.TimesPicked : 0f,
                    WinRateWhenSkipped = a.TimesSkipped > 0 ? (float)a.WinsWhenSkipped / a.TimesSkipped : 0f,
                    SampleSize = a.TimesOffered,
                    AvgFloorPicked = a.TimesPicked > 0 ? (float)a.FloorSum / a.TimesPicked : 0f,
                    ContextStats = contextStats
                };
            })
            .OrderByDescending(e => e.SampleSize)
            .ToList();

        return new RelicStatsResponse
        {
            Character = character,
            Relics = entries,
            GeneratedAt = DateTime.UtcNow
        };
    }

    // ─── Helpers ───

    private static List<string> ParseJsonArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Scans a deck snapshot for dominant archetype tags.
    /// Returns context labels like "poison_3+" for any tag appearing 3+ times.
    /// </summary>
    private static List<string> DetectContexts(List<string> deckSnapshot)
    {
        var tagCounts = new Dictionary<string, int>();
        foreach (var cardId in deckSnapshot)
        {
            var lower = cardId.ToLowerInvariant();
            foreach (var tag in ContextTags)
            {
                if (lower.Contains(tag))
                {
                    tagCounts.TryGetValue(tag, out int count);
                    tagCounts[tag] = count + 1;
                }
            }
        }

        var contexts = new List<string>();
        foreach (var kv in tagCounts)
        {
            if (kv.Value >= ContextThreshold)
            {
                contexts.Add($"{kv.Key}_{kv.Value}+");
            }
        }

        return contexts;
    }

    // ─── Aggregator ───

    private class CardAggregator
    {
        public int TimesOffered;
        public int TimesPicked;
        public int TimesSkipped;
        public int WinsWhenPicked;
        public int WinsWhenSkipped;
        public int FloorSum;
        public Dictionary<string, int> ContextPicked = new();
        public Dictionary<string, int> ContextWins = new();
    }
}
