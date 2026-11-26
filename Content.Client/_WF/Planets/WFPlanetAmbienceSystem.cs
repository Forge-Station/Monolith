using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;
using Content.Shared.Audio;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WF.Planets;

/// <summary>Client-only planet soundscape: at most two crossfading beds and one accent, played globally.</summary>
public sealed partial class WFPlanetAmbienceSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;

    private const float FadeRate = 12f;
    private const float AudibleMargin = 2f;

    /// <summary>Occlusion per second the muffle eases in or out by, so a way down fades rather than cuts.</summary>
    private const float OcclusionRate = 3f;

    private EntityUid? _loop;
    private EntityUid? _outgoingLoop;
    private string? _playlistKey;
    private int _loopIndex = -1;
    private float _crossfadeElapsed;
    private float _crossfadeDuration = 4f;
    private TimeSpan _nextLoop;
    private EntityUid? _oneShot;
    private string? _profileId;
    private NetEntity? _world;
    private float _occlusion;
    private string? _accentKey;
    private float _loopVolume = WFPlanetAmbience.SilentVolume;
    private float _oneShotVolume = WFPlanetAmbience.SilentVolume;
    private float _sliderVolume;
    private TimeSpan _nextOneShot;
    private bool _awaitingFirstAccent = true;

    /// <summary>The occlusion the ambience streams are held at, easing toward the listener's layer.</summary>
    public float Occlusion => _occlusion;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;

        Subs.CVar(_cfg, CCVars.AmbienceVolume, OnAmbienceVolumeChanged, true);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Shutdown()
    {
        StopAll();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        if (!TryGetContext(out var profile, out var layerOffset, out var night, out var occlusion, out var world))
        {
            StopAll();
            return;
        }

        if (_profileId != profile.ID)
        {
            // Planet boundaries are a hard stop; within one world, such as into its cavern, the beds crossfade.
            if (world == null || world != _world)
                StopAll();

            _profileId = profile.ID;
            _awaitingFirstAccent = true;
        }

        _world = world;

        CullFinishedStreams();

        var hullOffset = IsAboardHull() ? profile.HullVolumeOffset : 0f;
        var commonOffset = layerOffset + hullOffset + _sliderVolume;
        var loopTarget = ClampVolume(profile.LoopVolume + commonOffset);
        var oneShotTarget = ClampVolume(profile.OneShotVolume + commonOffset);

        UpdateLoop(profile, night, loopTarget, frameTime);
        UpdateOneShot(profile, night, oneShotTarget, frameTime);
        UpdateOcclusion(occlusion, frameTime);
    }

    /// <summary>The ambience profile, layer offset, night flag, muffle and world for the local player's map.</summary>
    private bool TryGetContext(out WFPlanetAmbiencePrototype profile, out float layerOffset, out bool night,
        out float occlusion, out NetEntity? world)
    {
        profile = default!;
        night = false;
        layerOffset = WFPlanetAmbience.SilentVolume;
        occlusion = 0f;
        world = null;

        if (_player.LocalEntity is not { } player ||
            !TryComp(player, out TransformComponent? xform) ||
            xform.MapUid is not { } map)
            return false;

        // A newly-created transit map may arrive one state before its soundscape snapshot.
        if (!HasComp<WFPlanetEnvironmentComponent>(map) &&
            TryComp<CEZTransitMapComponent>(map, out var transit) && transit.LowerMap is { } lower)
            map = lower;
        if (!TryComp<WFPlanetAmbienceComponent>(map, out var ambience) ||
            !TryComp<WFPlanetEnvironmentComponent>(map, out var environment) ||
            !_proto.TryIndex(ambience.Profile, out var resolved))
            return false;

        profile = resolved;
        night = environment.IsNight;
        layerOffset = ambience.VolumeOffset;
        occlusion = MathF.Max(0f, ambience.Occlusion);
        world = CompOrNull<WFPlanetLayerComponent>(map)?.Network;
        return true;
    }

    /// <summary>Eases the muffle toward the layer's and holds every playing stream at it; global streams keep what is set.</summary>
    private void UpdateOcclusion(float target, float frameTime)
    {
        _occlusion = Approach(_occlusion, target, OcclusionRate * frameTime);
        SetOcclusion(_loop);
        SetOcclusion(_outgoingLoop);
        SetOcclusion(_oneShot);
    }

    private void SetOcclusion(EntityUid? stream)
    {
        // Each write resets the source's EFX filter, so only a change is written.
        if (TryComp<AudioComponent>(stream, out var audio) && !audio.Occlusion.Equals(_occlusion))
            audio.Occlusion = _occlusion;
    }

    private bool IsAboardHull()
    {
        if (_player.LocalEntity is not { } player || !TryComp(player, out TransformComponent? xform))
            return false;

        // Map-grid ground and detached terrain are exposed; any other grid is a hull with the indoor mix.
        return xform.GridUid is { } grid &&
               grid != xform.MapUid &&
               !HasComp<WFDetachedTerrainComponent>(grid);
    }

    /// <summary>Starts, rotates and crossfades the looping bed.</summary>
    private void UpdateLoop(WFPlanetAmbiencePrototype profile, bool night, float target, float frameTime)
    {
        var playlist = profile.GetLoops(night);
        var phase = profile.DayLoops.Count == 0 && profile.NightLoops.Count == 0 ? "all" : night ? "night" : "day";
        var key = profile.ID + "/" + phase;
        var audible = target > WFPlanetAmbience.SilentVolume + AudibleMargin;
        var change = _playlistKey != key;
        // Finish an existing blend before starting another; never allocate a third bed.
        if (audible && playlist.Count > 0 && _outgoingLoop == null &&
            (_loop == null || change || playlist.Count > 1 && _timing.CurTime >= _nextLoop))
        {
            var index = change ? 0 : (_loopIndex + 1) % playlist.Count;
            var stream = _audio.PlayGlobal(playlist[index], Filter.Local(), false,
                AudioParams.Default.WithLoop(true).WithVolume(WFPlanetAmbience.SilentVolume));
            if (stream != null)
            {
                _outgoingLoop = _loop;
                _loop = stream.Value.Entity;
                _playlistKey = key;
                _loopIndex = index;
                Log.Debug($"Planet ambience: {key}, {stream.Value.Component.FileName}");
                _crossfadeElapsed = 0;
                var length = (float) _audio.GetAudioLength(new ResolvedPathSpecifier(stream.Value.Component.FileName)).TotalSeconds;
                if (!float.IsFinite(length) || length <= 0)
                    length = 60f;
                _crossfadeDuration = Math.Clamp(profile.CrossfadeSeconds, 0.1f, MathF.Max(0.1f, length / 4));
                _nextLoop = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(_crossfadeDuration + 1, length - _crossfadeDuration));
            }
        }

        _loopVolume = Approach(_loopVolume, target, FadeRate * frameTime);
        _crossfadeElapsed = MathF.Min(_crossfadeElapsed + frameTime, _crossfadeDuration);
        var blend = Math.Clamp(_crossfadeElapsed / _crossfadeDuration, 0f, 1f);
        var gain = SharedAudioSystem.VolumeToGain(_loopVolume);
        // Equal-power overlap avoids an audible dip between different recordings.
        _audio.SetGain(_loop, gain * MathF.Max(0f, MathF.Sin(blend * MathF.PI / 2)));
        _audio.SetGain(_outgoingLoop, gain * MathF.Max(0f, MathF.Cos(blend * MathF.PI / 2)));
        if (blend >= 1)
            _outgoingLoop = StopStream(_outgoingLoop);
        if (target <= WFPlanetAmbience.SilentVolume && _loopVolume <= WFPlanetAmbience.SilentVolume)
        {
            _loop = StopStream(_loop);
            _outgoingLoop = StopStream(_outgoingLoop);
        }
    }

    /// <summary>Fades the playing accent, or plays and schedules the next one.</summary>
    private void UpdateOneShot(WFPlanetAmbiencePrototype profile, bool night, float target, float frameTime)
    {
        var sounds = profile.GetOneShots(night);
        var phase = profile.DayOneShots.Count == 0 && profile.NightOneShots.Count == 0 ? "all" : night ? "night" : "day";
        var key = profile.ID + "/" + phase;
        if (_accentKey != key)
        {
            _oneShot = StopStream(_oneShot);
            _accentKey = key;
            _awaitingFirstAccent = true;
        }
        if (target <= WFPlanetAmbience.SilentVolume + AudibleMargin)
            _awaitingFirstAccent = true;
        else if (_awaitingFirstAccent)
        {
            // Start the arrival cue after reaching an audible layer, not during silent orbit.
            _nextOneShot = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(6f, 12f));
            _awaitingFirstAccent = false;
        }

        if (_oneShot != null)
        {
            _oneShotVolume = Approach(_oneShotVolume, target, FadeRate * frameTime);
            _audio.SetVolume(_oneShot, _oneShotVolume);

            if (target <= WFPlanetAmbience.SilentVolume && _oneShotVolume <= WFPlanetAmbience.SilentVolume)
                _oneShot = StopStream(_oneShot);

            return;
        }

        if (_timing.CurTime < _nextOneShot)
            return;

        if (target <= WFPlanetAmbience.SilentVolume + AudibleMargin || sounds.Count == 0)
        {
            ScheduleOneShot(profile);
            return;
        }

        _oneShotVolume = target;
        var sound = _random.Pick(sounds);
        _oneShot = _audio.PlayGlobal(
            sound,
            Filter.Local(),
            false,
            AudioParams.Default
                .WithVariation(0.04f)
                .WithVolume(_oneShotVolume))?.Entity;
        Log.Debug($"Planet accent: {profile.ID}, {sound}");
        ScheduleOneShot(profile);
    }

    private void CullFinishedStreams()
    {
        if (_loop != null && !TryComp<AudioComponent>(_loop, out _))
            _loop = null;

        if (_outgoingLoop != null && !TryComp<AudioComponent>(_outgoingLoop, out _))
            _outgoingLoop = null;

        if (_oneShot != null && !TryComp<AudioComponent>(_oneShot, out _))
            _oneShot = null;
    }

    private void ScheduleOneShot(WFPlanetAmbiencePrototype profile)
    {
        var maximum = MathF.Max(profile.MinInterval, profile.MaxInterval);
        var minimum = MathF.Min(profile.MinInterval, maximum);
        var delay = minimum < maximum ? _random.NextFloat(minimum, maximum) : minimum;
        _nextOneShot = _timing.CurTime + TimeSpan.FromSeconds(delay);
    }

    private void OnAmbienceVolumeChanged(float gain)
    {
        var volume = SharedAudioSystem.GainToVolume(gain);
        _sliderVolume = float.IsFinite(volume) ? MathF.Max(volume, WFPlanetAmbience.SilentVolume) : WFPlanetAmbience.SilentVolume;
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        StopAll();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        StopAll();
    }

    private EntityUid? StopStream(EntityUid? stream)
    {
        if (stream is { } uid && TryComp<AudioComponent>(uid, out var audio))
        {
            // Stop refuses during state application, when detach can arrive, so silence and delete instead.
            _audio.SetGain(uid, 0f, audio);
            QueueDel(uid);
        }
        return null;
    }

    private void StopAll()
    {
        _loop = StopStream(_loop);
        _outgoingLoop = StopStream(_outgoingLoop);
        _playlistKey = null;
        _loopIndex = -1;
        _crossfadeElapsed = 0;
        _nextLoop = TimeSpan.Zero;
        _oneShot = StopStream(_oneShot);
        _profileId = null;
        _world = null;
        _occlusion = 0f;
        _accentKey = null;
        _loopVolume = WFPlanetAmbience.SilentVolume;
        _oneShotVolume = WFPlanetAmbience.SilentVolume;
        _nextOneShot = TimeSpan.Zero;
        _awaitingFirstAccent = true;
    }

    private static float ClampVolume(float value)
        => Math.Clamp(value, WFPlanetAmbience.SilentVolume, 0f);

    private static float Approach(float current, float target, float amount)
    {
        if (current < target)
            return MathF.Min(current + amount, target);

        return MathF.Max(current - amount, target);
    }
}
