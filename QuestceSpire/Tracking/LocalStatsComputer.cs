using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using QuestceSpire.Core;

namespace QuestceSpire.Tracking;

public class LocalStatsComputer
{
	private readonly RunDatabase _db;
	private readonly TierEngine _tierEngine;

	public LocalStatsComputer(RunDatabase db, TierEngine tierEngine = null)
	{
		_db = db;
		_tierEngine = tierEngine;
	}

	public void RecomputeAll()
	{
		try
		{
			RecomputeCardStats();
			RecomputeRelicStats();
			if (_tierEngine != null)
			{
				ComputeArchetypeContext();
			}
		}
		catch (Exception ex)
		{
			Plugin.Log("LocalStatsComputer error: " + ex.Message);
		}
	}

	private void RecomputeCardStats()
	{
		string connectionString = _db.ConnectionString;
		if (connectionString == null)
		{
			Plugin.Log("RecomputeCardStats: database not initialized — skipping.");
			return;
		}
		using SqliteConnection sqliteConnection = new SqliteConnection(connectionString);
		sqliteConnection.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
		{
			sqliteCommand.CommandText = @"
                        DELETE FROM community_card_stats;

                        INSERT INTO community_card_stats
                            (card_id, character, pick_rate, win_rate_when_picked,
                             win_rate_when_skipped, sample_size, avg_floor_picked, archetype_context)
                        SELECT
                            j.value AS card_id,
                            r.character,
                            CAST(SUM(CASE WHEN d.chosen_id IS j.value THEN 1 ELSE 0 END) AS REAL) / COUNT(*) AS pick_rate,
                            CASE WHEN SUM(CASE WHEN d.chosen_id IS j.value THEN 1 ELSE 0 END) > 0
                                THEN CAST(SUM(CASE WHEN d.chosen_id IS j.value AND r.outcome = 'Win' THEN 1 ELSE 0 END) AS REAL)
                                     / SUM(CASE WHEN d.chosen_id IS j.value THEN 1 ELSE 0 END)
                                ELSE 0.0 END AS win_rate_when_picked,
                            CASE WHEN COUNT(*) - SUM(CASE WHEN d.chosen_id IS j.value THEN 1 ELSE 0 END) > 0
                                THEN CAST(SUM(CASE WHEN d.chosen_id IS NOT j.value AND r.outcome = 'Win' THEN 1 ELSE 0 END) AS REAL)
                                     / (COUNT(*) - SUM(CASE WHEN d.chosen_id IS j.value THEN 1 ELSE 0 END))
                                ELSE 0.0 END AS win_rate_when_skipped,
                            COUNT(*) AS sample_size,
                            COALESCE(AVG(CASE WHEN d.chosen_id IS j.value THEN d.floor ELSE NULL END), 0.0) AS avg_floor_picked,
                            NULL AS archetype_context
                        FROM decisions d
                        JOIN runs r ON d.run_id = r.run_id
                        JOIN json_each(d.offered_ids) j
                        WHERE d.event_type IN ('CardReward', 'Shop')
                        AND r.outcome IS NOT NULL
                        GROUP BY j.value, r.character
                        HAVING COUNT(*) >= 2;
                    ";
			sqliteCommand.ExecuteNonQuery();
			Plugin.Log("Local card stats recomputed.");
		}
		sqliteTransaction.Commit();
	}

	private void RecomputeRelicStats()
	{
		string connectionString = _db.ConnectionString;
		if (connectionString == null)
		{
			Plugin.Log("RecomputeRelicStats: database not initialized — skipping.");
			return;
		}
		using SqliteConnection sqliteConnection = new SqliteConnection(connectionString);
		sqliteConnection.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
		{
			sqliteCommand.CommandText = @"
                        DELETE FROM community_relic_stats;

                        INSERT INTO community_relic_stats
                            (relic_id, character, pick_rate, win_rate_when_picked,
                             win_rate_when_skipped, sample_size, avg_floor_picked, archetype_context)
                        SELECT
                            j.value AS relic_id,
                            r.character,
                            CAST(SUM(CASE WHEN d.chosen_id IS j.value THEN 1 ELSE 0 END) AS REAL) / COUNT(*) AS pick_rate,
                            CASE WHEN SUM(CASE WHEN d.chosen_id IS j.value THEN 1 ELSE 0 END) > 0
                                THEN CAST(SUM(CASE WHEN d.chosen_id IS j.value AND r.outcome = 'Win' THEN 1 ELSE 0 END) AS REAL)
                                     / SUM(CASE WHEN d.chosen_id IS j.value THEN 1 ELSE 0 END)
                                ELSE 0.0 END AS win_rate_when_picked,
                            CASE WHEN COUNT(*) - SUM(CASE WHEN d.chosen_id IS j.value THEN 1 ELSE 0 END) > 0
                                THEN CAST(SUM(CASE WHEN d.chosen_id IS NOT j.value AND r.outcome = 'Win' THEN 1 ELSE 0 END) AS REAL)
                                     / (COUNT(*) - SUM(CASE WHEN d.chosen_id IS j.value THEN 1 ELSE 0 END))
                                ELSE 0.0 END AS win_rate_when_skipped,
                            COUNT(*) AS sample_size,
                            COALESCE(AVG(CASE WHEN d.chosen_id IS j.value THEN d.floor ELSE NULL END), 0.0) AS avg_floor_picked,
                            NULL AS archetype_context
                        FROM decisions d
                        JOIN runs r ON d.run_id = r.run_id
                        JOIN json_each(d.offered_ids) j
                        WHERE d.event_type IN ('RelicReward', 'BossRelic')
                        AND r.outcome IS NOT NULL
                        GROUP BY j.value, r.character
                        HAVING COUNT(*) >= 2;
                    ";
			sqliteCommand.ExecuteNonQuery();
			Plugin.Log("Local relic stats recomputed.");
		}
		sqliteTransaction.Commit();
	}

