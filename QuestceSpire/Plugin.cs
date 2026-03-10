using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Saves;
using QuestceSpire.Core;
using QuestceSpire.Tracking;
using QuestceSpire.UI;

namespace QuestceSpire;

[ModInitializer("Init")]
public static class Plugin
{
	public const string ModName = "Qu'est-ce Spire?";

	public const string ModVersion = "0.6.1";

	public const string HarmonyId = "com.questcespire.mod";

	private static Harmony _harmony;

	private static bool _initialized;

	public static string PluginFolder { get; private set; }

	public static string LogPath { get; private set; }

	public static TierEngine TierEngine { get; private set; }

	public static DeckAnalyzer DeckAnalyzer { get; private set; }

	public static SynergyScorer SynergyScorer { get; private set; }

	public static AdaptiveScorer AdaptiveScorer { get; private set; }

	public static RunTracker RunTracker { get; private set; }

	public static RunDatabase RunDatabase { get; private set; }

	public static LocalStatsComputer LocalStats { get; private set; }

	public static CardPropertyScorer CardPropertyScorer { get; private set; }

	public static OverlayManager Overlay { get; set; }

	public static void Init()
	{
		if (_initialized) return;
		_initialized = true;
		PluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		if (string.IsNullOrEmpty(PluginFolder))
		{
			PluginFolder = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
		}
		if (string.IsNullOrEmpty(PluginFolder))
		{
			PluginFolder = Path.Combine(AppContext.BaseDirectory, "mods", "QuestceSpire");
		}
		LogPath = Path.Combine(PluginFolder, "questcespire.log");
		AppDomain.CurrentDomain.AssemblyResolve += delegate(object? sender, ResolveEventArgs args)
		{
			AssemblyName assemblyName = new AssemblyName(args.Name);
			string text = Path.Combine(PluginFolder, assemblyName.Name + ".dll");
			return File.Exists(text) ? Assembly.LoadFrom(text) : null;
		};
		Log($"{ModName} v{ModVersion} initializing...");
		TierEngine = new TierEngine(Path.Combine(PluginFolder, "Data"));
		CardPropertyScorer = new CardPropertyScorer(Path.Combine(PluginFolder, "Data", "CardProperties"));
		DeckAnalyzer = new DeckAnalyzer();
		SynergyScorer = new SynergyScorer();
		RunDatabase = new RunDatabase(PluginFolder);
		RunTracker = new RunTracker(RunDatabase);
		RunTracker.Initialize(PluginFolder);
		LocalStats = new LocalStatsComputer(RunDatabase, TierEngine);
		LocalStats.RecomputeAll();
		new GameDataImporter(RunDatabase).ImportAll();
		AdaptiveScorer = new AdaptiveScorer(RunDatabase);
		_harmony = new Harmony(HarmonyId);
		_harmony.PatchAll(typeof(GamePatches).Assembly);
		GamePatches.ApplyManualPatches(_harmony);
		MethodInfo methodInfo = typeof(UserDataPathProvider).GetProperty("IsRunningModded", BindingFlags.Static | BindingFlags.Public)?.GetGetMethod();
		if (methodInfo != null)
		{
			MethodInfo method = typeof(GamePatches).GetMethod("ForceNotModded", BindingFlags.Static | BindingFlags.Public);
			_harmony.Patch(methodInfo, null, new HarmonyMethod(method));
			Log("Patched IsRunningModded to false — using main profile.");
		}
		Log("Harmony patches applied.");
		Log($"{ModName} initialized successfully. Waiting for scene tree...");
	}

	private static volatile StreamWriter _logWriter;
	private static readonly object _logLock = new object();

	public static void Log(string message)
	{
		string text = $"[{DateTime.Now:HH:mm:ss}] [QuestceSpire] {message}";
		try
		{
			lock (_logLock)
			{
				if (_logWriter == null)
				{
					_logWriter = new StreamWriter(LogPath, append: true) { AutoFlush = true };
				}
				_logWriter.WriteLine(text);
			}
		}
		catch
		{
		}
	}
}
