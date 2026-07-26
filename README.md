# HS Rank Tracker Plugin

A Hearthstone Deck Tracker (HDT) plugin that uploads finished ranked matches
(with the post-match rank) to an HTTP endpoint, authenticated with a Bearer
API key.

See [src/plugin/README.md](src/plugin/README.md) for configuration details
and [design/rank-data-fetch-plugin.md](design/rank-data-fetch-plugin.md) for
the design.

## Installing on Windows

1. Get `RankTrackerPlugin.dll` — either build it yourself (see
   [design/how-to-build.md](design/how-to-build.md)) or obtain a release build.
2. Copy `RankTrackerPlugin.dll` (published alongside
   `RankTrackerPlugin.settings.json`) into HDT's plugin folder:

   ```text
   %AppData%\Roaming\HearthstoneDeckTracker\Plugins\
   ```

   This folder is flat — no per-plugin subfolder needed.
3. Restart HDT.
4. Enable the plugin: **Options → Tracker → Plugins**, find
   **RankTrackerPlugin** and switch it on. New plugins start disabled, and
   nothing runs (no settings file is created) until you do this.
5. Set your API key. After enabling once, HDT creates a settings file next
   to where it actually runs the plugin from:

   ```text
   %LocalAppData%\HearthstoneDeckTracker\app-<version>\Plugins\RankTrackerPlugin.settings.json
   ```

   Edit `ApiKey` (and `ApiUrl` if not using the default), then either
   restart HDT or click the plugin's "Reload settings" button under
   Options → Tracker → Plugins.

Without an `ApiKey` configured, the plugin logs a warning and skips
uploading rather than sending an unauthenticated request.

**Note:** when HDT auto-updates to a new `app-<version>` folder, re-copy the
DLL into the Roaming `Plugins` folder and re-enter your API key — the
settings file doesn't carry over automatically.
