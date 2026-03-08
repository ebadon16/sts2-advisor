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
					Plugin.Log("Overlay settings loaded.");
					return settings;
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log("Failed to load overlay settings: " + ex.Message);
		}
		return new OverlaySettings();
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
