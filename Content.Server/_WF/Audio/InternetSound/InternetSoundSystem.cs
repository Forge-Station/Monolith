using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.ShipPa;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared._WF.Audio.InternetSound;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ShipPa;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using Robust.Shared.Enums;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Network;
using Robust.Shared.Network.Transfer;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Audio.InternetSound;

/// <summary>
/// Internet sounds: fetches a link with yt-dlp, mounts the result as a resource on the server and receiving
/// clients, then plays it like any other sound â€” to everyone at once for admins, or out of a ship's PA
/// speakers, which crew can do from the shuttle console.
/// </summary>
/// <remarks>
/// PA assets go only to clients aboard or near their ship, including remote views. Recipients keep them
/// until the track ends. <see cref="InternetSoundCVars.MaxConcurrent"/> bounds retained audio and transfers.
/// </remarks>
public sealed partial class InternetSoundSystem : EntitySystem
{
    [Dependency] private ITransferManager _transfer = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ITaskManager _task = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IResourceManager _resourceManager = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ShipPaSystem _shipPa = default!;
    [Dependency] private ShipPaPlaylistSystem _playlist = default!;

    private const int MaxTitleLength = 200;

    /// <summary>
    /// Key the ship PA runs internet sounds under, so a second one replaces the first rather than layering.
    /// </summary>
    public const string TrackKey = "wf-internet-sound";

    private InternetSoundResources _resources = default!;

    /// <summary>Everything in flight, from the moment a fetch starts until its audio is released.</summary>
    private readonly Dictionary<int, Track> _tracks = new();

    /// <summary>The track each ship is running, so a second request replaces rather than layers.</summary>
    private readonly Dictionary<EntityUid, int> _byGrid = new();

    /// <summary>The track going out to everyone at once, if any. Only admins can start one.</summary>
    private int _global;

    private int _lastId;
    private readonly InternetSoundTransferQueue _outgoing = new();

    private enum TrackState
    {
        Fetching,
        Sending,
        Playing,
    }

    /// <summary>
    /// One internet sound on its way to, or out of, the speakers.
    /// </summary>
    private sealed class Track
    {
        public int Id;
        public string Title = string.Empty;

        /// <summary>Who asked for it, for the admin window and the logs.</summary>
        public string Requester = string.Empty;

        /// <summary>Hides the requester from players and other admins; only the admin logs name them.</summary>
        public bool Stealth;

        /// <summary>The name players and admin windows see.</summary>
        public string ShownRequester => Stealth ? string.Empty : Requester;

        /// <summary>The ship it plays on, or null for everyone at once.</summary>
        public EntityUid? Grid;

        public TrackState State;
        public CancellationTokenSource? Fetch;

        /// <summary>The admin to report progress to, if an admin started it.</summary>
        public ICommonSession? Admin;

        /// <summary>The console user to notify if this request fails after being accepted.</summary>
        public ICommonSession? ErrorRecipient;

        /// <summary>Clients that haven't confirmed they have the audio yet.</summary>
        public HashSet<NetUserId> Waiting = new();

        /// <summary>Connections already offered this asset, including queued/in-flight transfers.</summary>
        public readonly HashSet<INetChannel> Recipients = new();

        public TimeSpan Deadline;

        /// <summary>The audio entity, for a sound played to everyone. PA tracks live in ShipPaSystem.</summary>
        public EntityUid? Audio;

        public InternetSoundTransfer? Transfer;

        public bool IsPa => Grid != null;
    }

    public override void Initialize()
    {
        base.Initialize();

        _resources = InternetSoundResources.For(_resourceManager);

        // Send-only here; clients register the receiving side.
        _transfer.RegisterTransferMessage(InternetSoundProtocol.TransferKey);

        SubscribeNetworkEvent<InternetSoundStateRequestEvent>(OnStateRequest);
        SubscribeNetworkEvent<InternetSoundReadyEvent>(OnClientReady);
        SubscribeLocalEvent<ShipPaTrackFinishedEvent>(OnTrackFinished);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Stop(null));
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        base.Shutdown();

