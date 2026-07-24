# RankTrackerPlugin

HDT plugin implementing Option A from
[../../design/rank-data-fetch-plugin.md](../../design/rank-data-fetch-plugin.md):
uploads finished ranked matches (with the post-match rank) to an HTTP endpoint,
authenticated with a Bearer API key.

## Build

See [../../design/how-to-build.md](../../design/how-to-build.md) — covers
the VS Code build task, the equivalent terminal command, pointing the
project at HDT, and installing a build into HDT's plugin folder (not
automated by the build itself).

## Configure

HDT's `Plugins` folder is flat — every plugin's DLL sits directly under
`%AppData%\HearthstoneDeckTracker\Plugins\`, no per-plugin subfolder (this is
where `RankTrackerPlugin.dll` gets installed, per the build doc above). On
first load, the plugin writes
`%AppData%\HearthstoneDeckTracker\Plugins\RankTrackerPlugin.settings.json`
next to it, named after the assembly rather than a generic `settings.json`
so it can't collide with another plugin's own settings file in the same
folder, with defaults:

```json
{
  "ApiUrl": "http://localhost:3000/API",
  "ApiKey": ""
}
```

Edit `ApiKey` to a token issued by the backend (see
[../backend](../backend) for the local mock server used to test this). Then
either restart HDT, or click the plugin's button under Options → Tracker →
Plugins ("Reload settings") to pick up the change without a restart.

Without an `ApiKey` configured, the plugin logs a warning and skips uploading
rather than sending an unauthenticated request.

## What gets uploaded

Ranked games only (see `UploadedModes` in `RankTrackerPlugin.cs`). The plugin
waits up to 8 seconds after a match ends for HDT to fetch the post-match rank
before giving up and sending whatever it has — see "Timing budget" in the
design doc for why that delay exists.

There's no local retry queue in this version: a failed upload (network error,
non-2xx response) is logged and dropped. See the design doc's "Idempotency
and retries" section if that needs to change later.
