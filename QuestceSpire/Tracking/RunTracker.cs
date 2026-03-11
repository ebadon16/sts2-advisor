using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestceSpire.Core;
using QuestceSpire.GameBridge;

namespace QuestceSpire.Tracking;

public class RunTracker
{
	private RunLog _currentRun;

	private readonly List<DecisionEvent> _currentEvents = new List<DecisionEvent>();

	private string _playerId;

	private readonly RunDatabase _db;

	private readonly Dictionary<string, int> _relicAcquiredFloor = new Dictionary<string, int>();

	private readonly Dictionary<int, List<(string archetypeId, float strength)>> _archetypeHistory = new();

	public string PlayerId => _playerId;

	public bool IsRunActive => _currentRun != null;

	public string CurrentCharacter => _currentRun?.Character;

	public IReadOnlyList<DecisionEvent> GetCurrentRunEvents() => _currentEvents.AsReadOnly();

	public IReadOnlyDictionary<int, List<(string archetypeId, float strength)>> GetArchetypeHistory() => _archetypeHistory;

	public RunTracker(RunDatabase db)
	{
		_db = db;
	}

	public void Initialize(string pluginFolder)
	{
		string path = Path.Combine(pluginFolder, "player_id.txt");
		if (File.Exists(path))
		{
			_playerId = File.ReadAllText(path).Trim();
		}
		if (string.IsNullOrEmpty(_playerId))
		{
			_playerId = Guid.NewGuid().ToString();
			try
			{
				File.WriteAllText(path, _playerId);
			}
			catch (Exception ex)
			{
				Plugin.Log($"RunTracker: failed to write player_id.txt: {ex.Message}");
			}
		}
		_db.InitializeDatabase();
		Plugin.Log("RunTracker initialized. PlayerId: " + _playerId.Substring(0, Math.Min(_playerId.Length, 8)) + "...");
	}

	public void StartRun(string character, string seed, int ascensionLevel)
	{
		if (_currentRun != null)
		{
			Plugin.Log("StartRun called while a run is already active. Ending previous run as loss.");
			int prevFloor = _currentRun.FinalFloor.GetValueOrDefault();
			int prevAct = _currentRun.FinalAct.GetValueOrDefault();
			try
			{
				GameState gs = GameStateReader.ReadCurrentState();
				if (gs != null)
				{
					if (gs.Floor > 0) prevFloor = gs.Floor;
					if (gs.ActNumber > 0) prevAct = gs.ActNumber;
				}
			}
			catch (Exception ex)
			{
				Plugin.Log($"StartRun: failed to read game state for auto-end: {ex.Message}");
			}
			EndRun(RunOutcome.Loss, prevFloor, prevAct);
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
		_relicAcquiredFloor.Clear();
		_archetypeHistory.Clear();
		Plugin.Log($"Run started: {_currentRun.RunId.Substring(0, 8)}... ({character}, A{ascensionLevel})");
	}

	public void RecordDecision(DecisionEventType eventType, List<string> offeredIds, string chosenId, List<string> deckSnapshot, List<string> relicSnapshot, int hp, int maxHp, int gold, int act, int floor)
	{
		if (_currentRun == null)
		{
			string character = "unknown";
			int ascensionLevel = 0;
			try
			{
				GameState gameState = GameStateReader.ReadCurrentState();
				if (gameState != null)
				{
					character = gameState.Character;
					ascensionLevel = gameState.AscensionLevel;
				}
			}
			catch (Exception ex)
			{
				Plugin.Log($"RecordDecision: failed to infer game state for auto-start: {ex.Message}");
			}
			StartRun(character, "", ascensionLevel);
		}
		DecisionEvent item = new DecisionEvent
		{
			RunId = _currentRun.RunId,
			Floor = floor,
			Act = act,
			EventType = eventType,
			OfferedIds = (offeredIds ?? new List<string>()),
			ChosenId = chosenId,
			DeckSnapshot = (deckSnapshot ?? new List<string>()),
			RelicSnapshot = (relicSnapshot ?? new List<string>()),
			CurrentHP = hp,
			MaxHP = maxHp,
			Gold = gold,
			Timestamp = DateTime.UtcNow
		};
		_currentEvents.Add(item);
		UpdateRelicTracking(relicSnapshot ?? new List<string>(), floor);
		Plugin.Log($"Decision recorded: {eventType} on floor {floor} — chose {chosenId ?? "(skip)"}");
	}

	public void RecordArchetypeSnapshot(int floor, DeckAnalysis analysis)
	{
		if (analysis?.DetectedArchetypes == null || analysis.DetectedArchetypes.Count == 0)
			return;
		var snapshot = analysis.DetectedArchetypes
			.Take(3)
			.Select(a => (a.Archetype.Id, a.Strength))
			.ToList();
		_archetypeHistory[floor] = snapshot;
	}

	public void UpdateRelicTracking(List<string> relicIds, int floor)
	{
		foreach (string relicId in relicIds)
		{
			if (!_relicAcquiredFloor.ContainsKey(relicId))
			{
				_relicAcquiredFloor[relicId] = floor;
			}
		}
	}

	public int GetRelicTenure(string relicId, int currentFloor)
	{
		if (_relicAcquiredFloor.TryGetValue(relicId, out int acquiredFloor))
		{
			return currentFloor - acquiredFloor;
		}
		return 0;
	}

	public void UpdateLastDecisionChoice(string chosenId)
	{
		if (_currentEvents.Count > 0)
		{
			_currentEvents[_currentEvents.Count - 1].ChosenId = chosenId;
			Plugin.Log("Updated last decision with choice: " + chosenId);
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
			Plugin.Log($"Run ended: {outcome} on floor {finalFloor} (act {finalAct}). {_currentEvents.Count} decisions saved.");
		}
		catch (Exception ex)
		{
			Plugin.Log("Failed to save run to database: " + ex.Message);
		}
		_currentRun = null;
		_currentEvents.Clear();
	}
}
