using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using STS2Advisor.Core;
using STS2Advisor.GameBridge;
using STS2Advisor.Tracking;
using STS2Advisor.UI;
using UnityEngine;

namespace STS2Advisor
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.sts2advisor.mod";
        public const string PluginName = "STS2 Advisor";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        private Harmony _harmony;
        private OverlayRenderer _overlay;
        private TierEngine _tierEngine;
        private DeckAnalyzer _deckAnalyzer;
        private SynergyScorer _synergyScorer;
        private RunTracker _runTracker;
        private RunDatabase _runDatabase;
        private SyncClient _syncClient;
        private AdaptiveScorer _adaptiveScorer;

        public TierEngine TierEngine => _tierEngine;
        public DeckAnalyzer DeckAnalyzer => _deckAnalyzer;
        public SynergyScorer SynergyScorer => _synergyScorer;
        public RunTracker RunTracker => _runTracker;
        public AdaptiveScorer AdaptiveScorer => _adaptiveScorer;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Log.LogInfo($"{PluginName} v{PluginVersion} loading...");

            // Initialize core systems
            string dataPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Info.Location),
                "Data"
            );

            _tierEngine = new TierEngine(dataPath);
            _deckAnalyzer = new DeckAnalyzer();
            _synergyScorer = new SynergyScorer();

            // Initialize tracking + community data
            string pluginFolder = System.IO.Path.GetDirectoryName(Info.Location);
            _runDatabase = new RunDatabase(pluginFolder);
            _runTracker = new RunTracker(_runDatabase);
            _runTracker.Initialize(pluginFolder);
            _syncClient = new SyncClient(_runDatabase);
            _adaptiveScorer = new AdaptiveScorer(_runDatabase);

            // Fetch latest community stats on startup (background, non-blocking)
            _syncClient.FetchCommunityStatsAsync("ironclad");
            _syncClient.FetchCommunityStatsAsync("silent");
            _syncClient.FetchCommunityStatsAsync("defect");
            _syncClient.FetchCommunityStatsAsync("regent");
            _syncClient.FetchCommunityStatsAsync("necromancer");

            // Upload any unsynced runs from previous sessions
            _syncClient.UploadRunsAsync();

            // Create overlay
            _overlay = gameObject.AddComponent<OverlayRenderer>();

            // Apply Harmony patches
            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll(typeof(GamePatches));

            Log.LogInfo($"{PluginName} loaded successfully. Community data layer active.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }

    /// <summary>
    /// Harmony patches that hook into STS2's card/relic selection screens.
    ///
    /// !! PLACEHOLDER HOOKS !!
    /// These target classes/methods that need to be discovered via dnSpy/ILSpy.
    /// See SETUP_GUIDE.md for instructions on finding the correct targets.
    /// </summary>
    public static class GamePatches
    {
        // =====================================================================
        // CARD REWARD SCREEN
        // =====================================================================
        // TODO: Replace "CardRewardScreen" and "OnOpen" with actual class/method
        // names found in Assembly-CSharp.dll.
        //
        // In dnSpy, search for classes related to:
        //   - Card reward / card selection after combat
        //   - Look for methods that populate the reward card list
        //   - The class likely has a List<> of card objects being offered
        //
        // Example real names might be:
        //   CardRewardScreenController.Show()
        //   RewardManager.ShowCardReward()
        //   CombatRewardScreen.DisplayCards()
        // =====================================================================

        [HarmonyPatch(typeof(PlaceholderCardRewardScreen), "OnOpen")]
        [HarmonyPostfix]
        public static void OnCardRewardOpened()
        {
            Plugin.Log.LogInfo("Card reward screen detected — analyzing offerings...");

            var state = GameStateReader.ReadCurrentState();
            if (state == null) return;

            var deckArchetypes = Plugin.Instance.DeckAnalyzer.Analyze(
                state.Character, state.DeckCards
            );

            var scored = Plugin.Instance.SynergyScorer.ScoreOfferings(
                state.OfferedCards,
                deckArchetypes,
                state.Character,
                state.ActNumber,
                Plugin.Instance.TierEngine,
                Plugin.Instance.AdaptiveScorer
            );

            OverlayRenderer.Instance?.ShowCardAdvice(scored);

            // Track this decision point (chosen card recorded later via separate patch)
            Plugin.Instance.RunTracker?.RecordDecision(
                DecisionEventType.CardReward,
                state.OfferedCards.ConvertAll(c => c.Id),
                null, // chosen recorded when player actually picks
                state.DeckCards.ConvertAll(c => c.Id),
                state.CurrentRelics.ConvertAll(r => r.Id),
                0, 0, 0, // HP/gold filled from state when available
                state.ActNumber, 0
            );
        }

        // =====================================================================
        // RELIC REWARD SCREEN
        // =====================================================================
        // TODO: Replace "PlaceholderRelicRewardScreen" and "OnOpen" with actual
        // class/method names.
        //
        // Search in dnSpy for:
        //   - Relic selection / boss relic choice
        //   - Methods that display relic options to the player
        // =====================================================================

        [HarmonyPatch(typeof(PlaceholderRelicRewardScreen), "OnOpen")]
        [HarmonyPostfix]
        public static void OnRelicRewardOpened()
        {
            Plugin.Log.LogInfo("Relic reward screen detected — analyzing offerings...");

            var state = GameStateReader.ReadCurrentState();
            if (state == null) return;

            var deckArchetypes = Plugin.Instance.DeckAnalyzer.Analyze(
                state.Character, state.DeckCards
            );

            var scored = Plugin.Instance.SynergyScorer.ScoreRelicOfferings(
                state.OfferedRelics,
                deckArchetypes,
                state.Character,
                state.ActNumber,
                Plugin.Instance.TierEngine,
                Plugin.Instance.AdaptiveScorer
            );

            OverlayRenderer.Instance?.ShowRelicAdvice(scored);

            Plugin.Instance.RunTracker?.RecordDecision(
                DecisionEventType.RelicReward,
                state.OfferedRelics.ConvertAll(r => r.Id),
                null,
                state.DeckCards.ConvertAll(c => c.Id),
                state.CurrentRelics.ConvertAll(r => r.Id),
                0, 0, 0,
                state.ActNumber, 0
            );
        }

        // =====================================================================
        // SHOP SCREEN
        // =====================================================================
        // TODO: Replace with actual shop screen class/method.
        //
        // Search in dnSpy for:
        //   - Shop / merchant screen
        //   - Methods that populate shop inventory
        // =====================================================================

        [HarmonyPatch(typeof(PlaceholderShopScreen), "OnOpen")]
        [HarmonyPostfix]
        public static void OnShopOpened()
        {
            Plugin.Log.LogInfo("Shop screen detected — analyzing inventory...");

            var state = GameStateReader.ReadCurrentState();
            if (state == null) return;

            var deckArchetypes = Plugin.Instance.DeckAnalyzer.Analyze(
                state.Character, state.DeckCards
            );

            var scoredCards = Plugin.Instance.SynergyScorer.ScoreOfferings(
                state.ShopCards,
                deckArchetypes,
                state.Character,
                state.ActNumber,
                Plugin.Instance.TierEngine,
                Plugin.Instance.AdaptiveScorer
            );

            var scoredRelics = Plugin.Instance.SynergyScorer.ScoreRelicOfferings(
                state.ShopRelics,
                deckArchetypes,
                state.Character,
                state.ActNumber,
                Plugin.Instance.TierEngine,
                Plugin.Instance.AdaptiveScorer
            );

            OverlayRenderer.Instance?.ShowShopAdvice(scoredCards, scoredRelics);
        }
    }

    // =========================================================================
    // PLACEHOLDER CLASSES — DELETE THESE once you find the real game classes.
    // These exist only so the project compiles without Assembly-CSharp.dll.
    // =========================================================================
    #if !STS2_REAL_HOOKS
    public class PlaceholderCardRewardScreen { public void OnOpen() { } }
    public class PlaceholderRelicRewardScreen { public void OnOpen() { } }
    public class PlaceholderShopScreen { public void OnOpen() { } }
    #endif
}
