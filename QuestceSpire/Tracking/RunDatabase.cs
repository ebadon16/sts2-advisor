using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace QuestceSpire.Tracking;

public class RunDatabase
{
	private string _connectionString;

	private readonly string _pluginFolder;

	private bool _initialized;

	internal string ConnectionString
	{
		get
		{
			if (!_initialized)
			{
				return null;
			}
			return _connectionString;
		}
	}

	public RunDatabase(string pluginFolder)
	{
		_pluginFolder = pluginFolder;
	}

	private bool EnsureInitialized()
	{
		if (!_initialized || _connectionString == null)
		{
			Plugin.Log("RunDatabase not initialized — skipping operation.");
			return false;
		}
		return true;
	}

	public void InitializeDatabase()
	{
		string text = Path.Combine(_pluginFolder, "questcespire.db");
		_connectionString = "Data Source=" + text;
		using (SqliteConnection sqliteConnection = new SqliteConnection(_connectionString))
		{
			sqliteConnection.Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\r\n                        CREATE TABLE IF NOT EXISTS runs (\r\n                            run_id TEXT PRIMARY KEY,\r\n                            player_id TEXT NOT NULL,\r\n                            character TEXT NOT NULL,\r\n                            seed TEXT,\r\n                            start_time TEXT NOT NULL,\r\n                            end_time TEXT,\r\n                            outcome TEXT,\r\n                            final_floor INTEGER,\r\n                            final_act INTEGER,\r\n                            ascension_level INTEGER NOT NULL,\r\n                            synced INTEGER NOT NULL DEFAULT 0\r\n                        );\r\n\r\n                        CREATE TABLE IF NOT EXISTS decisions (\r\n                            id INTEGER PRIMARY KEY AUTOINCREMENT,\r\n                            run_id TEXT NOT NULL,\r\n                            floor INTEGER NOT NULL,\r\n                            act INTEGER NOT NULL,\r\n                            event_type TEXT NOT NULL,\r\n                            offered_ids TEXT NOT NULL,\r\n                            chosen_id TEXT,\r\n                            deck_snapshot TEXT NOT NULL,\r\n                            relic_snapshot TEXT NOT NULL,\r\n                            current_hp INTEGER NOT NULL,\r\n                            max_hp INTEGER NOT NULL,\r\n                            gold INTEGER NOT NULL,\r\n                            timestamp TEXT NOT NULL,\r\n                            FOREIGN KEY (run_id) REFERENCES runs(run_id)\r\n                        );\r\n\r\n                        CREATE TABLE IF NOT EXISTS community_card_stats (\r\n                            card_id TEXT NOT NULL,\r\n                            character TEXT NOT NULL,\r\n                            pick_rate REAL NOT NULL,\r\n                            win_rate_when_picked REAL NOT NULL,\r\n                            win_rate_when_skipped REAL NOT NULL,\r\n                            sample_size INTEGER NOT NULL,\r\n                            avg_floor_picked REAL NOT NULL,\r\n                            archetype_context TEXT,\r\n                            PRIMARY KEY (card_id, character)\r\n                        );\r\n\r\n                        CREATE TABLE IF NOT EXISTS community_relic_stats (\r\n                            relic_id TEXT NOT NULL,\r\n                            character TEXT NOT NULL,\r\n                            pick_rate REAL NOT NULL,\r\n                            win_rate_when_picked REAL NOT NULL,\r\n                            win_rate_when_skipped REAL NOT NULL,\r\n                            sample_size INTEGER NOT NULL,\r\n                            avg_floor_picked REAL NOT NULL,\r\n                            archetype_context TEXT,\r\n                            PRIMARY KEY (relic_id, character)\r\n                        );\r\n\r\n                        CREATE INDEX IF NOT EXISTS idx_decisions_run_id ON decisions(run_id);\r\n                        CREATE INDEX IF NOT EXISTS idx_runs_synced ON runs(synced);\r\n                    ";
			sqliteCommand.ExecuteNonQuery();
		}
		_initialized = true;
		Plugin.Log("RunDatabase initialized.");
	}

