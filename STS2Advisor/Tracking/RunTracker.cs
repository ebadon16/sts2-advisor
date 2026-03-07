using System;
using System.Collections.Generic;
using System.IO;

namespace STS2Advisor.Tracking
{
    public class RunTracker
    {
        private RunLog _currentRun;
        private readonly List<DecisionEvent> _currentEvents = new List<DecisionEvent>();
        private string _playerId;
        private readonly RunDatabase _db;

        public RunTracker(RunDatabase db)
        {
            _db = db;
        }

        public void Initialize(string pluginFolder)
        {
            // Use a simple file-based player ID instead of BepInEx config
            string idPath = Path.Combine(pluginFolder, "player_id.txt");
            if (File.Exists(idPath))
            {
                _playerId = File.ReadAllText(idPath).Trim();
            }

            if (string.IsNullOrEmpty(_playerId))
            {
                _playerId = Guid.NewGuid().ToString();
                try { File.WriteAllText(idPath, _playerId); }
                catch { /* Non-fatal: player ID won't persist across sessions */ }
            }

            _db.InitializeDatabase();
            Plugin.Log($"RunTracker initialized. PlayerId: {_playerId.Substring(0, Math.Min(_playerId.Length, 8))}...");
        }

        public bool IsRunActive => _currentRun != null;

        public void StartRun(string character, string seed, int ascensionLevel)
        {
            if (_currentRun != null)
            {
                Plugin.Log("StartRun called while a run is already active. Ending previous run as loss.");
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
            Plugin.Log($"Run started: {_currentRun.RunId.Substring(0, 8)}... ({character}, A{ascensionLevel})");
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
            int act,
            int floor)
        {
            if (_currentRun == null)
            {
                // Auto-start a run on first decision since we don't have a run-start hook yet
                string character = "unknown";
                try
                {
                    var state = GameBridge.GameStateReader.ReadCurrentState();
                    if (state != null)
                        character = state.Character;
                }
                catch { }
                StartRun(character, "", 0);
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
            Plugin.Log($"Decision recorded: {eventType} on floor {floor} — chose {chosenId ?? "(skip)"}");
        }

        public void UpdateLastDecisionChoice(string chosenId)
        {
            if (_currentEvents.Count > 0)
            {
                _currentEvents[_currentEvents.Count - 1].ChosenId = chosenId;
                Plugin.Log($"Updated last decision with choice: {chosenId}");
            }
        }

        public void EndRun(RunOutcome outcome, int finalFloor, int finalAct)
        {
            if (_currentRun == null)
            {
                Plugin.Log("EndRun called with no active run. Ignoring.");
                return;
            }

            _currentRun.EndTime = DateTime.UtcNow;
            _currentRun.Outcome = outcome;
            _currentRun.FinalFloor = finalFloor;
            _currentRun.FinalAct = finalAct;

            try
            {
                _db.SaveRun(_currentRun, _currentEvents);
                Plugin.Log($"Run ended: {outcome} on floor {finalFloor} (act {finalAct}). " +
                           $"{_currentEvents.Count} decisions saved.");
            }
            catch (Exception ex)
            {
                Plugin.Log($"Failed to save run to database: {ex.Message}");
            }

            _currentRun = null;
            _currentEvents.Clear();
        }
    }
}
