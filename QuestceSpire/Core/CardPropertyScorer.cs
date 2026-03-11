using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuestceSpire.Core;

public class CardPropertyScorer
{
	private readonly Dictionary<string, CardPropertyData> _cards = new(StringComparer.OrdinalIgnoreCase);

	// Dynamic var names that indicate scaling / multi-hit
	private static readonly HashSet<string> ScalingVars = new(StringComparer.OrdinalIgnoreCase)
	{
		"CalculatedHits", "Shivs", "CalculatedShivs", "PoisonPerTurn",
		"Increase", "CalculatedChannels", "CalculatedFocus",
		"StarsPerTurn", "CalculatedCards", "CalculatedForge",
		"CalculatedEnergy", "CalculatedDoom"
	};

	public int CardCount => _cards.Count;

	public CardPropertyScorer(string dataFolder)
	{
		LoadCards(Path.Combine(dataFolder, "all_cards_full.tsv"));
		LoadDynamicVars(Path.Combine(dataFolder, "card_dynamic_vars.tsv"));
		Plugin.Log($"CardPropertyScorer loaded {_cards.Count} card properties.");
	}

	private void LoadCards(string path)
	{
		if (!File.Exists(path))
		{
			Plugin.Log($"CardPropertyScorer: missing {path}");
			return;
		}
		foreach (string line in File.ReadLines(path))
		{
			if (line.StartsWith("Entry")) continue; // header
			string[] cols = line.Split('\t');
			if (cols.Length < 8) continue;

			string id = NormalizeId(cols[0]);
			var card = new CardPropertyData
			{
				Id = id,
				Character = cols[2]?.ToLowerInvariant() ?? "",
				Type = cols[3] ?? "",
				Rarity = cols[4] ?? "",
				EnergyCost = int.TryParse(cols[5], out int cost) ? cost : 1,
				TargetType = int.TryParse(cols[6], out int tt) ? tt : 0,
				Keywords = ParseList(cols.Length > 7 ? cols[7] : ""),
				Tags = ParseList(cols.Length > 8 ? cols[8] : ""),
				DynamicVars = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
			};
			_cards[id] = card;
		}
	}

	private void LoadDynamicVars(string path)
	{
		if (!File.Exists(path))
		{
			Plugin.Log($"CardPropertyScorer: missing {path}");
			return;
		}
		foreach (string line in File.ReadLines(path))
		{
			if (line.StartsWith("Entry")) continue;
			string[] cols = line.Split('\t');
			if (cols.Length < 4) continue;

			string id = NormalizeId(cols[0]);
			string varName = cols[2];
			int value = int.TryParse(cols[3], out int v) ? v : 0;

			if (_cards.TryGetValue(id, out var card))
			{
				card.DynamicVars[varName] = value;
			}
		}
	}

	public ComputedCardScore ComputeScore(string cardId)
	{
		string normId = NormalizeId(cardId);
		if (!_cards.TryGetValue(normId, out var card))
		{
			return new ComputedCardScore { Score = 2.0f, SynergyTags = null };
		}
		return ComputeScoreFromData(card);
	}

	private ComputedCardScore ComputeScoreFromData(CardPropertyData card)
	{
		// Rarity base
		float score = card.Rarity switch
		{
			"Common" => 1.5f,
			"Uncommon" => 2.5f,
			"Rare" => 3.5f,
			_ => 2.0f
		};

		// Cost efficiency
		if (card.EnergyCost == 0) score += 0.3f;
		else if (card.EnergyCost == 1) score += 0.1f;
		else if (card.EnergyCost >= 4) score -= 0.3f;

		// Type bonus
		if (card.Type == "Power") score += 0.3f;

		// Keyword adjustments
		foreach (string kw in card.Keywords)
		{
			switch (kw)
			{
				case "Innate": score += 0.2f; break;
				case "Retain": score += 0.15f; break;
				case "Sly": score += 0.1f; break;
				case "Exhaust": score -= 0.1f; break;
				case "Ethereal": score -= 0.15f; break;
			}
		}

		// AoE
		if (card.TargetType == 3) score += 0.2f;

		// Dynamic vars — scaling bonus
		int scalingCount = 0;
		foreach (var kv in card.DynamicVars)
		{
			if (ScalingVars.Contains(kv.Key))
				scalingCount++;
		}
		score += scalingCount * 0.2f;

		score = Math.Max(0f, Math.Min(5f, score));

		// Generate synergy tags
		var tags = GenerateSynergyTags(card);

		return new ComputedCardScore { Score = score, SynergyTags = tags };
	}

	private List<string> GenerateSynergyTags(CardPropertyData card)
	{
		var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (string kw in card.Keywords)
		{
			if (kw == "Exhaust") tagSet.Add("exhaust");
			if (kw == "Sly" && card.Character == "silent") tagSet.Add("discard");
		}

		foreach (string tag in card.Tags)
		{
			string lower = tag.ToLowerInvariant();
			if (lower == "shiv") tagSet.Add("shiv");
			if (lower == "strike") tagSet.Add("strike");
		}

		if (card.EnergyCost == 0) tagSet.Add("zero_cost");
		if (card.TargetType == 3) tagSet.Add("aoe");
		if (card.Type == "Power") tagSet.Add("scaling");

		foreach (var kv in card.DynamicVars)
		{
			string name = kv.Key;
			if (name == "Shivs" || name == "CalculatedShivs")
			{
				tagSet.Add("shiv");
				tagSet.Add("shiv_synergy");
			}
			else if (name == "PoisonPerTurn" || name == "Poison")
			{
				tagSet.Add("poison");
			}
			else if (name == "CalculatedHits")
			{
				tagSet.Add("multi_hit");
			}
			else if (name == "StrengthLoss")
			{
				tagSet.Add("debuff");
				tagSet.Add("weak");
			}
			else if (name == "OrbSlots" || name == "CalculatedChannels" || name == "CalculatedFocus")
			{
				tagSet.Add("orb");
			}
			else if (name == "BlockForStars" || name == "StarsPerTurn")
			{
				tagSet.Add("stellar");
			}
		}

		return tagSet.Count > 0 ? tagSet.ToList() : null;
	}

	private static List<string> ParseList(string csv)
	{
		var list = new List<string>();
		if (string.IsNullOrWhiteSpace(csv)) return list;
		foreach (string item in csv.Split(','))
		{
			string trimmed = item.Trim();
			if (trimmed.Length > 0 && !list.Contains(trimmed))
				list.Add(trimmed);
		}
		return list;
	}

	private static string NormalizeId(string id)
	{
		if (string.IsNullOrEmpty(id)) return "";
		// Strip upgrade suffix before normalizing
		if (id.EndsWith("+")) id = id.Substring(0, id.Length - 1);
		return id.Replace(" ", "_").Replace("-", "_").Replace("'", "");
	}
}

public class ComputedCardScore
{
	public float Score;
	public List<string> SynergyTags;
}
