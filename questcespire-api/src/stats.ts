interface CardStat {
	card_id: string;
	character: string;
	pick_rate: number;
	win_rate_when_picked: number;
	win_rate_when_skipped: number;
	sample_size: number;
	avg_floor_picked: number;
	archetype_context: string | null;
}

interface RelicStat {
	relic_id: string;
	character: string;
	pick_rate: number;
	win_rate_when_picked: number;
	win_rate_when_skipped: number;
	sample_size: number;
	avg_floor_picked: number;
	archetype_context: string | null;
}

function safeParseJson(value: string | null): Record<string, number> {
	if (!value) return {};
	try { return JSON.parse(value); }
	catch { return {}; }
}

export async function handleStats(url: URL, db: D1Database): Promise<Response> {
	const character = url.searchParams.get('character');
	const minSamples = parseInt(url.searchParams.get('min_samples') ?? '3', 10);

	// Get card stats
	let cardQuery = 'SELECT * FROM community_card_stats WHERE sample_size >= ?';
	const cardParams: unknown[] = [minSamples];
	if (character) {
		cardQuery += ' AND character = ?';
		cardParams.push(character);
	}
	const cardResults = await db.prepare(cardQuery).bind(...cardParams).all<CardStat>();

	// Get relic stats
	let relicQuery = 'SELECT * FROM community_relic_stats WHERE sample_size >= ?';
	const relicParams: unknown[] = [minSamples];
	if (character) {
		relicQuery += ' AND character = ?';
		relicParams.push(character);
	}
	const relicResults = await db.prepare(relicQuery).bind(...relicParams).all<RelicStat>();

	// Get total run count
	const countResult = await db.prepare('SELECT COUNT(*) as total FROM runs WHERE outcome IS NOT NULL').first<{ total: number }>();

	// Get last computed time
	const lastComputed = await db
		.prepare('SELECT computed_at FROM community_card_stats ORDER BY computed_at DESC LIMIT 1')
		.first<{ computed_at: string }>();

	const payload = {
		version: 1,
		card_stats: (cardResults.results ?? []).map((r) => ({
			CardId: r.card_id,
			Character: r.character,
			PickRate: r.pick_rate,
			WinRateWhenPicked: r.win_rate_when_picked,
			WinRateWhenSkipped: r.win_rate_when_skipped,
			SampleSize: r.sample_size,
			AvgFloorPicked: r.avg_floor_picked,
			ArchetypeContext: safeParseJson(r.archetype_context),
		})),
		relic_stats: (relicResults.results ?? []).map((r) => ({
			RelicId: r.relic_id,
			Character: r.character,
			PickRate: r.pick_rate,
			WinRateWhenPicked: r.win_rate_when_picked,
			WinRateWhenSkipped: r.win_rate_when_skipped,
			SampleSize: r.sample_size,
			AvgFloorPicked: r.avg_floor_picked,
			ArchetypeContext: safeParseJson(r.archetype_context),
		})),
		total_runs: countResult?.total ?? 0,
		last_updated: lastComputed?.computed_at ?? null,
	};

	return new Response(JSON.stringify(payload), {
		status: 200,
		headers: {
			'Content-Type': 'application/json',
			'Access-Control-Allow-Origin': '*',
			'Cache-Control': 'public, max-age=3600',
		},
	});
}
