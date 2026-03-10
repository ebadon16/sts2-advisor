import { handleUpload } from './upload';
import { handleStats } from './stats';
import { handleAggregate } from './aggregate';
import { getDashboardHtml } from './dashboard';

export interface Env {
	DB: D1Database;
}

function corsHeaders(): HeadersInit {
	return {
		'Access-Control-Allow-Origin': '*',
		'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
		'Access-Control-Allow-Headers': 'Content-Type',
	};
}

function jsonResponse(data: unknown, status = 200): Response {
	return new Response(JSON.stringify(data), {
		status,
		headers: {
			'Content-Type': 'application/json',
			...corsHeaders(),
		},
	});
}

function errorResponse(message: string, status: number): Response {
	return jsonResponse({ error: message }, status);
}

export default {
	async fetch(request: Request, env: Env): Promise<Response> {
		if (request.method === 'OPTIONS') {
			return new Response(null, { status: 204, headers: corsHeaders() });
		}

		const url = new URL(request.url);
		const path = url.pathname;

		try {
			if (path === '/api/upload' && request.method === 'POST') {
				return await handleUpload(request, env.DB);
			}

			if (path === '/api/stats' && request.method === 'GET') {
				return await handleStats(url, env.DB);
			}

			if (path === '/api/aggregate' && request.method === 'POST') {
				return await handleAggregate(env.DB);
			}

			if (path === '/' && request.method === 'GET') {
				return new Response(getDashboardHtml(), {
					headers: { 'Content-Type': 'text/html;charset=UTF-8' },
				});
			}

			return errorResponse('Not found', 404);
		} catch (err) {
			const message = err instanceof Error ? err.message : 'Internal error';
			console.error('Request error:', message);
			return errorResponse(message, 500);
		}
	},

	async scheduled(_event: ScheduledEvent, env: Env): Promise<void> {
		console.log('Cron triggered: recomputing community stats...');
		await handleAggregate(env.DB);
		console.log('Cron aggregation complete.');
	},
} satisfies ExportedHandler<Env>;
