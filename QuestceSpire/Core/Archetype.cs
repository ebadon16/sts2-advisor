using System.Collections.Generic;

namespace QuestceSpire.Core;

public class Archetype
{
	public string Id { get; set; }

	public string DisplayName { get; set; }

	public List<string> CoreTags { get; set; } = new List<string>();

	public List<string> SupportTags { get; set; } = new List<string>();

	public int CoreThreshold { get; set; } = 3;

	public int SupportThreshold { get; set; } = 2;
}
