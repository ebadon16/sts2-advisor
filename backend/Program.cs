using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QuestceSpire.API.Data;
using QuestceSpire.API.Models;
using QuestceSpire.API.Services;

var builder = WebApplication.CreateBuilder(args);

// ─── Services ───

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<StatsEngine>();
builder.Services.AddMemoryCache();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "STS2 Advisor API", Version = "v1" });
});

// CORS — allow all (mod clients are desktop apps)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// Rate limiting: 60 requests/minute per IP
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Request size limit: 10MB
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
});

var app = builder.Build();

// ─── Middleware ───

app.UseCors();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ─── Auto-migrate on startup ───

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// ─── Configuration ───

var cacheDuration = TimeSpan.FromMinutes(
    builder.Configuration.GetValue<int>("CacheDurationMinutes", 15));
var maxBulkSize = builder.Configuration.GetValue<int>("MaxBulkSize", 100);

// ─── Endpoints ───

// Health check
app.MapGet("/api/health", async (AppDbContext db) =>
{
    var playerCount = await db.Runs.Select(r => r.PlayerId).Distinct().CountAsync();
    var runCount = await db.Runs.CountAsync();

    return Results.Ok(new HealthResponse
    {
        Status = "ok",
        PlayerCount = playerCount,
        RunCount = runCount,
        Timestamp = DateTime.UtcNow
    });
});

// Bulk run submission
app.MapPost("/api/runs/bulk", async (HttpContext httpContext, AppDbContext db) =>
{
    // Accept three formats:
    // 1. Flat format from mod's CloudSync: { player_id, runs[], decisions[] } with snake_case
    // 2. Array format: [{ run: {...}, decisions: [...] }]
    // 3. Wrapper format: { runs: [{ run: {...}, decisions: [...] }] }
    List<RunSubmission>? submissions = null;

    try
    {
        var body = await new StreamReader(httpContext.Request.Body).ReadToEndAsync();
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Try flat format first (what the mod actually sends via CloudSync)
        var flat = JsonSerializer.Deserialize<FlatUploadPayload>(body, jsonOpts);
        if (flat != null && !string.IsNullOrEmpty(flat.PlayerId) && flat.Runs?.Count > 0)
        {
            // Convert flat format to nested RunSubmission list
            var decisionsByRun = new Dictionary<string, List<FlatDecisionDto>>();
            foreach (var d in flat.Decisions ?? new())
            {
                if (string.IsNullOrEmpty(d.RunId)) continue;
                if (!decisionsByRun.ContainsKey(d.RunId))
                    decisionsByRun[d.RunId] = new();
                decisionsByRun[d.RunId].Add(d);
            }

            submissions = new List<RunSubmission>();
            foreach (var r in flat.Runs)
            {
                var sub = new RunSubmission
                {
                    Run = new RunLogDto
                    {
                        RunId = r.RunId,
                        PlayerId = flat.PlayerId,
                        Character = r.Character,
                        Seed = r.Seed ?? "",
                        StartTime = DateTime.TryParse(r.StartTime, out var st) ? st : DateTime.UtcNow,
                        EndTime = DateTime.TryParse(r.EndTime, out var et) ? et : null,
                        Outcome = r.Outcome,
                        FinalFloor = r.FinalFloor,
                        FinalAct = r.FinalAct,
                        AscensionLevel = r.AscensionLevel,
                    },
                    Decisions = new()
                };

                if (decisionsByRun.TryGetValue(r.RunId, out var decs))
                {
                    foreach (var fd in decs)
                    {
                        sub.Decisions.Add(new DecisionEventDto
                        {
                            RunId = fd.RunId,
                            Floor = fd.Floor,
                            Act = fd.Act,
                            EventType = fd.EventType,
                            OfferedIds = ParseJsonStringArray(fd.OfferedIds),
                            ChosenId = fd.ChosenId,
                            DeckSnapshot = ParseJsonStringArray(fd.DeckSnapshot),
                            RelicSnapshot = ParseJsonStringArray(fd.RelicSnapshot),
                            CurrentHP = fd.CurrentHP,
                            MaxHP = fd.MaxHP,
                            Gold = fd.Gold,
                            Timestamp = DateTime.TryParse(fd.Timestamp, out var ts) ? ts : DateTime.UtcNow,
                        });
                    }
                }

                submissions.Add(sub);
            }
        }

        if (submissions == null || submissions.Count == 0)
        {
            // Try array format: [{ run, decisions }]
            submissions = JsonSerializer.Deserialize<List<RunSubmission>>(body, jsonOpts);
        }

        if (submissions == null || submissions.Count == 0)
        {
            // Try wrapper format: { runs: [{ run, decisions }] }
            var wrapper = JsonSerializer.Deserialize<BulkRunSubmission>(body, jsonOpts);
            submissions = wrapper?.Runs;
        }
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "Invalid JSON payload" });
    }

    if (submissions == null || submissions.Count == 0)
        return Results.BadRequest(new { error = "No runs provided" });

    if (submissions.Count > maxBulkSize)
        return Results.BadRequest(new { error = $"Bulk size exceeds maximum of {maxBulkSize}" });

    int accepted = 0;
    int duplicates = 0;

    foreach (var sub in submissions)
    {
        var runDto = sub.Run;

        if (string.IsNullOrWhiteSpace(runDto.RunId) || string.IsNullOrWhiteSpace(runDto.PlayerId)
            || string.IsNullOrWhiteSpace(runDto.Character))
            continue;

        // Deduplicate by RunId
        bool exists = await db.Runs.AnyAsync(r => r.RunId == runDto.RunId);
        if (exists)
        {
            duplicates++;
            continue;
        }

        // Normalize outcome
        string outcome = (runDto.Outcome?.ToLowerInvariant()) switch
        {
            "win" => "win",
            "loss" => "loss",
            _ => "loss" // default to loss if unknown
        };

        var dbRun = new DbRun
        {
            RunId = runDto.RunId,
            PlayerId = runDto.PlayerId,
            Character = runDto.Character.ToLowerInvariant(),
            Seed = runDto.Seed ?? "",
            AscensionLevel = runDto.AscensionLevel,
            Outcome = outcome,
            FinalFloor = runDto.FinalFloor,
            FinalAct = runDto.FinalAct,
            StartTime = runDto.StartTime,
            EndTime = runDto.EndTime,
            SubmittedAt = DateTime.UtcNow
        };

        db.Runs.Add(dbRun);

        foreach (var dec in sub.Decisions)
        {
            var dbDec = new DbDecision
            {
                RunId = runDto.RunId,
                Floor = dec.Floor,
                Act = dec.Act,
                EventType = dec.EventType,
                OfferedIds = JsonSerializer.Serialize(dec.OfferedIds),
                ChosenId = dec.ChosenId,
                DeckSnapshot = JsonSerializer.Serialize(dec.DeckSnapshot),
                RelicSnapshot = JsonSerializer.Serialize(dec.RelicSnapshot),
                CurrentHP = dec.CurrentHP,
                MaxHP = dec.MaxHP,
                Gold = dec.Gold,
                Timestamp = dec.Timestamp
            };

            db.Decisions.Add(dbDec);
        }

        accepted++;
    }

    if (accepted > 0)
    {
        await db.SaveChangesAsync();
    }

    return Results.Ok(new BulkSubmitResponse
    {
        Accepted = accepted,
        Duplicates = duplicates
    });
});

