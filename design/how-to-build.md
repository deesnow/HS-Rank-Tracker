# How to build RankTrackerPlugin

## Prerequisites

- .NET SDK (build was verified with the .NET 10 SDK; `dotnet --version` to check).
- .NET Framework 4.7.2 reference assemblies and the WindowsDesktop pack —
  present if Visual Studio / HDT itself has ever been built on this machine.
- Either a real HDT install, or this repo's HDT checkout built locally (see
  [Pointing at HDT](#pointing-at-hdt) below).

## Build from VS Code

`.vscode/tasks.json` has a **"build plugin"** task set as the default build
task. Run it with **Ctrl+Shift+B** (or Terminal → Run Build Task). It shows
up as a normal build in the Problems panel, and runs the same `dotnet build`
command described below with `HdtBinDir`/`HdtLibDir` pointed at this
machine's installed HDT version.

Update the `app-<version>` path in that task after HDT auto-updates — see
[Pointing at HDT](#pointing-at-hdt).

## Build from the terminal

```
dotnet build src/plugin/RankTrackerPlugin.csproj -p:HdtBinDir="C:\Users\<you>\AppData\Local\HearthstoneDeckTracker\app-<version>" -p:HdtLibDir="C:\Users\<you>\AppData\Local\HearthstoneDeckTracker\app-<version>"
```

Verified output: `src/plugin/bin/Debug/net472/RankTrackerPlugin.dll`, 0
warnings, 0 errors. This is the SDK's default output layout — the project
doesn't force `bin\x64\<Configuration>\` the way HDT's own `.csproj` does.

The build does **not** auto-copy the DLL into HDT's plugin folder. To
install a build, copy `RankTrackerPlugin.dll` (and `.pdb` for debug symbols)
directly into `%AppData%\HearthstoneDeckTracker\Plugins\` — HDT's plugin
folder is flat, with every plugin's DLL sitting straight in it rather than
in a per-plugin subfolder (confirmed against an existing real install: a
`d0nkey.top plugin.dll` sits directly there). Then restart HDT (or toggle
the plugin off/on under Options → Tracker → Plugins).

## Pointing at HDT

The project references HDT's own assembly and `Newtonsoft.Json.dll` directly
(not copy-local, so HDT's already-loaded copies are used at runtime — see
the design doc's "Plugin project setup and deployment" section). Two ways
to point at them:

- **A real HDT install (the common case)** — it ships
  `HearthstoneDeckTracker.exe` and `Newtonsoft.Json.dll` in the same folder,
  under `%LocalAppData%\HearthstoneDeckTracker\app-<version>\`. Use that
  folder for both `HdtBinDir` and `HdtLibDir`.

  HDT auto-updates into a new `app-<version>` folder, so this path needs
  bumping after an update if the build starts failing to resolve the
  reference — both in `.vscode/tasks.json` and in any terminal command
  you've saved.

- **This repo's own HDT checkout, built locally** — `RankTrackerPlugin.csproj`'s
  defaults assume it's been built at
  `HDT/Hearthstone-Deck-Tracker-master/Hearthstone Deck
  Tracker/bin/x64/Debug` (with `lib/` alongside it, one level up). No `-p:`
  overrides are needed in that case.

## Troubleshooting

**"I enabled/copied the plugin but there's no settings file / it's not
uploading."** Check `hdt_log.txt` first —
`%AppData%\HearthstoneDeckTracker\Logs\hdt_log.txt` (confirmed present on a
real install; rotated copies sit alongside it as `hdt_log_<timestamp>.txt`).
grep it for the plugin's name. Two things commonly explain "nothing
happened":

- **New plugins start disabled.** HDT discovers a plugin (adds it to its
  in-memory list) independently of enabling it — a freshly-added
  `PluginWrapper` defaults `IsEnabled`/`_loaded` to `false`
  (`Plugins/PluginWrapper.cs:18-36`), and `IPlugin.OnLoad()` only runs from
  inside the `IsEnabled` setter (`Plugins/PluginWrapper.cs:58-84`). That
  setter only fires from a pre-existing `plugins.xml` entry
  (`Plugins/PluginManager.cs:280-304`) or you manually flipping the toggle
  in **Options → Tracker → Plugins**. So the first time a plugin appears,
  it stays off until you switch it on by hand — expect no "loaded" log line
  and no settings file until then.
- **The settings file isn't where you installed the DLL.**
  `%AppData%\HearthstoneDeckTracker\Plugins\` (Roaming) is only a staging
  folder you copy builds into. HDT's `PluginManager` constructor syncs
  anything newer from there into its own current install folder,
  `%LocalAppData%\HearthstoneDeckTracker\app-<version>\Plugins\`
  (`Plugins/PluginManager.cs:24-43`), and actually loads/runs the plugin
  from that copy. Since the plugin writes its settings file next to
  `Assembly.GetExecutingAssembly().Location`, look for
  `RankTrackerPlugin.settings.json` there, not under `%AppData%\Roaming\...`.
  This also means the settings file doesn't carry over when HDT auto-updates
  into a new `app-<version>` folder — the DLL gets re-synced, but runtime
  state left behind in the old folder doesn't move with it.

## Source references

- [rank-data-fetch-plugin.md](rank-data-fetch-plugin.md) — "Plugin project
  setup and deployment" section.
- `src/plugin/RankTrackerPlugin.csproj` — `HdtBinDir`/`HdtLibDir` properties.
- `.vscode/tasks.json` — the "build plugin" task.
