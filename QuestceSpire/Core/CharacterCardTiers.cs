using System.Collections.Generic;
using Newtonsoft.Json;

namespace QuestceSpire.Core;

public class CharacterCardTiers
{
	[JsonProperty("character")]
	public string Character { get; set; }

	[JsonProperty("cards")]
	public List<CardTierEntry> Cards { get; set; } = new List<CardTierEntry>();
}
