# RankTrackerPlugin

HDT plugin implementing Option A from
[../../design/rank-data-fetch-plugin.md](../../design/rank-data-fetch-plugin.md):
uploads finished ranked matches (with the post-match rank) to an HTTP endpoint,
authenticated with a Bearer API key.

## Build

The project references HDT's own assembly and `Newtonsoft.Json.dll` directly
(not copy-local, so HDT's already-loaded copies are used at runtime — see the
design doc's "Plugin project setup and deployment" section). Point the build
at wherever those actually live:

```
dotnet build -p:HdtBinDir="..\path\to\Hearthstone Deck Tracker\bin\x64\Debug" -p:HdtLibDir="..\path\to\lib"
```

The defaults in `RankTrackerPlugin.csproj` assume this repo's HDT checkout has
been built at `HDT/Hearthstone-Deck-Tracker-master/Hearthstone Deck
Tracker/bin/x64/Debug`. If you're pointing at a real HDT install instead, use
its install directory for `HdtBinDir` and its `lib` folder for `HdtLibDir`.

The build output stays in this project's own `bin\x64\<Configuration>\`
folder — it is **not** auto-copied into HDT's plugin folder. To install a
build, copy `RankTrackerPlugin.dll` (and `.pdb` if you want debug symbols)
into:

```text
%AppData%\HearthstoneDeckTracker\Plugins\RankTrackerPlugin\
```

then restart HDT (or toggle the plugin off/on under Options → Tracker →
Plugins) to pick it up.

## Configure

On first load, the plugin writes
`%AppData%\HearthstoneDeckTracker\Plugins\RankTrackerPlugin\settings.json`
with defaults:

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
