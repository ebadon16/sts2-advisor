using System;
using System.Collections.Generic;

namespace QuestceSpire.Tracking;

public class DecisionEvent
{
	public string RunId { get; set; }

	public int Floor { get; set; }

	public int Act { get; set; }

	public DecisionEventType EventType { get; set; }

	public List<string> OfferedIds { get; set; } = new List<string>();

	public string ChosenId { get; set; }

	public List<string> DeckSnapshot { get; set; } = new List<string>();

	public List<string> RelicSnapshot { get; set; } = new List<string>();

	public int CurrentHP { get; set; }

	public int MaxHP { get; set; }

	public int Gold { get; set; }

	public DateTime Timestamp { get; set; }
}
