using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Windows.Controls;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Plugins;
using Hearthstone_Deck_Tracker.Stats;
using Hearthstone_Deck_Tracker.Utility.Logging;
using Newtonsoft.Json;

namespace RankTrackerPlugin
{
	// See design/rank-data-fetch-plugin.md for the full design writeup this
	// implementation follows (timing, retries, threading, etc.).
	public class RankTrackerPlugin : IPlugin
	{
		// Only these modes get uploaded. A HashSet keeps this a one-line change
		// later instead of a scattered set of if-checks.
		private static readonly HashSet<GameMode> UploadedModes = new HashSet<GameMode> { GameMode.Ranked };
		private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(8);
		private static readonly HttpClient Http = new HttpClient();

		private UploaderSettings _settings = new UploaderSettings();
		private GameStats? _pendingGame;
		private DateTime _pendingSince;

		public string Name => "Rank Tracker Uploader";
		public string Description => "Uploads finished ranked match results (including post-match rank) to a configured HTTP endpoint.";
		public string ButtonText => "Reload settings";
		public string Author => "you";
		public Version Version => new Version(1, 0, 0);
		public MenuItem MenuItem => null!;

		public void OnLoad()
		{
			// net472 does not always default to TLS 1.2; most HTTPS backends require it.
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			_settings = UploaderSettings.Load();
			GameEvents.OnGameEnd.Add(OnGameEnd);
			Log.Info($"{Name} loaded. Uploading to {_settings.ApiUrl}");
		}

		public void OnUnload() => _pendingGame = null;

		// Lets the user reload settings.json (e.g. after pasting in an API key)
		// without restarting HDT. There's no built-in settings panel for
		// plugin-specific fields, so this is the simplest available hook.
		public void OnButtonPress()
		{
			_settings = UploaderSettings.Load();
			Log.Info($"{Name}: settings reloaded. Uploading to {_settings.ApiUrl}");
		}

		private void OnGameEnd()
		{
			var gs = Core.Game.CurrentGameStats;
			if(gs == null)
				return;

			// Defends against HDT's 0-turn early-return path
			// (GameEventHandler.cs:809-811), which fires OnGameEnd before
			// GameMode/Rank/etc. are populated. Turns is always set by this
			// point in both code paths.
			if(gs.Turns < 1 || !UploadedModes.Contains(gs.GameMode))
				return;

			// Grab the reference now - CurrentGameStats will be replaced by
			// the time the next game starts.
			_pendingGame = gs;
			_pendingSince = DateTime.UtcNow;
			Log.Info($"{Name}: ranked match ended (gameId={gs.GameId}), waiting up to {PollTimeout.TotalSeconds:0}s for post-match rank...");
		}

		// Called by HDT roughly every 100ms (Plugins/PluginManager.cs:266-276).
		public void OnUpdate()
		{
			try
			{
				PollPendingGame();
			}
			catch(Exception ex)
			{
				// HDT disables a plugin after 100 exceptions thrown out of
				// OnUpdate (PluginManager.MaxExceptions) - never let this leak.
				Log.Error($"{Name}: OnUpdate error: {ex}");
			}
		}

		private void PollPendingGame()
		{
			var gs = _pendingGame;
			if(gs == null)
				return;

			var haveAfterData = gs.LegendRankAfter > 0 || gs.StarLevelAfter > 0 || gs.StarsAfter > 0;
			var timedOut = DateTime.UtcNow - _pendingSince > PollTimeout;

			if(haveAfterData || timedOut)
			{
				_pendingGame = null;
				if(timedOut && !haveAfterData)
					Log.Warn($"{Name}: timed out waiting for post-match rank for game {gs.GameId}, uploading with whatever rank data is available.");
				SendToServer(gs);
			}
		}

		private async void SendToServer(GameStats gs)
		{
			if(string.IsNullOrEmpty(_settings.ApiKey))
			{
				Log.Warn($"{Name}: no API key configured (see settings.json), skipping upload for game {gs.GameId}.");
				return;
			}

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

			var json = JsonConvert.SerializeObject(payload);
			var request = new HttpRequestMessage(HttpMethod.Post, _settings.ApiUrl)
			{
				Content = new StringContent(json, Encoding.UTF8, "application/json")
			};
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

			Log.Info($"{Name}: preparing to upload game {gs.GameId} to {_settings.ApiUrl}");
			Log.Info($"{Name}: payload for {gs.GameId}: {json}");

			try
			{
				var response = await Http.SendAsync(request);
				var body = await response.Content.ReadAsStringAsync();
				if(!response.IsSuccessStatusCode)
					Log.Warn($"{Name}: upload failed for game {gs.GameId}: {(int)response.StatusCode} {response.StatusCode}|{body}");
				else
					Log.Info($"{Name}: upload OK for game {gs.GameId}: {(int)response.StatusCode} {response.StatusCode}|{body}");
			}
			catch(Exception ex)
			{
				// No retry queue in this version - a failed upload is dropped.
				// See design/rank-data-fetch-plugin.md "Idempotency and retries"
				// if that turns out to not be acceptable.
				Log.Error($"{Name}: upload error for game {gs.GameId}: {ex}");
			}
		}
	}
}
