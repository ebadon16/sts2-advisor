using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace STS2Advisor.Tracking
{
    /// <summary>
    /// Computes pick rates and win rates from local play data stored in the decisions table.
    /// Results are written to the community_card_stats and community_relic_stats tables,
    /// which AdaptiveScorer already reads for score blending.
    ///
    /// This replaces the need for an external community server — your own play data
    /// drives the adaptive scoring system. The more you play, the smarter the advice gets.
    /// </summary>
    public class LocalStatsComputer
    {
        private readonly RunDatabase _db;

        public LocalStatsComputer(RunDatabase db)
        {
            _db = db;
        }

        /// <summary>
        /// Recomputes all local card and relic stats from the decisions + runs tables.
        /// Called after each run ends.
        /// </summary>
        public void RecomputeAll()
        {
            try
            {
                RecomputeCardStats();
                RecomputeRelicStats();
            }
            catch (Exception ex)
            {
                Plugin.Log($"LocalStatsComputer error: {ex.Message}");
            }
        }

        private void RecomputeCardStats()
        {
            string connStr = _db.ConnectionString;
            if (connStr == null) return;

            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                // Query: for each card offered in card reward decisions, compute stats
                // json_each expands the offered_ids JSON array into rows
                using (var cmd = conn.CreateCommand())
                {
                    // chosen_id can be NULL (skip) — use IS instead of = for null-safe comparison
                    // "picked" = chosen_id matches this card
                    // "skipped" = chosen_id is NULL or a different card
                    cmd.CommandText = @"
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
                    cmd.ExecuteNonQuery();
                    Plugin.Log($"Local card stats recomputed.");
                }
                transaction.Commit();
                }
            }
        }

        private void RecomputeRelicStats()
        {
            string connStr = _db.ConnectionString;
            if (connStr == null) return;

            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
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
                    cmd.ExecuteNonQuery();
                    Plugin.Log($"Local relic stats recomputed.");
                }
                transaction.Commit();
                }
            }
        }
    }
}
