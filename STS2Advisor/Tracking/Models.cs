using System;
using System.Collections.Generic;

namespace STS2Advisor.Tracking
{
    public enum DecisionEventType
    {
        CardReward,
        RelicReward,
        BossRelic,
        Shop,
        CardRemove,
        CardTransform
    }

    public enum RunOutcome
    {
        Win,
        Loss
    }

    public class RunLog
    {
        public string RunId { get; set; }
        public string PlayerId { get; set; }
        public string Character { get; set; }
        public string Seed { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public RunOutcome? Outcome { get; set; }
        public int? FinalFloor { get; set; }
        public int? FinalAct { get; set; }
        public int AscensionLevel { get; set; }
        public bool Synced { get; set; }
    }

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

    public class CommunityCardStats
    {
        public string CardId { get; set; }
        public string Character { get; set; }
        public float PickRate { get; set; }
        public float WinRateWhenPicked { get; set; }
        public float WinRateWhenSkipped { get; set; }
        public int SampleSize { get; set; }
        public float AvgFloorPicked { get; set; }
        public Dictionary<string, float> ArchetypeContext { get; set; } = new Dictionary<string, float>();
    }

    public class CommunityRelicStats
    {
        public string RelicId { get; set; }
        public string Character { get; set; }
        public float PickRate { get; set; }
        public float WinRateWhenPicked { get; set; }
        public float WinRateWhenSkipped { get; set; }
        public int SampleSize { get; set; }
        public float AvgFloorPicked { get; set; }
        public Dictionary<string, float> ArchetypeContext { get; set; } = new Dictionary<string, float>();
    }
}