        foreach (var track in _tracks.Values)
        {
            track.Fetch?.Cancel();
            track.Transfer?.Dispose();
        }

        _tracks.Clear();
        _byGrid.Clear();
        _global = 0;

        // Nothing is playing after this, so anything still mounted is dead weight.
        foreach (var id in _resources.StoredIds().ToList())
        {
            _resources.Remove(id);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdatePaAudience();

        foreach (var track in _tracks.Values.ToList())
        {
            switch (track.State)
            {
                // Whoever hasn't confirmed by the deadline misses it rather than holding everyone up.
                case TrackState.Sending when _timing.CurTime >= track.Deadline:
                    StartPlayback(track);
                    break;

                // The engine deletes a finished non-looping audio entity by itself.
                case TrackState.Playing when !track.IsPa && track.Audio is { } audio && !Exists(audio):
                    Finish(track);
                    break;

                // A ship that blew up or left takes its track with it.
                case TrackState.Playing when track.Grid is { } grid && !Exists(grid):
                    Finish(track);
                    break;
            }
        }
    }

    /// <summary>
    /// Fetches a link and plays it to every connected player. Admin only; refused while another global sound
    /// is in play. A <paramref name="stealth"/> sound doesn't say who played it.
    /// </summary>
    public void Play(ICommonSession? admin, string url, bool stealth = false)
    {
        if (_global != 0)
        {
            Report(admin, Loc.GetString("wf-internet-sound-busy", ("title", _tracks[_global].Title)), true);
            return;
        }

        Begin(admin, admin?.Name ?? Loc.GetString("wf-internet-sound-server-name"), url, null, out _, stealth: stealth);
    }

    /// <summary>
    /// Fetches a link and plays it out of one ship's speakers, positioned and distorted like anything else
    /// its PA carries. Replaces whatever that ship was already playing.
    /// </summary>
    public void PlayOverPa(ICommonSession? admin, string url, EntityUid grid, bool stealth = false)
    {
        PlayOverPa(admin, admin?.Name ?? Loc.GetString("wf-internet-sound-server-name"), url, grid, out _, stealth: stealth);
    }

    /// <summary>
    /// Fetches a link for one ship's PA on behalf of whoever asked. <paramref name="error"/> is a loc string
    /// for the caller to show when this returns false.
    /// </summary>
    public bool PlayOverPa(ICommonSession? admin, string requester, string url, EntityUid grid, out string? error, ICommonSession? errorRecipient = null, bool stealth = false)
    {
        error = null;

        if (!Exists(grid) || TerminatingOrDeleted(grid))
        {
            error = Loc.GetString("wf-internet-sound-no-ship");
            Report(admin, error, true);
            return false;
        }

        var (online, _, _) = _shipPa.CountSpeakers(grid);

        if (online == 0)
        {
            error = Loc.GetString("wf-internet-sound-no-speakers", ("ship", _shipPa.GetShipName(grid)));
            Report(admin, error, true);
            return false;
        }

        // A ship replacing its own track doesn't add to the load, so it doesn't count against the cap.
        if (!_byGrid.ContainsKey(grid) && _tracks.Count >= _cfg.GetCVar(InternetSoundCVars.MaxConcurrent))
        {
            error = Loc.GetString("wf-internet-sound-too-many");
            Report(admin, error, true);
            return false;
        }

        return Begin(admin, requester, url, grid, out error, errorRecipient, stealth);
    }

