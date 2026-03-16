using System.Collections.Generic;
using Newtonsoft.Json;

namespace QuestceSpire.Core;

public class PotionTierEntry
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("baseTier")]
	public string BaseTier { get; set; }

	[JsonProperty("synergies")]
	public List<string> Synergies { get; set; } = new List<string>();

	[JsonProperty("notes")]
	public string Notes { get; set; }
}

public class PotionTierFile
{
	[JsonProperty("category")]
	public string Category { get; set; }

	[JsonProperty("potions")]
	public List<PotionTierEntry> Potions { get; set; } = new List<PotionTierEntry>();
}
