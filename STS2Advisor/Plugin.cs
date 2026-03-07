using System;
using System.IO;
using System.Reflection;
using System.Threading;
using HarmonyLib;
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
    ///
    /// Since Godot's scene tree isn't ready yet at startup hook time,
    /// we apply Harmony patches immediately but defer overlay creation
    /// until the scene tree is available.
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
                File.AppendAllText(Plugin.LogPath, $"[STS2Advisor] FATAL: {ex}\n");
            }
        }
    }

    public static class Plugin
    {
        public const string ModName = "STS2 Advisor";
        public const string ModVersion = "0.2.0";
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
        public static SyncClient SyncClient { get; private set; }
        public static OverlayManager Overlay { get; set; }

        public static void Init()
        {
            // Determine mod folder (where STS2Advisor.dll lives)
            PluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            LogPath = Path.Combine(PluginFolder, "sts2advisor.log");

            Log($"{ModName} v{ModVersion} initializing...");

            // Initialize core systems
            string dataPath = Path.Combine(PluginFolder, "Data");

            TierEngine = new TierEngine(dataPath);
            DeckAnalyzer = new DeckAnalyzer();
            SynergyScorer = new SynergyScorer();

            // Initialize tracking + community data
            RunDatabase = new RunDatabase(PluginFolder);
            RunTracker = new RunTracker(RunDatabase);
            RunTracker.Initialize(PluginFolder);
            SyncClient = new SyncClient(RunDatabase);
            AdaptiveScorer = new AdaptiveScorer(RunDatabase);

            // Apply Harmony patches
            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(GamePatches));

            Log("Harmony patches applied.");

            // Fetch community stats in background
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    SyncClient.UploadRunsAsync();
                    foreach (string c in new[] { "ironclad", "silent", "defect", "regent", "necromancer" })
                        SyncClient.FetchCommunityStatsAsync(c);
                }
                catch (Exception ex)
                {
                    Log($"Background sync error (non-fatal): {ex.Message}");
                }
            });

            Log($"{ModName} initialized successfully. Waiting for scene tree...");

            // Overlay creation is deferred — OverlayManager hooks into the scene tree
            // via a Harmony patch on a game initialization method (see GamePatches).
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
                // Can't log, silently fail
            }
        }
    }

    /// <summary>
    /// Harmony patches that hook into STS2's game screens.
    ///
    /// !! PLACEHOLDER HOOKS !!
    /// These target classes/methods in sts2.dll that need to be discovered
    /// via dnSpy/ILSpy/dotPeek. See SETUP_GUIDE.md for instructions.
    ///
    /// To find the real targets:
    /// 1. Open sts2.dll in dnSpy (or ILSpy/dotPeek)
    /// 2. Search for card reward, relic selection, shop screen classes
    /// 3. Replace the placeholder types and method names below
    /// </summary>
    public static class GamePatches
    {
        // =====================================================================
        // SCENE TREE READY — Initialize overlay once Godot is running
        // =====================================================================
        // TODO: Find a class that runs during game initialization.
        // Patch its ready/init method to create our overlay.
        //
        // In dnSpy, look for:
        //   - A main game manager or autoload singleton
        //   - A class with _Ready() or Initialize() that runs at game start
        //   - Example: GameManager._Ready() or Main._Ready()
        //
        // Once patched, this creates the Godot CanvasLayer overlay.
        // =====================================================================

        // [HarmonyPatch(typeof(SomeGameManager), "_Ready")]
        // [HarmonyPostfix]
        // public static void OnGameReady()
        // {
        //     if (Plugin.Overlay == null)
        //     {
        //         Plugin.Overlay = new OverlayManager();
        //         Plugin.Log("Overlay created.");
        //     }
        // }

        // =====================================================================
        // CARD REWARD SCREEN
        // =====================================================================
        // TODO: Replace with actual card reward screen class/method from sts2.dll.
        //
        // In dnSpy, search for:
        //   - Classes with "Reward", "CardReward", "CardChoice"
        //   - Methods that show/open the card selection after combat
        //   - The class will have a collection of offered card objects
        //
        // Look for Godot patterns:
        //   - _Ready(), _Process(), Show(), Open(), Initialize()
        //   - Signal connections like "card_selected", "reward_shown"
        // =====================================================================

        [HarmonyPatch(typeof(PlaceholderCardRewardScreen), "OnOpen")]
        [HarmonyPostfix]
        public static void OnCardRewardOpened()
        {
            Plugin.Log("Card reward screen detected — analyzing...");

            var state = GameBridge.GameStateReader.ReadCurrentState();
            if (state == null) return;

            var deckArchetypes = Plugin.DeckAnalyzer.Analyze(
                state.Character, state.DeckCards
            );

            var scored = Plugin.SynergyScorer.ScoreOfferings(
                state.OfferedCards,
                deckArchetypes,
                state.Character,
                state.ActNumber,
                Plugin.TierEngine,
                Plugin.AdaptiveScorer
            );

            Plugin.Overlay?.ShowCardAdvice(scored);

            Plugin.RunTracker?.RecordDecision(
                DecisionEventType.CardReward,
                state.OfferedCards.ConvertAll(c => c.Id),
                null,
                state.DeckCards.ConvertAll(c => c.Id),
                state.CurrentRelics.ConvertAll(r => r.Id),
                0, 0, 0,
                state.ActNumber, 0
            );
        }

        // =====================================================================
        // RELIC REWARD SCREEN
        // =====================================================================

        [HarmonyPatch(typeof(PlaceholderRelicRewardScreen), "OnOpen")]
        [HarmonyPostfix]
        public static void OnRelicRewardOpened()
        {
            Plugin.Log("Relic reward screen detected — analyzing...");

            var state = GameBridge.GameStateReader.ReadCurrentState();
            if (state == null) return;

            var deckArchetypes = Plugin.DeckAnalyzer.Analyze(
                state.Character, state.DeckCards
            );

            var scored = Plugin.SynergyScorer.ScoreRelicOfferings(
                state.OfferedRelics,
                deckArchetypes,
                state.Character,
                state.ActNumber,
                Plugin.TierEngine,
                Plugin.AdaptiveScorer
            );

            Plugin.Overlay?.ShowRelicAdvice(scored);

            Plugin.RunTracker?.RecordDecision(
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

        [HarmonyPatch(typeof(PlaceholderShopScreen), "OnOpen")]
        [HarmonyPostfix]
        public static void OnShopOpened()
        {
            Plugin.Log("Shop screen detected — analyzing...");

            var state = GameBridge.GameStateReader.ReadCurrentState();
            if (state == null) return;

            var deckArchetypes = Plugin.DeckAnalyzer.Analyze(
                state.Character, state.DeckCards
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

            Plugin.Overlay?.ShowShopAdvice(scoredCards, scoredRelics);
        }
    }

    // =========================================================================
    // PLACEHOLDER CLASSES — DELETE THESE once you find the real game classes.
    // These exist only so the project compiles without sts2.dll references.
    // =========================================================================
    #if !STS2_REAL_HOOKS
    public class PlaceholderCardRewardScreen { public void OnOpen() { } }
    public class PlaceholderRelicRewardScreen { public void OnOpen() { } }
    public class PlaceholderShopScreen { public void OnOpen() { } }
    #endif
}