	/// <summary>
	/// Second pass: compute per-archetype win rates for cards picked in specific archetype contexts.
	/// For each decision, classify the deck into archetypes via tag counts from TierEngine,
	/// then aggregate win rates per card per archetype.
	/// </summary>
	private void ComputeArchetypeContext()
	{
		string connectionString = _db.ConnectionString;
		if (connectionString == null)
		{
			Plugin.Log("ComputeArchetypeContext: database not initialized — skipping.");
			return;
		}

		try
		{
			// Key: (cardId, character) -> archetype_tag_3+ -> (wins, total)
			var context = new Dictionary<(string cardId, string character), Dictionary<string, (int wins, int total)>>();

			using (var conn = new SqliteConnection(connectionString))
			{
				conn.Open();
				using var cmd = conn.CreateCommand();
				cmd.CommandText = @"
					SELECT d.chosen_id, d.deck_snapshot, r.character, r.outcome
					FROM decisions d
					JOIN runs r ON d.run_id = r.run_id
					WHERE d.event_type IN ('CardReward', 'Shop')
					AND d.chosen_id IS NOT NULL
					AND r.outcome IS NOT NULL";

				using var reader = cmd.ExecuteReader();
				while (reader.Read())
				{
					string chosenId = reader.GetString(0);
					string deckJson = reader.GetString(1);
					string character = reader.GetString(2);
					string outcome = reader.GetString(3);
					bool isWin = outcome == "Win";

					List<string> deckIds;
					try { deckIds = JsonConvert.DeserializeObject<List<string>>(deckJson); }
					catch { continue; }
					if (deckIds == null || deckIds.Count == 0) continue;

					// Count archetype core tags in the deck using TierEngine synergies
					var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
					foreach (string cardId in deckIds)
					{
						var tier = _tierEngine.GetCardTier(character, cardId);
						if (tier?.Synergies == null) continue;
						foreach (string syn in tier.Synergies)
						{
							string tag = syn.ToLowerInvariant();
							tagCounts[tag] = tagCounts.TryGetValue(tag, out int c) ? c + 1 : 1;
						}
					}

					// Check each archetype definition for this character
					string charKey = character?.ToLowerInvariant() ?? "";
					if (!ArchetypeDefinitions.ByCharacter.TryGetValue(charKey, out var archetypes))
						continue;

					foreach (var arch in archetypes)
					{
						if (arch.CoreTags == null || arch.CoreTags.Count == 0) continue;
						int coreCount = 0;
						foreach (string coreTag in arch.CoreTags)
						{
							if (tagCounts.TryGetValue(coreTag, out int v))
								coreCount += v;
						}
						if (coreCount < 3) continue;

						// This deck qualifies for this archetype context
						string contextKey = arch.CoreTags[0] + "_3+";
						var key = (chosenId, character);
						if (!context.TryGetValue(key, out var archDict))
						{
							archDict = new Dictionary<string, (int, int)>();
							context[key] = archDict;
						}
						(int w, int t) prev = archDict.TryGetValue(contextKey, out var v2) ? v2 : (0, 0);
						archDict[contextKey] = (prev.w + (isWin ? 1 : 0), prev.t + 1);
					}
				}
			}

			// Now update archetype_context for cards that have context data
			if (context.Count == 0) return;

			using (var conn = new SqliteConnection(connectionString))
			{
				conn.Open();
				using var tx = conn.BeginTransaction();
				using var updateCmd = conn.CreateCommand();
				updateCmd.CommandText = "UPDATE community_card_stats SET archetype_context = @ctx WHERE card_id = @cardId AND character = @character";
				var pCtx = updateCmd.Parameters.Add("@ctx", SqliteType.Text);
				var pCard = updateCmd.Parameters.Add("@cardId", SqliteType.Text);
				var pChar = updateCmd.Parameters.Add("@character", SqliteType.Text);

				int updated = 0;
				foreach (var kvp in context)
				{
					// Convert (wins, total) to win rates, require at least 2 samples
					var winRates = new Dictionary<string, float>();
					foreach (var arch in kvp.Value)
					{
						if (arch.Value.Item2 >= 2)
						{
							winRates[arch.Key] = (float)arch.Value.Item1 / arch.Value.Item2;
						}
					}
					if (winRates.Count == 0) continue;

					pCtx.Value = JsonConvert.SerializeObject(winRates);
					pCard.Value = kvp.Key.cardId;
					pChar.Value = kvp.Key.character;
					updateCmd.ExecuteNonQuery();
					updated++;
				}
				tx.Commit();
				if (updated > 0)
				{
					Plugin.Log($"Archetype context computed for {updated} card stats.");
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log("ComputeArchetypeContext error: " + ex.Message);
		}
	}
}
