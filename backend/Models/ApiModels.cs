using System.Text.Json.Serialization;

namespace QuestceSpire.API.Models;

// ─── Database Entities ───

public class DbRun
{
    public string RunId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string Character { get; set; } = "";
    public string Seed { get; set; } = "";
    public int AscensionLevel { get; set; }
    public string Outcome { get; set; } = ""; // "win" or "loss"
    public int? FinalFloor { get; set; }
    public int? FinalAct { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public DateTime SubmittedAt { get; set; }
}

public class DbDecision
{
    public int Id { get; set; } // auto-increment
    public string RunId { get; set; } = "";
    public int Floor { get; set; }
    public int Act { get; set; }
    public string EventType { get; set; } = "";
    public string OfferedIds { get; set; } = "[]"; // JSON string
    public string? ChosenId { get; set; }
    public string DeckSnapshot { get; set; } = "[]"; // JSON string
    public string RelicSnapshot { get; set; } = "[]"; // JSON string
    public int CurrentHP { get; set; }
    public int MaxHP { get; set; }
    public int Gold { get; set; }
    public DateTime Timestamp { get; set; }

    // Navigation
    public DbRun? Run { get; set; }
}

// ─── API Request Models ───

/// <summary>
/// Matches the mod's SyncClient payload: list of { run, decisions }
/// </summary>
public class BulkRunSubmission
{
    public List<RunSubmission> Runs { get; set; } = new();
}

public class RunSubmission
{
    public RunLogDto Run { get; set; } = new();
    public List<DecisionEventDto> Decisions { get; set; } = new();
}

public class RunLogDto
{
    public string RunId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string Character { get; set; } = "";
    public string Seed { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? Outcome { get; set; } // "Win" or "Loss" (from enum name)
    public int? FinalFloor { get; set; }
    public int? FinalAct { get; set; }
    public int AscensionLevel { get; set; }
}

public class DecisionEventDto
{
    public string RunId { get; set; } = "";
    public int Floor { get; set; }
    public int Act { get; set; }
    public string EventType { get; set; } = ""; // enum name: "CardReward", "RelicReward", etc.
    public List<string> OfferedIds { get; set; } = new();
    public string? ChosenId { get; set; }
    public List<string> DeckSnapshot { get; set; } = new();
    public List<string> RelicSnapshot { get; set; } = new();
    public int CurrentHP { get; set; }
    public int MaxHP { get; set; }
    public int Gold { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Flat format sent by the mod's CloudSync: { player_id, runs[], decisions[] }
/// with snake_case property names and pre-serialized JSON string arrays.
/// </summary>
public class FlatUploadPayload
{
    [JsonPropertyName("player_id")]
    public string PlayerId { get; set; } = "";

    [JsonPropertyName("runs")]
    public List<FlatRunDto> Runs { get; set; } = new();

    [JsonPropertyName("decisions")]
    public List<FlatDecisionDto> Decisions { get; set; } = new();
}

public class FlatRunDto
{
    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = "";

    [JsonPropertyName("character")]
    public string Character { get; set; } = "";

    [JsonPropertyName("seed")]
    public string? Seed { get; set; }

    [JsonPropertyName("start_time")]
    public string? StartTime { get; set; }

    [JsonPropertyName("end_time")]
    public string? EndTime { get; set; }

    [JsonPropertyName("outcome")]
    public string? Outcome { get; set; }

    [JsonPropertyName("final_floor")]
    public int? FinalFloor { get; set; }

    [JsonPropertyName("final_act")]
    public int? FinalAct { get; set; }

    [JsonPropertyName("ascension_level")]
    public int AscensionLevel { get; set; }

    [JsonPropertyName("mod_version")]
    public string? ModVersion { get; set; }
}

public class FlatDecisionDto
{
    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = "";

    [JsonPropertyName("floor")]
    public int Floor { get; set; }

    [JsonPropertyName("act")]
    public int Act { get; set; }

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = "";

    /// <summary>Pre-serialized JSON string (e.g. "[\"CARD_A\",\"CARD_B\"]")</summary>
    [JsonPropertyName("offered_ids")]
    public string? OfferedIds { get; set; }

    [JsonPropertyName("chosen_id")]
    public string? ChosenId { get; set; }

    /// <summary>Pre-serialized JSON string</summary>
    [JsonPropertyName("deck_snapshot")]
    public string? DeckSnapshot { get; set; }

    /// <summary>Pre-serialized JSON string</summary>
    [JsonPropertyName("relic_snapshot")]
    public string? RelicSnapshot { get; set; }

    [JsonPropertyName("current_hp")]
    public int CurrentHP { get; set; }

    [JsonPropertyName("max_hp")]
    public int MaxHP { get; set; }

    [JsonPropertyName("gold")]
    public int Gold { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}

// ─── API Response Models ───

public class CardStatsResponse
{
    public string Character { get; set; } = "";
    public List<CardStatEntry> Cards { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

public class CardStatEntry
{
    public string CardId { get; set; } = "";
    public float PickRate { get; set; }
    public float WinRateWhenPicked { get; set; }
    public float WinRateWhenSkipped { get; set; }
    public int SampleSize { get; set; }
    public float AvgFloorPicked { get; set; }
    public Dictionary<string, float> ContextStats { get; set; } = new();
}

public class RelicStatsResponse
{
    public string Character { get; set; } = "";
    public List<RelicStatEntry> Relics { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

public class RelicStatEntry
{
    public string RelicId { get; set; } = "";
    public float PickRate { get; set; }
    public float WinRateWhenPicked { get; set; }
    public float WinRateWhenSkipped { get; set; }
    public int SampleSize { get; set; }
    public float AvgFloorPicked { get; set; }
    public Dictionary<string, float> ContextStats { get; set; } = new();
}

public class HealthResponse
{
    public string Status { get; set; } = "ok";
    public int PlayerCount { get; set; }
    public int RunCount { get; set; }
    public DateTime Timestamp { get; set; }
}

public class BulkSubmitResponse
{
    public int Accepted { get; set; }
    public int Duplicates { get; set; }
}
