using System.IO;
using System.Linq;
using System.Numerics;
using Content.Client._WF.ShipPa;
using Content.Client.Audio;
using Content.Shared._WF.Audio.InternetSound;
using Content.Shared._WF.CCVar;
using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Client.Console;
using Robust.Client.ResourceManagement;
using Robust.Shared.Asynchronous;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Network.Transfer;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._WF.Audio.InternetSound;

/// <summary>
/// Receives admin internet sounds and mounts them as resources, so a ship's PA can play them like ordinary
/// sounds. A sound played to everyone runs as this client's own stream from the moment the server says so:
/// a radio needs no tick-exact sync, and the engine's resync of a networked stream skipped at every server
/// hiccup. Also runs the radio window, and frees a track's decoded audio once the server is done with it.
/// </summary>
public sealed partial class InternetSoundSystem : EntitySystem
{
    [Dependency] private ITransferManager _transfer = default!;
    [Dependency] private ITaskManager _task = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IClientConsoleHost _console = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private ShipPaMeshSystem _pa = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ContentAudioSystem _contentAudio = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ClientGlobalSoundSystem _globalSound = default!;

    /// <summary>
    /// How long to keep waiting for a released track's streams to disappear before giving up on freeing it.
    /// Deleting an OpenAL buffer a source still holds silently does nothing but loses our handle to it, so
    /// running out of patience has to mean leaving the audio alone, not freeing it anyway.
    /// </summary>
    private static readonly TimeSpan FreeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The transfer key outlives entity systems, which are rebuilt on reconnect, so it routes to the live instance.
    /// </summary>
    private static readonly List<WeakReference<ReceiverRegistration>> Receivers = new();

    private ReceiverRegistration _registration = default!;

    private sealed class ReceiverRegistration
    {
        public readonly WeakReference<ITransferManager> Transfer;
        public InternetSoundSystem? Receiver;

        public ReceiverRegistration(ITransferManager transfer)
        {
            Transfer = new WeakReference<ITransferManager>(transfer);
        }
    }

    private sealed record Header(int Id, string Title, string Requester, bool IsPa);

    private InternetSoundResources _resources = default!;

    /// <summary>
    /// Tracks the server has released, waiting for their streams to go away before the audio is freed.
    /// </summary>
    private readonly List<(int Id, TimeSpan Deadline)> _releasing = new();

    /// <summary>
    /// Independent transfers; a second ship must not cancel the first ship's download.
    /// </summary>
    private readonly HashSet<int> _loading = new();
    private readonly HashSet<int> _stopped = new();
    private readonly HashSet<int> _received = new();
    private bool _shutdown;

    /// <summary>This client's own stream of each sound played to everyone, by track.</summary>
    private readonly Dictionary<int, EntityUid> _globalStreams = new();

    /// <summary>Sounds told to play before their audio had arrived, and when, so they start that far in.</summary>
    private readonly Dictionary<int, TimeSpan> _pendingPlays = new();

    private InternetSoundPopup? _popup;

    /// <summary>Track the radio is showing, so another one ending doesn't close it.</summary>
    private int _popupId;

    /// <summary>
    /// Status messages for the admin who requested a sound. Also written to the console.
    /// </summary>
    public event Action<string, bool>? StatusReceived;

    /// <summary>
    /// Server state changes, for admins only.
    /// </summary>
    public event Action<InternetSoundStateEvent>? StateReceived;