    private bool Begin(ICommonSession? admin, string requester, string url, EntityUid? grid, out string? error, ICommonSession? errorRecipient = null, bool stealth = false)
    {
        error = null;

        if (!_cfg.GetCVar(InternetSoundCVars.Enabled))
        {
            error = Loc.GetString("wf-internet-sound-disabled");
            Report(admin, error, true);
            return false;
        }

        // A ship replacing its own track doesn't add to the load, so it doesn't count against the cap.
        var replacing = grid is { } existing && _byGrid.ContainsKey(existing);

        if (!replacing && _tracks.Count >= _cfg.GetCVar(InternetSoundCVars.MaxConcurrent))
        {
            error = Loc.GetString("wf-internet-sound-too-many");
            Report(admin, error, true);
            return false;
        }

        // Require encrypted links before starting a fetch or replacing an existing track.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443)
        {
            error = Loc.GetString("wf-internet-sound-invalid-url");
            Report(admin, error, true);
            return false;
        }

        // Whatever that ship had is replaced, so its audio comes back rather than piling up.
        if (grid is { } target && _byGrid.TryGetValue(target, out var old))
            Drop(_tracks[old]);

        var id = ++_lastId;
        var fetch = new CancellationTokenSource();

        var track = new Track
        {
            Id = id,
            Title = uri.AbsoluteUri,
            Requester = requester,
            Stealth = stealth,
            Grid = grid,
            State = TrackState.Fetching,
            Fetch = fetch,
            Admin = admin,
            ErrorRecipient = errorRecipient,
        };

        _tracks[id] = track;

        if (grid is { } uid)
            _byGrid[uid] = id;
        else
            _global = id;

        // Ship PA is positional, so stereo would only cost memory for an image the speakers throw away.
        var stereo = _cfg.GetCVar(InternetSoundCVars.Stereo) && grid == null;

        var settings = new InternetSoundDownloader.Settings(
            _cfg.GetCVar(InternetSoundCVars.YtDlpPath),
            _cfg.GetCVar(InternetSoundCVars.FfmpegPath),
            _cfg.GetCVar(InternetSoundCVars.MaxDuration),
            _cfg.GetCVar(InternetSoundCVars.Timeout),
            _cfg.GetCVar(InternetSoundCVars.MaxSizeMb),
            Math.Clamp(_cfg.GetCVar(InternetSoundCVars.SampleRate), 8000, 48000),
            stereo ? 2 : 1,
            Math.Clamp(_cfg.GetCVar(InternetSoundCVars.Bitrate), 8, 192),
            (long) Math.Max(1, _cfg.GetCVar(InternetSoundCVars.MaxDownloadMb)) * 1024 * 1024);

        Report(admin, Loc.GetString("wf-internet-sound-fetching", ("url", uri.AbsoluteUri)), false);

        _adminLogger.Add(LogType.AdminCommands, LogImpact.Low,
            $"{requester} requested {(stealth ? "stealth " : "")}internet sound {uri.AbsoluteUri}{(grid is { } ship ? $" for {_shipPa.GetShipName(ship)}" : " for everyone")}");

        SendState();

        Task.Run(async () =>
        {
            try
            {
                var result = await InternetSoundDownloader.Fetch(uri.AbsoluteUri, settings, fetch.Token);
                _task.RunOnMainThread(() => Distribute(id, fetch, uri.AbsoluteUri, result));
            }
            catch (OperationCanceledException) when (fetch.IsCancellationRequested)
            {
                // Stopped while fetching.
            }
            catch (OperationCanceledException)
            {
                _task.RunOnMainThread(() => Fail(id, fetch, Loc.GetString("wf-internet-sound-error-timeout")));
            }
            catch (InternetSoundDownloader.FetchException e)
            {
                _task.RunOnMainThread(() => Fail(id, fetch, Loc.GetString(e.LocKey, ("detail", e.Message))));
            }
            catch (Exception e)
            {
                _task.RunOnMainThread(() =>
                {
                    Log.Error($"Internet sound fetch for {uri.AbsoluteUri} failed: {e}");
                    Fail(id, fetch, Loc.GetString("wf-internet-sound-error-unknown"));
                });
            }
        });

        return true;
    }

