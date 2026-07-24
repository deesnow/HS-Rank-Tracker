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
				// Real HDT plugin installs are flat - HDT's Plugins folder holds every
				// plugin's DLL directly (confirmed: an installed "d0nkey.top plugin.dll"
				// sits straight in %AppData%\HearthstoneDeckTracker\Plugins\, no
				// per-plugin subfolder). A generic "settings.json" next to the DLL
				// would collide with any other flatly-installed plugin's own settings
				// file, so this is named after the assembly instead.
				var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
				var assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
				return Path.Combine(dir, $"{assemblyName}.settings.json");
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
