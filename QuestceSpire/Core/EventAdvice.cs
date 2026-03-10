using System.Collections.Generic;

namespace QuestceSpire.Core;

public class EventAdviceData
{
	public List<EventAdviceEntry> Events { get; set; } = new();
}

public class EventAdviceEntry
{
	public string EventId { get; set; }
	public string EventName { get; set; }
	public List<EventChoiceAdvice> Choices { get; set; } = new();
}

public class EventChoiceAdvice
{
	public string Label { get; set; }
	public string DefaultRating { get; set; } // "good", "bad", "situational"
	public List<EventCondition> Conditions { get; set; } = new();
	public string Notes { get; set; }
}

public class EventCondition
{
	public string When { get; set; } // "low_hp", "high_hp", "low_gold", "high_gold", "large_deck", "small_deck", "act1", "act2", "act3"
	public string Rating { get; set; } // overrides default when condition met
	public string Notes { get; set; }
}
