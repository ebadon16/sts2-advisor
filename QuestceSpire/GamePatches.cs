using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using QuestceSpire.Core;
using QuestceSpire.GameBridge;
using QuestceSpire.Tracking;
using QuestceSpire.UI;

namespace QuestceSpire;

public static class GamePatches
{
	// Track the screen instance that ShowScreen was called on, so _Ready/retry
	// don't inject badges on unrelated screens (discard pile, removal, etc.)
	private static WeakReference<NCardRewardSelectionScreen> _activeCardRewardScreen;

	// Dedup: track last recorded card reward to prevent ShowScreen+RefreshOptions double-recording
	private static string _lastCardRewardFingerprint;

	// Debug: track when each hook last fired
	public static Dictionary<string, DateTime> HookLastFired { get; } = new();

	private static void RecordHook(string hookName)
	{
		HookLastFired[hookName] = DateTime.Now;
	}

	private static void EnsureOverlay()
	{
		if (Plugin.Overlay == null)
		{
			try
			{
				Plugin.Overlay = new OverlayManager();
				Plugin.Log("Overlay created.");
			}
			catch (Exception value)
			{
				Plugin.Log($"Overlay creation failed: {value}");
			}
		}
	}

	public static void OnCardRewardScreenReady(NCardRewardSelectionScreen __instance)
	{
		EnsureOverlay();
		// Only retry for screens that ShowScreen was called on (not discard pile, etc.)
		if (_activeCardRewardScreen == null || !_activeCardRewardScreen.TryGetTarget(out var active) || active != __instance)
			return;
		ScheduleRetry(() => TryShowCardRewardFromScreen(__instance), 5, 0.3);
	}

	public static void OnCardRewardOpened(NCardRewardSelectionScreen __result, IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> extraOptions)
	{
		try
		{
			// Always clean up existing badges first — prevents stale badges on pile viewers, etc.
			Plugin.Overlay?.CleanupAllBadges();
			// Draw/discard pile viewers also use ShowScreen but with many cards — skip those
			if (options != null && options.Count > 5)
			{
				Plugin.Log($"ShowScreen with {options.Count} cards — likely pile viewer, skipping.");
				return;
			}
			// Only proceed if overlay is idle or already on card reward — any other active screen
			// means this ShowScreen call is reused for a non-reward purpose (removal, upgrade, shop, event, etc.)
			string curScreen = Plugin.Overlay?.CurrentScreen;
			if (curScreen != null && curScreen != "IDLE" && curScreen != "CARD REWARD")
			{
				Plugin.Log($"ShowScreen fired during {curScreen} — skipping card reward logic.");
				return;
			}
			EnsureOverlay();
			_activeCardRewardScreen = new WeakReference<NCardRewardSelectionScreen>(__result);
			Plugin.Log("Card reward screen detected — analyzing...");
			RecordHook("OnCardRewardOpened");
			if (options != null)
				GameStateReader._lastCardOptions = options;
			GameStateReader._lastRelicOptions = null;
			GameStateReader._lastMerchantInventory = null;
			if (!TryShowCardRewardFromScreen(__result))
			{
				Plugin.Log("Game state not ready for card reward, scheduling retry...");
				ScheduleRetry(() => TryShowCardRewardFromScreen(__result));
			}
		}
		catch (Exception value)
		{
			Plugin.Log($"OnCardRewardOpened error: {value}");
		}
	}

	public static void OnCardRewardRefreshed(NCardRewardSelectionScreen __instance, IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> extraOptions)
	{
		try
		{
			if (options != null && options.Count > 5)
				return;
			string curScreen2 = Plugin.Overlay?.CurrentScreen;
			if (curScreen2 != null && curScreen2 != "IDLE" && curScreen2 != "CARD REWARD")
				return;
			EnsureOverlay();
			RecordHook("OnCardRewardRefreshed");
			_activeCardRewardScreen = new WeakReference<NCardRewardSelectionScreen>(__instance);
			Plugin.Log("Card reward RefreshOptions detected — re-analyzing...");
			if (options != null)
				GameStateReader._lastCardOptions = options;
			GameStateReader._lastRelicOptions = null;
			GameStateReader._lastMerchantInventory = null;
			if (!TryShowCardRewardFromScreen(__instance))
			{
				Plugin.Log("Game state not ready for card refresh, scheduling retry...");
				ScheduleRetry(() => TryShowCardRewardFromScreen(__instance));
			}
		}
		catch (Exception value)
		{
			Plugin.Log($"OnCardRewardRefreshed error: {value}");
		}
	}