// Card stats
app.MapGet("/api/stats/cards/{character}", async (
    string character,
    StatsEngine engine,
    IMemoryCache cache) =>
{
    var cacheKey = $"card_stats_{character.ToLowerInvariant()}";

    if (cache.TryGetValue<CardStatsResponse>(cacheKey, out var cached) && cached != null)
        return Results.Ok(cached);

    var stats = await engine.ComputeCardStats(character);

    cache.Set(cacheKey, stats, cacheDuration);

    return Results.Ok(stats);
});

// Relic stats
app.MapGet("/api/stats/relics/{character}", async (
    string character,
    StatsEngine engine,
    IMemoryCache cache) =>
{
    var cacheKey = $"relic_stats_{character.ToLowerInvariant()}";

    if (cache.TryGetValue<RelicStatsResponse>(cacheKey, out var cached) && cached != null)
        return Results.Ok(cached);

    var stats = await engine.ComputeRelicStats(character);

    cache.Set(cacheKey, stats, cacheDuration);

    return Results.Ok(stats);
});

app.Run();

/// <summary>
/// Parses a pre-serialized JSON string array (e.g. "[\"A\",\"B\"]") into List&lt;string&gt;.
/// The mod's CloudSync double-serializes arrays before sending.
/// </summary>
static List<string> ParseJsonStringArray(string? jsonString)
{
    if (string.IsNullOrEmpty(jsonString)) return new();
    try
    {
        return JsonSerializer.Deserialize<List<string>>(jsonString) ?? new();
    }
    catch
    {
        return new();
    }
}
