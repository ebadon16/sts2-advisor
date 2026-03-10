interface UploadRun {
	run_id: string;
	character: string;
	seed?: string;
	start_time?: string;
	end_time?: string;
	outcome?: string;
	final_floor?: number;
	final_act?: number;
	ascension_level?: number;
	mod_version?: string;
}

interface UploadDecision {
	run_id: string;
	floor?: number;
	act?: number;
	event_type?: string;
	offered_ids?: string;
	chosen_id?: string;
	deck_snapshot?: string;
	relic_snapshot?: string;
	current_hp?: number;
	max_hp?: number;
	gold?: number;
	timestamp?: string;
}

interface UploadPayload {
	player_id: string;
	runs: UploadRun[];
	decisions: UploadDecision[];
}

const MAX_UPLOADS_PER_MINUTE = 10;

async function checkRateLimit(db: D1Database, playerId: string): Promise<boolean> {
	const windowStart = new Date();
	windowStart.setSeconds(0, 0);
	const windowKey = windowStart.toISOString();

	const row = await db
		.prepare('SELECT request_count FROM rate_limits WHERE player_id = ? AND window_start = ?')
		.bind(playerId, windowKey)
		.first<{ request_count: number }>();

	if (row && row.request_count >= MAX_UPLOADS_PER_MINUTE) {
		return false;
	}

	await db
		.prepare(
			`INSERT INTO rate_limits (player_id, window_start, request_count) VALUES (?, ?, 1)
			 ON CONFLICT(player_id, window_start) DO UPDATE SET request_count = request_count + 1`
		)
		.bind(playerId, windowKey)
		.run();

	// Clean old rate limit entries (older than 5 minutes)
	const cutoff = new Date(Date.now() - 5 * 60 * 1000).toISOString();
	await db.prepare('DELETE FROM rate_limits WHERE window_start < ?').bind(cutoff).run();

	return true;
}

export async function handleUpload(request: Request, db: D1Database): Promise<Response> {
	const body = (await request.json()) as UploadPayload;

	if (!body.player_id || !body.runs || !Array.isArray(body.runs)) {
		return new Response(JSON.stringify({ error: 'Invalid payload: player_id and runs required' }), {
			status: 400,
			headers: { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' },
		});
	}

	const allowed = await checkRateLimit(db, body.player_id);
	if (!allowed) {
		return new Response(JSON.stringify({ error: 'Rate limit exceeded (10/min)' }), {
			status: 429,
			headers: { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' },
		});
	}

	let accepted = 0;
	let duplicates = 0;
	const acceptedRunIds = new Set<string>();

	// Insert runs (dedup by run_id)
	for (const run of body.runs) {
		if (!run.run_id || !run.character) continue;

		try {
			const result = await db
				.prepare(
					`INSERT OR IGNORE INTO runs
					 (run_id, player_id, character, seed, start_time, end_time, outcome, final_floor, final_act, ascension_level, mod_version)
					 VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
				)
				.bind(
					run.run_id,
					body.player_id,
					run.character,
					run.seed ?? null,
					run.start_time ?? null,
					run.end_time ?? null,
					run.outcome ?? null,
					run.final_floor ?? null,
					run.final_act ?? null,
					run.ascension_level ?? null,
					run.mod_version ?? null
				)
				.run();

			if (result.meta.changes > 0) {
				accepted++;
				acceptedRunIds.add(run.run_id);
			} else {
				duplicates++;
			}
		} catch {
			duplicates++;
		}
	}

	// Insert decisions
	let decisionsInserted = 0;
	if (body.decisions && Array.isArray(body.decisions)) {
		for (const d of body.decisions) {
			if (!d.run_id || !acceptedRunIds.has(d.run_id)) continue;

			try {
				await db
					.prepare(
						`INSERT INTO decisions
						 (run_id, floor, act, event_type, offered_ids, chosen_id, deck_snapshot, relic_snapshot, current_hp, max_hp, gold, timestamp)
						 VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
					)
					.bind(
						d.run_id,
						d.floor ?? null,
						d.act ?? null,
						d.event_type ?? null,
						d.offered_ids ?? null,
						d.chosen_id ?? null,
						d.deck_snapshot ?? null,
						d.relic_snapshot ?? null,
						d.current_hp ?? null,
						d.max_hp ?? null,
						d.gold ?? null,
						d.timestamp ?? null
					)
					.run();
				decisionsInserted++;
			} catch {
				// Skip duplicate or invalid decisions
			}
		}
	}

	return new Response(
		JSON.stringify({
			accepted,
			duplicates,
			decisions_inserted: decisionsInserted,
		}),
		{
			status: 200,
			headers: { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' },
		}
	);
}