    /// <summary>
    /// Stops everything: cancels fetches, silences every track and frees their audio.
    /// </summary>
    public void Stop(ICommonSession? admin)
    {
        _playlist.ClearAll();

        foreach (var track in _tracks.Values.ToList())
        {
            Drop(track);
        }

        Report(admin, Loc.GetString("wf-internet-sound-stopped"), false);
        _adminLogger.Add(LogType.AdminCommands, LogImpact.Low, $"{admin?.Name ?? "Server"} stopped every internet sound");
        SendState();
    }

    /// <summary>
    /// Stops whatever one ship is playing. True if there was something to stop.
    /// </summary>
    public bool StopForGrid(EntityUid grid)
    {
        if (!_byGrid.TryGetValue(grid, out var id) || !_tracks.TryGetValue(id, out var track))
            return false;

        Drop(track);
        SendState();
        return true;
    }

    /// <summary>
    /// True when this ship has an internet sound fetching, sending or playing.
    /// </summary>
    public bool IsPlayingOn(EntityUid grid)
    {
        return _byGrid.ContainsKey(grid);
    }

    /// <summary>
    /// True when a track for this ship would not be turned away for want of room: it already holds a slot,
    /// or the server-wide cap has one free.
    /// </summary>
    public bool HasFreeSlot(EntityUid grid)
    {
        return _byGrid.ContainsKey(grid) || _tracks.Count < _cfg.GetCVar(InternetSoundCVars.MaxConcurrent);
    }

    /// <summary>
    /// Mounts and distributes the audio. PA playback begins immediately; global admin sounds retain
    /// their readiness barrier. Late PA downloads join the timeline rather than delaying everyone.
    /// </summary>
    private void Distribute(int id, CancellationTokenSource fetch, string url, InternetSoundDownloader.Result result)
    {
        // Stopped or replaced while converting.
        if (!_tracks.TryGetValue(id, out var track) || track.Fetch != fetch)
            return;

        track.Fetch = null;
        track.Title = result.Title;
        _resources.Store(id, result.Audio);

        // Reading the length here both gives us a duration and proves the Ogg is one the engine can play,
        // before any of it goes out to clients.
        try
        {
            _audio.GetAudioLength(InternetSoundResources.PathFor(id));
        }
        catch (Exception e)
        {
            Log.Error($"Internet sound \"{result.Title}\" from {url} isn't loadable audio: {e}");
            ReportError(track, Loc.GetString("wf-internet-sound-error-unplayable"));
            Drop(track);
            SendState();
            return;
        }

        track.Transfer = new InternetSoundTransfer(Encode(id, result.Title, track.ShownRequester, track.IsPa, result.Audio));
        track.Waiting.Clear();

        if (track.IsPa)
        {
            SendToPaAudience(track);
        }
        else
        {
            foreach (var session in _players.Sessions)
            {
                track.Waiting.Add(session.UserId);
                SendOnce(track, session);
            }
        }

        track.State = TrackState.Sending;
        track.Deadline = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(InternetSoundCVars.ReadyTimeout));

        var megabytes = result.Audio.Length / (1024f * 1024f);
        Report(track.Admin, Loc.GetString("wf-internet-sound-sending",
            ("title", result.Title),
            ("count", track.Recipients.Count),
            ("size", megabytes.ToString("0.0"))), false);

        _adminLogger.Add(LogType.AdminCommands, LogImpact.Low,
            $"{track.Requester} is sending internet sound \"{result.Title}\" ({url}) to {track.Recipients.Count} players");

        SendState();

        // PA sources are created locally only once their asset exists. They need no readiness barrier.
        if (track.IsPa || track.Waiting.Count == 0)
            StartPlayback(track);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
        {
            // A reconnect gets a new connection and a fresh client resource root.
            foreach (var track in _tracks.Values)
                track.Recipients.Remove(args.Session.Channel);
            return;
        }

