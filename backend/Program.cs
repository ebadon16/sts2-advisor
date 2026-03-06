using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using STS2Advisor.API.Data;
using STS2Advisor.API.Models;
using STS2Advisor.API.Services;

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
    // The mod sends a raw JSON array of { run, decisions } objects.
    // We accept both that format and a { runs: [...] } wrapper.
    List<RunSubmission>? submissions;

    try
    {
        var body = await new StreamReader(httpContext.Request.Body).ReadToEndAsync();

        // Try array format first (what the mod actually sends)
        submissions = JsonSerializer.Deserialize<List<RunSubmission>>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (submissions == null)
        {
            // Try wrapper format
            var wrapper = JsonSerializer.Deserialize<BulkRunSubmission>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
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

        if (string.IsNullOrWhiteSpace(runDto.RunId) || string.IsNullOrWhiteSpace(runDto.PlayerId))
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
