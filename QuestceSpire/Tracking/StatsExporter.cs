using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace QuestceSpire.Tracking;

public static class StatsExporter
{
	private const int FormatVersion = 1;
	private const string ExportFileName = "questcespire_stats_export.json";
	public const string ImportFileName = "questcespire_stats_import.json";

	public static string Export(RunDatabase db)
	{
		var payload = new ExportPayload
		{
			Version = FormatVersion,
			ModVersion = Plugin.ModVersion,
			ExportedAt = DateTime.UtcNow.ToString("o"),
			TotalRuns = db.GetTotalRunCount(),
			CardStats = db.GetAllCommunityCardStats(),
			RelicStats = db.GetAllCommunityRelicStats()
		};
		return JsonConvert.SerializeObject(payload, Formatting.Indented);
	}

	public static (string path, int cards, int relics) ExportToFile(RunDatabase db, string folder)
	{
		var cardStats = db.GetAllCommunityCardStats();
		var relicStats = db.GetAllCommunityRelicStats();
		var payload = new ExportPayload
		{
			Version = FormatVersion,
			ModVersion = Plugin.ModVersion,
			ExportedAt = DateTime.UtcNow.ToString("o"),
			TotalRuns = db.GetTotalRunCount(),
			CardStats = cardStats,
			RelicStats = relicStats
		};
		string path = Path.Combine(folder, ExportFileName);
		File.WriteAllText(path, JsonConvert.SerializeObject(payload, Formatting.Indented));
		return (path, cardStats.Count, relicStats.Count);
	}

	public static (int cards, int relics) ImportFromFile(RunDatabase db, string filePath)
	{
		if (!File.Exists(filePath))
			return (-1, -1);

		string json = File.ReadAllText(filePath);
		var payload = JsonConvert.DeserializeObject<ExportPayload>(json);
		if (payload == null)
			throw new InvalidOperationException("Invalid stats export file.");

		if (payload.Version > FormatVersion)
			Plugin.Log($"StatsExporter: import file version {payload.Version} is newer than supported {FormatVersion} — proceeding anyway.");

		int cards = payload.CardStats?.Count ?? 0;
		int relics = payload.RelicStats?.Count ?? 0;

		// Reset to local-only stats first to avoid double-counting on repeated imports
		Plugin.LocalStats?.RecomputeAll();

		if (payload.CardStats != null && payload.CardStats.Count > 0)
			db.MergeCommunityCardStats(payload.CardStats);

		if (payload.RelicStats != null && payload.RelicStats.Count > 0)
			db.MergeCommunityRelicStats(payload.RelicStats);

		// Update CloudSync cache so run-end remerge uses the imported data
		if (Plugin.CloudSync != null)
		{
			if (payload.CardStats != null && payload.CardStats.Count > 0)
				Plugin.CloudSync.CachedCardStats = payload.CardStats;
			if (payload.RelicStats != null && payload.RelicStats.Count > 0)
				Plugin.CloudSync.CachedRelicStats = payload.RelicStats;
		}

		return (cards, relics);
	}

	/// <summary>
	/// Scans multiple locations for an importable stats file.
	/// Priority: plugin folder, Downloads, Desktop.
	/// Accepts questcespire_stats_export*.json or questcespire_stats_import.json.
	/// </summary>
	public static string FindImportFile(string pluginFolder)
	{
		var foldersToScan = new List<string> { pluginFolder };
		string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (!string.IsNullOrEmpty(userProfile))
		{
			foldersToScan.Add(Path.Combine(userProfile, "Downloads"));
			string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
			if (!string.IsNullOrEmpty(desktop))
				foldersToScan.Add(desktop);
		}

		foreach (string folder in foldersToScan)
		{
			if (!Directory.Exists(folder)) continue;
			try
			{
				// Check for import file first, then any export file
				string importPath = Path.Combine(folder, ImportFileName);
				if (File.Exists(importPath))
					return importPath;

				var exportFiles = Directory.GetFiles(folder, "questcespire_stats_export*.json");
				if (exportFiles.Length > 0)
					return exportFiles.OrderByDescending(File.GetLastWriteTimeUtc).First();
			}
			catch (Exception ex)
			{
				Plugin.Log($"StatsExporter: error scanning {folder}: {ex.Message}");
			}
		}
		return null;
	}

	private class ExportPayload
	{
		[JsonProperty("version")]
		public int Version { get; set; }

		[JsonProperty("mod_version")]
		public string ModVersion { get; set; }

		[JsonProperty("exported_at")]
		public string ExportedAt { get; set; }

		[JsonProperty("total_runs")]
		public int TotalRuns { get; set; }

		[JsonProperty("card_stats")]
		public List<CommunityCardStats> CardStats { get; set; }

		[JsonProperty("relic_stats")]
		public List<CommunityRelicStats> RelicStats { get; set; }
	}
}
