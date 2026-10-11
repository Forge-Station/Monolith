using Content.Client._WF.Shuttles.Systems;
using System.Numerics;
using Content.Shared._WF.ShipPa;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Replays.Playback;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Spawners;
using Robust.Shared.Utility;

namespace Content.Client._WF.ShipPa;

/// <summary>
/// One foreground PA programme per listener, with a second source only for a handoff. All source
/// positions are real speakers; the timeline continues even while out of range or preempted by an alarm.
/// </summary>
public sealed partial class ShipPaMeshSystem : EntitySystem
{
    [Dependency] private IReplayPlaybackManager _replay = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ShuttleCameraListenerSystem _listener = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IMapManager _maps = default!;

    private EntityUid? _subtitleGrid;
    private readonly List<Voice> _voices = new();
    private readonly HashSet<string> _released = new();
    private readonly HashSet<int> _captioned = new();
    private readonly Queue<int> _captionOrder = new();
    private Candidate? _selected;
    private float _selectionRemaining;
    private Label? _subtitle;
    private PanelContainer? _subtitlePanel;
    private TimeSpan _subtitleUntil;
    private int _subtitlePriority;
    private string? _lastCaption;
    private TimeSpan _lastCaptionAt;
    private bool _shuttingDown;

    private sealed class Voice
    {
        public EntityUid Entity;
        public EntityUid Speaker;
        public ShipPaBroadcast Broadcast = default!;
        public float Fade;
    }

