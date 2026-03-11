using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace QuestceSpire.Core;

public class TierEngine
{
	private readonly Dictionary<string, CharacterCardTiers> _cardTiers = new Dictionary<string, CharacterCardTiers>();

	private readonly Dictionary<string, RelicTierFile> _relicTiers = new Dictionary<string, RelicTierFile>();

	// Pre-indexed lookups: character -> normalizedId -> entry
	private readonly Dictionary<string, Dictionary<string, CardTierEntry>> _cardIndex = new Dictionary<string, Dictionary<string, CardTierEntry>>();
	private readonly Dictionary<string, Dictionary<string, RelicTierEntry>> _relicIndex = new Dictionary<string, Dictionary<string, RelicTierEntry>>();

	public TierEngine(string dataPath)
	{
		LoadCardTiers(Path.Combine(dataPath, "CardTiers"));
		LoadRelicTiers(Path.Combine(dataPath, "RelicTiers"));
	}

	private void LoadCardTiers(string folder)
	{
		if (!Directory.Exists(folder))
		{
			Plugin.Log("Card tier folder not found: " + folder);
			return;
		}
		string[] files = Directory.GetFiles(folder, "*.json");
		foreach (string text in files)
		{
			try
			{
				CharacterCardTiers characterCardTiers = JsonConvert.DeserializeObject<CharacterCardTiers>(File.ReadAllText(text));
				if (characterCardTiers != null && characterCardTiers.Character != null)
				{
					string charKey = characterCardTiers.Character.ToLowerInvariant();
					_cardTiers[charKey] = characterCardTiers;
					var index = new Dictionary<string, CardTierEntry>(StringComparer.OrdinalIgnoreCase);
					if (characterCardTiers.Cards != null)
					{
						foreach (var card in characterCardTiers.Cards)
						{
							string normId = NormalizeId(card.Id);
							if (!string.IsNullOrEmpty(normId))
								index[normId] = card;
						}
					}
					_cardIndex[charKey] = index;
					Plugin.Log($"Loaded {characterCardTiers.Cards?.Count ?? 0} card tiers for {characterCardTiers.Character}");
				}
			}
			catch (Exception ex)
			{
				Plugin.Log("Failed to load card tiers from " + text + ": " + ex.Message);
			}
		}
	}

	private void LoadRelicTiers(string folder)
	{
		if (!Directory.Exists(folder))
		{
			Plugin.Log("Relic tier folder not found: " + folder);
			return;
		}
		string[] files = Directory.GetFiles(folder, "*.json");
		foreach (string text in files)
		{
			try
			{
				RelicTierFile relicTierFile = JsonConvert.DeserializeObject<RelicTierFile>(File.ReadAllText(text));
				if (relicTierFile != null && relicTierFile.Category != null)
				{
					string catKey = relicTierFile.Category.ToLowerInvariant();
					_relicTiers[catKey] = relicTierFile;
					var index = new Dictionary<string, RelicTierEntry>(StringComparer.OrdinalIgnoreCase);
					if (relicTierFile.Relics != null)
					{
						foreach (var relic in relicTierFile.Relics)
						{
							string normId = NormalizeId(relic.Id);
							if (!string.IsNullOrEmpty(normId))
								index[normId] = relic;
						}
					}
					_relicIndex[catKey] = index;
					Plugin.Log($"Loaded {relicTierFile.Relics?.Count ?? 0} relic tiers for {relicTierFile.Category}");
				}
			}
			catch (Exception ex)
			{
				Plugin.Log("Failed to load relic tiers from " + text + ": " + ex.Message);
			}
		}
	}

	private static string NormalizeId(string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return "";
		}
		return id.Replace(' ', '_').Replace('-', '_').Replace("'", "").TrimEnd('+');
	}

	public CardTierEntry GetCardTier(string character, string cardId)
	{
		string normId = NormalizeId(cardId);
		string charKey = character?.ToLowerInvariant();
		if (charKey != null && _cardIndex.TryGetValue(charKey, out var charIdx) && charIdx.TryGetValue(normId, out var entry))
		{
			return entry;
		}
		if (_cardIndex.TryGetValue("colorless", out var colorlessIdx) && colorlessIdx.TryGetValue(normId, out var colorlessEntry))
		{
			return colorlessEntry;
		}
		return null;
	}

	public RelicTierEntry GetRelicTier(string character, string relicId)
	{
		string normId = NormalizeId(relicId);
		string charKey = character?.ToLowerInvariant();
		if (charKey != null && _relicIndex.TryGetValue(charKey, out var charIdx) && charIdx.TryGetValue(normId, out var entry))
		{
			return entry;
		}
		if (_relicIndex.TryGetValue("common", out var commonIdx) && commonIdx.TryGetValue(normId, out var commonEntry))
		{
			return commonEntry;
		}
		return null;
	}

	public static TierGrade ParseGrade(string grade)
	{
		if (string.IsNullOrEmpty(grade))
		{
			return TierGrade.C;
		}
		return grade.Trim().ToUpperInvariant() switch
		{
			"S" => TierGrade.S, 
			"A" => TierGrade.A, 
			"B" => TierGrade.B, 
			"C" => TierGrade.C, 
			"D" => TierGrade.D, 
			"F" => TierGrade.F, 
			_ => TierGrade.C, 
		};
	}

	public static TierGrade ScoreToGrade(float score)
	{
		if (score >= 4.5f)
		{
			return TierGrade.S;
		}
		if (score >= 3.5f)
		{
			return TierGrade.A;
		}
		if (score >= 2.5f)
		{
			return TierGrade.B;
		}
		if (score >= 1.5f)
		{
			return TierGrade.C;
		}
		if (score >= 0.5f)
		{
			return TierGrade.D;
		}
		return TierGrade.F;
	}

	/// <summary>
	/// Returns a sub-grade string (e.g. "A+", "B-") for finer differentiation.
	/// Each major grade spans 1.0 points; +/- split the top/bottom third.
	/// </summary>
	public static string ScoreToSubGrade(float score)
	{
		if (score >= 5.2f) return "S+";
		if (score >= 4.8f) return "S";
		if (score >= 4.5f) return "S-";
		if (score >= 4.17f) return "A+";
		if (score >= 3.83f) return "A";
		if (score >= 3.5f) return "A-";
		if (score >= 3.17f) return "B+";
		if (score >= 2.83f) return "B";
		if (score >= 2.5f) return "B-";
		if (score >= 2.17f) return "C+";
		if (score >= 1.83f) return "C";
		if (score >= 1.5f) return "C-";
		if (score >= 1.0f) return "D+";
		if (score >= 0.5f) return "D";
		return "F";
	}
}
