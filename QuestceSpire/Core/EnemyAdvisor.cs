using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace QuestceSpire.Core;

public class EnemyAdvisor
{
	private readonly Dictionary<string, EnemyTipEntry> _tipsByEnemyId = new(StringComparer.OrdinalIgnoreCase);

	public EnemyAdvisor(string dataFolder)
	{
		try
		{
			string path = Path.Combine(dataFolder, "EnemyTips", "enemies.json");
			if (File.Exists(path))
			{
				string json = File.ReadAllText(path);
				var data = JsonConvert.DeserializeObject<EnemyTipData>(json);
				if (data?.Enemies != null)
				{
					foreach (var entry in data.Enemies)
					{
						if (!string.IsNullOrEmpty(entry.EnemyId))
						{
							_tipsByEnemyId[entry.EnemyId] = entry;
							// Also index by UPPER_SNAKE_CASE, PascalCase, and Title Case variants
							string snake = PascalToSnake(entry.EnemyId);
							if (snake != entry.EnemyId)
								_tipsByEnemyId.TryAdd(snake, entry);
							string pascal = SnakeToPascal(entry.EnemyId);
							if (pascal != entry.EnemyId)
								_tipsByEnemyId.TryAdd(pascal, entry);
							// Title Case with spaces (game's EncounterModel.Id.Entry format)
							string titleCase = entry.EnemyId.Replace("_", " ");
							if (titleCase != entry.EnemyId)
								_tipsByEnemyId.TryAdd(titleCase, entry);
						}
					}
				}
				Plugin.Log($"EnemyAdvisor loaded {_tipsByEnemyId.Count} enemy entries.");
			}
			else
			{
				Plugin.Log("EnemyAdvisor: enemies.json not found, enemy-specific tips unavailable.");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log($"EnemyAdvisor init error: {ex.Message}");
		}
	}

	/// <summary>PascalCase → UPPER_SNAKE_CASE (e.g., ShrinkerBeetleWeak → SHRINKER_BEETLE_WEAK)</summary>
	private static string PascalToSnake(string pascal)
	{
		return Regex.Replace(pascal, @"(?<!^)([A-Z])", "_$1").ToUpperInvariant();
	}

	/// <summary>UPPER_SNAKE_CASE → PascalCase (e.g., SHRINKER_BEETLE_WEAK → ShrinkerBeetleWeak)</summary>
	private static string SnakeToPascal(string snake)
	{
		if (!snake.Contains('_')) return snake;
		return string.Join("", snake.Split('_').Select(part =>
			part.Length > 0 ? char.ToUpper(part[0]) + part.Substring(1).ToLower() : ""));
	}

	public List<EnemyTipEntry> GetTips(List<string> enemyIds)
	{
		if (enemyIds == null || enemyIds.Count == 0)
			return null;

		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var result = new List<EnemyTipEntry>();
		foreach (var id in enemyIds)
		{
			if (seen.Contains(id)) continue;
			seen.Add(id);
			// Try exact match first, then strip variant suffixes (_WEAK, _ELITE, etc.)
			if (_tipsByEnemyId.TryGetValue(id, out var entry))
				result.Add(entry);
			else
			{
				string baseId = System.Text.RegularExpressions.Regex.Replace(id, @"_(WEAK|ELITE|STRONG|BOSS|HARD|EASY)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
				if (baseId != id && _tipsByEnemyId.TryGetValue(baseId, out var baseEntry) && !seen.Contains(baseId))
				{
					seen.Add(baseId);
					result.Add(baseEntry);
				}
			}
		}

		return result.Count > 0 ? result.OrderByDescending(e => e.Priority).ToList() : null;
	}
}
