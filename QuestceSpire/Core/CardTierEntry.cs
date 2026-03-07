using System.Collections.Generic;
using Newtonsoft.Json;

namespace QuestceSpire.Core;

public class CardTierEntry
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("baseTier")]
	public string BaseTier { get; set; }

	[JsonProperty("synergies")]
	public List<string> Synergies { get; set; } = new List<string>();

	[JsonProperty("antiSynergies")]
	public List<string> AntiSynergies { get; set; } = new List<string>();

	[JsonProperty("notes")]
	public string Notes { get; set; }
}
