using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace QuestceSpire.Tracking;

/// <summary>
/// Imports card/relic pick and win rate data from the game's native save files
/// (progress.save and .run history files) into the community stats tables.
/// </summary>
public class GameDataImporter
{
	private readonly RunDatabase _db;

	public GameDataImporter(RunDatabase db)
	{
		_db = db;
	}

	public void ImportAll()
	{
		string saveDir = FindSaveDirectory();
		if (saveDir == null)
		{
			Plugin.Log("GameDataImporter: could not find game save directory.");
			return;
		}

		string historyDir = Path.Combine(saveDir, "saves", "history");
		if (!Directory.Exists(historyDir))
		{
			Plugin.Log("GameDataImporter: no history directory found at " + historyDir);
			return;
		}

		string[] runFiles = Directory.GetFiles(historyDir, "*.run");
		if (runFiles.Length == 0)
		{
			Plugin.Log("GameDataImporter: no .run files found.");
			return;
		}

		Plugin.Log($"GameDataImporter: found {runFiles.Length} run history files.");

		// card_id -> character -> {picked_wins, picked_losses, skipped_wins, skipped_losses}
		var cardAgg = new Dictionary<string, Dictionary<string, int[]>>();
		// relic_id -> character -> {picked_wins, picked_losses, skipped_wins, skipped_losses}
		var relicAgg = new Dictionary<string, Dictionary<string, int[]>>();

		int importedRuns = 0;
		foreach (string runFile in runFiles)
		{
			try
			{
				string json = File.ReadAllText(runFile);
				var run = JObject.Parse(json);
				bool? win = (bool?)run["win"];
				bool? abandoned = (bool?)run["was_abandoned"];
				if (win == null || abandoned == true) continue;
				bool isWin = win.Value;

				var players = run["players"] as JArray;
				if (players == null || players.Count == 0) continue;
				string character = NormalizeCharacter((string)players[0]["character"]);
				if (character == null) continue;

				var mapHistory = run["map_point_history"] as JArray;
				if (mapHistory == null) continue;

				foreach (JArray act in mapHistory)
				{
					foreach (JObject node in act)
					{
						var playerStats = node["player_stats"] as JArray;
						if (playerStats == null) continue;

						foreach (JObject ps in playerStats)
						{
							// Card choices
							var cardChoices = ps["card_choices"] as JArray;
							if (cardChoices != null && cardChoices.Count > 0)
							{
								foreach (JToken ccToken in cardChoices)
								{
									if (ccToken is not JObject cc) continue;
									string cardId = NormalizeCardId((string)cc["card"]?["id"]);
									if (cardId == null) continue;
									bool picked = (bool)(cc["was_picked"] ?? false);
									AggregateChoice(cardAgg, cardId, character, picked, isWin);
								}
							}

							// Relic choices
							var relicChoices = ps["relic_choices"] as JArray;
							if (relicChoices != null && relicChoices.Count > 0)
							{
								foreach (JToken rcToken in relicChoices)
								{
									if (rcToken is not JObject rc) continue;
									string relicId = NormalizeRelicId((string)rc["choice"]);
									if (relicId == null) continue;
									bool picked = (bool)(rc["was_picked"] ?? false);
									AggregateChoice(relicAgg, relicId, character, picked, isWin);
								}
							}
						}
					}
				}
				importedRuns++;
			}
			catch (Exception ex)
			{
				Plugin.Log("GameDataImporter: error reading " + Path.GetFileName(runFile) + ": " + ex.Message);
			}
		}

		Plugin.Log($"GameDataImporter: processed {importedRuns} runs.");

		// Convert to CommunityCardStats and save
		var cardStatsList = new List<CommunityCardStats>();
		foreach (var cardKvp in cardAgg)
		{
			foreach (var charKvp in cardKvp.Value)
			{
				int[] counts = charKvp.Value;
				int pickedTotal = counts[0] + counts[1];
				int skippedTotal = counts[2] + counts[3];
				int sampleSize = pickedTotal + skippedTotal;
				if (sampleSize < 2) continue;

				cardStatsList.Add(new CommunityCardStats
				{
					CardId = cardKvp.Key,
					Character = charKvp.Key,
					PickRate = sampleSize > 0 ? (float)pickedTotal / sampleSize : 0f,
					WinRateWhenPicked = pickedTotal > 0 ? (float)counts[0] / pickedTotal : 0f,
					WinRateWhenSkipped = skippedTotal > 0 ? (float)counts[2] / skippedTotal : 0f,
					SampleSize = sampleSize,
					AvgFloorPicked = 0f
				});
			}
		}

		var relicStatsList = new List<CommunityRelicStats>();
		foreach (var relicKvp in relicAgg)
		{
			foreach (var charKvp in relicKvp.Value)
			{
				int[] counts = charKvp.Value;
				int pickedTotal = counts[0] + counts[1];
				int skippedTotal = counts[2] + counts[3];
				int sampleSize = pickedTotal + skippedTotal;
				if (sampleSize < 2) continue;

				relicStatsList.Add(new CommunityRelicStats
				{
					RelicId = relicKvp.Key,
					Character = charKvp.Key,
					PickRate = sampleSize > 0 ? (float)pickedTotal / sampleSize : 0f,
					WinRateWhenPicked = pickedTotal > 0 ? (float)counts[0] / pickedTotal : 0f,
					WinRateWhenSkipped = skippedTotal > 0 ? (float)counts[2] / skippedTotal : 0f,
					SampleSize = sampleSize,
					AvgFloorPicked = 0f
				});
			}
		}

		if (cardStatsList.Count > 0)
		{
			_db.MergeCommunityCardStats(cardStatsList);
			Plugin.Log($"GameDataImporter: merged {cardStatsList.Count} card stat entries from game history.");
		}
		if (relicStatsList.Count > 0)
		{
			_db.MergeCommunityRelicStats(relicStatsList);
			Plugin.Log($"GameDataImporter: merged {relicStatsList.Count} relic stat entries from game history.");
		}
	}

