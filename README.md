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
3. Get your own API key: send the `/hdttoken` command to the Jeeves bot.
   You'll receive your API key as a DM.
4. Put your own API key into `RankTrackerPlugin.settings.json` (the copy in
   the Roaming `Plugins` folder from step 2) and save it — set `ApiKey` to
   the value from Jeeves.
5. Restart HDT.
6. Enable the plugin: **Options → Tracker → Plugins**, find
   **RankTrackerPlugin** and switch it on. New plugins start disabled, and
   nothing runs until you do this.

Without an `ApiKey` configured, the plugin logs a warning and skips
uploading rather than sending an unauthenticated request.

**Note:** once enabled, HDT syncs its settings file into
`%LocalAppData%\HearthstoneDeckTracker\app-<version>\Plugins\RankTrackerPlugin.settings.json`
and actually runs from there. When HDT auto-updates to a new `app-<version>`
folder, re-copy the DLL and settings JSON into the Roaming `Plugins` folder
and re-enter your API key — the settings file doesn't carry over
automatically.