    /// <summary>
    /// Last state the server sent, so windows opened later know what's playing.
    /// </summary>
    public InternetSoundStateEvent? State { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        _resources = InternetSoundResources.For(_resourceCache);
        InitializeReplay();
        UpdatesBefore.Add(typeof(ShipPaMeshSystem));

        // The engine starts new music on its own audio frame; run after it so we can re-pause in the same frame.
        UpdatesAfter.Add(typeof(AudioSystem));

        lock (Receivers)
        {
            ReceiverRegistration? registration = null;
            for (var i = Receivers.Count - 1; i >= 0; i--)
            {
                if (!Receivers[i].TryGetTarget(out var existing) || !existing.Transfer.TryGetTarget(out var transfer))
                {
                    Receivers.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(transfer, _transfer))
                    registration = existing;
            }

            if (registration == null)
            {
                registration = new ReceiverRegistration(_transfer);
                // The transfer manager owns this registration through its callback; the static list does not.
                var route = registration;
                _transfer.RegisterTransferMessage(InternetSoundProtocol.TransferKey, ev => Route(route, ev));
                Receivers.Add(new WeakReference<ReceiverRegistration>(registration));
            }

            _registration = registration;
            registration.Receiver = this;
        }

        SubscribeNetworkEvent<InternetSoundPlayingEvent>(OnPlaying);
        SubscribeNetworkEvent<InternetSoundReleaseEvent>(OnRelease);
        SubscribeNetworkEvent<InternetSoundStopEvent>(OnStop);
        SubscribeNetworkEvent<InternetSoundStatusEvent>(OnStatus);
        SubscribeNetworkEvent<InternetSoundStateEvent>(OnState);

        Subs.CVar(_cfg, InternetSoundCVars.Volume, OnVolumeChanged);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        ShutdownReplay();
        _shutdown = true;
        _loading.Clear();
        ClosePopup();
        ResumeMusic();

        // Disconnecting took every audio entity with it, so everything still mounted is safe to free.
        foreach (var id in _resources.StoredIds().ToList())
        {
            _pa.ReleasePath(InternetSoundResources.PathFor(id));
            Free(id);
        }

        _releasing.Clear();
        _resources.Clear();
        _globalStreams.Clear();
        _pendingPlays.Clear();

        lock (Receivers)
        {
            if (_registration.Receiver == this)
                _registration.Receiver = null;
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        UpdateReplayResources();
        UpdateReleases();
        UpdateGlobalSound();
    }

    /// <summary>
    /// Frees released tracks once nothing is playing them any more.
    /// </summary>
    private void UpdateReleases()
    {
        for (var i = _releasing.Count - 1; i >= 0; i--)
        {
            var (id, deadline) = _releasing[i];

            if (IsPlaying(id))
            {
                if (_timing.RealTime < deadline)
                    continue;

                // Leaving it mounted wastes memory; freeing it now would waste it permanently.
                Log.Warning($"Internet sound {id} still had streams running when it was released; leaving its audio loaded.");
                _releasing.RemoveAt(i);
                continue;
            }

            Free(id);
            _releasing.RemoveAt(i);
        }
    }

    /// <summary>
    /// Holds a sound played to everyone at the listener's own volume, and keeps music out of its way.
    /// The engine rewrites gain on its audio frames, so this is done every frame rather than once.
    /// </summary>
    private void UpdateGlobalSound()
    {
        var volume = _cfg.GetCVar(CCVars.AdminSoundsEnabled) ? _cfg.GetCVar(InternetSoundCVars.Volume) : 0f;
        var playing = false;

        var query = EntityQueryEnumerator<InternetSoundAudioComponent, AudioComponent>();

        while (query.MoveNext(out _, out _, out var audio))
        {
            audio.Gain = SharedAudioSystem.VolumeToGain(audio.Params.Volume) * volume;

            if (audio.Playing)
                playing = true;
        }

        if (!playing)
        {
            ResumeMusic();
            return;
        }

        // Music loops keep starting tracks while paused; hold them until the sound finishes.
        _contentAudio.PauseMusicWolfgate(WfMusicPauseReason.InternetSound);
        _globalSound.PauseEventMusicWolfgate();
        _contentAudio.EnforceMusicPauseWolfgate();
        _globalSound.EnforceEventMusicPauseWolfgate();
    }

    private void ResumeMusic()
    {
        _contentAudio.ResumeMusicWolfgate(WfMusicPauseReason.InternetSound);
        _globalSound.ResumeEventMusicWolfgate();
    }

    /// <summary>
    /// True while any audio entity is still playing this track.
    /// </summary>
    private bool IsPlaying(int id)
    {
        var path = InternetSoundResources.PathFor(id);
        var query = EntityQueryEnumerator<AudioComponent>();

        while (query.MoveNext(out var audio))
        {
            if (audio.FileName == path)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Drops a track's decoded audio. The resource cache has no way to evict an audio resource, so the file
    /// is swapped for a moment of silence and reloaded: that path frees the old OpenAL buffer and leaves a
    /// harmless stub behind. Only safe once <see cref="IsPlaying"/> is false.
    /// </summary>
    private void Free(int id)
    {
        if (!_resources.Has(id))
            return;

        _resources.Silence(id);

        try
        {
            _resourceCache.ReloadResource<AudioResource>(InternetSoundResources.PathFor(id));
        }
        catch (Exception e)
        {
            Log.Warning($"Couldn't unload internet sound {id}: {e.Message}");
        }

        _resources.Remove(id);
    }

    /// <summary>
    /// Reads the header first so the radio shows while audio is still arriving, then mounts the track and
    /// tells the server it's ready. Nothing plays until the server says so.
    /// </summary>
    private static async void Route(ReceiverRegistration registration, TransferReceivedEvent ev)
    {
        InternetSoundSystem? receiver;
        lock (Receivers)
        {
            receiver = registration.Receiver;
        }

        Header? header = null;
        try
        {
            await using var stream = ev.DataStream;

            var lengthBytes = new byte[4];
            await stream.ReadExactlyAsync(lengthBytes);
            var headerLength = lengthBytes[0] | lengthBytes[1] << 8 | lengthBytes[2] << 16 | lengthBytes[3] << 24;
            if (headerLength is <= 0 or > InternetSoundProtocol.MaxHeaderBytes)
                throw new IOException($"Bad internet sound header length {headerLength}.");

            var headerBytes = new byte[headerLength];
            await stream.ReadExactlyAsync(headerBytes);
            var loaded = header = ReadHeader(headerBytes);
            receiver?._task.RunOnMainThread(() => receiver.BeginLoading(loaded));

            // Capped so a bad or oversized transfer can't exhaust client memory.
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk)) > 0)
            {
                if (buffer.Length + read > InternetSoundProtocol.MaxPayloadBytes)
                    throw new IOException("Internet sound is larger than the client limit.");

                buffer.Write(chunk, 0, read);
            }

            var audio = buffer.ToArray();
            receiver?._task.RunOnMainThread(() => receiver.FinishLoading(loaded, audio));
        }
        catch (Exception e)
        {
            var failed = header;
            receiver?._task.RunOnMainThread(() => receiver.FailLoading(failed, e));
        }
    }

