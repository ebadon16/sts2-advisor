using System.Collections.Generic;

namespace QuestceSpire.Core;

public class CardPropertyData
{
	public string Id;
	public string Character;
	public string Type;
	public string Rarity;
	public int EnergyCost;
	public int TargetType;
	public List<string> Keywords;
	public List<string> Tags;
	public Dictionary<string, int> DynamicVars;
}
