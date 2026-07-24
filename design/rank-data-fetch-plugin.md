# Fetching post-match rank via HDT plugin (Option A)

## Goal

From a Hearthstone Deck Tracker (HDT) plugin, capture the player's rank
*after* each finished match (post-match `LegendRank` / `StarLevel` / `Stars`),
without touching the Hearthstone process directly (no HearthMirror calls of
our own). Everything is read off the same objects HDT itself uses,
via the public plugin API (`Hearthstone_Deck_Tracker.API`).

## Why this isn't a single read

HDT populates rank data on `Core.Game.CurrentGameStats` (a `GameStats`
instance) in two separate passes, at two different times:

1. **Pre-match rank** — `Rank`, `LegendRank`, `StarLevel`, `LeagueId`, `Stars`,
   `OpponentRank`, `OpponentLegendRank` — set synchronously in
   `GameEventHandler.HandleGameEnd()` before `GameEvents.OnGameEnd.Execute()`
   fires. These reflect the rank the player *entered* the match with.
2. **Post-match rank** — `StarLevelAfter`, `StarsAfter`, `LegendRankAfter` —
   set later, asynchronously, by `UpdatePostGameRanks()`, which runs *after*
   `OnGameEnd` has already fired. It:
   - waits for `STATE COMPLETE` to show up in the power log (up to ~5.5s),
   - then polls the game client for updated medal info with retries
     (5 tries × 150ms by default, per `Helper.RetryWhileNull`).

So a plugin that reads `CurrentGameStats` synchronously inside its
`OnGameEnd` callback will see the pre-match rank fields populated, but the
`...After` fields will still be `0` — HDT hasn't fetched them yet.

Also important: `StarLevelAfter` / `StarsAfter` / `LegendRankAfter` are plain
auto-properties with **no `INotifyPropertyChanged` notification** (unlike
`Rank` / `LegendRank` / `StarLevel`, which do raise `PropertyChanged`). There
is no event to subscribe to for "the after-rank just arrived" — polling is
the only option.

## Mechanism

1. In `IPlugin.OnLoad()`, subscribe to `GameEvents.OnGameEnd`.
2. In the callback, immediately capture a reference to
   `Core.Game.CurrentGameStats` into a local field. Do **not** re-read
   `Core.Game.CurrentGameStats` later — it gets replaced with a new instance
   as soon as HDT resets state for the next match (`GameV2.Reset()`), so the
   reference must be grabbed at the moment `OnGameEnd` fires.
3. Do not block inside the `OnGameEnd` callback. Plugin actions run
   synchronously on HDT's own thread (`ActionList.Execute()`), and HDT logs a
   warning if a single action takes longer than
   `PluginManager.MaxPluginExecutionTime` (2000ms). A blocking wait for the
   rank update would trip this.
4. Instead, use `IPlugin.OnUpdate()` — called by HDT roughly every 100ms —
   to poll the captured `GameStats` reference until the post-match fields
   are populated (or a timeout elapses), then send the completed record to
   the server.
5. Guard against capturing an incomplete `GameStats`: `GameEvents.OnGameEnd`
   has a second call site, for 0-turn games discarded via
   `Config.Instance.DiscardZeroTurnGame` (`GameEventHandler.cs:809-811`),
   which fires *before* `GameMode`/`Rank`/etc. are assigned (those happen at
   `GameEventHandler.cs:813` onward). That config defaults to `false`
   (`Config.cs:313`), so this is dormant by default, but the guard costs
   nothing: in the `OnGameEnd` callback, check `Turns < 1` and skip capturing
   the reference entirely if so — a 0-turn game isn't a meaningful rank
   sample either way.

## Timing budget

Worst case observed in HDT's own code path before `...After` fields land:

- up to ~5.5s waiting for `STATE COMPLETE` in the log
- + up to ~750ms of retry polling for medal info

