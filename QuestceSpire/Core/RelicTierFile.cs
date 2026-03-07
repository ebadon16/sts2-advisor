using System.Collections.Generic;
using Newtonsoft.Json;

namespace QuestceSpire.Core;

public class RelicTierFile
{
	[JsonProperty("category")]
	public string Category { get; set; }

	[JsonProperty("relics")]
	public List<RelicTierEntry> Relics { get; set; } = new List<RelicTierEntry>();
}