        // The next audience check also covers body attachment, remote-view changes and ship movement.
        if (args.NewStatus == SessionStatus.InGame)
            _nextAudienceUpdate = TimeSpan.Zero;
    }

    private void OnClientReady(InternetSoundReadyEvent ev, EntitySessionEventArgs args)
    {
        if (!_tracks.TryGetValue(ev.Id, out var track) || track.State != TrackState.Sending)
            return;

        if (ev.Failed)
            Log.Warning($"{args.SenderSession.Name} couldn't load internet sound \"{track.Title}\".");

        if (!track.Waiting.Remove(args.SenderSession.UserId) || track.Waiting.Count > 0)
            return;

        StartPlayback(track);
    }

    /// <summary>
    /// Everyone who's going to have the audio has it, so play it like any other sound.
    /// </summary>
    private void StartPlayback(Track track)
    {
        if (track.State != TrackState.Sending)
            return;

        var path = InternetSoundResources.PathFor(track.Id);
        var sound = new SoundPathSpecifier(path);

        if (track.Grid is { } grid)
        {
            if (!Exists(grid) || TerminatingOrDeleted(grid))
            {
                ReportError(track, Loc.GetString("wf-internet-sound-no-ship"));
                Drop(track);
                SendState();
                return;
            }

            if (!_shipPa.StartTrack(grid, TrackKey, sound, AudioParams.Default, track.Title))
            {
                ReportError(track, Loc.GetString("wf-internet-sound-no-speakers", ("ship", _shipPa.GetShipName(grid))));
                Drop(track);
                SendState();
                return;
            }

            track.State = TrackState.Playing;

            Report(track.Admin, Loc.GetString("wf-internet-sound-playing-pa",
                ("title", track.Title),
                ("ship", _shipPa.GetShipName(grid))), false);
        }
        else
        {
            var stream = _audio.PlayGlobal(path, Filter.Broadcast(), true, AudioParams.Default);

            if (stream == null)
            {
                ReportError(track, Loc.GetString("wf-internet-sound-error-unplayable"));
                Drop(track);
                SendState();
                return;
            }

            EnsureComp<InternetSoundAudioComponent>(stream.Value.Entity);
            track.Audio = stream.Value.Entity;
            track.State = TrackState.Playing;

            Report(track.Admin, Loc.GetString("wf-internet-sound-playing", ("title", track.Title)), false);
            RaiseNetworkEvent(new InternetSoundPlayingEvent(track.Id));
        }

        SendState();
    }

    private void OnTrackFinished(ref ShipPaTrackFinishedEvent ev)
    {
        if (ev.Key != TrackKey || !_byGrid.TryGetValue(ev.Grid, out var id) || !_tracks.TryGetValue(id, out var track))
            return;

        Finish(track);
    }

    /// <summary>
    /// The sound played through to its end on its own.
    /// </summary>
    private void Finish(Track track)
    {
        Drop(track);
        SendState();
    }

    /// <summary>
    /// Takes a track out of play wherever it got to: cancels its fetch, stops its streams, tells clients to
    /// close the radio and frees the audio everywhere.
    /// </summary>
    private void Drop(Track track)
    {
        track.Fetch?.Cancel();
        track.Fetch = null;

        if (track.Grid is { } grid)
        {
            _shipPa.StopAlarm(grid, TrackKey);
            _byGrid.Remove(grid);
        }
        else
        {
            if (track.Audio is { } audio && Exists(audio))
                _audio.Stop(audio);

            if (_global == track.Id)
                _global = 0;
        }

        track.Transfer?.Dispose();
        track.Transfer = null;
        _tracks.Remove(track.Id);

        RaiseNetworkEvent(new InternetSoundStopEvent(track.Id));

        // Only safe now every stream of it is stopped. Clients still wait for the entities to actually go
        // away before freeing the decoded audio.
        RaiseNetworkEvent(new InternetSoundReleaseEvent(track.Id));
        _resources.Remove(track.Id);
    }

    private void Fail(int id, CancellationTokenSource fetch, string message)
    {
        if (!_tracks.TryGetValue(id, out var track) || track.Fetch != fetch)
            return;

        ReportError(track, message);
        Drop(track);
        SendState();
    }

    private void OnStateRequest(InternetSoundStateRequestEvent ev, EntitySessionEventArgs args)
    {
        if (_adminManager.HasAdminFlag(args.SenderSession, AdminFlags.Fun))
            RaiseNetworkEvent(BuildState(), Filter.SinglePlayer(args.SenderSession));
    }

    /// <summary>
    /// Tells every admin who can play sounds what's going on, so their windows stay accurate.
    /// </summary>
    private void SendState()
    {
        var admins = _adminManager.ActiveAdmins
            .Where(session => _adminManager.HasAdminFlag(session, AdminFlags.Fun))
            .ToList();

        if (admins.Count > 0)
            RaiseNetworkEvent(BuildState(), Filter.Empty().AddPlayers(admins));
    }

    private InternetSoundStateEvent BuildState()
    {
        var entries = new List<InternetSoundEntry>();

        foreach (var track in _tracks.Values)
        {
            var ship = track.Grid is { } grid && Exists(grid) ? _shipPa.GetShipName(grid) : null;
            var netGrid = track.Grid is { } uid && Exists(uid) ? GetNetEntity(uid) : (NetEntity?) null;

            entries.Add(new InternetSoundEntry(
                track.Id,
                track.Title,
                track.ShownRequester,
                ship,
                netGrid,
                track.State == TrackState.Fetching,
                track.State == TrackState.Sending));
        }

        return new InternetSoundStateEvent(entries, _global != 0);
    }

    private async void Send(INetChannel channel, InternetSoundTransfer transfer)
    {
        try
        {
            await _outgoing.SendAsync(channel, transfer,
                () => OpenTransfer(channel, transfer.Token));
        }
        catch (OperationCanceledException) when (transfer.Token.IsCancellationRequested)
        {
            // Stop or replacement stopped this transfer before its next chunk.
        }
        catch (Exception e)
        {
            // Usually a client that disconnected mid-transfer.
            Log.Warning($"Failed to send internet sound to {channel.UserName}: {e.Message}");
        }
    }

    private Task<Stream> OpenTransfer(INetChannel channel, CancellationToken cancel)
    {
        // Queued senders can resume on a worker thread; network connection state belongs to the game thread.
        var completion = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        _task.RunOnMainThread(() =>
        {
            try
            {
                cancel.ThrowIfCancellationRequested();
                completion.SetResult(_transfer.StartTransfer(channel, InternetSoundProtocol.TransferKey));
            }
            catch (Exception e)
            {
                completion.SetException(e);
            }
        });
        return completion.Task;
    }

    private void ReportError(Track track, string message)
    {
        // The pilot may have closed the console or changed bodies while the download was running.
        // Route the failure to their session rather than to the console or the ship's current pilot.
        if (track.ErrorRecipient is { Status: SessionStatus.InGame } recipient && recipient != track.Admin)
            _popup.PopupCursor(message, recipient, PopupType.MediumCaution);

        Report(track.Admin, message, true);
    }

    private void Report(ICommonSession? admin, string message, bool isError)
    {
        if (admin == null || admin.Status != SessionStatus.InGame)
        {
            Log.Info(message);
            return;
        }

        if (isError)
            _popup.PopupCursor(message, admin, PopupType.MediumCaution);

        RaiseNetworkEvent(new InternetSoundStatusEvent(message, isError), Filter.SinglePlayer(admin));
    }

    /// <summary>
    /// Header first, length-prefixed, so clients can show the radio before the audio finishes arriving.
    /// </summary>
    private static byte[] Encode(int id, string title, string requester, bool isPa, byte[] audio)
    {
        using var header = new MemoryStream();
        using (var writer = new BinaryWriter(header, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(id);
            writer.Write(title.Length > MaxTitleLength ? title[..MaxTitleLength] : title);
            writer.Write(requester);
            writer.Write(isPa);
        }

        using var payload = new MemoryStream((int) header.Length + audio.Length + 4);
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((int) header.Length);
            writer.Write(header.GetBuffer(), 0, (int) header.Length);
            writer.Write(audio);
        }

        return payload.ToArray();
    }
}