	public void SaveRun(RunLog run, List<DecisionEvent> decisions)
	{
		if (!EnsureInitialized())
		{
			return;
		}
		using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
		sqliteConnection.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
		{
			sqliteCommand.CommandText = "\r\n                            INSERT INTO runs (run_id, player_id, character, seed, start_time, end_time,\r\n                                              outcome, final_floor, final_act, ascension_level, synced)\r\n                            VALUES (@runId, @playerId, @character, @seed, @startTime, @endTime,\r\n                                    @outcome, @finalFloor, @finalAct, @ascensionLevel, @synced)";
			sqliteCommand.Parameters.AddWithValue("@runId", run.RunId);
			sqliteCommand.Parameters.AddWithValue("@playerId", run.PlayerId);
			sqliteCommand.Parameters.AddWithValue("@character", run.Character);
			sqliteCommand.Parameters.AddWithValue("@seed", ((object)run.Seed) ?? ((object)DBNull.Value));
			sqliteCommand.Parameters.AddWithValue("@startTime", run.StartTime.ToString("o"));
			sqliteCommand.Parameters.AddWithValue("@endTime", run.EndTime.HasValue ? ((IConvertible)run.EndTime.Value.ToString("o")) : ((IConvertible)DBNull.Value));
			sqliteCommand.Parameters.AddWithValue("@outcome", run.Outcome.HasValue ? ((IConvertible)run.Outcome.Value.ToString()) : ((IConvertible)DBNull.Value));
			sqliteCommand.Parameters.AddWithValue("@finalFloor", ((object)run.FinalFloor) ?? DBNull.Value);
			sqliteCommand.Parameters.AddWithValue("@finalAct", ((object)run.FinalAct) ?? DBNull.Value);
			sqliteCommand.Parameters.AddWithValue("@ascensionLevel", run.AscensionLevel);
			sqliteCommand.Parameters.AddWithValue("@synced", run.Synced ? 1 : 0);
			sqliteCommand.ExecuteNonQuery();
		}
		foreach (DecisionEvent decision in decisions)
		{
			using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
			sqliteCommand2.CommandText = "\r\n                                INSERT INTO decisions (run_id, floor, act, event_type, offered_ids, chosen_id,\r\n                                                       deck_snapshot, relic_snapshot, current_hp, max_hp, gold, timestamp)\r\n                                VALUES (@runId, @floor, @act, @eventType, @offeredIds, @chosenId,\r\n                                        @deckSnapshot, @relicSnapshot, @currentHp, @maxHp, @gold, @timestamp)";
			sqliteCommand2.Parameters.AddWithValue("@runId", decision.RunId);
			sqliteCommand2.Parameters.AddWithValue("@floor", decision.Floor);
			sqliteCommand2.Parameters.AddWithValue("@act", decision.Act);
			sqliteCommand2.Parameters.AddWithValue("@eventType", decision.EventType.ToString());
			sqliteCommand2.Parameters.AddWithValue("@offeredIds", JsonConvert.SerializeObject(decision.OfferedIds));
			sqliteCommand2.Parameters.AddWithValue("@chosenId", ((object)decision.ChosenId) ?? ((object)DBNull.Value));
			sqliteCommand2.Parameters.AddWithValue("@deckSnapshot", JsonConvert.SerializeObject(decision.DeckSnapshot));
			sqliteCommand2.Parameters.AddWithValue("@relicSnapshot", JsonConvert.SerializeObject(decision.RelicSnapshot));
			sqliteCommand2.Parameters.AddWithValue("@currentHp", decision.CurrentHP);
			sqliteCommand2.Parameters.AddWithValue("@maxHp", decision.MaxHP);
			sqliteCommand2.Parameters.AddWithValue("@gold", decision.Gold);
			sqliteCommand2.Parameters.AddWithValue("@timestamp", decision.Timestamp.ToString("o"));
			sqliteCommand2.ExecuteNonQuery();
		}
		sqliteTransaction.Commit();
	}

