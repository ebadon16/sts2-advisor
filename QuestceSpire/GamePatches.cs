using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
	}

	public static void OnCardRewardOpened(NCardRewardSelectionScreen __result, IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> extraOptions)
	{
		try
		{
			EnsureOverlay();
			Plugin.Log("Card reward screen detected — analyzing...");
			GameStateReader._lastCardOptions = options;
			GameStateReader._lastRelicOptions = null;
			GameStateReader._lastMerchantInventory = null;
			GameState gameState = GameStateReader.ReadCurrentState();
			if (gameState != null)
			{
				DeckAnalysis deckAnalysis = Plugin.DeckAnalyzer.Analyze(gameState.Character, gameState.DeckCards, Plugin.TierEngine);
				Plugin.RunTracker?.RecordArchetypeSnapshot(gameState.Floor, deckAnalysis);
				List<ScoredCard> cards = Plugin.SynergyScorer.ScoreOfferings(gameState.OfferedCards, deckAnalysis, gameState.Character, gameState.ActNumber, gameState.Floor, Plugin.TierEngine, Plugin.AdaptiveScorer);
				Plugin.Overlay?.ShowCardAdvice(cards, deckAnalysis, gameState.Character);
				// Inject grade badges directly onto game card nodes (STS1 mod style)
				Plugin.Overlay?.InjectCardGrades(__result, cards);
				Plugin.RunTracker?.RecordDecision(DecisionEventType.CardReward, gameState.OfferedCards.ConvertAll((CardInfo c) => c.Id), null, gameState.DeckCards.ConvertAll((CardInfo c) => c.Id), gameState.CurrentRelics.ConvertAll((RelicInfo r) => r.Id), gameState.CurrentHP, gameState.MaxHP, gameState.Gold, gameState.ActNumber, gameState.Floor);
			}
		}
		catch (Exception value)
		{
			Plugin.Log($"OnCardRewardOpened error: {value}");
		}
	}

	public static void OnRelicRewardOpened(NChooseARelicSelection __result, IReadOnlyList<RelicModel> relics)
	{
		try
		{
			EnsureOverlay();
			Plugin.Log("Relic reward screen detected — analyzing...");
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
				Plugin.Overlay?.InjectRelicGrades(__result, relics2);
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
				// Inject grade badges onto shop items
				Plugin.Overlay?.InjectShopGrades(__instance, cards, relics);
				// Record shop cards as a decision for adaptive scoring
				List<string> shopOfferedIds = gameState.ShopCards.ConvertAll((CardInfo c) => c.Id);
				shopOfferedIds.AddRange(gameState.ShopRelics.ConvertAll((RelicInfo r) => r.Id));
				Plugin.RunTracker?.RecordDecision(DecisionEventType.Shop, shopOfferedIds, null, gameState.DeckCards.ConvertAll((CardInfo c) => c.Id), gameState.CurrentRelics.ConvertAll((RelicInfo r) => r.Id), gameState.CurrentHP, gameState.MaxHP, gameState.Gold, gameState.ActNumber, gameState.Floor);
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

	public static void OnUpgradeScreenOpened(object __instance)
	{
		try
		{
			EnsureOverlay();
			Plugin.Log("Upgrade card selection detected — showing upgrade priorities...");
			GameState gameState = GameStateReader.ReadCurrentState();
			if (gameState != null)
			{
				string character = gameState.Character ?? "unknown";
				DeckAnalysis deckAnalysis = Plugin.DeckAnalyzer.Analyze(character, gameState.DeckCards, Plugin.TierEngine);
				Plugin.Overlay?.ShowUpgradeAdvice(deckAnalysis, gameState, character);
			}
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
			GameState gameState = GameStateReader.ReadCurrentState();
			if (gameState != null)
			{
				DeckAnalysis deckAnalysis = Plugin.DeckAnalyzer.Analyze(gameState.Character, gameState.DeckCards, Plugin.TierEngine);
				Plugin.RunTracker?.RecordArchetypeSnapshot(gameState.Floor, deckAnalysis);
				Plugin.Overlay?.ShowCombatAdvice(deckAnalysis, gameState.CurrentHP, gameState.MaxHP, gameState.ActNumber, gameState.Floor, gameState);
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
			GameState gameState = GameStateReader.ReadCurrentState();
			if (gameState != null)
			{
				DeckAnalysis deckAnalysis = Plugin.DeckAnalyzer.Analyze(gameState.Character, gameState.DeckCards, Plugin.TierEngine);
				Plugin.RunTracker?.RecordArchetypeSnapshot(gameState.Floor, deckAnalysis);
				Plugin.Overlay?.ShowEventAdvice(deckAnalysis, gameState.CurrentHP, gameState.MaxHP, gameState.Gold, gameState.ActNumber, gameState.Floor);
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
			GameStateReader._lastCardOptions = null;
			GameStateReader._lastRelicOptions = null;
			GameStateReader._lastMerchantInventory = null;
			GameState gameState = GameStateReader.ReadCurrentState();
			int num = gameState?.Floor ?? 0;
			int num2 = gameState?.ActNumber ?? 0;
			RunOutcome runOutcome = (!__0 ? RunOutcome.Loss : RunOutcome.Win);
			Plugin.Overlay?.ShowRunSummary(runOutcome, num, num2);
			Plugin.RunTracker?.EndRun(runOutcome, num, num2);
			Plugin.LocalStats?.RecomputeAll();
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
		PatchMethod(harmony, typeof(NChooseARelicSelection), "ShowScreen", nameof(OnRelicRewardOpened));
		PatchMethod(harmony, typeof(NMerchantInventory), "Open", nameof(OnShopOpened));
		PatchMethod(harmony, typeof(NMerchantCardRemoval), "FillSlot", nameof(OnCardRemovalOpened));
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
