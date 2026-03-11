-- Raw run data (mirrors client schema)
CREATE TABLE IF NOT EXISTS runs (
    run_id TEXT PRIMARY KEY,
    player_id TEXT NOT NULL,
    character TEXT NOT NULL,
    seed TEXT,
    start_time TEXT,
    end_time TEXT,
    outcome TEXT,
    final_floor INTEGER,
    final_act INTEGER,
    ascension_level INTEGER,
    mod_version TEXT,
    uploaded_at TEXT DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS idx_runs_player ON runs(player_id);
CREATE INDEX IF NOT EXISTS idx_runs_character ON runs(character);

-- Raw decision events
CREATE TABLE IF NOT EXISTS decisions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id TEXT NOT NULL REFERENCES runs(run_id),
    floor INTEGER,
    act INTEGER,
    event_type TEXT,
    offered_ids TEXT,
    chosen_id TEXT,
    deck_snapshot TEXT,
    relic_snapshot TEXT,
    current_hp INTEGER,
    max_hp INTEGER,
    gold INTEGER,
    timestamp TEXT
);
CREATE INDEX IF NOT EXISTS idx_decisions_run ON decisions(run_id);

-- Pre-aggregated community stats (recomputed periodically)
CREATE TABLE IF NOT EXISTS community_card_stats (
    card_id TEXT NOT NULL,
    character TEXT NOT NULL,
    pick_rate REAL,
    win_rate_when_picked REAL,
    win_rate_when_skipped REAL,
    sample_size INTEGER,
    avg_floor_picked REAL,
    computed_at TEXT,
    archetype_context TEXT,
    PRIMARY KEY (card_id, character)
);

CREATE TABLE IF NOT EXISTS community_relic_stats (
    relic_id TEXT NOT NULL,
    character TEXT NOT NULL,
    pick_rate REAL,
    win_rate_when_picked REAL,
    win_rate_when_skipped REAL,
    sample_size INTEGER,
    avg_floor_picked REAL,
    computed_at TEXT,
    archetype_context TEXT,
    PRIMARY KEY (relic_id, character)
);

-- Rate limiting
CREATE TABLE IF NOT EXISTS rate_limits (
    player_id TEXT NOT NULL,
    window_start TEXT NOT NULL,
    request_count INTEGER NOT NULL DEFAULT 1,
    PRIMARY KEY (player_id, window_start)
);