	public List<(RunLog Run, List<DecisionEvent> Decisions)> GetUnsynced()
	{
		List<(RunLog, List<DecisionEvent>)> list = new List<(RunLog, List<DecisionEvent>)>();
		if (!EnsureInitialized())
		{
			return list;
		}
		using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
		sqliteConnection.Open();
		List<RunLog> list2 = new List<RunLog>();
		using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
		{
			sqliteCommand.CommandText = "SELECT * FROM runs WHERE synced = 0";
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				list2.Add(ReadRunLog(sqliteDataReader));
			}
		}
		foreach (RunLog item in list2)
		{
			List<DecisionEvent> list3 = new List<DecisionEvent>();
			using (SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand())
			{
				sqliteCommand2.CommandText = "SELECT * FROM decisions WHERE run_id = @runId ORDER BY timestamp";
				sqliteCommand2.Parameters.AddWithValue("@runId", item.RunId);
				using SqliteDataReader sqliteDataReader2 = sqliteCommand2.ExecuteReader();
				while (sqliteDataReader2.Read())
				{
					list3.Add(ReadDecisionEvent(sqliteDataReader2));
				}
			}
			list.Add((item, list3));
		}
		return list;
	}

	public void MarkSynced(string runId)
	{
		if (!EnsureInitialized())
		{
			return;
		}
		using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
		sqliteConnection.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "UPDATE runs SET synced = 1 WHERE run_id = @runId";
		sqliteCommand.Parameters.AddWithValue("@runId", runId);
		sqliteCommand.ExecuteNonQuery();
	}

	public void SaveCommunityCardStats(List<CommunityCardStats> statsList)
	{
		if (!EnsureInitialized())
		{
			return;
		}
		using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
		sqliteConnection.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		foreach (CommunityCardStats stats in statsList)
		{
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\r\n                                INSERT OR REPLACE INTO community_card_stats\r\n                                    (card_id, character, pick_rate, win_rate_when_picked, win_rate_when_skipped,\r\n                                     sample_size, avg_floor_picked, archetype_context)\r\n                                VALUES (@cardId, @character, @pickRate, @winPicked, @winSkipped,\r\n                                        @sampleSize, @avgFloor, @archetypeContext)";
			sqliteCommand.Parameters.AddWithValue("@cardId", stats.CardId);
			sqliteCommand.Parameters.AddWithValue("@character", stats.Character);
			sqliteCommand.Parameters.AddWithValue("@pickRate", stats.PickRate);
			sqliteCommand.Parameters.AddWithValue("@winPicked", stats.WinRateWhenPicked);
			sqliteCommand.Parameters.AddWithValue("@winSkipped", stats.WinRateWhenSkipped);
			sqliteCommand.Parameters.AddWithValue("@sampleSize", stats.SampleSize);
			sqliteCommand.Parameters.AddWithValue("@avgFloor", stats.AvgFloorPicked);
			sqliteCommand.Parameters.AddWithValue("@archetypeContext", JsonConvert.SerializeObject(stats.ArchetypeContext));
			sqliteCommand.ExecuteNonQuery();
		}
		sqliteTransaction.Commit();
	}

	public void SaveCommunityRelicStats(List<CommunityRelicStats> statsList)
	{
		if (!EnsureInitialized())
		{
			return;
		}
		using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
		sqliteConnection.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		foreach (CommunityRelicStats stats in statsList)
		{
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\r\n                                INSERT OR REPLACE INTO community_relic_stats\r\n                                    (relic_id, character, pick_rate, win_rate_when_picked, win_rate_when_skipped,\r\n                                     sample_size, avg_floor_picked, archetype_context)\r\n                                VALUES (@relicId, @character, @pickRate, @winPicked, @winSkipped,\r\n                                        @sampleSize, @avgFloor, @archetypeContext)";
			sqliteCommand.Parameters.AddWithValue("@relicId", stats.RelicId);
			sqliteCommand.Parameters.AddWithValue("@character", stats.Character);
			sqliteCommand.Parameters.AddWithValue("@pickRate", stats.PickRate);
			sqliteCommand.Parameters.AddWithValue("@winPicked", stats.WinRateWhenPicked);
			sqliteCommand.Parameters.AddWithValue("@winSkipped", stats.WinRateWhenSkipped);
			sqliteCommand.Parameters.AddWithValue("@sampleSize", stats.SampleSize);
			sqliteCommand.Parameters.AddWithValue("@avgFloor", stats.AvgFloorPicked);
			sqliteCommand.Parameters.AddWithValue("@archetypeContext", JsonConvert.SerializeObject(stats.ArchetypeContext));
			sqliteCommand.ExecuteNonQuery();
		}
		sqliteTransaction.Commit();
	}

	public CommunityCardStats GetCommunityCardStats(string character, string cardId)
	{
		if (!EnsureInitialized())
		{
			return null;
		}
		using (SqliteConnection sqliteConnection = new SqliteConnection(_connectionString))
		{
			sqliteConnection.Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\r\n                        SELECT * FROM community_card_stats\r\n                        WHERE character = @character AND card_id = @cardId";
			sqliteCommand.Parameters.AddWithValue("@character", character);
			sqliteCommand.Parameters.AddWithValue("@cardId", cardId);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			if (sqliteDataReader.Read())
			{
				return new CommunityCardStats
				{
					CardId = sqliteDataReader.GetString(sqliteDataReader.GetOrdinal("card_id")),
					Character = sqliteDataReader.GetString(sqliteDataReader.GetOrdinal("character")),
					PickRate = sqliteDataReader.GetFloat(sqliteDataReader.GetOrdinal("pick_rate")),
					WinRateWhenPicked = sqliteDataReader.GetFloat(sqliteDataReader.GetOrdinal("win_rate_when_picked")),
					WinRateWhenSkipped = sqliteDataReader.GetFloat(sqliteDataReader.GetOrdinal("win_rate_when_skipped")),
					SampleSize = sqliteDataReader.GetInt32(sqliteDataReader.GetOrdinal("sample_size")),
					AvgFloorPicked = sqliteDataReader.GetFloat(sqliteDataReader.GetOrdinal("avg_floor_picked")),
					ArchetypeContext = DeserializeDict(sqliteDataReader, "archetype_context")
				};
			}
		}
		return null;
	}

	public CommunityRelicStats GetCommunityRelicStats(string character, string relicId)
	{
		if (!EnsureInitialized())
		{
			return null;
		}
		using (SqliteConnection sqliteConnection = new SqliteConnection(_connectionString))
		{
			sqliteConnection.Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\r\n                        SELECT * FROM community_relic_stats\r\n                        WHERE character = @character AND relic_id = @relicId";
			sqliteCommand.Parameters.AddWithValue("@character", character);
			sqliteCommand.Parameters.AddWithValue("@relicId", relicId);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			if (sqliteDataReader.Read())
			{
				return new CommunityRelicStats
				{
					RelicId = sqliteDataReader.GetString(sqliteDataReader.GetOrdinal("relic_id")),
					Character = sqliteDataReader.GetString(sqliteDataReader.GetOrdinal("character")),
					PickRate = sqliteDataReader.GetFloat(sqliteDataReader.GetOrdinal("pick_rate")),
					WinRateWhenPicked = sqliteDataReader.GetFloat(sqliteDataReader.GetOrdinal("win_rate_when_picked")),
					WinRateWhenSkipped = sqliteDataReader.GetFloat(sqliteDataReader.GetOrdinal("win_rate_when_skipped")),
					SampleSize = sqliteDataReader.GetInt32(sqliteDataReader.GetOrdinal("sample_size")),
					AvgFloorPicked = sqliteDataReader.GetFloat(sqliteDataReader.GetOrdinal("avg_floor_picked")),
					ArchetypeContext = DeserializeDict(sqliteDataReader, "archetype_context")
				};
			}
		}
		return null;
	}

	private static RunLog ReadRunLog(SqliteDataReader reader)
	{
		RunLog runLog = new RunLog
		{
			RunId = reader.GetString(reader.GetOrdinal("run_id")),
			PlayerId = reader.GetString(reader.GetOrdinal("player_id")),
			Character = reader.GetString(reader.GetOrdinal("character")),
			Seed = (reader.IsDBNull(reader.GetOrdinal("seed")) ? null : reader.GetString(reader.GetOrdinal("seed"))),
			StartTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("start_time")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
			AscensionLevel = reader.GetInt32(reader.GetOrdinal("ascension_level")),
			Synced = (reader.GetInt32(reader.GetOrdinal("synced")) == 1)
		};
		if (!reader.IsDBNull(reader.GetOrdinal("end_time")))
		{
			runLog.EndTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("end_time")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
		}
		if (!reader.IsDBNull(reader.GetOrdinal("outcome")) && Enum.TryParse<RunOutcome>(reader.GetString(reader.GetOrdinal("outcome")), out var result))
		{
			runLog.Outcome = result;
		}
		if (!reader.IsDBNull(reader.GetOrdinal("final_floor")))
		{
			runLog.FinalFloor = reader.GetInt32(reader.GetOrdinal("final_floor"));
		}
		if (!reader.IsDBNull(reader.GetOrdinal("final_act")))
		{
			runLog.FinalAct = reader.GetInt32(reader.GetOrdinal("final_act"));
		}
		return runLog;
	}

	private static DecisionEvent ReadDecisionEvent(SqliteDataReader reader)
	{
		DecisionEventType result;
		return new DecisionEvent
		{
			RunId = reader.GetString(reader.GetOrdinal("run_id")),
			Floor = reader.GetInt32(reader.GetOrdinal("floor")),
			Act = reader.GetInt32(reader.GetOrdinal("act")),
			EventType = (Enum.TryParse<DecisionEventType>(reader.GetString(reader.GetOrdinal("event_type")), out result) ? result : DecisionEventType.CardReward),
			OfferedIds = JsonConvert.DeserializeObject<List<string>>(reader.GetString(reader.GetOrdinal("offered_ids"))),
			ChosenId = (reader.IsDBNull(reader.GetOrdinal("chosen_id")) ? null : reader.GetString(reader.GetOrdinal("chosen_id"))),
			DeckSnapshot = JsonConvert.DeserializeObject<List<string>>(reader.GetString(reader.GetOrdinal("deck_snapshot"))),
			RelicSnapshot = JsonConvert.DeserializeObject<List<string>>(reader.GetString(reader.GetOrdinal("relic_snapshot"))),
			CurrentHP = reader.GetInt32(reader.GetOrdinal("current_hp")),
			MaxHP = reader.GetInt32(reader.GetOrdinal("max_hp")),
			Gold = reader.GetInt32(reader.GetOrdinal("gold")),
			Timestamp = DateTime.Parse(reader.GetString(reader.GetOrdinal("timestamp")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
		};
	}

	public (int wins, int total) GetCharacterWinRate(string character)
	{
		if (!EnsureInitialized())
			return (0, 0);
		try
		{
			using var conn = new SqliteConnection(_connectionString);
			conn.Open();
			using var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT COUNT(*) as total, SUM(CASE WHEN outcome='Win' THEN 1 ELSE 0 END) as wins FROM runs WHERE character=@char AND outcome IS NOT NULL";
			cmd.Parameters.AddWithValue("@char", character);
			using var reader = cmd.ExecuteReader();
			if (reader.Read())
			{
				int total = reader.GetInt32(0);
				int wins = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
				return (wins, total);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log($"GetCharacterWinRate error: {ex.Message}");
		}
		return (0, 0);
	}

	public int GetTotalRunCount()
	{
		if (!EnsureInitialized())
			return 0;
		try
		{
			using var conn = new SqliteConnection(_connectionString);
			conn.Open();
			using var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT COUNT(*) FROM runs";
			return Convert.ToInt32(cmd.ExecuteScalar());
		}
		catch (Exception ex)
		{
			Plugin.Log($"GetTotalRunCount error: {ex.Message}");
			return 0;
		}
	}

	public List<CommunityCardStats> GetAllCommunityCardStats()
	{
		var list = new List<CommunityCardStats>();
		if (!EnsureInitialized())
			return list;
		try
		{
			using var conn = new SqliteConnection(_connectionString);
			conn.Open();
			using var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT * FROM community_card_stats";
			using var reader = cmd.ExecuteReader();
			while (reader.Read())
			{
				list.Add(new CommunityCardStats
				{
					CardId = reader.GetString(reader.GetOrdinal("card_id")),
					Character = reader.GetString(reader.GetOrdinal("character")),
					PickRate = reader.GetFloat(reader.GetOrdinal("pick_rate")),
					WinRateWhenPicked = reader.GetFloat(reader.GetOrdinal("win_rate_when_picked")),
					WinRateWhenSkipped = reader.GetFloat(reader.GetOrdinal("win_rate_when_skipped")),
					SampleSize = reader.GetInt32(reader.GetOrdinal("sample_size")),
					AvgFloorPicked = reader.GetFloat(reader.GetOrdinal("avg_floor_picked")),
					ArchetypeContext = DeserializeDict(reader, "archetype_context")
				});
			}
		}
		catch (Exception ex)
		{
			Plugin.Log($"GetAllCommunityCardStats error: {ex.Message}");
		}
		return list;
	}

	public List<CommunityRelicStats> GetAllCommunityRelicStats()
	{
		var list = new List<CommunityRelicStats>();
		if (!EnsureInitialized())
			return list;
		try
		{
			using var conn = new SqliteConnection(_connectionString);
			conn.Open();
			using var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT * FROM community_relic_stats";
			using var reader = cmd.ExecuteReader();
			while (reader.Read())
			{
				list.Add(new CommunityRelicStats
				{
					RelicId = reader.GetString(reader.GetOrdinal("relic_id")),
					Character = reader.GetString(reader.GetOrdinal("character")),
					PickRate = reader.GetFloat(reader.GetOrdinal("pick_rate")),
					WinRateWhenPicked = reader.GetFloat(reader.GetOrdinal("win_rate_when_picked")),
					WinRateWhenSkipped = reader.GetFloat(reader.GetOrdinal("win_rate_when_skipped")),
					SampleSize = reader.GetInt32(reader.GetOrdinal("sample_size")),
					AvgFloorPicked = reader.GetFloat(reader.GetOrdinal("avg_floor_picked")),
					ArchetypeContext = DeserializeDict(reader, "archetype_context")
				});
			}
		}
		catch (Exception ex)
		{
			Plugin.Log($"GetAllCommunityRelicStats error: {ex.Message}");
		}
		return list;
	}

	public void MergeCommunityCardStats(List<CommunityCardStats> imported)
	{
		if (!EnsureInitialized() || imported == null)
			return;
		var merged = new List<CommunityCardStats>();
		foreach (var imp in imported)
		{
			var local = GetCommunityCardStats(imp.Character, imp.CardId);
			if (local != null)
			{
				int totalSamples = local.SampleSize + imp.SampleSize;
				if (totalSamples == 0) continue;
				float lw = (float)local.SampleSize / totalSamples;
				float iw = (float)imp.SampleSize / totalSamples;
				// Weight win rates by pick/skip counts, not total offers
				float localPicks = local.SampleSize * local.PickRate;
				float impPicks = imp.SampleSize * imp.PickRate;
				float totalPicks = localPicks + impPicks;
				float localSkips = local.SampleSize * (1f - local.PickRate);
				float impSkips = imp.SampleSize * (1f - imp.PickRate);
				float totalSkips = localSkips + impSkips;
				merged.Add(new CommunityCardStats
				{
					CardId = imp.CardId,
					Character = imp.Character,
					PickRate = local.PickRate * lw + imp.PickRate * iw,
					WinRateWhenPicked = totalPicks > 0
						? (local.WinRateWhenPicked * localPicks + imp.WinRateWhenPicked * impPicks) / totalPicks
						: 0f,
					WinRateWhenSkipped = totalSkips > 0
						? (local.WinRateWhenSkipped * localSkips + imp.WinRateWhenSkipped * impSkips) / totalSkips
						: 0f,
					SampleSize = totalSamples,
					AvgFloorPicked = totalPicks > 0
						? (local.AvgFloorPicked * localPicks + imp.AvgFloorPicked * impPicks) / totalPicks
						: 0f,
					ArchetypeContext = MergeArchetypes(local.ArchetypeContext, local.SampleSize, imp.ArchetypeContext, imp.SampleSize)
				});
			}
			else
			{
				merged.Add(imp);
			}
		}
		SaveCommunityCardStats(merged);
	}

	public void MergeCommunityRelicStats(List<CommunityRelicStats> imported)
	{
		if (!EnsureInitialized() || imported == null)
			return;
		var merged = new List<CommunityRelicStats>();
		foreach (var imp in imported)
		{
			var local = GetCommunityRelicStats(imp.Character, imp.RelicId);
			if (local != null)
			{
				int totalSamples = local.SampleSize + imp.SampleSize;
				if (totalSamples == 0) continue;
				float lw = (float)local.SampleSize / totalSamples;
				float iw = (float)imp.SampleSize / totalSamples;
				float localPicks = local.SampleSize * local.PickRate;
				float impPicks = imp.SampleSize * imp.PickRate;
				float totalPicks = localPicks + impPicks;
				float localSkips = local.SampleSize * (1f - local.PickRate);
				float impSkips = imp.SampleSize * (1f - imp.PickRate);
				float totalSkips = localSkips + impSkips;
				merged.Add(new CommunityRelicStats
				{
					RelicId = imp.RelicId,
					Character = imp.Character,
					PickRate = local.PickRate * lw + imp.PickRate * iw,
					WinRateWhenPicked = totalPicks > 0
						? (local.WinRateWhenPicked * localPicks + imp.WinRateWhenPicked * impPicks) / totalPicks
						: 0f,
					WinRateWhenSkipped = totalSkips > 0
						? (local.WinRateWhenSkipped * localSkips + imp.WinRateWhenSkipped * impSkips) / totalSkips
						: 0f,
					SampleSize = totalSamples,
					AvgFloorPicked = totalPicks > 0
						? (local.AvgFloorPicked * localPicks + imp.AvgFloorPicked * impPicks) / totalPicks
						: 0f,
					ArchetypeContext = MergeArchetypes(local.ArchetypeContext, local.SampleSize, imp.ArchetypeContext, imp.SampleSize)
				});
			}
			else
			{
				merged.Add(imp);
			}
		}
		SaveCommunityRelicStats(merged);
	}

	public (float localWinRate, int localRuns, float communityWinRate, int communitySamples) GetStatsComparison(string character)
	{
		if (!EnsureInitialized())
			return (0f, 0, 0f, 0);
		try
		{
			// Local stats
			var (wins, total) = GetCharacterWinRate(character);
			float localWinRate = total > 0 ? (float)wins / total * 100f : 0f;

			// Community average: sample-weighted mean of card win_rate_when_picked for this character
			float communityWinRate = 0f;
			int communitySamples = 0;
			using var conn = new SqliteConnection(_connectionString);
			conn.Open();
			using var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT CASE WHEN SUM(sample_size) > 0 THEN SUM(win_rate_when_picked * sample_size) / SUM(sample_size) ELSE 0.0 END as avg_wr, SUM(sample_size) as total_samples FROM community_card_stats WHERE character=@char AND sample_size > 0";
			cmd.Parameters.AddWithValue("@char", character);
			using var reader = cmd.ExecuteReader();
			if (reader.Read() && !reader.IsDBNull(0))
			{
				communityWinRate = reader.GetFloat(0) * 100f;
				communitySamples = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
			}

			return (localWinRate, total, communityWinRate, communitySamples);
		}
		catch (Exception ex)
		{
			Plugin.Log($"GetStatsComparison error: {ex.Message}");
			return (0f, 0, 0f, 0);
		}
	}

	private static Dictionary<string, float> MergeArchetypes(
		Dictionary<string, float> a, int aSamples,
		Dictionary<string, float> b, int bSamples)
	{
		var result = new Dictionary<string, float>();
		int total = aSamples + bSamples;
		if (total == 0) return result;
		float aw = (float)aSamples / total;
		float bw = (float)bSamples / total;

		var allKeys = new HashSet<string>();
		if (a != null) foreach (var k in a.Keys) allKeys.Add(k);
		if (b != null) foreach (var k in b.Keys) allKeys.Add(k);

		foreach (var key in allKeys)
		{
			float va = a != null && a.TryGetValue(key, out var av) ? av : 0f;
			float vb = b != null && b.TryGetValue(key, out var bv) ? bv : 0f;
			bool inA = a != null && a.ContainsKey(key);
			bool inB = b != null && b.ContainsKey(key);
			if (inA && inB)
				result[key] = va * aw + vb * bw;
			else if (inA)
				result[key] = va;
			else
				result[key] = vb;
		}
		return result;
	}

	private static Dictionary<string, float> DeserializeDict(SqliteDataReader reader, string column)
	{
		if (reader.IsDBNull(reader.GetOrdinal(column)))
		{
			return new Dictionary<string, float>();
		}
		return JsonConvert.DeserializeObject<Dictionary<string, float>>(reader.GetString(reader.GetOrdinal(column))) ?? new Dictionary<string, float>();
	}
}
