using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
							_tipsByEnemyId[entry.EnemyId] = entry;
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

	public List<EnemyTipEntry> GetTips(List<string> enemyIds)
	{
		if (enemyIds == null || enemyIds.Count == 0)
			return null;

		var result = new List<EnemyTipEntry>();
		foreach (var id in enemyIds)
		{
			if (_tipsByEnemyId.TryGetValue(id, out var entry))
				result.Add(entry);
		}

		return result.Count > 0 ? result.OrderByDescending(e => e.Priority).ToList() : null;
	}
}