	private static void AggregateChoice(Dictionary<string, Dictionary<string, int[]>> agg, string id, string character, bool picked, bool isWin)
	{
		if (!agg.TryGetValue(id, out var charDict))
		{
			charDict = new Dictionary<string, int[]>();
			agg[id] = charDict;
		}
		if (!charDict.TryGetValue(character, out var counts))
		{
			// [picked_wins, picked_losses, skipped_wins, skipped_losses]
			counts = new int[4];
			charDict[character] = counts;
		}
		if (picked)
		{
			if (isWin) counts[0]++;
			else counts[1]++;
		}
		else
		{
			if (isWin) counts[2]++;
			else counts[3]++;
		}
	}

	/// <summary>
	/// Convert CARD.STRIKE_IRONCLAD → Strike Ironclad (mod format).
	/// CARD.FEEL_NO_PAIN → Feel No Pain
	/// </summary>
	private static string NormalizeCardId(string gameId)
	{
		if (string.IsNullOrEmpty(gameId)) return null;
		// Strip "CARD." prefix
		string id = gameId.StartsWith("CARD.", StringComparison.OrdinalIgnoreCase) && gameId.Length > 5
			? gameId.Substring(5)
			: gameId;
		// Convert UPPER_SNAKE to Title Case: FEEL_NO_PAIN → Feel No Pain
		string[] parts = id.Split('_');
		for (int i = 0; i < parts.Length; i++)
		{
			if (parts[i].Length > 0)
			{
				parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1).ToLower();
			}
		}
		return string.Join(" ", parts);
	}

	/// <summary>
	/// Convert RELIC.HAPPY_FLOWER → Happy Flower (mod format).
	/// </summary>
	private static string NormalizeRelicId(string gameId)
	{
		if (string.IsNullOrEmpty(gameId)) return null;
		string id = gameId.StartsWith("RELIC.", StringComparison.OrdinalIgnoreCase) && gameId.Length > 6
			? gameId.Substring(6)
			: gameId;
		string[] parts = id.Split('_');
		for (int i = 0; i < parts.Length; i++)
		{
			if (parts[i].Length > 0)
			{
				parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1).ToLower();
			}
		}
		return string.Join(" ", parts);
	}

	/// <summary>
	/// Convert CHARACTER.IRONCLAD → ironclad
	/// </summary>
	private static string NormalizeCharacter(string gameChar)
	{
		if (string.IsNullOrEmpty(gameChar)) return null;
		string id = gameChar.StartsWith("CHARACTER.", StringComparison.OrdinalIgnoreCase) && gameChar.Length > 10
			? gameChar.Substring(10)
			: gameChar;
		return id.ToLowerInvariant();
	}

	private static string FindSaveDirectory()
	{
		// Standard location: %APPDATA%/SlayTheSpire2/steam/<steamid>/profile1
		string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		string sts2Dir = Path.Combine(appData, "SlayTheSpire2", "steam");
		if (!Directory.Exists(sts2Dir)) return null;

		// Find the first steam ID subdirectory
		string[] steamDirs = Directory.GetDirectories(sts2Dir);
		if (steamDirs.Length == 0) return null;

		// Try profile1 first (main profile), then fallback to any profile
		foreach (string steamDir in steamDirs)
		{
			string profile1 = Path.Combine(steamDir, "profile1");
			if (Directory.Exists(profile1))
				return profile1;
		}
		// Fallback: try any profile directory (profile2, profile3, etc.)
		foreach (string steamDir in steamDirs)
		{
			string[] profileDirs = Directory.GetDirectories(steamDir, "profile*");
			if (profileDirs.Length > 0)
				return profileDirs[0];
		}
		return null;
	}
}
