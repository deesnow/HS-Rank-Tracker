using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace RankTrackerPlugin
{
	// Lives next to the plugin DLL (not in HDT's own plugin-enabled/disabled
	// PluginSettings), since HDT has no built-in storage for plugin-specific
	// config. See design/rank-data-fetch-plugin.md "Configuring the target API URL".
	public class UploaderSettings
	{
		public string ApiUrl { get; set; } = "http://localhost:3000/API";
		public string ApiKey { get; set; } = "";

		private static string SettingsFilePath
		{
			get
			{
				var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
				return Path.Combine(dir, "settings.json");
			}
		}

		public static UploaderSettings Load()
		{
			try
			{
				if(File.Exists(SettingsFilePath))
				{
					var json = File.ReadAllText(SettingsFilePath);
					var loaded = JsonConvert.DeserializeObject<UploaderSettings>(json);
					if(loaded != null)
						return loaded;
				}
			}
			catch
			{
				// Corrupt or unreadable settings file: fall through to defaults
				// rather than let a bad file crash the plugin.
			}

			var defaults = new UploaderSettings();
			defaults.Save();
			return defaults;
		}

		public void Save()
		{
			try
			{
				File.WriteAllText(SettingsFilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
			}
			catch
			{
				// Best-effort; the plugin still runs with in-memory settings if this fails.
			}
		}
	}
}