So a plugin poll loop should tolerate **at least ~6-8 seconds** after
`OnGameEnd` before giving up. Non-ranked game modes (Arena, Battlegrounds,
Mercenaries, Casual/Friendly) don't get `...After` fields populated this way
at all — those modes use their own fields (`BattlegroundsRatingAfter`,
`MercenariesRatingAfter`, `ArenaRating`, etc.) or have no post-match delta.

## Sketch

```csharp
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Stats;
using System;
using System.Net;

public class MyPlugin : IPlugin
{
    // Only these modes get uploaded. A HashSet keeps this a one-line change
    // later (e.g. adding Battlegrounds rating tracking) instead of a
    // scattered set of if-checks.
    private static readonly HashSet<GameMode> UploadedModes = new HashSet<GameMode> { GameMode.Ranked };

    private GameStats? _pendingGame;
    private DateTime _pendingSince;
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(8);

    public void OnLoad()
    {
        // net472 does not always default to TLS 1.2; the backend requires it.
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        GameEvents.OnGameEnd.Add(OnGameEnd);
    }

    private void OnGameEnd()
    {
        var gs = Core.Game.CurrentGameStats;

        // Defends against the 0-turn early-return path in HDT's own
        // HandleGameEnd (GameEventHandler.cs:809-811), which fires OnGameEnd
        // before GameMode/Rank/etc. are populated. Turns is always set by
        // this point in both code paths.
        if (gs.Turns < 1 || !UploadedModes.Contains(gs.GameMode))
            return;

        // Grab the reference now - CurrentGameStats will be replaced
        // by the time the next game starts.
        _pendingGame = gs;
        _pendingSince = DateTime.UtcNow;
    }

    // Called by HDT roughly every 100ms.
    public void OnUpdate()
    {
        try
        {
            PollPendingGame();
        }
        catch (Exception ex)
        {
            // HDT auto-disables a plugin after 100 exceptions thrown out of
            // OnUpdate (PluginManager.MaxExceptions) - never let this leak.
            Log.Error($"Rank tracker OnUpdate error: {ex}");
        }
    }

    private void PollPendingGame()
    {
        if (_pendingGame == null)
            return;

        var gs = _pendingGame;
        var haveAfterData = gs.LegendRankAfter > 0 || gs.StarLevelAfter > 0 || gs.StarsAfter > 0;
        var timedOut = DateTime.UtcNow - _pendingSince > PollTimeout;

        if (haveAfterData || timedOut)
        {
            SendToServer(gs);
            _pendingGame = null;
        }
    }

    private void SendToServer(GameStats gs)
    {
        // gs.Rank / gs.LegendRank / gs.StarLevel / gs.LeagueId   -> pre-match rank
        // gs.StarLevelAfter / gs.StarsAfter / gs.LegendRankAfter -> post-match rank
        // gs.OpponentRank / gs.OpponentLegendRank                -> opponent's rank
        // gs.WasConceded                                        -> concede flag
        // gs.GameId                                             -> idempotency key
        // ... build payload and POST it (see "Authentication and payload delivery")
    }

    // IPlugin boilerplate (Name, Description, Author, Version, ButtonText,
    // MenuItem, OnUnload, OnButtonPress) omitted for brevity.
}
```

## Trade-off vs. Option B (calling HearthMirror directly)

This approach depends on HDT's internal `SaveReplays` / `UpdatePostGameRanks`
flow actually completing and writing to the same `GameStats` object. It's
simpler to implement (no extra DLL reference, no reflection into the game
process), but it's racing against HDT's private timing rather than owning
the fetch. If reliability turns out to be an issue in practice, switching to
Option B — referencing `lib/HearthMirror.dll` from the plugin and calling
`HearthMirror.Reflection.Client.GetMedalInfo()` with an independent retry
loop — removes that race entirely.

## Configuring the target API URL

HDT has no built-in settings storage for plugin-specific config — `PluginSettings`
only tracks `FileName` / `IsEnabled` / `Name`
(`Hearthstone Deck Tracker/Plugins/PluginSettings.cs`). Anything beyond
enable/disable is entirely up to the plugin, since it's just a normal .NET
assembly loaded from `%AppData%\HearthstoneDeckTracker\Plugins`
(`Hearthstone Deck Tracker/Plugins/PluginManager.cs:19-25`).

