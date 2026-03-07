using System.Collections.Generic;

namespace QuestceSpire.GameBridge;

public class CardInfo
{
	public string Id { get; set; }

	public string Name { get; set; }

	public int Cost { get; set; }

	public string Type { get; set; }

	public string Rarity { get; set; }

	public bool Upgraded { get; set; }

	public List<string> Tags { get; set; } = new List<string>();

	public int Price { get; set; }

	public float ScreenX { get; set; }

	public float ScreenY { get; set; }
}
