using System.Collections.Generic;

namespace QuestceSpire.GameBridge;

public class GameState
{
	public string Character { get; set; }

	public int ActNumber { get; set; }

	public int Floor { get; set; }

	public int CurrentHP { get; set; }

	public int MaxHP { get; set; }

	public int Gold { get; set; }

	public int AscensionLevel { get; set; }

	public List<CardInfo> DeckCards { get; set; } = new List<CardInfo>();

	public List<RelicInfo> CurrentRelics { get; set; } = new List<RelicInfo>();

	public List<CardInfo> OfferedCards { get; set; } = new List<CardInfo>();

	public List<RelicInfo> OfferedRelics { get; set; } = new List<RelicInfo>();

	public List<CardInfo> ShopCards { get; set; } = new List<CardInfo>();

	public List<RelicInfo> ShopRelics { get; set; } = new List<RelicInfo>();

	public List<CardInfo> DrawPile { get; set; } = new List<CardInfo>();

	public List<CardInfo> DiscardPile { get; set; } = new List<CardInfo>();

	public List<CardInfo> HandCards { get; set; } = new List<CardInfo>();
}
