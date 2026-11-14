using Content.Client._WF.RoundSetup;
using Content.Client.Audio;
using Content.Client.Eui;
using Content.Client.GameTicking;
using Content.Client.Lobby.UI;
using Content.Client.Message;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.Voting;
using Content.Shared._Forge.CCVars; // Forge-Change
using Content.Shared.CCVar;
using Robust.Client;
using Robust.Client.Console;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Lobby
{
    public sealed partial class LobbyState : Robust.Client.State.State
    {
        [Dependency] private IBaseClient _baseClient = default!;
        [Dependency] private IConfigurationManager _cfg = default!;
        [Dependency] private IClientConsoleHost _consoleHost = default!;
        [Dependency] private IEntityManager _entityManager = default!;
        [Dependency] private IResourceCache _resourceCache = default!;
        [Dependency] private IUserInterfaceManager _userInterfaceManager = default!;
        [Dependency] private IGameTiming _gameTiming = default!;
        [Dependency] private IVoteManager _voteManager = default!;

        private ClientGameTicker _gameTicker = default!;
        private ContentAudioSystem _contentAudioSystem = default!;

        protected override Type? LinkedScreenType { get; } = typeof(LobbyGui);
        public LobbyGui? Lobby;

        protected override void Startup()
        {
            if (_userInterfaceManager.ActiveScreen == null)
            {
                return;
            }

            Lobby = (LobbyGui) _userInterfaceManager.ActiveScreen;

            var chatController = _userInterfaceManager.GetUIController<ChatUIController>();
            _gameTicker = _entityManager.System<ClientGameTicker>();
            _contentAudioSystem = _entityManager.System<ContentAudioSystem>();
            _contentAudioSystem.LobbySoundtrackChanged += UpdateLobbySoundtrackInfo;

            chatController.SetMainChat(true);

            _voteManager.SetPopupContainer(Lobby.VoteContainer);
            LayoutContainer.SetAnchorPreset(Lobby, LayoutContainer.LayoutPreset.Wide);

            var lobbyNameCvar = _cfg.GetCVar(CCVars.ServerLobbyName);
            var serverName = _baseClient.GameInfo?.ServerName ?? string.Empty;

            Lobby.ServerName.SetMessage(FormattedMessage.FromUnformatted(string.IsNullOrEmpty(lobbyNameCvar)
                ? serverName
                : lobbyNameCvar));

            InitializeRoundSetupHome();
            UpdateLobbyUi();

            Lobby.CharacterPreview.CharacterSetupButton.OnPressed += OnSetupPressed;
            Lobby.ReadyButton.OnPressed += OnReadyPressed;

            _gameTicker.InfoBlobUpdated += UpdateLobbyUi;
            _gameTicker.LobbyStatusUpdated += LobbyStatusUpdated;
            _gameTicker.LobbyLateJoinStatusUpdated += LobbyLateJoinStatusUpdated;
            _cfg.OnValueChanged(ForgeCCVars.UiSimplified, OnSimplifiedChanged); // Forge-Change: the background follows the option at once
        }

        private void OnSimplifiedChanged(bool _) => UpdateLobbyBackground(); // Forge-Change

        protected override void Shutdown()
        {
            _cfg.UnsubValueChanged(ForgeCCVars.UiSimplified, OnSimplifiedChanged); // Forge-Change
            var chatController = _userInterfaceManager.GetUIController<ChatUIController>();
            chatController.SetMainChat(false);
            _gameTicker.InfoBlobUpdated -= UpdateLobbyUi;
            _gameTicker.LobbyStatusUpdated -= LobbyStatusUpdated;
            _gameTicker.LobbyLateJoinStatusUpdated -= LobbyLateJoinStatusUpdated;
            _contentAudioSystem.LobbySoundtrackChanged -= UpdateLobbySoundtrackInfo;

            _voteManager.ClearPopupContainer();

            Lobby!.CharacterPreview.CharacterSetupButton.OnPressed -= OnSetupPressed;
            Lobby!.ReadyButton.OnPressed -= OnReadyPressed;

            ShutdownRoundSetupHome();
            Lobby = null;
        }

        public void SwitchState(LobbyGui.LobbyGuiState state)
        {
            // Yeah I hate this but LobbyState contains all the badness for now.
            Lobby?.SwitchState(state);
        }

        private void OnSetupPressed(BaseButton.ButtonEventArgs args)
        {
            SetReady(false);
            Lobby?.SwitchState(LobbyGui.LobbyGuiState.CharacterSetup);
        }

        private void OnReadyPressed(BaseButton.ButtonEventArgs args)
        {
            JoinFromRoundSetupHome();
        }

        // Forge-Change-Start: the two lobby clocks change once a second at most. Assigning Label.Text always invalidates
        // the label's layout and each Loc.GetString runs Fluent, so both are now done only when the shown value changes
        // instead of every frame. The wording and the branches are the ones the upstream FrameUpdate had.
        private const long ClockNotStarted = long.MinValue;
        private const long ClockPaused = -1;
        private const long ClockSoon = -2;
        private const long ClockRightNow = -3;
        private const long ClockRightNowQuestion = -4;

        private long? _stationTimeStamp;
        private string _stationTimeText = string.Empty;
        private long? _startTimeStamp;
        private string _startTimeText = string.Empty;

        public override void FrameUpdate(FrameEventArgs e)
        {
            if (_gameTicker.IsGameStarted)
            {
                SetClock(Lobby!.StartTime, string.Empty);
                var roundTime = _gameTiming.CurTime.Subtract(_gameTicker.RoundStartTimeSpan);
                // Hours and minutes are all the text shows, so they are all that can change it.
                var shown = roundTime.Hours * 60L + roundTime.Minutes;
                if (_stationTimeStamp != shown)
                {
                    _stationTimeStamp = shown;
                    _stationTimeText = Loc.GetString("lobby-state-player-status-round-time", ("hours", roundTime.Hours), ("minutes", roundTime.Minutes));
                }
                SetClock(Lobby.StationTime, _stationTimeText);
                return;
            }

            if (_stationTimeStamp != ClockNotStarted)
            {
                _stationTimeStamp = ClockNotStarted;
                _stationTimeText = Loc.GetString("lobby-state-player-status-round-not-started");
            }
            SetClock(Lobby!.StationTime, _stationTimeText);

            long stamp;
            var difference = _gameTicker.StartTime - _gameTiming.CurTime;
            if (_gameTicker.Paused)
                stamp = ClockPaused;
            else if (_gameTicker.StartTime < _gameTiming.CurTime)
                stamp = ClockSoon;
            else if (difference.TotalSeconds < 0)
                stamp = difference.TotalSeconds < -5 ? ClockRightNowQuestion : ClockRightNow;
            else
                stamp = (long) difference.TotalSeconds; // whole seconds left, the finest step the countdown shows

            if (_startTimeStamp != stamp)
            {
                _startTimeStamp = stamp;
                if (stamp == ClockSoon)
                {
                    _startTimeText = Loc.GetString("lobby-state-soon");
                }
                else
                {
                    var text = stamp switch
                    {
                        ClockPaused => Loc.GetString("lobby-state-paused"),
                        ClockRightNowQuestion => Loc.GetString("lobby-state-right-now-question"),
                        ClockRightNow => Loc.GetString("lobby-state-right-now-confirmation"),
                        _ => difference.TotalHours >= 1
                            ? $"{Math.Floor(difference.TotalHours)}:{difference.Minutes:D2}:{difference.Seconds:D2}"
                            : $"{difference.Minutes}:{difference.Seconds:D2}",
                    };
                    _startTimeText = Loc.GetString("lobby-state-round-start-countdown-text", ("timeLeft", text));
                }
            }
            SetClock(Lobby.StartTime, _startTimeText);
        }

        /// <summary>Assigns a clock label only when its text differs, so an unchanged clock costs no layout pass.</summary>
        private static void SetClock(Label label, string text)
        {
            if (label.Text != text)
                label.Text = text;
        }
        // Forge-Change-End

        private void LobbyStatusUpdated()
        {
            UpdateLobbyBackground();
            UpdateLobbyUi();
        }

        private void LobbyLateJoinStatusUpdated()
        {
            UpdateRoundSetupHome();
        }

        private void UpdateLobbyUi()
        {
            if (_gameTicker.IsGameStarted)
            {
                Lobby!.ReadyButton.Text = Loc.GetString("lobby-state-ready-button-join-state");
                Lobby!.ReadyButton.ToggleMode = false;
                Lobby!.ReadyButton.Pressed = false;
                Lobby!.ObserveButton.Disabled = false;
            }
            else
            {
                Lobby!.StartTime.Text = string.Empty;
                Lobby!.ReadyButton.Text = Loc.GetString(Lobby!.ReadyButton.Pressed ? "lobby-state-player-status-ready": "lobby-state-player-status-not-ready");
                Lobby!.ReadyButton.ToggleMode = true;
                Lobby!.ReadyButton.Disabled = false;
                Lobby!.ReadyButton.Pressed = _gameTicker.AreWeReady;
                Lobby!.ObserveButton.Disabled = true;
            }

            UpdateRoundSetupHome();

            if (_gameTicker.ServerInfoBlob != null)
            {
                //Lobby!.ServerInfo.SetInfoBlob(_gameTicker.ServerInfoBlob); // Frontier: ???
            }
        }

        private void UpdateLobbySoundtrackInfo(LobbySoundtrackChangedEvent ev)
        {
            if (ev.SoundtrackFilename == null)
            {
                Lobby!.LobbySong.SetMarkup(Loc.GetString("lobby-state-song-no-song-text"));
            }
            else if (
                ev.SoundtrackFilename != null
                && _resourceCache.TryGetResource<AudioResource>(ev.SoundtrackFilename, out var lobbySongResource)
                )
            {
                var lobbyStream = lobbySongResource.AudioStream;

                var title = string.IsNullOrEmpty(lobbyStream.Title)
                    ? Loc.GetString("lobby-state-song-unknown-title")
                    : lobbyStream.Title;

                var artist = string.IsNullOrEmpty(lobbyStream.Artist)
                    ? Loc.GetString("lobby-state-song-unknown-artist")
                    : lobbyStream.Artist;

                var markup = Loc.GetString("lobby-state-song-text",
                    ("songTitle", title),
                    ("songArtist", artist));

                Lobby!.LobbySong.SetMarkup(markup);
            }
        }

        private void UpdateLobbyBackground()
        {
            if (_gameTicker.LobbyBackground != null)
            {
                var path = _gameTicker.LobbyBackground;
                var animated = SymphonyAnimatedBackground.IsAnimated(path);
                // Forge-Change-Start: the simplified interface leaves an animated background out. Its sprite sheet holds
                // every frame in one texture, so it is not even loaded and the lobby is left on a plain dark screen.
                if (animated && _cfg.GetCVar(ForgeCCVars.UiSimplified))
                {
                    Lobby!.Background.Texture = null;
                    Lobby.AnimatedBackground.Play(null, null);
                    return;
                }
                // Forge-Change-End
                Robust.Client.Graphics.Texture sheet = _resourceCache.GetResource<TextureResource>(path);
                Lobby!.Background.Texture = animated ? null : sheet;
                Lobby.AnimatedBackground.Play(animated ? sheet : null, animated ? path : null);
            }
            else
            {
                Lobby!.Background.Texture = null;
                Lobby.AnimatedBackground.Play(null, null);
            }
        }

        private void SetReady(bool newReady)
        {
            if (_gameTicker.IsGameStarted)
            {
                return;
            }

            _consoleHost.ExecuteCommand($"toggleready {newReady}");
        }
    }
}
