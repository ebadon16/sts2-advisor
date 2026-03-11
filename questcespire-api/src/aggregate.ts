export async function handleAggregate(db: D1Database): Promise<Response> {
	const now = new Date().toISOString();

	// Recompute card stats (delete + insert in batch for atomicity)
	await db.batch([
		db.prepare(`DELETE FROM community_card_stats`),
		db.prepare(
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
			WHERE d.event_type IN ('CardReward', 'CardTransform')
			  AND r.outcome IS NOT NULL
			GROUP BY j.value, r.character
			HAVING COUNT(*) >= 3`
		)
		.bind(now),
	]);

	// Recompute relic stats (delete + insert in batch for atomicity)
	await db.batch([
		db.prepare(`DELETE FROM community_relic_stats`),
		db.prepare(
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
		.bind(now),
	]);

	// Ensure archetype_context column exists (idempotent migration)
	for (const table of ['community_card_stats', 'community_relic_stats']) {
		try {
			await db.prepare(`ALTER TABLE ${table} ADD COLUMN archetype_context TEXT`).run();
		} catch {
			// Column already exists — safe to ignore
		}
	}

	// Compute archetype context stats
	const CONTEXT_TAGS = [
		'poison', 'shiv', 'strength', 'exhaust', 'block', 'draw', 'discard',
		'focus', 'frost', 'lightning', 'dark', 'orb', 'zero_cost',
		'stellar', 'cosmic', 'authority', 'minion',
		'summon', 'undead', 'curse', 'sacrifice', 'soul', 'death',
	];
	const CONTEXT_THRESHOLD = 3;

	function detectContexts(deckSnapshot: string | null): string[] {
		if (!deckSnapshot) return [];
		// Parse deck snapshot into individual card IDs (JSON array or comma-separated)
		let cardIds: string[];
		try {
			const parsed = JSON.parse(deckSnapshot);
			cardIds = Array.isArray(parsed) ? parsed.map((s: unknown) => String(s).toLowerCase()) : [];
		} catch {
			// Fallback: split on commas/brackets for non-JSON formats
			cardIds = deckSnapshot.replace(/[\[\]"]/g, '').split(',').map(s => s.trim().toLowerCase());
		}
		if (cardIds.length === 0) return [];
		const contexts: string[] = [];
		for (const tag of CONTEXT_TAGS) {
			// Count how many distinct card IDs contain this tag
			const count = cardIds.filter(id => id.includes(tag)).length;
			if (count >= CONTEXT_THRESHOLD) {
				contexts.push(`${tag}_${count}+`);
			}
		}
		return contexts;
	}

	// Card context stats
	const cardDecisions = await db.prepare(
		`SELECT d.chosen_id, d.deck_snapshot, r.outcome, r.character
		 FROM decisions d
		 JOIN runs r ON d.run_id = r.run_id
		 WHERE d.event_type IN ('CardReward', 'CardTransform')
		   AND r.outcome IS NOT NULL
		   AND d.chosen_id IS NOT NULL`
	).all<{ chosen_id: string; deck_snapshot: string | null; outcome: string; character: string }>();

	// Accumulate per (card_id, character) → per context → {picks, wins}
	const cardContextMap = new Map<string, Map<string, { picks: number; wins: number }>>();
	for (const row of cardDecisions.results ?? []) {
		const contexts = detectContexts(row.deck_snapshot);
		if (contexts.length === 0) continue;
		const key = `${row.chosen_id}||${row.character}`;
		let contextStats = cardContextMap.get(key);
		if (!contextStats) {
			contextStats = new Map();
			cardContextMap.set(key, contextStats);
		}
		for (const ctx of contexts) {
			let s = contextStats.get(ctx);
			if (!s) { s = { picks: 0, wins: 0 }; contextStats.set(ctx, s); }
			s.picks++;
			if (row.outcome === 'Win') s.wins++;
		}
	}

	// Relic context stats
	const relicDecisions = await db.prepare(
		`SELECT d.chosen_id, d.deck_snapshot, r.outcome, r.character
		 FROM decisions d
		 JOIN runs r ON d.run_id = r.run_id
		 WHERE d.event_type IN ('RelicReward', 'BossRelic')
		   AND r.outcome IS NOT NULL
		   AND d.chosen_id IS NOT NULL`
	).all<{ chosen_id: string; deck_snapshot: string | null; outcome: string; character: string }>();

	const relicContextMap = new Map<string, Map<string, { picks: number; wins: number }>>();
	for (const row of relicDecisions.results ?? []) {
		const contexts = detectContexts(row.deck_snapshot);
		if (contexts.length === 0) continue;
		const key = `${row.chosen_id}||${row.character}`;
		let contextStats = relicContextMap.get(key);
		if (!contextStats) {
			contextStats = new Map();
			relicContextMap.set(key, contextStats);
		}
		for (const ctx of contexts) {
			let s = contextStats.get(ctx);
			if (!s) { s = { picks: 0, wins: 0 }; contextStats.set(ctx, s); }
			s.picks++;
			if (row.outcome === 'Win') s.wins++;
		}
	}

	// Batch UPDATE card stats with archetype_context JSON
	function buildContextJson(contextStats: Map<string, { picks: number; wins: number }>): string {
		const obj: Record<string, number> = {};
		for (const [ctx, s] of contextStats) {
			if (s.picks >= CONTEXT_THRESHOLD) {
				obj[ctx] = Math.round((s.wins / s.picks) * 1000) / 1000;
			}
		}
		return JSON.stringify(obj);
	}

	const cardUpdateStmts: D1PreparedStatement[] = [];
	for (const [key, contextStats] of cardContextMap) {
		const [cardId, character] = key.split('||');
		const json = buildContextJson(contextStats);
		if (json !== '{}') {
			cardUpdateStmts.push(
				db.prepare('UPDATE community_card_stats SET archetype_context = ? WHERE card_id = ? AND character = ?')
					.bind(json, cardId, character)
			);
		}
	}
	// D1 batch limit is ~100 statements; chunk if needed
	for (let i = 0; i < cardUpdateStmts.length; i += 80) {
		await db.batch(cardUpdateStmts.slice(i, i + 80));
	}

	const relicUpdateStmts: D1PreparedStatement[] = [];
	for (const [key, contextStats] of relicContextMap) {
		const [relicId, character] = key.split('||');
		const json = buildContextJson(contextStats);
		if (json !== '{}') {
			relicUpdateStmts.push(
				db.prepare('UPDATE community_relic_stats SET archetype_context = ? WHERE relic_id = ? AND character = ?')
					.bind(json, relicId, character)
			);
		}
	}
	for (let i = 0; i < relicUpdateStmts.length; i += 80) {
		await db.batch(relicUpdateStmts.slice(i, i + 80));
	}

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