    private sealed record Candidate(EntityUid Grid, EntityUid Speaker, ShipPaBroadcast Broadcast, float Score);

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(AudioSystem));
    }

    public override void Shutdown()
    {
        _shuttingDown = true;
        StopVoices();
        _subtitlePanel?.Dispose();
        _subtitlePanel = null;
        _subtitle = null;
        base.Shutdown();
    }

    /// <summary>Local voices and deduplication belong to a replay position, not the whole recording.</summary>
    public void ResetReplayPosition()
    {
        StopVoices();
        _selected = null;
        _selectionRemaining = 0f;
        _released.Clear();
        _captioned.Clear();
        _captionOrder.Clear();
        _lastCaption = null;
        _subtitleUntil = TimeSpan.Zero;
        if (_subtitlePanel != null)
            _subtitlePanel.Visible = false;
    }

    /// <summary>Release may arrive before the grid's replicated stop; prevent that old state restarting it.</summary>
    public void ReleasePath(string path)
    {
        _released.Add(path);
        if (_selected?.Broadcast.Path == path)
            _selected = null;
        for (var i = _voices.Count - 1; i >= 0; i--)
        {
            if (_voices[i].Broadcast.Path != path)
                continue;
            StopVoice(_voices[i]);
            _voices.RemoveAt(i);
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (_shuttingDown)
            return;

        _selectionRemaining -= frameTime;
        if (_selectionRemaining <= 0f)
        {
            _selectionRemaining = 0.1f;
            Select();
            SelectCaption();
        }

        Reconcile();
        for (var i = _voices.Count - 1; i >= 0; i--)
        {
            var voice = _voices[i];
            if (!TryComp(voice.Entity, out AudioComponent? audio))
            {
                _voices.RemoveAt(i);
                continue;
            }

            var wanted = _selected is { } selected && selected.Speaker == voice.Speaker
                         && selected.Broadcast.Id == voice.Broadcast.Id && IsAvailable(selected);
            voice.Fade = Math.Clamp(voice.Fade + (wanted ? 1f : -1f) * frameTime / ShipPaPlaybackPolicy.FadeSeconds, 0f, 1f);
            if (voice.Fade == 0f && !wanted)
            {
                StopVoice(voice);
                _voices.RemoveAt(i);
                continue;
            }

            Apply(voice, audio);
        }

        if (_subtitlePanel != null)
        {
            var remaining = (float) (_subtitleUntil - _timing.RealTime).TotalSeconds;
            _subtitlePanel.Visible = remaining > 0f;
            _subtitlePanel.Modulate = new Color(1f, 1f, 1f, Math.Clamp(remaining, 0f, 1f));
        }
    }

    private bool IsAvailable(Candidate candidate)
    {
        if (_released.Contains(candidate.Broadcast.Path) || !candidate.Broadcast.IsActive(_timing.CurTime)
            || !TryComp(candidate.Grid, out ShipPaBroadcastComponent? state)
            || !state.Broadcasts.Exists(b => b.Id == candidate.Broadcast.Id)
            || !TryComp(candidate.Speaker, out ShipPaSpeakerComponent? speaker) || !speaker.Enabled
            || !TryComp(candidate.Speaker, out TransformComponent? xform)
            || !xform.Anchored || xform.GridUid != candidate.Grid || TerminatingOrDeleted(candidate.Speaker))
            return false;

        var listener = _listener.GetListener();
        return xform.MapID == listener.MapId
               && (_xform.GetWorldPosition(candidate.Speaker) - listener.Position).Length() < speaker.Range;
    }

    private void Select()
    {
        Candidate? best = null;
        Candidate? current = null;
        var listener = _listener.GetListener();
        var query = EntityQueryEnumerator<ShipPaSpeakerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var speaker, out var xform))
        {
            if (!speaker.Enabled || !xform.Anchored || xform.MapID != listener.MapId
                || xform.GridUid is not { } grid || !TryComp(grid, out ShipPaBroadcastComponent? state)
                || state.Broadcasts.Count == 0)
                continue;

            var delta = _xform.GetWorldPosition(uid) - listener.Position;
            var distance = delta.Length();
            if (distance >= speaker.Range)
                continue;

            var occlusion = _audio.GetOcclusion(listener, delta, distance, uid);
            var score = ShipPaPlaybackPolicy.Score(distance, speaker.Range, occlusion);
            foreach (var broadcast in state.Broadcasts)
            {
                if (_released.Contains(broadcast.Path))
                    continue;

                var candidate = new Candidate(grid, uid, broadcast, score);
                var hasAsset = !string.IsNullOrEmpty(broadcast.Path)
                               && _resources.ContentFileExists(new ResPath(broadcast.Path));
                // Do not query the resource cache before the file arrives: its missing-file cache would
                // otherwise poison later playback. An incomplete download must not block other audio.
                if (!broadcast.IsActive(_timing.CurTime) || !hasAsset)
                    continue;

                if (_selected is { } old && old.Speaker == uid && old.Broadcast.Id == broadcast.Id)
                    current = candidate;
                if (Better(candidate, best))
                    best = candidate;
            }
        }

        if (best != null && current != null && best.Broadcast.Id == current.Broadcast.Id
            && !ShipPaPlaybackPolicy.ShouldSwitch(current.Score, best.Score))
            best = current;

        _selected = best;

    }

    private void SelectCaption()
    {
        // Captions belong to the whole grid, independently of speaker audibility and downloads.
        EntityUid? grid = null;
        if (TryComp(_players.LocalEntity, out TransformComponent? playerTransform))
            grid = playerTransform.GridUid;
        else if (_maps.TryFindGridAt(_listener.GetListener(), out var eyeGrid, out _))
            grid = eyeGrid;

        if (_subtitleGrid != grid)
            _subtitleUntil = TimeSpan.Zero;
        _subtitleGrid = grid;
        if (grid == null || !TryComp(grid, out ShipPaBroadcastComponent? state))
            return;

        ShipPaBroadcast? caption = null;
        foreach (var broadcast in state.Broadcasts)
        {
            // Keep urgent text visible for its full lifetime; only equal or higher priority may interrupt.
            if (_timing.RealTime < _subtitleUntil && broadcast.Priority < _subtitlePriority)
                continue;
            if (broadcast.Caption == null || _captioned.Contains(broadcast.Id)
                || _released.Contains(broadcast.Path)
                || (!broadcast.Loop && _timing.CurTime >= broadcast.RetainUntil))
                continue;
            if (caption == null || broadcast.Priority > caption.Priority
                || (broadcast.Priority == caption.Priority && broadcast.Id > caption.Id))
                caption = broadcast;
        }

        if (caption != null)
            ShowCaption(grid.Value, caption);
    }

    private static bool Better(Candidate candidate, Candidate? other)
    {
        if (other == null)
            return true;
        if (candidate.Broadcast.Priority != other.Broadcast.Priority)
            return candidate.Broadcast.Priority > other.Broadcast.Priority;
        if (candidate.Score != other.Score)
            return candidate.Score < other.Score;
        if (candidate.Broadcast.Id != other.Broadcast.Id)
            return candidate.Broadcast.Id > other.Broadcast.Id;
        return candidate.Speaker.Id < other.Speaker.Id;
    }

    private void Reconcile()
    {
        if (_selected is not { } target || !IsAvailable(target))
        {
            _selected = null;
            return;
        }

        foreach (var voice in _voices)
        {
            if (voice.Broadcast.Id == target.Broadcast.Id && voice.Speaker == target.Speaker && Exists(voice.Entity))
                return;
        }

        // Rapid movement or an emergency interrupt during a fade must never allocate a third voice.
        if (_voices.Count >= ShipPaPlaybackPolicy.MaxSources)
        {
            var drop = _voices[0].Fade <= _voices[1].Fade ? 0 : 1;
            StopVoice(_voices[drop]);
            _voices.RemoveAt(drop);
        }

        var speaker = Comp<ShipPaSpeakerComponent>(target.Speaker);
        var parameters = target.Broadcast.Params.WithLoop(target.Broadcast.Loop).WithPitchScale(1f).WithVariation(null)
            .WithMaxDistance(speaker.Range).WithReferenceDistance(3f).WithRolloffFactor(1.5f)
            .WithPlayOffset(target.Broadcast.Position(_timing.CurTime));
        var resolved = _audio.ResolveSound(new SoundPathSpecifier(target.Broadcast.Path));
        AudioResource resource;
        try
        {
            resource = _resources.GetResource<AudioResource>(target.Broadcast.Path, useFallback: false);
        }
        catch (Exception e)
        {
            // A malformed runtime file must not throw every frame or substitute an unrelated sound.
            _released.Add(target.Broadcast.Path);
            _selected = null;
            Log.Warning($"Cannot play PA track {target.Broadcast.Path}: {e.Message}");
            return;
        }
        // Start muted; PlayEntity starts the underlying source immediately.
        var stream = _audio.PlayEntity(resource.AudioStream, target.Speaker, resolved, parameters.WithVolume(-80f));
        if (stream == null)
            return;

        // The engine's normal local source despawn must use the remaining duration, not a fresh track.
        _audio.SetPlaybackPosition(stream.Value.Entity, target.Broadcast.Position(_timing.CurTime));
        // This system owns voice lifetime, including the wait before a scheduled start.
        // The engine's ordinary despawn timer would otherwise consume that wait from the clip.
        RemComp<TimedDespawnComponent>(stream.Value.Entity);
        var prepared = _timing.CurTime < target.Broadcast.Start;
        if (prepared)
        {
            var audio = Comp<AudioComponent>(stream.Value.Entity);
            audio.Pause();
            audio.PlaybackPosition = 0f;
        }
        var marker = EnsureComp<ShipPaAudioComponent>(stream.Value.Entity);
        marker.BroadcastId = target.Broadcast.Id;
        marker.Speaker = target.Speaker;
        _voices.Add(new Voice { Entity = stream.Value.Entity, Speaker = target.Speaker, Broadcast = target.Broadcast, Fade = prepared ? 1f : 0f });
    }

    private void Apply(Voice voice, AudioComponent audio)
    {
        // No Doppler on the PA. OpenAL pitches a source by its velocity relative to the listener, and a
        // pitch shift would also pull the voice off the ship's shared timeline. Matching the listener's
        // velocity every frame, after the engine's own write, leaves no relative motion to shift by.
        audio.Velocity = _players.LocalEntity is { } local ? _physics.GetMapLinearVelocity(local) : Vector2.Zero;

        if (!TryComp(voice.Speaker, out ShipPaSpeakerComponent? speaker)
            || !TryComp(voice.Speaker, out TransformComponent? xform))
        {
            audio.Gain = 0f;
            return;
        }

        var listener = _listener.GetListener();
        var delta = _xform.GetWorldPosition(voice.Speaker) - listener.Position;
        var distance = delta.Length();
        if (xform.MapID != listener.MapId || distance >= speaker.Range || !speaker.Enabled)
        {
            audio.Gain = 0f;
            return;
        }

        // Evaluate from the engine's base each frame, never add muffling to last frame's result.
        audio.Occlusion = _audio.GetOcclusion(listener, delta, distance, voice.Speaker) + 3f * speaker.Distortion;
        var tick = MathF.Floor((float) _timing.RealTime.TotalSeconds * 12f);
        var noise = MathF.Sin(tick * 12.9898f + voice.Speaker.Id * 78.233f) * 43758.5453f;
        noise -= MathF.Floor(noise);
        var flutter = noise > 1f - 0.8f * speaker.Distortion ? 0.15f : 1f;
        // Extra edge fade prevents crossing the hard MaxDistance boundary at nonzero gain.
        var edge = Math.Clamp((speaker.Range - distance) / 2f, 0f, 1f);
        audio.Gain = SharedAudioSystem.VolumeToGain(voice.Broadcast.Params.Volume + speaker.Volume - 6f * speaker.Distortion)
                     * voice.Fade * edge * flutter;

        if (_timing.CurTime < voice.Broadcast.Start)
        {
            audio.Gain = 0f;
            audio.Pause();
            audio.PlaybackPosition = 0f;
            return;
        }

        if (_timing.Paused || (_replay.Replay != null && !_replay.Playing))
        {
            audio.Pause();
            return;
        }
        if (!audio.Playing && voice.Broadcast.IsPlaying(_timing.CurTime))
            audio.StartPlaying();

        // Correct only significant clock drift; seeking every frame would itself create glitches.
        var expected = voice.Broadcast.Position(_timing.CurTime);
        var error = Math.Abs(audio.PlaybackPosition - expected);
        if (voice.Broadcast.Loop)
            error = Math.Min(error, Math.Abs(voice.Broadcast.Length - error));
        if (error > 0.15f)
            audio.PlaybackPosition = expected;
    }

    private void ShowCaption(EntityUid grid, ShipPaBroadcast broadcast)
    {
        _captioned.Add(broadcast.Id);
        _captionOrder.Enqueue(broadcast.Id);
        // Bounded history survives brief PVS loss and prevents a handoff replaying captions.
        while (_captionOrder.Count > 256)
            _captioned.Remove(_captionOrder.Dequeue());

        // General quarters also sends a chime announcement containing the same text.
        if (_lastCaption == broadcast.Caption && _timing.RealTime - _lastCaptionAt < TimeSpan.FromSeconds(10))
            return;
        _lastCaption = broadcast.Caption;
        _lastCaptionAt = _timing.RealTime;

        if (_subtitle == null)
        {
            _subtitle = new Label
            {
                HorizontalAlignment = Control.HAlignment.Center,
                VerticalAlignment = Control.VAlignment.Top,
                MouseFilter = Control.MouseFilterMode.Ignore,
                FontColorShadowOverride = Color.Black,
                ShadowOffsetXOverride = 1,
                ShadowOffsetYOverride = 1,
            };
            var glass = new StyleBoxFlat
            {
                BackgroundColor = new Color(0.06f, 0.10f, 0.14f, 0.55f),
                BorderColor = new Color(0.65f, 0.85f, 1f, 0.55f),
                BorderThickness = new Thickness(1),
            };
            glass.SetContentMarginOverride(StyleBox.Margin.Horizontal, 16);
            glass.SetContentMarginOverride(StyleBox.Margin.Vertical, 8);
            _subtitlePanel = new PanelContainer
            {
                Name = "ShipPaSubtitle",
                PanelOverride = glass,
                Margin = new Thickness(12, 72, 12, 0),
                MouseFilter = Control.MouseFilterMode.Ignore,
            };
            _subtitlePanel.AddChild(_subtitle);
            LayoutContainer.SetAnchorPreset(_subtitlePanel, LayoutContainer.LayoutPreset.CenterTop);
            LayoutContainer.SetGrowHorizontal(_subtitlePanel, LayoutContainer.GrowDirection.Both);
            _ui.PopupRoot.AddChild(_subtitlePanel);
        }

        // Label uses plain text, so a fetched title cannot inject UI markup.
        var title = Loc.GetString("ship-pa-subtitle-title", ("ship", Name(grid)));
        _subtitle.Text = title + "\n" + WrapCaption(broadcast.Caption!);
        _subtitle.FontColorOverride = broadcast.Color;
        _subtitlePriority = broadcast.Priority;
        _subtitleUntil = _timing.RealTime + TimeSpan.FromSeconds(8);
    }

    private static string WrapCaption(string text)
    {
        // Keep the transparent subtitle compact even for long announcement text or URL-like titles.
        text = text.Replace('\n', ' ').Replace('\r', ' ');
        if (text.Length > 240)
            text = text[..237] + "...";
        var lines = new List<string>();
        while (text.Length > 64)
        {
            var split = text.LastIndexOf(' ', 64, 64);
            if (split <= 0)
                split = 64;
            lines.Add(text[..split]);
            text = text[split..].TrimStart();
        }
        lines.Add(text);
        return string.Join("\n", lines);
    }

    private void StopVoice(Voice voice)
    {
        if (!Exists(voice.Entity) || TerminatingOrDeleted(voice.Entity))
            return;
        if (TryComp(voice.Entity, out AudioComponent? audio))
            audio.StopPlaying();
        // Dispose the source before an internet sound buffer becomes eligible for release.
        EntityManager.DeleteEntity(voice.Entity);
    }

    private void StopVoices()
    {
        foreach (var voice in _voices)
            StopVoice(voice);
        _voices.Clear();
    }
}
