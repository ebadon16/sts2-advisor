export async function handleAggregate(db: D1Database): Promise<Response> {
	const now = new Date().toISOString();

	// Recompute card stats
	await db.exec(`DELETE FROM community_card_stats`);
	await db
		.prepare(
			`INSERT INTO community_card_stats (card_id, character, pick_rate, win_rate_when_picked, win_rate_when_skipped, sample_size, avg_floor_picked, computed_at)
			SELECT
				j.value AS card_id,
				r.character,
				CAST(SUM(CASE WHEN d.chosen_id = j.value THEN 1 ELSE 0 END) AS REAL) / COUNT(*) AS pick_rate,
				CASE WHEN SUM(CASE WHEN d.chosen_id = j.value THEN 1 ELSE 0 END) > 0
					THEN CAST(SUM(CASE WHEN d.chosen_id = j.value AND r.outcome = 'Win' THEN 1 ELSE 0 END) AS REAL)
						 / SUM(CASE WHEN d.chosen_id = j.value THEN 1 ELSE 0 END)
					ELSE 0.0 END AS win_rate_when_picked,
				CASE WHEN COUNT(*) - SUM(CASE WHEN d.chosen_id = j.value THEN 1 ELSE 0 END) > 0
					THEN CAST(SUM(CASE WHEN (d.chosen_id != j.value OR d.chosen_id IS NULL) AND r.outcome = 'Win' THEN 1 ELSE 0 END) AS REAL)
						 / (COUNT(*) - SUM(CASE WHEN d.chosen_id = j.value THEN 1 ELSE 0 END))
					ELSE 0.0 END AS win_rate_when_skipped,
				COUNT(*) AS sample_size,
				COALESCE(AVG(CASE WHEN d.chosen_id = j.value THEN d.floor ELSE NULL END), 0.0) AS avg_floor_picked,
				?
			FROM decisions d
			JOIN runs r ON d.run_id = r.run_id
			JOIN json_each(d.offered_ids) j
			WHERE d.event_type IN ('CardReward', 'Shop')
			  AND r.outcome IS NOT NULL
			GROUP BY j.value, r.character
			HAVING COUNT(*) >= 3`
		)
		.bind(now)
		.run();

	// Recompute relic stats
	await db.exec(`DELETE FROM community_relic_stats`);
	await db
		.prepare(
			`INSERT INTO community_relic_stats (relic_id, character, pick_rate, win_rate_when_picked, win_rate_when_skipped, sample_size, avg_floor_picked, computed_at)
			SELECT
				j.value AS relic_id,
				r.character,
				CAST(SUM(CASE WHEN d.chosen_id = j.value THEN 1 ELSE 0 END) AS REAL) / COUNT(*) AS pick_rate,
				CASE WHEN SUM(CASE WHEN d.chosen_id = j.value THEN 1 ELSE 0 END) > 0
					THEN CAST(SUM(CASE WHEN d.chosen_id = j.value AND r.outcome = 'Win' THEN 1 ELSE 0 END) AS REAL)
						 / SUM(CASE WHEN d.chosen_id = j.value THEN 1 ELSE 0 END)
					ELSE 0.0 END AS win_rate_when_picked,
				CASE WHEN COUNT(*) - SUM(CASE WHEN d.chosen_id = j.value THEN 1 ELSE 0 END) > 0
					THEN CAST(SUM(CASE WHEN (d.chosen_id != j.value OR d.chosen_id IS NULL) AND r.outcome = 'Win' THEN 1 ELSE 0 END) AS REAL)
						 / (COUNT(*) - SUM(CASE WHEN d.chosen_id = j.value THEN 1 ELSE 0 END))
					ELSE 0.0 END AS win_rate_when_skipped,
				COUNT(*) AS sample_size,
				COALESCE(AVG(CASE WHEN d.chosen_id = j.value THEN d.floor ELSE NULL END), 0.0) AS avg_floor_picked,
				?
			FROM decisions d
			JOIN runs r ON d.run_id = r.run_id
			JOIN json_each(d.offered_ids) j
			WHERE d.event_type IN ('RelicReward', 'BossRelic')
			  AND r.outcome IS NOT NULL
			GROUP BY j.value, r.character
			HAVING COUNT(*) >= 3`
		)
		.bind(now)
		.run();

	// Get counts for response
	const cardCount = await db.prepare('SELECT COUNT(*) as c FROM community_card_stats').first<{ c: number }>();
	const relicCount = await db.prepare('SELECT COUNT(*) as c FROM community_relic_stats').first<{ c: number }>();
	const runCount = await db.prepare('SELECT COUNT(*) as c FROM runs WHERE outcome IS NOT NULL').first<{ c: number }>();

	return new Response(
		JSON.stringify({
			status: 'ok',
			card_stats: cardCount?.c ?? 0,
			relic_stats: relicCount?.c ?? 0,
			total_runs: runCount?.c ?? 0,
			computed_at: now,
		}),
		{
			status: 200,
			headers: { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' },
		}
	);
}
