using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using QuestceSpire.Core;

namespace QuestceSpire.Tracking;

public class CloudSync
{
	private const string DefaultApiBase = "https://questcespire-api.questcespire.workers.dev/api";

	private readonly RunDatabase _db;
	private readonly string _playerId;
	private readonly HttpClient _http;
	private readonly string _apiBase;

	private DateTime _lastDownload = DateTime.MinValue;
	private static readonly TimeSpan DownloadInterval = TimeSpan.FromMinutes(30);

	// Cache last downloaded/imported community stats for safe re-application after local recompute
	private List<CommunityCardStats> _cachedCardStats;
	private List<CommunityRelicStats> _cachedRelicStats;

	/// <summary>Cached community card stats — set by download or import.</summary>
	public List<CommunityCardStats> CachedCardStats
	{
		get => _cachedCardStats;
		set => _cachedCardStats = value;
	}

	/// <summary>Cached community relic stats — set by download or import.</summary>
	public List<CommunityRelicStats> CachedRelicStats
	{
		get => _cachedRelicStats;
		set => _cachedRelicStats = value;
	}

	public CloudSync(RunDatabase db, string playerId, string apiBase = null)
	{
		_db = db;
		_playerId = playerId;
		_apiBase = apiBase ?? DefaultApiBase;
		_http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
	}

	public async Task UploadPendingRuns()
	{
		if (!OverlaySettings.Load().CloudSyncEnabled)
			return;

		try
		{
			var unsynced = _db.GetUnsynced();
			if (unsynced.Count == 0) return;

			var runs = new List<object>();
			var decisions = new List<object>();

			foreach (var (run, events) in unsynced)
			{
				runs.Add(new
				{
					run_id = run.RunId,
					character = run.Character,
					seed = run.Seed,
					start_time = run.StartTime.ToString("o"),
					end_time = run.EndTime?.ToString("o"),
					outcome = run.Outcome?.ToString(),
					final_floor = run.FinalFloor,
					final_act = run.FinalAct,
					ascension_level = run.AscensionLevel,
					mod_version = Plugin.ModVersion,
				});

				foreach (var e in events)
				{
					decisions.Add(new
					{
						run_id = e.RunId,
						floor = e.Floor,
						act = e.Act,
						event_type = e.EventType.ToString(),
						offered_ids = JsonConvert.SerializeObject(e.OfferedIds),
						chosen_id = e.ChosenId,
						deck_snapshot = JsonConvert.SerializeObject(e.DeckSnapshot),
						relic_snapshot = JsonConvert.SerializeObject(e.RelicSnapshot),
						current_hp = e.CurrentHP,
						max_hp = e.MaxHP,
						gold = e.Gold,
						timestamp = e.Timestamp.ToString("o"),
					});
				}
			}

			var payload = new { player_id = _playerId, runs, decisions };
			var json = JsonConvert.SerializeObject(payload);
			var content = new StringContent(json, Encoding.UTF8, "application/json");

			var response = await _http.PostAsync($"{_apiBase}/upload", content);
			if (response.IsSuccessStatusCode)
			{
				// Parse response to find which runs were actually accepted
				var respBody = await response.Content.ReadAsStringAsync();
				var respData = JsonConvert.DeserializeObject<UploadResponse>(respBody);
				var acceptedIds = respData?.AcceptedRunIds != null
					? new HashSet<string>(respData.AcceptedRunIds)
					: null;

				int marked = 0;
				foreach (var (run, _) in unsynced)
				{
					// If server returned accepted IDs, only mark those; otherwise fall back to marking all
					if (acceptedIds == null || acceptedIds.Contains(run.RunId))
					{
						_db.MarkSynced(run.RunId);
						marked++;
					}
				}
				Plugin.Log($"CloudSync: uploaded {unsynced.Count} run(s), {marked} marked synced.");
			}
			else
			{
				Plugin.Log($"CloudSync: upload failed — HTTP {(int)response.StatusCode}");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log($"CloudSync upload error: {ex.Message}");
		}
	}

	public async Task DownloadCommunityStats(string character = null)
	{
		if (!OverlaySettings.Load().CloudSyncEnabled)
			return;

		if (DateTime.UtcNow - _lastDownload < DownloadInterval)
			return;

		try
		{
			var url = $"{_apiBase}/stats?min_samples=3";
			if (!string.IsNullOrEmpty(character))
				url += $"&character={Uri.EscapeDataString(character)}";

			var response = await _http.GetAsync(url);
			if (!response.IsSuccessStatusCode)
			{
				Plugin.Log($"CloudSync: download failed — HTTP {(int)response.StatusCode}");
				return;
			}

			var body = await response.Content.ReadAsStringAsync();
			var payload = JsonConvert.DeserializeObject<StatsPayload>(body);
			if (payload == null) return;

			if (payload.CardStats != null && payload.CardStats.Count > 0)
				_cachedCardStats = payload.CardStats;

			if (payload.RelicStats != null && payload.RelicStats.Count > 0)
				_cachedRelicStats = payload.RelicStats;

			// Reset to local-only stats, then merge cloud once to avoid double-counting
			ApplyCachedStats();

			_lastDownload = DateTime.UtcNow;
			Plugin.Log($"CloudSync: downloaded {payload.CardStats?.Count ?? 0} card stats, {payload.RelicStats?.Count ?? 0} relic stats from {payload.TotalRuns} community runs.");
		}
		catch (Exception ex)
		{
			Plugin.Log($"CloudSync download error: {ex.Message}");
		}
	}

	/// <summary>
	/// Recomputes local-only stats and then merges cached community data once.
	/// This avoids double-counting: local stats are computed fresh from decisions,
	/// then cloud data is merged in exactly once.
	/// Call after run ends or on download to ensure correct totals.
	/// </summary>
	public void ApplyCachedStats()
	{
		try
		{
			// Step 1: Reset to local-only (deletes community_* and reinserts from decisions)
			Plugin.LocalStats?.RecomputeAll();

			// Step 2: Merge cloud data exactly once on top of local-only data
			if (_cachedCardStats != null && _cachedCardStats.Count > 0)
				_db.MergeCommunityCardStats(_cachedCardStats);
			if (_cachedRelicStats != null && _cachedRelicStats.Count > 0)
				_db.MergeCommunityRelicStats(_cachedRelicStats);
		}
		catch (Exception ex)
		{
			Plugin.Log($"CloudSync ApplyCachedStats error: {ex.Message}");
		}
	}

	private class StatsPayload
	{
		[JsonProperty("version")]
		public int Version { get; set; }

		[JsonProperty("card_stats")]
		public List<CommunityCardStats> CardStats { get; set; }

		[JsonProperty("relic_stats")]
		public List<CommunityRelicStats> RelicStats { get; set; }

		[JsonProperty("total_runs")]
		public int TotalRuns { get; set; }

		[JsonProperty("last_updated")]
		public string LastUpdated { get; set; }
	}

	private class UploadResponse
	{
		[JsonProperty("accepted")]
		public int Accepted { get; set; }

		[JsonProperty("duplicates")]
		public int Duplicates { get; set; }

		[JsonProperty("accepted_run_ids")]
		public List<string> AcceptedRunIds { get; set; }
	}
}
