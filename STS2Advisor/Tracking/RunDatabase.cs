using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace STS2Advisor.Tracking
{
    public class RunDatabase
    {
        private static RunDatabase _instance;
        public static RunDatabase Instance => _instance ?? (_instance = new RunDatabase());

        private string _connectionString;

        private RunDatabase() { }

        public void InitializeDatabase()
        {
            string pluginDir = Path.GetDirectoryName(Plugin.Instance.Info.Location);
            string dbPath = Path.Combine(pluginDir, "sts2advisor.db");
            _connectionString = $"Data Source={dbPath}";

            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS runs (
                            run_id TEXT PRIMARY KEY,
                            player_id TEXT NOT NULL,
                            character TEXT NOT NULL,
                            seed TEXT,
                            start_time TEXT NOT NULL,
                            end_time TEXT,
                            outcome TEXT,
                            final_floor INTEGER,
                            final_act INTEGER,
                            ascension_level INTEGER NOT NULL,
                            synced INTEGER NOT NULL DEFAULT 0
                        );

                        CREATE TABLE IF NOT EXISTS decisions (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            run_id TEXT NOT NULL,
                            floor INTEGER NOT NULL,
                            act INTEGER NOT NULL,
                            event_type TEXT NOT NULL,
                            offered_ids TEXT NOT NULL,
                            chosen_id TEXT,
                            deck_snapshot TEXT NOT NULL,
                            relic_snapshot TEXT NOT NULL,
                            current_hp INTEGER NOT NULL,
                            max_hp INTEGER NOT NULL,
                            gold INTEGER NOT NULL,
                            timestamp TEXT NOT NULL,
                            FOREIGN KEY (run_id) REFERENCES runs(run_id)
                        );

                        CREATE TABLE IF NOT EXISTS community_card_stats (
                            card_id TEXT NOT NULL,
                            character TEXT NOT NULL,
                            pick_rate REAL NOT NULL,
                            win_rate_when_picked REAL NOT NULL,
                            win_rate_when_skipped REAL NOT NULL,
                            sample_size INTEGER NOT NULL,
                            avg_floor_picked REAL NOT NULL,
                            archetype_context TEXT,
                            PRIMARY KEY (card_id, character)
                        );

                        CREATE TABLE IF NOT EXISTS community_relic_stats (
                            relic_id TEXT NOT NULL,
                            character TEXT NOT NULL,
                            pick_rate REAL NOT NULL,
                            win_rate_when_picked REAL NOT NULL,
                            win_rate_when_skipped REAL NOT NULL,
                            sample_size INTEGER NOT NULL,
                            avg_floor_picked REAL NOT NULL,
                            archetype_context TEXT,
                            PRIMARY KEY (relic_id, character)
                        );

                        CREATE INDEX IF NOT EXISTS idx_decisions_run_id ON decisions(run_id);
                        CREATE INDEX IF NOT EXISTS idx_runs_synced ON runs(synced);
                    ";
                    cmd.ExecuteNonQuery();
                }
            }

            Plugin.Log.LogInfo("RunDatabase initialized.");
        }

        public void SaveRun(RunLog run, List<DecisionEvent> decisions)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                    // Insert run
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            INSERT INTO runs (run_id, player_id, character, seed, start_time, end_time,
                                              outcome, final_floor, final_act, ascension_level, synced)
                            VALUES (@runId, @playerId, @character, @seed, @startTime, @endTime,
                                    @outcome, @finalFloor, @finalAct, @ascensionLevel, @synced)";

                        cmd.Parameters.AddWithValue("@runId", run.RunId);
                        cmd.Parameters.AddWithValue("@playerId", run.PlayerId);
                        cmd.Parameters.AddWithValue("@character", run.Character);
                        cmd.Parameters.AddWithValue("@seed", (object)run.Seed ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@startTime", run.StartTime.ToString("o"));
                        cmd.Parameters.AddWithValue("@endTime", run.EndTime.HasValue ? run.EndTime.Value.ToString("o") : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@outcome", run.Outcome.HasValue ? run.Outcome.Value.ToString() : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@finalFloor", (object)run.FinalFloor ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@finalAct", (object)run.FinalAct ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ascensionLevel", run.AscensionLevel);
                        cmd.Parameters.AddWithValue("@synced", run.Synced ? 1 : 0);
                        cmd.ExecuteNonQuery();
                    }

                    // Insert decisions
                    foreach (var decision in decisions)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = @"
                                INSERT INTO decisions (run_id, floor, act, event_type, offered_ids, chosen_id,
                                                       deck_snapshot, relic_snapshot, current_hp, max_hp, gold, timestamp)
                                VALUES (@runId, @floor, @act, @eventType, @offeredIds, @chosenId,
                                        @deckSnapshot, @relicSnapshot, @currentHp, @maxHp, @gold, @timestamp)";

                            cmd.Parameters.AddWithValue("@runId", decision.RunId);
                            cmd.Parameters.AddWithValue("@floor", decision.Floor);
                            cmd.Parameters.AddWithValue("@act", decision.Act);
                            cmd.Parameters.AddWithValue("@eventType", decision.EventType.ToString());
                            cmd.Parameters.AddWithValue("@offeredIds", JsonConvert.SerializeObject(decision.OfferedIds));
                            cmd.Parameters.AddWithValue("@chosenId", (object)decision.ChosenId ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@deckSnapshot", JsonConvert.SerializeObject(decision.DeckSnapshot));
                            cmd.Parameters.AddWithValue("@relicSnapshot", JsonConvert.SerializeObject(decision.RelicSnapshot));
                            cmd.Parameters.AddWithValue("@currentHp", decision.CurrentHP);
                            cmd.Parameters.AddWithValue("@maxHp", decision.MaxHP);
                            cmd.Parameters.AddWithValue("@gold", decision.Gold);
                            cmd.Parameters.AddWithValue("@timestamp", decision.Timestamp.ToString("o"));
                            cmd.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                }
            }
        }

        public List<(RunLog Run, List<DecisionEvent> Decisions)> GetUnsynced()
        {
            var results = new List<(RunLog, List<DecisionEvent>)>();

            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();

                // Get unsynced runs
                var runs = new List<RunLog>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM runs WHERE synced = 0";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            runs.Add(ReadRunLog(reader));
                        }
                    }
                }

                // Get decisions for each run
                foreach (var run in runs)
                {
                    var decisions = new List<DecisionEvent>();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM decisions WHERE run_id = @runId ORDER BY timestamp";
                        cmd.Parameters.AddWithValue("@runId", run.RunId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                decisions.Add(ReadDecisionEvent(reader));
                            }
                        }
                    }
                    results.Add((run, decisions));
                }
            }

            return results;
        }

        public void MarkSynced(string runId)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE runs SET synced = 1 WHERE run_id = @runId";
                    cmd.Parameters.AddWithValue("@runId", runId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void SaveCommunityCardStats(List<CommunityCardStats> statsList)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                    foreach (var stats in statsList)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = @"
                                INSERT OR REPLACE INTO community_card_stats
                                    (card_id, character, pick_rate, win_rate_when_picked, win_rate_when_skipped,
                                     sample_size, avg_floor_picked, archetype_context)
                                VALUES (@cardId, @character, @pickRate, @winPicked, @winSkipped,
                                        @sampleSize, @avgFloor, @archetypeContext)";

                            cmd.Parameters.AddWithValue("@cardId", stats.CardId);
                            cmd.Parameters.AddWithValue("@character", stats.Character);
                            cmd.Parameters.AddWithValue("@pickRate", stats.PickRate);
                            cmd.Parameters.AddWithValue("@winPicked", stats.WinRateWhenPicked);
                            cmd.Parameters.AddWithValue("@winSkipped", stats.WinRateWhenSkipped);
                            cmd.Parameters.AddWithValue("@sampleSize", stats.SampleSize);
                            cmd.Parameters.AddWithValue("@avgFloor", stats.AvgFloorPicked);
                            cmd.Parameters.AddWithValue("@archetypeContext", JsonConvert.SerializeObject(stats.ArchetypeContext));
                            cmd.ExecuteNonQuery();
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        public void SaveCommunityRelicStats(List<CommunityRelicStats> statsList)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                    foreach (var stats in statsList)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = @"
                                INSERT OR REPLACE INTO community_relic_stats
                                    (relic_id, character, pick_rate, win_rate_when_picked, win_rate_when_skipped,
                                     sample_size, avg_floor_picked, archetype_context)
                                VALUES (@relicId, @character, @pickRate, @winPicked, @winSkipped,
                                        @sampleSize, @avgFloor, @archetypeContext)";

                            cmd.Parameters.AddWithValue("@relicId", stats.RelicId);
                            cmd.Parameters.AddWithValue("@character", stats.Character);
                            cmd.Parameters.AddWithValue("@pickRate", stats.PickRate);
                            cmd.Parameters.AddWithValue("@winPicked", stats.WinRateWhenPicked);
                            cmd.Parameters.AddWithValue("@winSkipped", stats.WinRateWhenSkipped);
                            cmd.Parameters.AddWithValue("@sampleSize", stats.SampleSize);
                            cmd.Parameters.AddWithValue("@avgFloor", stats.AvgFloorPicked);
                            cmd.Parameters.AddWithValue("@archetypeContext", JsonConvert.SerializeObject(stats.ArchetypeContext));
                            cmd.ExecuteNonQuery();
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        public CommunityCardStats GetCommunityCardStats(string character, string cardId)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT * FROM community_card_stats
                        WHERE character = @character AND card_id = @cardId";
                    cmd.Parameters.AddWithValue("@character", character);
                    cmd.Parameters.AddWithValue("@cardId", cardId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new CommunityCardStats
                            {
                                CardId = reader.GetString(reader.GetOrdinal("card_id")),
                                Character = reader.GetString(reader.GetOrdinal("character")),
                                PickRate = reader.GetFloat(reader.GetOrdinal("pick_rate")),
                                WinRateWhenPicked = reader.GetFloat(reader.GetOrdinal("win_rate_when_picked")),
                                WinRateWhenSkipped = reader.GetFloat(reader.GetOrdinal("win_rate_when_skipped")),
                                SampleSize = reader.GetInt32(reader.GetOrdinal("sample_size")),
                                AvgFloorPicked = reader.GetFloat(reader.GetOrdinal("avg_floor_picked")),
                                ArchetypeContext = DeserializeDict(reader, "archetype_context")
                            };
                        }
                    }
                }
            }
            return null;
        }

        public CommunityRelicStats GetCommunityRelicStats(string character, string relicId)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT * FROM community_relic_stats
                        WHERE character = @character AND relic_id = @relicId";
                    cmd.Parameters.AddWithValue("@character", character);
                    cmd.Parameters.AddWithValue("@relicId", relicId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new CommunityRelicStats
                            {
                                RelicId = reader.GetString(reader.GetOrdinal("relic_id")),
                                Character = reader.GetString(reader.GetOrdinal("character")),
                                PickRate = reader.GetFloat(reader.GetOrdinal("pick_rate")),
                                WinRateWhenPicked = reader.GetFloat(reader.GetOrdinal("win_rate_when_picked")),
                                WinRateWhenSkipped = reader.GetFloat(reader.GetOrdinal("win_rate_when_skipped")),
                                SampleSize = reader.GetInt32(reader.GetOrdinal("sample_size")),
                                AvgFloorPicked = reader.GetFloat(reader.GetOrdinal("avg_floor_picked")),
                                ArchetypeContext = DeserializeDict(reader, "archetype_context")
                            };
                        }
                    }
                }
            }
            return null;
        }

        private static RunLog ReadRunLog(SqliteDataReader reader)
        {
            var run = new RunLog
            {
                RunId = reader.GetString(reader.GetOrdinal("run_id")),
                PlayerId = reader.GetString(reader.GetOrdinal("player_id")),
                Character = reader.GetString(reader.GetOrdinal("character")),
                Seed = reader.IsDBNull(reader.GetOrdinal("seed")) ? null : reader.GetString(reader.GetOrdinal("seed")),
                StartTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("start_time"))),
                AscensionLevel = reader.GetInt32(reader.GetOrdinal("ascension_level")),
                Synced = reader.GetInt32(reader.GetOrdinal("synced")) == 1
            };

            if (!reader.IsDBNull(reader.GetOrdinal("end_time")))
                run.EndTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("end_time")));

            if (!reader.IsDBNull(reader.GetOrdinal("outcome")))
                run.Outcome = (RunOutcome)Enum.Parse(typeof(RunOutcome), reader.GetString(reader.GetOrdinal("outcome")));

            if (!reader.IsDBNull(reader.GetOrdinal("final_floor")))
                run.FinalFloor = reader.GetInt32(reader.GetOrdinal("final_floor"));

            if (!reader.IsDBNull(reader.GetOrdinal("final_act")))
                run.FinalAct = reader.GetInt32(reader.GetOrdinal("final_act"));

            return run;
        }

        private static DecisionEvent ReadDecisionEvent(SqliteDataReader reader)
        {
            return new DecisionEvent
            {
                RunId = reader.GetString(reader.GetOrdinal("run_id")),
                Floor = reader.GetInt32(reader.GetOrdinal("floor")),
                Act = reader.GetInt32(reader.GetOrdinal("act")),
                EventType = (DecisionEventType)Enum.Parse(typeof(DecisionEventType), reader.GetString(reader.GetOrdinal("event_type"))),
                OfferedIds = JsonConvert.DeserializeObject<List<string>>(reader.GetString(reader.GetOrdinal("offered_ids"))),
                ChosenId = reader.IsDBNull(reader.GetOrdinal("chosen_id")) ? null : reader.GetString(reader.GetOrdinal("chosen_id")),
                DeckSnapshot = JsonConvert.DeserializeObject<List<string>>(reader.GetString(reader.GetOrdinal("deck_snapshot"))),
                RelicSnapshot = JsonConvert.DeserializeObject<List<string>>(reader.GetString(reader.GetOrdinal("relic_snapshot"))),
                CurrentHP = reader.GetInt32(reader.GetOrdinal("current_hp")),
                MaxHP = reader.GetInt32(reader.GetOrdinal("max_hp")),
                Gold = reader.GetInt32(reader.GetOrdinal("gold")),
                Timestamp = DateTime.Parse(reader.GetString(reader.GetOrdinal("timestamp")))
            };
        }

        private static Dictionary<string, float> DeserializeDict(SqliteDataReader reader, string column)
        {
            if (reader.IsDBNull(reader.GetOrdinal(column)))
                return new Dictionary<string, float>();

            var json = reader.GetString(reader.GetOrdinal(column));
            return JsonConvert.DeserializeObject<Dictionary<string, float>>(json)
                   ?? new Dictionary<string, float>();
        }
    }
}
