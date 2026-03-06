using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace STS2Advisor.Tracking
{
    public class RunTracker
    {
        private static RunTracker _instance;
        public static RunTracker Instance => _instance ?? (_instance = new RunTracker());

        private RunLog _currentRun;
        private readonly List<DecisionEvent> _currentEvents = new List<DecisionEvent>();
        private string _playerId;

        private RunTracker() { }

        /// <summary>
        /// Initializes the tracker with a persistent anonymous player ID.
        /// Call once during plugin startup.
        /// </summary>
        public void Initialize(ConfigFile config)
        {
            var playerIdEntry = config.Bind(
                "Tracking",
                "PlayerId",
                "",
                "Anonymous player ID for community stats. Auto-generated, do not edit."
            );

            if (string.IsNullOrEmpty(playerIdEntry.Value))
            {
                playerIdEntry.Value = Guid.NewGuid().ToString();
            }

            _playerId = playerIdEntry.Value;

            RunDatabase.Instance.InitializeDatabase();
            Plugin.Log.LogInfo($"RunTracker initialized. PlayerId: {_playerId.Substring(0, 8)}...");
        }

        public bool IsRunActive => _currentRun != null;

        public void StartRun(string character, string seed, int ascensionLevel)
        {
            if (_currentRun != null)
            {
                Plugin.Log.LogWarning("StartRun called while a run is already active. Ending previous run as loss.");
                EndRun(RunOutcome.Loss, _currentRun.FinalFloor ?? 0, _currentRun.FinalAct ?? 0);
            }

            _currentRun = new RunLog
            {
                RunId = Guid.NewGuid().ToString(),
                PlayerId = _playerId,
                Character = character,
                Seed = seed,
                StartTime = DateTime.UtcNow,
                AscensionLevel = ascensionLevel,
                Synced = false
            };

            _currentEvents.Clear();
            Plugin.Log.LogInfo($"Run started: {_currentRun.RunId.Substring(0, 8)}... ({character}, A{ascensionLevel})");
        }

        public void RecordDecision(
            DecisionEventType eventType,
            List<string> offeredIds,
            string chosenId,
            List<string> deckSnapshot,
            List<string> relicSnapshot,
            int hp,
            int maxHp,
            int gold,
            int floor,
            int act)
        {
            if (_currentRun == null)
            {
                Plugin.Log.LogWarning("RecordDecision called with no active run. Ignoring.");
                return;
            }

            var decision = new DecisionEvent
            {
                RunId = _currentRun.RunId,
                Floor = floor,
                Act = act,
                EventType = eventType,
                OfferedIds = offeredIds ?? new List<string>(),
                ChosenId = chosenId,
                DeckSnapshot = deckSnapshot ?? new List<string>(),
                RelicSnapshot = relicSnapshot ?? new List<string>(),
                CurrentHP = hp,
                MaxHP = maxHp,
                Gold = gold,
                Timestamp = DateTime.UtcNow
            };

            _currentEvents.Add(decision);
            Plugin.Log.LogInfo($"Decision recorded: {eventType} on floor {floor} — chose {chosenId ?? "(skip)"}");
        }

        public void EndRun(RunOutcome outcome, int finalFloor, int finalAct)
        {
            if (_currentRun == null)
            {
                Plugin.Log.LogWarning("EndRun called with no active run. Ignoring.");
                return;
            }

            _currentRun.EndTime = DateTime.UtcNow;
            _currentRun.Outcome = outcome;
            _currentRun.FinalFloor = finalFloor;
            _currentRun.FinalAct = finalAct;

            try
            {
                RunDatabase.Instance.SaveRun(_currentRun, _currentEvents);
                Plugin.Log.LogInfo($"Run ended: {outcome} on floor {finalFloor} (act {finalAct}). " +
                                   $"{_currentEvents.Count} decisions saved.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to save run to database: {ex.Message}");
            }

            // Queue sync on background thread
            try
            {
                SyncClient.Instance.QueueSync();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to queue sync: {ex.Message}");
            }

            _currentRun = null;
            _currentEvents.Clear();
        }
    }
}
