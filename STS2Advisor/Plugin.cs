using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Runs;
using STS2Advisor.Core;
using STS2Advisor.Tracking;
using STS2Advisor.UI;

namespace STS2Advisor
{
    /// <summary>
    /// Entry point for the STS2 Advisor mod.
    ///
    /// Loaded via .NET startup hook (DOTNET_STARTUP_HOOKS environment variable).
    /// The Initialize() method runs before the game's Main().
    /// </summary>
    public static class StartupHook
    {
        public static void Initialize()
        {
            try
            {
                Plugin.Init();
            }
            catch (Exception ex)
            {
                string fallbackLog = Plugin.LogPath
                    ?? Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "sts2advisor.log");
                File.AppendAllText(fallbackLog, $"[STS2Advisor] FATAL: {ex}\n");
            }
        }
    }

    public static class Plugin
    {
        public const string ModName = "STS2 Advisor";
        public const string ModVersion = "0.3.0";
        public const string HarmonyId = "com.sts2advisor.mod";

        public static string PluginFolder { get; private set; }
        public static string LogPath { get; private set; }

        private static Harmony _harmony;

        public static TierEngine TierEngine { get; private set; }
        public static DeckAnalyzer DeckAnalyzer { get; private set; }
        public static SynergyScorer SynergyScorer { get; private set; }
        public static AdaptiveScorer AdaptiveScorer { get; private set; }
        public static RunTracker RunTracker { get; private set; }
        public static RunDatabase RunDatabase { get; private set; }
        public static LocalStatsComputer LocalStats { get; private set; }
        public static OverlayManager Overlay { get; set; }

        public static void Init()
        {
            PluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(PluginFolder))
                PluginFolder = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            if (string.IsNullOrEmpty(PluginFolder))
                PluginFolder = Path.Combine(AppContext.BaseDirectory, "mods", "STS2Advisor");
            LogPath = Path.Combine(PluginFolder, "sts2advisor.log");

            Log($"{ModName} v{ModVersion} initializing...");

            string dataPath = Path.Combine(PluginFolder, "Data");

            TierEngine = new TierEngine(dataPath);
            DeckAnalyzer = new DeckAnalyzer();
            SynergyScorer = new SynergyScorer();

            RunDatabase = new RunDatabase(PluginFolder);
            RunTracker = new RunTracker(RunDatabase);
            RunTracker.Initialize(PluginFolder);
            LocalStats = new LocalStatsComputer(RunDatabase);
            AdaptiveScorer = new AdaptiveScorer(RunDatabase);

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(GamePatches).Assembly);
            GamePatches.ApplyManualPatches(_harmony);

            Log("Harmony patches applied.");
            Log($"{ModName} initialized successfully. Waiting for scene tree...");
        }

        public static void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] [STS2Advisor] {message}";
            try
            {
                File.AppendAllText(LogPath, line + "\n");
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Harmony patches that hook into STS2's game screens.
    ///
    /// Targets real classes discovered from sts2.dll:
    ///   - NCardRewardSelectionScreen.ShowScreen — card reward after combat
    ///   - NChooseARelicSelection.ShowScreen — relic selection (boss/treasure)
    ///   - NMerchantInventory.Open — shop screen
    ///   - NCardRewardSelectionScreen._Ready — used to create overlay on first screen
    /// </summary>
    public static class GamePatches
    {
        // =====================================================================
        // OVERLAY CREATION — called from any screen patch to ensure overlay exists
        // =====================================================================

        private static void EnsureOverlay()
        {
            if (Plugin.Overlay == null)
            {
                try
                {
                    Plugin.Overlay = new OverlayManager();
                    Plugin.Log("Overlay created.");
                }
                catch (Exception ex)
                {
                    Plugin.Log($"Overlay creation failed: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(NCardRewardSelectionScreen), "_Ready")]
        [HarmonyPostfix]
        public static void OnCardRewardScreenReady(NCardRewardSelectionScreen __instance)
        {
            EnsureOverlay();
        }

        // =====================================================================
        // CARD REWARD SCREEN — intercept ShowScreen to capture offered cards
        // =====================================================================

        [HarmonyPatch(typeof(NCardRewardSelectionScreen), "ShowScreen")]
        [HarmonyPostfix]
        public static void OnCardRewardOpened(
            IReadOnlyList<CardCreationResult> options,
            IReadOnlyList<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative> extraOptions)
        {
            try
            {
                EnsureOverlay();
                Plugin.Log("Card reward screen detected — analyzing...");

                // Store the offered cards for GameStateReader
                GameBridge.GameStateReader._lastCardOptions = options;
                GameBridge.GameStateReader._lastRelicOptions = null;
                GameBridge.GameStateReader._lastMerchantInventory = null;

                var state = GameBridge.GameStateReader.ReadCurrentState();
                if (state == null) return;

                var deckArchetypes = Plugin.DeckAnalyzer.Analyze(
                    state.Character, state.DeckCards, Plugin.TierEngine
                );

                var scored = Plugin.SynergyScorer.ScoreOfferings(
                    state.OfferedCards,
                    deckArchetypes,
                    state.Character,
                    state.ActNumber,
                    Plugin.TierEngine,
                    Plugin.AdaptiveScorer
                );

                Plugin.Overlay?.ShowCardAdvice(scored, deckArchetypes);

                Plugin.RunTracker?.RecordDecision(
                    DecisionEventType.CardReward,
                    state.OfferedCards.ConvertAll(c => c.Id),
                    null,
                    state.DeckCards.ConvertAll(c => c.Id),
                    state.CurrentRelics.ConvertAll(r => r.Id),
                    state.CurrentHP, state.MaxHP, state.Gold,
                    state.ActNumber, state.Floor
                );
            }
            catch (Exception ex)
            {
                Plugin.Log($"OnCardRewardOpened error: {ex}");
            }
        }

        // =====================================================================
        // RELIC SELECTION SCREEN — intercept ShowScreen
        // =====================================================================

        [HarmonyPatch(typeof(NChooseARelicSelection), "ShowScreen")]
        [HarmonyPostfix]
        public static void OnRelicRewardOpened(IReadOnlyList<RelicModel> relics)
        {
            try
            {
                EnsureOverlay();
                Plugin.Log("Relic reward screen detected — analyzing...");

                GameBridge.GameStateReader._lastCardOptions = null;
                GameBridge.GameStateReader._lastRelicOptions = relics;
                GameBridge.GameStateReader._lastMerchantInventory = null;

                var state = GameBridge.GameStateReader.ReadCurrentState();
                if (state == null) return;

                var deckArchetypes = Plugin.DeckAnalyzer.Analyze(
                    state.Character, state.DeckCards, Plugin.TierEngine
                );

                var scored = Plugin.SynergyScorer.ScoreRelicOfferings(
                    state.OfferedRelics,
                    deckArchetypes,
                    state.Character,
                    state.ActNumber,
                    Plugin.TierEngine,
                    Plugin.AdaptiveScorer
                );

                Plugin.Overlay?.ShowRelicAdvice(scored, deckArchetypes);

                Plugin.RunTracker?.RecordDecision(
                    DecisionEventType.RelicReward,
                    state.OfferedRelics.ConvertAll(r => r.Id),
                    null,
                    state.DeckCards.ConvertAll(c => c.Id),
                    state.CurrentRelics.ConvertAll(r => r.Id),
                    state.CurrentHP, state.MaxHP, state.Gold,
                    state.ActNumber, state.Floor
                );
            }
            catch (Exception ex)
            {
                Plugin.Log($"OnRelicRewardOpened error: {ex}");
            }
        }

        // =====================================================================
        // SHOP SCREEN — intercept NMerchantInventory.Open
        // =====================================================================

        [HarmonyPatch(typeof(NMerchantInventory), "Open")]
        [HarmonyPostfix]
        public static void OnShopOpened(NMerchantInventory __instance)
        {
            try
            {
                EnsureOverlay();
                Plugin.Log("Shop screen detected — analyzing...");

                var inventory = __instance.Inventory;
                GameBridge.GameStateReader._lastCardOptions = null;
                GameBridge.GameStateReader._lastRelicOptions = null;
                GameBridge.GameStateReader._lastMerchantInventory = inventory;

                var state = GameBridge.GameStateReader.ReadCurrentState();
                if (state == null) return;

                var deckArchetypes = Plugin.DeckAnalyzer.Analyze(
                    state.Character, state.DeckCards, Plugin.TierEngine
                );

                var scoredCards = Plugin.SynergyScorer.ScoreOfferings(
                    state.ShopCards,
                    deckArchetypes,
                    state.Character,
                    state.ActNumber,
                    Plugin.TierEngine,
                    Plugin.AdaptiveScorer
                );

                var scoredRelics = Plugin.SynergyScorer.ScoreRelicOfferings(
                    state.ShopRelics,
                    deckArchetypes,
                    state.Character,
                    state.ActNumber,
                    Plugin.TierEngine,
                    Plugin.AdaptiveScorer
                );

                Plugin.Overlay?.ShowShopAdvice(scoredCards, scoredRelics, deckArchetypes);
            }
            catch (Exception ex)
            {
                Plugin.Log($"OnShopOpened error: {ex}");
            }
        }

        // =====================================================================
        // RUN LIFECYCLE — detect run start and end
        // =====================================================================

        [HarmonyPatch(typeof(RunManager), "Launch")]
        [HarmonyPostfix]
        public static void OnRunLaunched(RunManager __instance, RunState __result)
        {
            try
            {
                if (__result == null) return;

                var player = __result.Players?.FirstOrDefault();
                string character = "unknown";
                int ascension = __result.AscensionLevel;

                if (player?.Character?.Id != null)
                    character = player.Character.Id.Entry?.ToLowerInvariant() ?? "unknown";

                Plugin.RunTracker?.StartRun(character, "", ascension);
                Plugin.Log($"Run launched: {character} A{ascension}");
            }
            catch (Exception ex)
            {
                Plugin.Log($"OnRunLaunched error: {ex}");
            }
        }

        [HarmonyPatch(typeof(RunManager), "OnEnded")]
        [HarmonyPostfix]
        public static void OnRunEnded(RunManager __instance, bool isVictory)
        {
            try
            {
                var state = GameBridge.GameStateReader.ReadCurrentState();
                int floor = state?.Floor ?? 0;
                int act = state?.ActNumber ?? 0;

                var outcome = isVictory ? RunOutcome.Win : RunOutcome.Loss;
                Plugin.RunTracker?.EndRun(outcome, floor, act);
                Plugin.LocalStats?.RecomputeAll();
                Plugin.Overlay?.Clear();
                Plugin.Log($"Run ended: {outcome} on floor {floor} (act {act})");
            }
            catch (Exception ex)
            {
                Plugin.Log($"OnRunEnded error: {ex}");
            }
        }

        // =====================================================================
        // CARD/RELIC PICK TRACKING — manual patches for private SelectCard/SelectHolder
        // Registered from Plugin.Init() via ApplyManualPatches()
        // =====================================================================

        public static void ApplyManualPatches(Harmony harmony)
        {
            try
            {
                // Patch NCardRewardSelectionScreen.SelectCard (private)
                var selectCard = AccessTools.Method(
                    typeof(NCardRewardSelectionScreen), "SelectCard");
                if (selectCard != null)
                {
                    var postfix = typeof(GamePatches).GetMethod(
                        nameof(OnCardSelected),
                        BindingFlags.Static | BindingFlags.Public);
                    harmony.Patch(selectCard, postfix: new HarmonyMethod(postfix));
                    Plugin.Log("Patched NCardRewardSelectionScreen.SelectCard");
                }

                // Patch NChooseARelicSelection.SelectHolder (private)
                var selectHolder = AccessTools.Method(
                    typeof(NChooseARelicSelection), "SelectHolder");
                if (selectHolder != null)
                {
                    var postfix = typeof(GamePatches).GetMethod(
                        nameof(OnRelicSelected),
                        BindingFlags.Static | BindingFlags.Public);
                    harmony.Patch(selectHolder, postfix: new HarmonyMethod(postfix));
                    Plugin.Log("Patched NChooseARelicSelection.SelectHolder");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log($"Manual patch error (non-fatal): {ex}");
            }
        }

        /// <summary>
        /// Postfix for NCardRewardSelectionScreen.SelectCard.
        /// The cardHolder parameter is typed as object to avoid compile-time dependency on NCardHolder.
        /// We extract the chosen card ID via reflection.
        /// </summary>
        public static void OnCardSelected(object __0)
        {
            try
            {
                string chosenId = null;

                // Try to get card model from the holder via reflection
                // NCardHolder typically has a Card or CardModel property
                if (__0 != null)
                {
                    var holderType = __0.GetType();
                    // Try common property names
                    var cardProp = holderType.GetProperty("Card") ?? holderType.GetProperty("CardModel");
                    if (cardProp != null)
                    {
                        var cardObj = cardProp.GetValue(__0);
                        if (cardObj != null)
                        {
                            var idProp = cardObj.GetType().GetProperty("Id");
                            var idVal = idProp?.GetValue(cardObj);
                            if (idVal != null)
                            {
                                var entryProp = idVal.GetType().GetProperty("Entry");
                                chosenId = entryProp?.GetValue(idVal)?.ToString();
                            }
                        }
                    }

                    // Fallback: try CreationResult.Card pattern
                    if (chosenId == null)
                    {
                        var crProp = holderType.GetProperty("CreationResult");
                        if (crProp != null)
                        {
                            var cr = crProp.GetValue(__0);
                            var cardProp2 = cr?.GetType().GetProperty("Card");
                            var card = cardProp2?.GetValue(cr);
                            if (card != null)
                            {
                                var idProp = card.GetType().GetProperty("Id");
                                var idVal = idProp?.GetValue(card);
                                var entryProp = idVal?.GetType().GetProperty("Entry");
                                chosenId = entryProp?.GetValue(idVal)?.ToString();
                            }
                        }
                    }
                }

                Plugin.Log($"Card picked: {chosenId ?? "(unknown)"}");

                // Update the last decision with the chosen card
                Plugin.RunTracker?.UpdateLastDecisionChoice(chosenId);

                // Auto-hide overlay after picking
                Plugin.Overlay?.Clear();
            }
            catch (Exception ex)
            {
                Plugin.Log($"OnCardSelected error: {ex}");
            }
        }

        /// <summary>
        /// Postfix for NChooseARelicSelection.SelectHolder.
        /// </summary>
        public static void OnRelicSelected(object __0)
        {
            try
            {
                string chosenId = null;

                if (__0 != null)
                {
                    var holderType = __0.GetType();
                    var relicProp = holderType.GetProperty("Relic") ?? holderType.GetProperty("RelicModel") ?? holderType.GetProperty("Model");
                    if (relicProp != null)
                    {
                        var relic = relicProp.GetValue(__0);
                        if (relic != null)
                        {
                            var idProp = relic.GetType().GetProperty("Id");
                            var idVal = idProp?.GetValue(relic);
                            if (idVal != null)
                            {
                                var entryProp = idVal.GetType().GetProperty("Entry");
                                chosenId = entryProp?.GetValue(idVal)?.ToString();
                            }
                        }
                    }
                }

                Plugin.Log($"Relic picked: {chosenId ?? "(unknown)"}");
                Plugin.RunTracker?.UpdateLastDecisionChoice(chosenId);

                // Auto-hide overlay after picking
                Plugin.Overlay?.Clear();
            }
            catch (Exception ex)
            {
                Plugin.Log($"OnRelicSelected error: {ex}");
            }
        }
    }
}