**Recommendation:** hardcode a sane default API URL, but read an optional
override from a small JSON settings file if present. This is cheap to add
and avoids a rebuild if the server ever moves or a dev build needs to point
elsewhere.

- Store it at `%AppData%\HearthstoneDeckTracker\Plugins\MyPlugin\settings.json`,
  e.g. `{ "apiUrl": "https://...", "apiKey": "..." }` (see
  [Authentication and payload delivery](#authentication-and-payload-delivery)
  for the `apiKey` field).
- Read it in `OnLoad()`; fall back to the hardcoded default if the file is
  missing or the field is empty.
- `Newtonsoft.Json` is already loaded in the HDT process (it's an HDT
  dependency), so it can be used for parsing without adding a separate
  reference.
- If user-editable configuration is ever needed, `IPlugin.MenuItem` (adds an
  entry to HDT's "Plugins" menu) or `OnButtonPress()` (Options → Tracker →
  Plugins button) can open a small WPF window to edit the URL, persisting
  through the same JSON file. There's no built-in settings panel for
  plugin-specific fields, so this is the standard pattern other HDT plugins
  use when they need user-configurable options.

Plain hardcoding (no override file at all) is a legitimate simplification
only if this stays a private tool pointed at a single, unchanging server —
otherwise every URL change requires a rebuild and redeploy of the plugin.

## Authentication and payload delivery

**Use a Bearer token in the `Authorization` header**
(`Authorization: Bearer <api-key>`) rather than a custom header or a
query-string token. It's the standard convention, every HTTP client has
first-class support for it, and it keeps the key out of URLs/logs that might
capture query strings.

- **Issuance**: the backend generates the key (opaque random string, one per
  user/account) and the user pastes it into the plugin's config — the
  `apiKey` field in the same `settings.json` used for `apiUrl`.
- **Server side**: store keys hashed (like a password), map each to a
  user/account, and support revocation/regeneration. Accept the endpoint over
  TLS only — the key is a bearer credential, so anyone who obtains it can
  post data as that user.
- **Plugin side**: never log the key; treat the settings file like a secret
  (it's already sandboxed to the user's own `%AppData%`, which is an
  acceptable trust boundary for a desktop tool).

Payload is plain JSON, POSTed with a single reused `HttpClient` (don't
create one per request — that leaks sockets over a long HDT session). Wrap
the send in try/catch so a network failure or non-2xx response never bubbles
up into HDT itself:

```csharp
private static readonly HttpClient _http = new HttpClient();

private async Task SendToServer(GameStats gs, PluginSettings settings)
{
    var payload = new
    {
        schemaVersion = 1,
        gameId = gs.GameId,
        startTime = gs.StartTime,
        endTime = gs.EndTime,
        gameMode = gs.GameMode.ToString(),
        format = gs.Format.ToString(),
        result = gs.Result.ToString(),
        wasConceded = gs.WasConceded,
        playerBattleTag = gs.PlayerName,
        opponentBattleTag = gs.OpponentName,
        rank = new { gs.LeagueId, gs.Rank, gs.StarLevel, gs.Stars, gs.LegendRank },
        rankAfter = new { gs.StarLevelAfter, gs.StarsAfter, gs.LegendRankAfter },
    };

    var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl)
    {
        Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

    try
    {
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            Log.Warn($"Rank upload failed: {(int)response.StatusCode} {response.StatusCode}");
    }
    catch (Exception ex)
    {
        Log.Error($"Rank upload error: {ex}");
    }
}
```

This replaces the `SendToServer` stub in the [Sketch](#sketch) above — called
from `OnUpdate()` once the post-match fields land (or the poll times out).

`playerBattleTag` is included even though the token already establishes
*who's authorized to upload* — `gs.PlayerName` is ground truth for *which
BattleTag was actually detected playing this match* (same log-derived
mechanism as `gs.OpponentName`, see
`Hearthstone Deck Tracker/GameEventHandler.cs:793`), which matters if an
account plays under multiple BattleTags, and doubles as an audit trail
against the token-to-account mapping.

`wasConceded` surfaces `gs.WasConceded`, which HDT already tracks
(`Stats/GameStats.cs:173`) but which wasn't in the original payload sketch —
worth keeping since a concede is a normal, common way a ranked match ends and
callers may want to distinguish it from a natural win/loss.

`schemaVersion` is a plain integer bumped whenever a field is added, renamed,
or removed. Costs nothing now and lets the backend branch on payload shape
once the plugin has shipped a few iterations, instead of guessing from
whichever fields happen to be present.

## Game modes uploaded

Only `GameMode.Ranked` is uploaded (see the `UploadedModes` filter in the
[Sketch](#sketch)), matching the project's purpose as a rank tracker.
Non-ranked modes (Arena, Battlegrounds, Casual, Mercenaries, etc.) don't
populate `...After` rank fields the same way — Battlegrounds uses
`BattlegroundsRatingAfter`, Arena uses `ArenaRating`, and so on — so
supporting them later means adding both a new field set to the payload and a
new "is this data ready yet" check to `PollPendingGame`, not just adding an
entry to `UploadedModes`.

## Idempotency and retries

**Idempotency key**: `GameStats.GameId` (`Stats/GameStats.cs:43,76`) is a
`Guid.NewGuid()` generated client-side by HDT for every match — globally
unique, already in the payload, and safe to use as-is. The backend should
enforce a unique constraint on `gameId` and treat a repeat POST of the same
id as a no-op (e.g. `200`/`409` without inserting a duplicate row). This is
what makes retries safe to replay without double-counting a match.

**Failure handling**: the sketch above has no retry — a failed `SendAsync`
(network error, non-2xx) just logs and drops the match. That's an explicit
choice to make, not an oversight left in by accident. If dropped matches are
acceptable (the user can see it happened via the log, and it's a low-stakes
personal stats tool), stop here. If not, add a small local retry queue:

- On send failure, append the JSON payload (plus an attempt count and first-
  attempt timestamp) as one line to a local file, e.g.
  `%AppData%\HearthstoneDeckTracker\Plugins\MyPlugin\pending-uploads.jsonl`.
- On `OnLoad()` (and optionally every few minutes from `OnUpdate()`), read
  the file, retry each entry, and rewrite the file with only the entries
  that still failed.
- Cap it — drop entries past some attempt count or age (e.g. 20 attempts or
  7 days) so a permanently invalid API key doesn't grow the file forever.
- This is safe specifically *because* `gameId` is idempotent: replaying a
  queued entry that actually succeeded server-side (but failed to
  acknowledge locally) can't create a duplicate.

## Threading and TLS

`PluginManager.StartUpdateAsync()` (`Plugins/PluginManager.cs:266-276`) runs
`OnUpdate()` on a `while(true) { Update(); await Task.Delay(100); }` loop
under the UI thread's synchronization context — confirming both that
`OnUpdate()` must never block (already true: `SendToServer` is invoked
without `await`, fire-and-forget) and that `OnGameEnd`/`OnUpdate` callbacks
run on HDT's own thread rather than a background one.

HDT targets `net472` (`Hearthstone Deck Tracker.csproj:7`), and .NET
Framework apps don't always default `ServicePointManager.SecurityProtocol`
to include TLS 1.2 depending on the Windows/.NET configuration. If the
backend is HTTPS-only (it should be, given it accepts a bearer token), the
very first request can fail with a handshake error unless the plugin
explicitly opts in — hence `ServicePointManager.SecurityProtocol |=
SecurityProtocolType.Tls12;` in `OnLoad()` in the sketch above.

## Defensive error handling in `OnUpdate`

`PluginWrapper.Update()` (`Plugins/PluginWrapper.cs:111-138`) counts every
exception thrown out of `IPlugin.OnUpdate()` and disables the plugin once
`PluginManager.MaxExceptions` (100, `Plugins/PluginManager.cs:163`) is
exceeded — at a 100ms tick rate, a bug that throws on every call disables the
plugin within ~10 seconds. The sketch wraps `PollPendingGame()` in its own
try/catch inside `OnUpdate()` for exactly this reason, rather than relying on
`SendToServer`'s internal try/catch alone (which only covers the network
call, not a bug earlier in the polling logic).

## Plugin project setup and deployment

- **Target framework**: `net472`, matching HDT's own target
  (`Hearthstone Deck Tracker.csproj:7`), so the assembly loads cleanly into
  HDT's existing AppDomain via `Assembly.LoadFrom`
  (`Plugins/PluginManager.cs:218`).
- **References**: reference `Hearthstone Deck Tracker.exe` (for
  `Hearthstone_Deck_Tracker.API`/`.Stats`/`.Enums`) and `Newtonsoft.Json`
  with **Copy Local = False** for both. HDT already loads its own copies of
  these in-process; shipping a second copy into the Plugins folder risks a
  stale or mismatched-version assembly being loaded side-by-side, which can
  produce confusing type-identity mismatches even when everything compiles.
- **Output location**: HDT auto-loads every assembly under
  `%AppData%\HearthstoneDeckTracker\Plugins`
  (`Plugins/PluginManager.cs:165-175`, `LoadPluginsFromDefaultPath`). A
  post-build step that copies the compiled DLL straight into
  `...\Plugins\MyPlugin\` saves a manual copy on every rebuild during
  development.

## Source references (HDT `Hearthstone-Deck-Tracker-master`)

- `Hearthstone Deck Tracker/GameEventHandler.cs:243-261` — `UpdatePostGameRanks`
- `Hearthstone Deck Tracker/GameEventHandler.cs:809-811` — early `OnGameEnd` fire for 0-turn discarded games
- `Hearthstone Deck Tracker/GameEventHandler.cs:828-839` — pre-match rank fields set
- `Hearthstone Deck Tracker/GameEventHandler.cs:901` — `GameEvents.OnGameEnd.Execute()`
- `Hearthstone Deck Tracker/GameEventHandler.cs:1034` — `await SaveReplays(...)`
- `Hearthstone Deck Tracker/Stats/GameStats.cs:43,76` — `GameId` (`Guid.NewGuid()`)
- `Hearthstone Deck Tracker/Stats/GameStats.cs:173` — `WasConceded`
- `Hearthstone Deck Tracker/Stats/GameStats.cs:175-218` — rank field definitions
- `Hearthstone Deck Tracker/Enums/GameMode.cs` — `GameMode` enum values
- `Hearthstone Deck Tracker/Config.cs:313` — `DiscardZeroTurnGame` default (`false`)
- `Hearthstone Deck Tracker/API/ActionList.cs` — plugin action dispatch, `MaxPluginExecutionTime`
- `Hearthstone Deck Tracker/Plugins/PluginManager.cs:155` — `MaxPluginExecutionTime = 2000`
- `Hearthstone Deck Tracker/Plugins/PluginManager.cs:163` — `MaxExceptions = 100`
- `Hearthstone Deck Tracker/Plugins/PluginManager.cs:165-175` — `LoadPluginsFromDefaultPath`
- `Hearthstone Deck Tracker/Plugins/PluginManager.cs:218` — `Assembly.LoadFrom`
- `Hearthstone Deck Tracker/Plugins/PluginManager.cs:266-276` — `StartUpdateAsync` (100ms poll loop)
- `Hearthstone Deck Tracker/Plugins/PluginWrapper.cs:111-138` — `Update()`, exception counting
- `Hearthstone Deck Tracker/Hearthstone Deck Tracker.csproj:7` — `TargetFramework` (`net472`)
- `Hearthstone Deck Tracker/Utility/Helper.cs:898` — `RetryWhileNull`
