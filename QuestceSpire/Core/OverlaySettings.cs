using System;
using System.IO;
using Newtonsoft.Json;

namespace QuestceSpire.Core;

public class OverlaySettings
{
	public float OffsetLeft { get; set; } = -380f;
	public float OffsetRight { get; set; } = -30f;
	public float OffsetTop { get; set; } = 30f;
	public float OffsetBottom { get; set; } = 700f;
	public bool Visible { get; set; } = true;
	public bool ShowTooltips { get; set; } = true;
	public bool ShowInGameBadges { get; set; } = true;
	public float PanelOpacity { get; set; } = 1.0f;
	public bool Collapsed { get; set; } = false;
	public bool ShowDeckBreakdown { get; set; } = true;
	public bool ShowDecisionHistory { get; set; } = false;
	public bool ShowDrawProbability { get; set; } = true;
	public bool ShowEnemyTips { get; set; } = true;
	public bool ShowEventAdvice { get; set; } = true;
	public bool ShowMapAdvice { get; set; } = true;
	public bool ShowCombatAdvice { get; set; } = true;
	public bool CloudSyncEnabled { get; set; } = false;

	// Bump this when defaults change to force migration on old saved files
	public int SettingsVersion { get; set; } = 0;
	private const int CurrentVersion = 4;

	private static string GetSettingsPath()
	{
		return Path.Combine(Plugin.PluginFolder, "overlay_settings.json");
	}

	public static OverlaySettings Load()
	{
		try
		{
			string path = GetSettingsPath();
			if (File.Exists(path))
			{
				string json = File.ReadAllText(path);
				var settings = JsonConvert.DeserializeObject<OverlaySettings>(json);
				if (settings != null)
				{
					if (settings.SettingsVersion < CurrentVersion)
					{
						Plugin.Log($"Migrating settings from v{settings.SettingsVersion} to v{CurrentVersion}");
						// v0-1 → v2: ShowDecisionHistory forced off
						if (settings.SettingsVersion < 2)
							settings.ShowDecisionHistory = false;
						// v2 → v3: CloudSyncEnabled defaults to true (no action needed)
						// v3 → v4: CloudSyncEnabled disabled (no server deployed yet)
						if (settings.SettingsVersion < 4)
							settings.CloudSyncEnabled = false;
						settings.SettingsVersion = CurrentVersion;
						settings.Save();
					}
					Plugin.Log("Overlay settings loaded.");
					return settings;
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log("Failed to load overlay settings: " + ex.Message);
		}
		var defaults = new OverlaySettings { SettingsVersion = CurrentVersion };
		defaults.Save();
		return defaults;
	}

	public void Save()
	{
		try
		{
			string path = GetSettingsPath();
			string json = JsonConvert.SerializeObject(this, Formatting.Indented);
			File.WriteAllText(path, json);
		}
		catch (Exception ex)
		{
			Plugin.Log("Failed to save overlay settings: " + ex.Message);
		}
	}
}
