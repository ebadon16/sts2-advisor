using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Configuration;
using Newtonsoft.Json;

namespace STS2Advisor.Tracking
{
    public class SyncClient
    {
        private static SyncClient _instance;
        public static SyncClient Instance => _instance ?? (_instance = new SyncClient());

        private const int MaxRetries = 3;
        private const int BaseDelayMs = 1000;

        private HttpClient _httpClient;
        private string _serverUrl;
        private volatile bool _syncing;

        private SyncClient() { }

        public void Initialize(ConfigFile config)
        {
            var serverUrlEntry = config.Bind(
                "Tracking",
                "ServerUrl",
                "https://api.sts2advisor.com",
                "Community stats server URL."
            );

            _serverUrl = serverUrlEntry.Value.TrimEnd('/');

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", $"STS2Advisor/{Plugin.PluginVersion}");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            Plugin.Log.LogInfo($"SyncClient initialized. Server: {_serverUrl}");
        }

        /// <summary>
        /// Queues a background sync of unsynced runs. Non-blocking.
        /// </summary>
        public void QueueSync()
        {
            if (_syncing) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    UploadRunsAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Background sync failed: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Uploads all unsynced runs to the server.
        /// </summary>
        public async Task UploadRunsAsync()
        {
            if (_syncing) return;
            _syncing = true;

            try
            {
                var unsynced = RunDatabase.Instance.GetUnsynced();
                if (unsynced.Count == 0) return;

                var payload = new List<object>();
                foreach (var (run, decisions) in unsynced)
                {
                    payload.Add(new { run, decisions });
                }

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var success = await SendWithRetry(HttpMethod.Post, "/api/runs/bulk", content);

                if (success)
                {
                    foreach (var (run, _) in unsynced)
                    {
                        RunDatabase.Instance.MarkSynced(run.RunId);
                    }
                    Plugin.Log.LogInfo($"Synced {unsynced.Count} run(s) to server.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Upload failed: {ex.Message}");
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>
        /// Fetches community stats for a character and saves to local DB.
        /// </summary>
        public async Task FetchCommunityStatsAsync(string character)
        {
            try
            {
                // Fetch card stats
                var cardJson = await GetWithRetry($"/api/stats/cards/{character}");
                if (cardJson != null)
                {
                    var cardStats = JsonConvert.DeserializeObject<List<CommunityCardStats>>(cardJson);
                    if (cardStats != null && cardStats.Count > 0)
                    {
                        RunDatabase.Instance.SaveCommunityCardStats(cardStats);
                        Plugin.Log.LogInfo($"Updated {cardStats.Count} community card stats for {character}.");
                    }
                }

                // Fetch relic stats
                var relicJson = await GetWithRetry($"/api/stats/relics/{character}");
                if (relicJson != null)
                {
                    var relicStats = JsonConvert.DeserializeObject<List<CommunityRelicStats>>(relicJson);
                    if (relicStats != null && relicStats.Count > 0)
                    {
                        RunDatabase.Instance.SaveCommunityRelicStats(relicStats);
                        Plugin.Log.LogInfo($"Updated {relicStats.Count} community relic stats for {character}.");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to fetch community stats for {character}: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetches community stats on a background thread. Non-blocking.
        /// </summary>
        public void FetchCommunityStatsInBackground(string character)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    FetchCommunityStatsAsync(character).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Background fetch failed: {ex.Message}");
                }
            });
        }

        private async Task<bool> SendWithRetry(HttpMethod method, string path, HttpContent content)
        {
            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    var request = new HttpRequestMessage(method, _serverUrl + path)
                    {
                        Content = content
                    };

                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                        return true;

                    Plugin.Log.LogWarning($"Server returned {(int)response.StatusCode} for {method} {path}");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Request failed (attempt {attempt + 1}/{MaxRetries}): {ex.Message}");
                }

                if (attempt < MaxRetries - 1)
                {
                    int delay = BaseDelayMs * (1 << attempt); // exponential backoff
                    await Task.Delay(delay);
                }
            }

            return false;
        }

        private async Task<string> GetWithRetry(string path)
        {
            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    var response = await _httpClient.GetAsync(_serverUrl + path);
                    if (response.IsSuccessStatusCode)
                        return await response.Content.ReadAsStringAsync();

                    Plugin.Log.LogWarning($"Server returned {(int)response.StatusCode} for GET {path}");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"GET failed (attempt {attempt + 1}/{MaxRetries}): {ex.Message}");
                }

                if (attempt < MaxRetries - 1)
                {
                    int delay = BaseDelayMs * (1 << attempt);
                    await Task.Delay(delay);
                }
            }

            return null;
        }
    }
}
