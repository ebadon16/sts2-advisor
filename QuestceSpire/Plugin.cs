using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
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

	public const string ModVersion = "0.7.0";

	public const string HarmonyId = "com.questcespire.mod";

	private static Harmony _harmony;

	private static bool _initialized;

	private static volatile bool _backgroundInitDone;

	public static bool IsBackgroundInitDone => _backgroundInitDone;

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

	public static CloudSync CloudSync { get; private set; }

	public static EventAdvisor EventAdvisor { get; private set; }

	public static EnemyAdvisor EnemyAdvisor { get; private set; }

	public static string LatestVersion { get; set; }

	public static string UpdateUrl { get; set; }

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
		EventAdvisor = new EventAdvisor(Path.Combine(PluginFolder, "Data"));
		EnemyAdvisor = new EnemyAdvisor(Path.Combine(PluginFolder, "Data"));
		CloudSync = new CloudSync(RunDatabase, RunTracker.PlayerId);
		var overlaySettings = OverlaySettings.Load();
		if (overlaySettings.CloudSyncEnabled)
		{
			// Download community stats and merge on top of local+imported data.
			// DownloadCommunityStats calls ApplyCachedStats which recomputes local
			// then merges cloud — this preserves correct totals.
			Task.Run(async () =>
			{
				try
				{
					await CloudSync.DownloadCommunityStats();
					// Re-apply game history import after cloud merge
					new GameDataImporter(RunDatabase).ImportAll();
				}
				catch (Exception ex)
				{
					Log("Background cloud sync error: " + ex.Message);
				}
				finally
				{
					_backgroundInitDone = true;
				}
			});
		}
		else
		{
			_backgroundInitDone = true;
		}
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
		// Fire-and-forget version check
		Task.Run(async () =>
		{
			try
			{
				using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
				var resp = await http.GetStringAsync("https://questcespire-api.questcespire.workers.dev/api/version");
				var ver = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(resp);
				if (ver != null && ver.TryGetValue("latest", out var latest))
				{
					if (CompareVersions(ModVersion, latest) < 0)
					{
						LatestVersion = latest;
						ver.TryGetValue("release_url", out var url);
						UpdateUrl = url;
						Log($"Update available: v{latest} (current: v{ModVersion})");
					}
					else
					{
						Log($"Version check: up to date (v{ModVersion})");
					}
				}
			}
			catch (Exception ex)
			{
				Log($"Version check failed: {ex.Message}");
			}
		});
		Log($"{ModName} initialized successfully. Waiting for scene tree...");
	}

	internal static int CompareVersions(string a, string b)
	{
		var pa = a.Split('.');
		var pb = b.Split('.');
		int len = Math.Max(pa.Length, pb.Length);
		for (int i = 0; i < len; i++)
		{
			int va = i < pa.Length && int.TryParse(pa[i], out var x) ? x : 0;
			int vb = i < pb.Length && int.TryParse(pb[i], out var y) ? y : 0;
			if (va != vb) return va.CompareTo(vb);
		}
		return 0;
	}

	private static volatile StreamWriter _logWriter;
	private static readonly object _logLock = new object();
	private static bool _exitHandlerRegistered;

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
					if (!_exitHandlerRegistered)
					{
						_exitHandlerRegistered = true;
						AppDomain.CurrentDomain.ProcessExit += (_, _) =>
						{
							lock (_logLock) { _logWriter?.Dispose(); _logWriter = null; }
						};
					}
				}
				_logWriter.WriteLine(text);
			}
		}
		catch
		{
		}
	}
}
