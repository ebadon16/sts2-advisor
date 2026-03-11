using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace QuestceSpire.Core;

public class EventAdvisor
{
	private readonly Dictionary<string, EventAdviceEntry> _adviceByEventId = new(StringComparer.OrdinalIgnoreCase);

	public EventAdvisor(string dataFolder)
	{
		try
		{
			string path = Path.Combine(dataFolder, "EventAdvice", "events.json");
			if (File.Exists(path))
			{
				string json = File.ReadAllText(path);
				var data = JsonConvert.DeserializeObject<EventAdviceData>(json);
				if (data?.Events != null)
				{
					foreach (var entry in data.Events)
					{
						if (!string.IsNullOrEmpty(entry.EventId))
							_adviceByEventId[entry.EventId] = entry;
					}
				}
				Plugin.Log($"EventAdvisor loaded {_adviceByEventId.Count} event entries.");
			}
			else
			{
				Plugin.Log("EventAdvisor: events.json not found, event-specific advice unavailable.");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log($"EventAdvisor init error: {ex.Message}");
		}
	}

	public EventAdviceEntry GetAdvice(string eventId)
	{
		if (string.IsNullOrEmpty(eventId))
			return null;
		if (_adviceByEventId.TryGetValue(eventId, out var entry))
			return entry;
		// Try PascalCase → UPPER_SNAKE_CASE conversion
		string snake = Regex.Replace(eventId, @"(?<!^)([A-Z])", "_$1").ToUpperInvariant();
		if (snake != eventId && _adviceByEventId.TryGetValue(snake, out entry))
			return entry;
		// Try UPPER_SNAKE_CASE → PascalCase conversion
		if (eventId.Contains('_'))
		{
			string pascal = string.Join("", eventId.Split('_').Select(p =>
				p.Length > 0 ? char.ToUpper(p[0]) + p.Substring(1).ToLower() : ""));
			if (_adviceByEventId.TryGetValue(pascal, out entry))
				return entry;
		}
		return null;
	}

	/// <summary>
	/// Evaluates conditions against current game state and returns rated choices.
	/// </summary>
	public List<(string label, string rating, string notes)> EvaluateChoices(
		EventAdviceEntry entry, int hp, int maxHP, int gold, int deckSize, int act)
	{
		if (entry?.Choices == null)
			return null;

		float hpRatio = maxHP > 0 ? (float)hp / maxHP : 1f;
		var result = new List<(string, string, string)>();

		foreach (var choice in entry.Choices)
		{
			string rating = choice.DefaultRating ?? "situational";
			string notes = choice.Notes ?? "";

			// Check conditions — last matching condition wins
			if (choice.Conditions != null)
			{
				foreach (var cond in choice.Conditions)
				{
					if (EvaluateCondition(cond.When, hpRatio, gold, deckSize, act))
					{
						if (!string.IsNullOrEmpty(cond.Rating))
							rating = cond.Rating;
						if (!string.IsNullOrEmpty(cond.Notes))
							notes = cond.Notes;
					}
				}
			}

			result.Add((choice.Label, rating, notes));
		}

		return result;
	}

	private static bool EvaluateCondition(string when, float hpRatio, int gold, int deckSize, int act)
	{
		if (string.IsNullOrEmpty(when))
			return false;
		return when.ToLowerInvariant() switch
		{
			"low_hp" => hpRatio < 0.35f,
			"high_hp" => hpRatio > 0.70f,
			"low_gold" => gold < 50,
			"high_gold" => gold >= 200,
			"large_deck" => deckSize >= 25,
			"small_deck" => deckSize <= 15,
			"act1" => act == 1,
			"act2" => act == 2,
			"act3" => act >= 3,
			_ => false
		};
	}
}