	private static bool TryShowCardRewardFromScreen(NCardRewardSelectionScreen screen)
	{
		// Guard: only proceed if we're still on a card reward screen (retries may fire after screen changed)
		string curScreen = Plugin.Overlay?.CurrentScreen;
		if (curScreen != null && curScreen != "CARD REWARD" && curScreen != "IDLE")
		{
			Plugin.Log($"TryShowCardRewardFromScreen skipped — screen is now {curScreen}");
			return true; // return true to stop retries
		}
		// Guard: verify the screen node is still valid and visible
		if (screen == null || !GodotObject.IsInstanceValid(screen) || !screen.IsVisibleInTree())
		{
			Plugin.Log("TryShowCardRewardFromScreen skipped — screen no longer valid/visible");
			return true;
		}
		// Try reading card options from the screen instance via reflection
		// This works even when Harmony parameter injection fails
		if (GameStateReader._lastCardOptions == null || GameStateReader._lastCardOptions.Count == 0)
		{
			try
			{
				var cardsField = screen.GetType().GetField("_cards",
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				var cardsProp = screen.GetType().GetProperty("Cards",
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				object cardsObj = cardsField?.GetValue(screen) ?? cardsProp?.GetValue(screen);
				if (cardsObj is IReadOnlyList<CardCreationResult> screenCards && screenCards.Count > 0)
				{
					Plugin.Log($"Read {screenCards.Count} cards from screen instance");
					GameStateReader._lastCardOptions = screenCards;
				}
				else
				{
					// Try getting card holders and extracting cards from children
					var holdersField = screen.GetType().GetField("_cardHolders",
						BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
					if (holdersField != null)
					{
						var holders = holdersField.GetValue(screen);
						if (holders is System.Collections.IList holderList && holderList.Count > 0)
						{
							var results = new List<CardCreationResult>();
							foreach (var holder in holderList)
							{
								var crProp = holder.GetType().GetProperty("CreationResult",
									BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
								if (crProp?.GetValue(holder) is CardCreationResult cr)
									results.Add(cr);
							}
							if (results.Count > 0)
							{
								Plugin.Log($"Read {results.Count} cards from card holders");
								GameStateReader._lastCardOptions = results;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log($"Failed to read cards from screen: {ex.Message}");
			}
		}

		GameState gameState = GameStateReader.ReadCurrentState();
		if (gameState == null) return false;
		if (gameState.OfferedCards == null || gameState.OfferedCards.Count == 0)
		{
			Plugin.Log("Card reward: no offered cards in game state");
			return false;
		}

		DeckAnalysis deckAnalysis = Plugin.DeckAnalyzer.Analyze(gameState.Character, gameState.DeckCards, Plugin.TierEngine);
		Plugin.RunTracker?.RecordArchetypeSnapshot(gameState.Floor, deckAnalysis);
		List<ScoredCard> cards = Plugin.SynergyScorer.ScoreOfferings(gameState.OfferedCards, deckAnalysis, gameState.Character, gameState.ActNumber, gameState.Floor, Plugin.TierEngine, Plugin.AdaptiveScorer);
		Plugin.Overlay?.ShowCardAdvice(cards, deckAnalysis, gameState.Character);
		Plugin.Overlay?.InjectCardGrades(screen, cards);
		// Dedup: only record if this is a new offering (prevents ShowScreen+RefreshOptions double-recording)
		var offeredIds = gameState.OfferedCards.ConvertAll((CardInfo c) => c.Id);
		offeredIds.Sort();
		string fingerprint = $"{gameState.Floor}:{string.Join(",", offeredIds)}";
		if (fingerprint != _lastCardRewardFingerprint)
		{
			_lastCardRewardFingerprint = fingerprint;
			Plugin.RunTracker?.RecordDecision(DecisionEventType.CardReward, gameState.OfferedCards.ConvertAll((CardInfo c) => c.Id), null, gameState.DeckCards.ConvertAll((CardInfo c) => c.Id), gameState.CurrentRelics.ConvertAll((RelicInfo r) => r.Id), gameState.CurrentHP, gameState.MaxHP, gameState.Gold, gameState.ActNumber, gameState.Floor);
		}
		return true;
	}

	private static void ScheduleRetry(Func<bool> action, int retriesLeft = 3, double delay = 0.2)
	{
		try
		{
			var tree = (SceneTree)Engine.GetMainLoop();
			if (tree == null) return;
			var timer = tree.CreateTimer(delay);
			timer.Timeout += () =>
			{
				try
				{
					if (!action() && retriesLeft > 1)
					{
						Plugin.Log($"Retry failed, {retriesLeft - 1} attempts remaining...");
						ScheduleRetry(action, retriesLeft - 1, delay);
					}
				}
				catch (Exception ex)
				{
					Plugin.Log($"Retry error: {ex.Message}");
				}
			};
		}
		catch (Exception ex)
		{
			Plugin.Log($"ScheduleRetry error: {ex.Message}");
		}
	}

	public static void OnRelicRewardOpened(NChooseARelicSelection __result, IReadOnlyList<RelicModel> relics)
	{
		try
		{
			EnsureOverlay();
			Plugin.Log("Relic reward screen detected — analyzing...");
			RecordHook("OnRelicRewardOpened");
			GameStateReader._lastCardOptions = null;
			GameStateReader._lastRelicOptions = relics;
			GameStateReader._lastMerchantInventory = null;
			GameState gameState = GameStateReader.ReadCurrentState();
			if (gameState != null)
			{
				DeckAnalysis deckAnalysis = Plugin.DeckAnalyzer.Analyze(gameState.Character, gameState.DeckCards, Plugin.TierEngine);
				Plugin.RunTracker?.RecordArchetypeSnapshot(gameState.Floor, deckAnalysis);
				List<ScoredRelic> relics2 = Plugin.SynergyScorer.ScoreRelicOfferings(gameState.OfferedRelics, deckAnalysis, gameState.Character, gameState.ActNumber, gameState.Floor, Plugin.TierEngine, Plugin.AdaptiveScorer);
				Plugin.Overlay?.ShowRelicAdvice(relics2, deckAnalysis, gameState.Character);
				// Inject grade badges directly onto game relic nodes
				// Relic badge injection removed — overlay panel handles relic screens
				bool isBossRelic = gameState.OfferedRelics.Count > 0 && gameState.OfferedRelics.TrueForAll(r => string.Equals(r.Rarity, "Boss", StringComparison.OrdinalIgnoreCase));
				DecisionEventType relicEventType = isBossRelic ? DecisionEventType.BossRelic : DecisionEventType.RelicReward;
				Plugin.RunTracker?.RecordDecision(relicEventType, gameState.OfferedRelics.ConvertAll((RelicInfo r) => r.Id), null, gameState.DeckCards.ConvertAll((CardInfo c) => c.Id), gameState.CurrentRelics.ConvertAll((RelicInfo r) => r.Id), gameState.CurrentHP, gameState.MaxHP, gameState.Gold, gameState.ActNumber, gameState.Floor);
			}
		}
		catch (Exception value)
		{
			Plugin.Log($"OnRelicRewardOpened error: {value}");
		}
	}

	public static void OnShopOpened(NMerchantInventory __instance)
	{
		try
		{
			EnsureOverlay();
			Plugin.Log("Shop screen detected — analyzing...");
			RecordHook("OnShopOpened");
			MerchantInventory? inventory = __instance.Inventory;
			GameStateReader._lastCardOptions = null;
			GameStateReader._lastRelicOptions = null;
			GameStateReader._lastMerchantInventory = inventory;
			GameState gameState = GameStateReader.ReadCurrentState();
			if (gameState != null)
			{
				DeckAnalysis deckAnalysis = Plugin.DeckAnalyzer.Analyze(gameState.Character, gameState.DeckCards, Plugin.TierEngine);
				Plugin.RunTracker?.RecordArchetypeSnapshot(gameState.Floor, deckAnalysis);
				List<ScoredCard> cards = Plugin.SynergyScorer.ScoreOfferings(gameState.ShopCards, deckAnalysis, gameState.Character, gameState.ActNumber, gameState.Floor, Plugin.TierEngine, Plugin.AdaptiveScorer);
				List<ScoredRelic> relics = Plugin.SynergyScorer.ScoreRelicOfferings(gameState.ShopRelics, deckAnalysis, gameState.Character, gameState.ActNumber, gameState.Floor, Plugin.TierEngine, Plugin.AdaptiveScorer);
				Plugin.Overlay?.ShowShopAdvice(cards, relics, deckAnalysis, gameState.Character);
				// Shop decisions not recorded — no purchase hook means chosenId is always null,
				// and mixed card+relic offered IDs corrupt card stats. Shop tracking deferred
				// until proper purchase event hooking is implemented.
			}
		}
		catch (Exception value)
		{
			Plugin.Log($"OnShopOpened error: {value}");
		}
	}

	public static void OnRestSiteOpened(NRestSiteRoom __result)
	{
		try
		{
			EnsureOverlay();
			Plugin.Log("Rest site detected — showing upgrade advice...");
			RecordHook("OnRestSiteOpened");
			GameState gameState = GameStateReader.ReadCurrentState();
			if (gameState != null)
			{
				DeckAnalysis deckAnalysis = Plugin.DeckAnalyzer.Analyze(gameState.Character, gameState.DeckCards, Plugin.TierEngine);
				Plugin.RunTracker?.RecordArchetypeSnapshot(gameState.Floor, deckAnalysis);
				Plugin.Overlay?.ShowRestSiteAdvice(deckAnalysis, gameState.CurrentHP, gameState.MaxHP, gameState.ActNumber, gameState.Floor, gameState);
			}
		}
		catch (Exception value)
		{
			Plugin.Log($"OnRestSiteOpened error: {value}");
		}
	}

	public static void OnUpgradeScreenOpened(object __result, IReadOnlyList<CardModel> cards)
	{
		try
		{
			EnsureOverlay();
			Plugin.Log($"Upgrade card selection detected — {cards?.Count ?? 0} cards offered...");
			GameState gameState = GameStateReader.ReadCurrentState();
			if (gameState == null) return;

			string character = gameState.Character ?? "unknown";
			DeckAnalysis deckAnalysis = Plugin.DeckAnalyzer.Analyze(character, gameState.DeckCards, Plugin.TierEngine);

			// Convert offered CardModels to CardInfo and score for upgrade delta
			if (cards != null && cards.Count > 0)
			{
				var offeredCards = new List<CardInfo>();
				foreach (var card in cards)
				{
					if (card != null)
						offeredCards.Add(GameStateReader.CardModelToInfo(card));
				}
				if (offeredCards.Count > 0)
				{
					var scored = Plugin.SynergyScorer.ScoreForUpgrade(offeredCards, deckAnalysis, character,
						gameState.ActNumber, gameState.Floor, Plugin.TierEngine, Plugin.AdaptiveScorer);
					Plugin.Overlay?.ShowCardAdvice(scored, deckAnalysis, character, "CARD UPGRADE");
					Plugin.Overlay?.CleanupAllBadges();
					return;
				}
			}

			// Fallback: just show deck analysis
			Plugin.Overlay?.ShowUpgradeAdvice(deckAnalysis, gameState, character);
		}
		catch (Exception value)
		{
			Plugin.Log($"OnUpgradeScreenOpened error: {value}");
		}
	}

	public static void OnCombatSetup(NCombatRoom __result)
	{
		try
		{
			EnsureOverlay();
			Plugin.Log("Combat room detected — showing deck status...");
			RecordHook("OnCombatSetup");
			List<string> enemyIds = null;
			try
			{
				// Try extracting enemy IDs via reflection on NCombatRoom
				var enemiesProp = __result.GetType().GetProperty("Enemies",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				var combatProp = __result.GetType().GetProperty("CombatState",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				object source = enemiesProp?.GetValue(__result) ?? combatProp?.GetValue(__result);
				if (source is System.Collections.IEnumerable enemyList && source is not string)
				{
					enemyIds = new List<string>();
					foreach (var enemy in enemyList)
					{
						var idProp = enemy.GetType().GetProperty("Id",
							BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						var idObj = idProp?.GetValue(enemy);
						if (idObj != null)
						{
							var entryProp = idObj.GetType().GetProperty("Entry",
								BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							var entry = entryProp?.GetValue(idObj)?.ToString();
							if (entry != null)
								enemyIds.Add(entry);
						}
					}
					if (enemyIds.Count > 0)
						Plugin.Log($"Enemy IDs extracted: {string.Join(", ", enemyIds)}");
					else
						enemyIds = null;
				}
			}
			catch (Exception ex)
			{
				Plugin.Log($"Enemy ID extraction failed: {ex.Message}");
			}
			GameState gameState = GameStateReader.ReadCurrentState();
			if (gameState != null)
			{
				DeckAnalysis deckAnalysis = Plugin.DeckAnalyzer.Analyze(gameState.Character, gameState.DeckCards, Plugin.TierEngine);
				Plugin.RunTracker?.RecordArchetypeSnapshot(gameState.Floor, deckAnalysis);
				Plugin.Overlay?.ShowCombatAdvice(deckAnalysis, gameState.CurrentHP, gameState.MaxHP, gameState.ActNumber, gameState.Floor, gameState, enemyIds);
			}
		}
		catch (Exception value)
		{
			Plugin.Log($"OnCombatSetup error: {value}");
		}
	}

	public static void OnEventShowChoices(NEventRoom __result)
	{
		try
		{
			EnsureOverlay();
			Plugin.Log("Event screen detected — showing context...");
			RecordHook("OnEventShowChoices");
			string eventId = null;
			try
			{
				// Try extracting event ID via reflection: NEventRoom → Event → Id → Entry
				var eventProp = __result.GetType().GetProperty("Event",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				var eventObj = eventProp?.GetValue(__result);
				if (eventObj != null)
				{
					var idProp = eventObj.GetType().GetProperty("Id",
						BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					var idObj = idProp?.GetValue(eventObj);
					if (idObj != null)
					{
						var entryProp = idObj.GetType().GetProperty("Entry",
							BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						eventId = entryProp?.GetValue(idObj)?.ToString();
					}
				}
				if (eventId != null)
					Plugin.Log($"Event ID extracted: {eventId}");
			}
			catch (Exception ex)
			{
				Plugin.Log($"Event ID extraction failed: {ex.Message}");
			}
			GameState gameState = GameStateReader.ReadCurrentState();
			if (gameState != null)
			{
				DeckAnalysis deckAnalysis = Plugin.DeckAnalyzer.Analyze(gameState.Character, gameState.DeckCards, Plugin.TierEngine);
				Plugin.RunTracker?.RecordArchetypeSnapshot(gameState.Floor, deckAnalysis);
				Plugin.Overlay?.ShowEventAdvice(deckAnalysis, gameState.CurrentHP, gameState.MaxHP, gameState.Gold, gameState.ActNumber, gameState.Floor, eventId);
			}
		}
		catch (Exception value)
		{
			Plugin.Log($"OnEventShowChoices error: {value}");
		}
	}

	public static void OnMapScreenEntered(NMapScreen __result)
	{
		try
		{
			EnsureOverlay();
			Plugin.Log("Map screen detected — generating path advice...");
			RecordHook("OnMapScreenEntered");
			GameStateReader._lastCardOptions = null;
			GameStateReader._lastRelicOptions = null;
			GameStateReader._lastMerchantInventory = null;
			GameState gameState = GameStateReader.ReadCurrentState();
			if (gameState != null)
			{
				DeckAnalysis deckAnalysis = Plugin.DeckAnalyzer.Analyze(gameState.Character, gameState.DeckCards, Plugin.TierEngine);
				Plugin.RunTracker?.RecordArchetypeSnapshot(gameState.Floor, deckAnalysis);
				Plugin.Overlay?.ShowMapAdvice(deckAnalysis, gameState.CurrentHP, gameState.MaxHP, gameState.Gold, gameState.ActNumber, gameState.Floor);
			}
		}
		catch (Exception value)
		{
			Plugin.Log($"OnMapScreenEntered error: {value}");
		}
	}

	public static void OnCardRemovalOpened(NMerchantCardRemoval __instance)
	{
		try
		{
			EnsureOverlay();
			Plugin.Log("Card removal screen detected — analyzing deck for removal...");
			GameStateReader._lastCardOptions = null;
			GameStateReader._lastRelicOptions = null;
			GameStateReader._lastMerchantInventory = null;
			GameState gameState = GameStateReader.ReadCurrentState();
			if (gameState != null)
			{
				DeckAnalysis deckAnalysis = Plugin.DeckAnalyzer.Analyze(gameState.Character, gameState.DeckCards, Plugin.TierEngine);
				Plugin.RunTracker?.RecordArchetypeSnapshot(gameState.Floor, deckAnalysis);
				List<ScoredCard> removalCandidates = Plugin.SynergyScorer.ScoreForRemoval(gameState.DeckCards, deckAnalysis, gameState.Character, gameState.ActNumber, gameState.Floor, Plugin.TierEngine, Plugin.AdaptiveScorer);
				Plugin.Overlay?.ShowCardRemovalAdvice(removalCandidates, deckAnalysis, gameState.Character);
				Plugin.RunTracker?.RecordDecision(DecisionEventType.CardRemove, gameState.DeckCards.ConvertAll((CardInfo c) => c.Id), null, gameState.DeckCards.ConvertAll((CardInfo c) => c.Id), gameState.CurrentRelics.ConvertAll((RelicInfo r) => r.Id), gameState.CurrentHP, gameState.MaxHP, gameState.Gold, gameState.ActNumber, gameState.Floor);
			}
		}
		catch (Exception value)
		{
			Plugin.Log($"OnCardRemovalOpened error: {value}");
		}
	}

	public static void OnRunLaunched(RunManager __instance, RunState __result)
	{
		try
		{
			EnsureOverlay();
			RecordHook("OnRunLaunched");
			if (__result != null)
			{
				Player player = __result.Players?.FirstOrDefault();
				string text = "unknown";
				int ascensionLevel = __result.AscensionLevel;
				if (player?.Character?.Id != null)
				{
					text = player.Character.Id.Entry?.ToLowerInvariant() ?? "unknown";
				}
				Plugin.RunTracker?.StartRun(text, "", ascensionLevel);
				Plugin.Log($"Run launched: {text} A{ascensionLevel}");
			}
		}
		catch (Exception value)
		{
			Plugin.Log($"OnRunLaunched error: {value}");
		}
	}

	public static void OnRunEnded(RunManager __instance, bool __0)
	{
		try
		{
			RecordHook("OnRunEnded");
			GameStateReader._lastCardOptions = null;
			GameStateReader._lastRelicOptions = null;
			GameStateReader._lastMerchantInventory = null;
			GameState gameState = GameStateReader.ReadCurrentState();
			int num = gameState?.Floor ?? 0;
			int num2 = gameState?.ActNumber ?? 0;
			RunOutcome runOutcome = (!__0 ? RunOutcome.Loss : RunOutcome.Win);
			Plugin.Overlay?.ShowRunSummary(runOutcome, num, num2);
			Plugin.RunTracker?.EndRun(runOutcome, num, num2);
			// ApplyCachedStats: recompute local + merge cached cloud data once (no double-counting)
			if (Plugin.CloudSync != null)
				Plugin.CloudSync.ApplyCachedStats();
			else
				Plugin.LocalStats?.RecomputeAll();
			if (Plugin.Overlay?.Settings?.CloudSyncEnabled ?? false)
				Task.Run(() => Plugin.CloudSync?.UploadPendingRuns());
			Plugin.Log($"Run ended: {runOutcome} on floor {num} (act {num2})");
		}
		catch (Exception value)
		{
			Plugin.Log($"OnRunEnded error: {value}");
		}
	}

	public static void ApplyManualPatches(Harmony harmony)
	{
		PatchMethod(harmony, typeof(NCardRewardSelectionScreen), "SelectCard", nameof(OnCardSelected));
		PatchMethod(harmony, typeof(NChooseARelicSelection), "SelectHolder", nameof(OnRelicSelected));
		PatchMethod(harmony, typeof(NCardRewardSelectionScreen), "_Ready", nameof(OnCardRewardScreenReady));
		PatchMethod(harmony, typeof(NCardRewardSelectionScreen), "ShowScreen", nameof(OnCardRewardOpened));
		PatchMethod(harmony, typeof(NCardRewardSelectionScreen), "RefreshOptions", nameof(OnCardRewardRefreshed));
		PatchMethod(harmony, typeof(NChooseARelicSelection), "ShowScreen", nameof(OnRelicRewardOpened));
		PatchMethod(harmony, typeof(NMerchantInventory), "Open", nameof(OnShopOpened));
		// NMerchantCardRemoval.FillSlot fires on shop load, not user click — skip it
		// Card removal advice shown as part of shop screen instead
		PatchMethod(harmony, typeof(NMapScreen), "Open", nameof(OnMapScreenEntered));
		PatchMethod(harmony, typeof(NEventRoom), "Create", nameof(OnEventShowChoices));
		PatchMethod(harmony, typeof(NCombatRoom), "Create", nameof(OnCombatSetup));
		PatchMethod(harmony, typeof(NRestSiteRoom), "Create", nameof(OnRestSiteOpened));
		// Upgrade card selection screen — use Type.GetType since it may not be directly referenced
		try
		{
			var upgradeScreenType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NDeckUpgradeSelectScreen");
			if (upgradeScreenType != null)
				PatchMethod(harmony, upgradeScreenType, "ShowScreen", nameof(OnUpgradeScreenOpened));
			else
				Plugin.Log("WARN: NDeckUpgradeSelectScreen not found — upgrade advice unavailable");
		}
		catch (Exception ex) { Plugin.Log($"WARN: Upgrade screen patch failed: {ex.Message}"); }
		PatchMethod(harmony, typeof(RunManager), "Launch", nameof(OnRunLaunched));
		PatchMethod(harmony, typeof(RunManager), "OnEnded", nameof(OnRunEnded));
	}

	private static void PatchMethod(Harmony harmony, Type targetType, string methodName, string postfixName)
	{
		try
		{
			MethodInfo target = AccessTools.Method(targetType, methodName);
			if (target == null)
			{
				Plugin.Log($"WARN: Could not find {targetType.Name}.{methodName}");
				return;
			}
			MethodInfo postfix = typeof(GamePatches).GetMethod(postfixName, BindingFlags.Static | BindingFlags.Public);
			if (postfix == null)
			{
				Plugin.Log($"WARN: Could not find postfix {postfixName}");
				return;
			}
			harmony.Patch(target, postfix: new HarmonyMethod(postfix));
			Plugin.Log($"Patched {targetType.Name}.{methodName} (static={target.IsStatic})");
		}
		catch (Exception ex)
		{
			Plugin.Log($"WARN: Failed to patch {targetType.Name}.{methodName}: {ex.Message}");
		}
	}

	public static void ForceNotModded(ref bool __result)
	{
		__result = false;
	}

	public static void OnCardSelected(object __0)
	{
		try
		{
			string text = null;
			if (__0 != null)
			{
				Type type = __0.GetType();
				PropertyInfo propertyInfo = type.GetProperty("Card") ?? type.GetProperty("CardModel");
				if (propertyInfo != null)
				{
					object value = propertyInfo.GetValue(__0);
					if (value != null)
					{
						object obj = value.GetType().GetProperty("Id")?.GetValue(value);
						if (obj != null)
						{
							text = obj.GetType().GetProperty("Entry")?.GetValue(obj)?.ToString();
						}
					}
				}
				if (text == null)
				{
					PropertyInfo property = type.GetProperty("CreationResult");
					if (property != null)
					{
						object value2 = property.GetValue(__0);
						object obj2 = (value2?.GetType().GetProperty("Card"))?.GetValue(value2);
						if (obj2 != null)
						{
							object obj3 = obj2.GetType().GetProperty("Id")?.GetValue(obj2);
							text = (obj3?.GetType().GetProperty("Entry"))?.GetValue(obj3)?.ToString();
						}
					}
				}
			}
			Plugin.Log("Card picked: " + (text ?? "(unknown)"));
			Plugin.RunTracker?.UpdateLastDecisionChoice(text);
			GameStateReader._lastCardOptions = null;
			GameStateReader._lastRelicOptions = null;
			GameStateReader._lastMerchantInventory = null;
			Plugin.Overlay?.Clear();
		}
		catch (Exception value3)
		{
			Plugin.Log($"OnCardSelected error: {value3}");
		}
	}

	public static void OnRelicSelected(object __0)
	{
		try
		{
			string text = null;
			if (__0 != null)
			{
				Type type = __0.GetType();
				PropertyInfo propertyInfo = type.GetProperty("Relic") ?? type.GetProperty("RelicModel") ?? type.GetProperty("Model");
				if (propertyInfo != null)
				{
					object value = propertyInfo.GetValue(__0);
					if (value != null)
					{
						object obj = value.GetType().GetProperty("Id")?.GetValue(value);
						if (obj != null)
						{
							text = obj.GetType().GetProperty("Entry")?.GetValue(obj)?.ToString();
						}
					}
				}
			}
			Plugin.Log("Relic picked: " + (text ?? "(unknown)"));
			Plugin.RunTracker?.UpdateLastDecisionChoice(text);
			GameStateReader._lastCardOptions = null;
			GameStateReader._lastRelicOptions = null;
			GameStateReader._lastMerchantInventory = null;
			Plugin.Overlay?.Clear();
		}
		catch (Exception value2)
		{
			Plugin.Log($"OnRelicSelected error: {value2}");
		}
	}
}