    private static Header ReadHeader(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        return new Header(reader.ReadInt32(), reader.ReadString(), reader.ReadString(), reader.ReadBoolean());
    }

    private void BeginLoading(Header header)
    {
        if (_shutdown || _stopped.Contains(header.Id) || _received.Contains(header.Id))
            return;
        _loading.Add(header.Id);

        // A ship's PA is diegetic: it comes out of speakers you can walk away from, so there's no radio and
        // nothing to turn down beyond the ordinary volume sliders.
        if (!header.IsPa)
            ShowPopup(header);
    }

    /// <summary>
    /// Mounts the track so the engine can resolve it, then confirms to the server.
    /// </summary>
    private void FinishLoading(Header header, byte[] audio)
    {
        if (_shutdown || _stopped.Contains(header.Id) || !_loading.Remove(header.Id))
            return;
        _resources.Store(header.Id, audio);
        // Resource caches outlive reconnects. Reusing an id on another server must replace its old
        // silent stub, while a duplicate transfer in this session must never replace live audio.
        try
        {
            _resourceCache.ReloadResource<AudioResource>(InternetSoundResources.PathFor(header.Id));
            _received.Add(header.Id);
        }
        catch (Exception e)
        {
            FailLoading(header, e);
            return;
        }
        RaiseNetworkEvent(new InternetSoundReadyEvent(header.Id, false));

        // Told to play before the audio was here: join it that far in rather than from the top.
        if (_pendingPlays.Remove(header.Id, out var since))
        {
            StartGlobal(header.Id, (float) (_timing.RealTime - since).TotalSeconds);
            if (_popupId == header.Id)
                _popup?.SetPlaying();
        }
    }

    /// <summary>
    /// Starts this client's stream of a sound played to everyone, <paramref name="offset"/> seconds in.
    /// </summary>
    private void StartGlobal(int id, float offset)
    {
        StopGlobal(id);
        var path = InternetSoundResources.PathFor(id);
        var stream = _audio.PlayGlobal(new ResolvedPathSpecifier(path), Filter.Local(), false,
            AudioParams.Default.WithPlayOffset(offset));
        if (stream == null)
        {
            Log.Warning($"Couldn't play internet sound {id}.");
            return;
        }

        EnsureComp<InternetSoundAudioComponent>(stream.Value.Entity);
        _globalStreams[id] = stream.Value.Entity;
    }

    /// <summary>Stops this client's stream of a sound played to everyone, or every one of them for id 0.</summary>
    private void StopGlobal(int id)
    {
        if (id == 0)
        {
            foreach (var other in _globalStreams.Keys.ToList())
                StopGlobal(other);
            _pendingPlays.Clear();
            return;
        }

        _pendingPlays.Remove(id);
        if (_globalStreams.Remove(id, out var entity) && Exists(entity))
            _audio.Stop(entity);
    }

