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

## Source references

- [rank-data-fetch-plugin.md](rank-data-fetch-plugin.md) — "Plugin project
  setup and deployment" section.
- `src/plugin/RankTrackerPlugin.csproj` — `HdtBinDir`/`HdtLibDir` properties.
- `.vscode/tasks.json` — the "build plugin" task.