    private void FailLoading(Header? header, Exception e)
    {
        Log.Error($"Failed to receive internet sound: {e}");

        if (header == null || _received.Contains(header.Id))
            return;

        _loading.Remove(header.Id);
        _stopped.Add(header.Id);
        if (header.IsPa)
            _pa.ReleasePath(InternetSoundResources.PathFor(header.Id));
        _resources.Remove(header.Id);
        if (_popupId == header.Id)
            ClosePopup();
        if (_shutdown)
            return;

        // Tell the server anyway, so one bad client doesn't hold the track up until the deadline.
        RaiseNetworkEvent(new InternetSoundReadyEvent(header.Id, true));
    }

    private void ShowPopup(Header header)
    {
        ClosePopup();

        // Players who muted admin sounds opt out of the radio too. A ship's PA is diegetic and always shows
        // nothing, so there's no case here for a sound they'd hear but have no window for.
        if (!_cfg.GetCVar(CCVars.AdminSoundsEnabled))
            return;

        var popup = new InternetSoundPopup(header.Title, header.Requester, _cfg.GetCVar(InternetSoundCVars.Volume));
        popup.VolumeChanged += volume => _cfg.SetCVar(InternetSoundCVars.Volume, volume);
        popup.VolumeReleased += () => _cfg.SaveToFile();

        // Stopping is just turning it down; the sound belongs to the server now.
        popup.StopPressed += () =>
        {
            _cfg.SetCVar(InternetSoundCVars.Volume, 0f);
            _cfg.SaveToFile();
            ClosePopup();
        };

        popup.OnClose += () =>
        {
            if (_popup != popup)
                return;

            _popup = null;
            _popupId = 0;
        };

        _popup = popup;
        _popupId = header.Id;

        // Middle top; the window keeps itself on screen.
        popup.OpenCenteredAt(new Vector2(0.5f, 0f));
    }

    private void ClosePopup()
    {
        if (_popup is not { } popup)
            return;

        _popup = null;
        _popupId = 0;
        popup.Close();
    }

    private void OnPlaying(InternetSoundPlayingEvent ev)
    {
        if (_popupId == ev.Id)
            _popup?.SetPlaying();

        if (_stopped.Contains(ev.Id))
            return;

        // The server can say play before this client has even seen the header; keep the word until the audio comes.
        if (_received.Contains(ev.Id))
            StartGlobal(ev.Id, 0f);
        else
            _pendingPlays[ev.Id] = _timing.RealTime;
    }

    private void OnRelease(InternetSoundReleaseEvent ev)
    {
        if (_replay.Replay != null)
            return;
        _stopped.Add(ev.Id);
        _loading.Remove(ev.Id);
        _pa.ReleasePath(InternetSoundResources.PathFor(ev.Id));
        if (_releasing.Any(entry => entry.Id == ev.Id))
            return;

        _releasing.Add((ev.Id, _timing.RealTime + FreeTimeout));
    }

    private void OnStop(InternetSoundStopEvent ev)
    {
        StopGlobal(ev.Id);
        if (_replay.Replay != null)
            return;
        if (ev.Id == 0)
            _loading.Clear();
        else
        {
            _stopped.Add(ev.Id);
            _loading.Remove(ev.Id);
            _pa.ReleasePath(InternetSoundResources.PathFor(ev.Id));
        }

        // Several tracks can be in play, so one ending mustn't close another's radio.
        if (ev.Id == 0 || ev.Id == _popupId)
            ClosePopup();
    }

    private void OnStatus(InternetSoundStatusEvent ev)
    {
        if (ev.IsError)
            _console.WriteError(null, ev.Message);
        else
            _console.WriteLine(null, ev.Message);

        StatusReceived?.Invoke(ev.Message, ev.IsError);
    }

    /// <summary>
    /// Asks the server what's playing. Admin windows call this when they open.
    /// </summary>
    public void RequestState()
    {
        RaiseNetworkEvent(new InternetSoundStateRequestEvent());
    }

    private void OnState(InternetSoundStateEvent ev)
    {
        State = ev;
        StateReceived?.Invoke(ev);
    }

    private void OnVolumeChanged(float volume)
    {
        _popup?.SetVolume(volume);
    }
}
