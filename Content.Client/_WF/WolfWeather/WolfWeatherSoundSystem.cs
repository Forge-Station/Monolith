using Content.Shared._WF.WolfWeather;
using Content.Shared.CCVar;
using Content.Shared.Light.Components;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.WolfWeather;

/// <summary>Loops weather sounds and plays thunder, quieter and muffled the further the player is from open sky.</summary>
public sealed partial class WolfWeatherSoundSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private WolfWeatherSystem _wolfWeather = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<RoofComponent> _roofQuery = default!;
    [Dependency] private EntityQuery<WolfWeatherMapComponent> _weatherQuery = default!;
    [Dependency] private EntityQuery<AudioComponent> _audioQuery = default!;

    private const int MaxSearch = 32;
    private const float IndoorSmoothing = 2f;
    private const float StrengthSmoothing = 0.8f;
    private const float StopBelow = 0.001f;

    private static readonly TimeSpan SearchInterval = TimeSpan.FromSeconds(0.25);
    private static readonly Vector2i[] SearchOffsets = BuildOffsets();
    private static readonly int[] RingStarts = BuildRingStarts();

    private float _indoor;
    private float _indoorTarget;
    private bool _hasIndoor;
    private TimeSpan _nextSearch;
    private float _indoorVolume = 1f;
    private float _indoorMuffle;
    private float _ambienceGain;
    private readonly List<(TimeSpan At, WolfWeatherLightning Lightning)> _thunder = new();
    private readonly Dictionary<(ProtoId<WolfWeatherPrototype>, int), Loop> _loops = new();
    private readonly Dictionary<(ProtoId<WolfWeatherPrototype>, int), float> _targets = new();
    private readonly List<(ProtoId<WolfWeatherPrototype>, int)> _finished = new();
    private readonly List<ResPath> _files = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WolfLightningEvent>(OnLightning);
        // Relative to the slider's default, so weather at the default setting plays at its own volume.
        Subs.CVar(_cfg, CCVars.AmbienceVolume, v => _ambienceGain = v / CCVars.AmbienceVolume.DefaultValue, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        foreach (var loop in _loops.Values)
        {
            _audio.Stop(loop.Stream);
        }

        _loops.Clear();
    }

    private void OnLightning(WolfLightningEvent ev)
    {
        var map = GetEntity(ev.Map);
        if (!_weatherQuery.HasComp(map)
            || !_proto.TryIndex(ev.Weather, out var proto)
            || proto.Lightning is not { } lightning)
            return;

        _wolfWeather.Flash(map, lightning.FlashColor, ev.Strike ? lightning.StrikeFlash : lightning.FarFlash);

        if (!ev.Strike && lightning.FarSound != null)
        {
            var delay = _random.Roll(lightning.FarDelay);
            _thunder.Add((_timing.RealTime + TimeSpan.FromSeconds(delay), lightning));
        }
    }

    /// <summary>Plays far thunder that is due, muffled and quieter indoors.</summary>
    private void PlayThunder()
    {
        var now = _timing.RealTime;
        for (var i = _thunder.Count - 1; i >= 0; i--)
        {
            var (at, lightning) = _thunder[i];
            if (now < at)
                continue;

            _thunder.RemoveAt(i);
            if (lightning.FarSound is not { } sound)
                continue;

            var audioParams = sound.Params.WithVolume(sound.Params.Volume + _indoor * lightning.IndoorVolume);
            if (_audio.PlayGlobal(sound, Filter.Local(), false, audioParams) is { } played)
                played.Component.Occlusion = _indoor * lightning.IndoorOcclusion;
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        PlayThunder();

        _targets.Clear();
        UpdateTargets(frameTime);
        UpdateLoops(frameTime);
    }

    /// <summary>Finds the indoor level and how strongly each weather sound on the player's map should play.</summary>
    private void UpdateTargets(float frameTime)
    {
        if (_player.LocalEntity is not { } player)
        {
            _hasIndoor = false;
            return;
        }

        // Inside a vehicle the weather is the world around its hull, heard from indoors.
        var xform = Transform(player);
        var inVehicle = _wolfWeather.TryGetHull(player, out var hull);
        if ((inVehicle ? Transform(hull).MapUid : xform.MapUid) is not { } mapUid
            || !_wolfWeather.IsSurface(mapUid)
            || !_weatherQuery.TryComp(mapUid, out var weather)
            || !_proto.TryIndex(weather.Settings, out var settings))
        {
            _hasIndoor = false;
            return;
        }

        var real = _timing.RealTime;
        if (!_hasIndoor || real >= _nextSearch)
        {
            _nextSearch = real + SearchInterval;
            _indoorTarget = inVehicle ? 1f : FindIndoor(xform, settings);
            if (!_hasIndoor)
                _indoor = _indoorTarget;

            _hasIndoor = true;
        }

        _indoor += (_indoorTarget - _indoor) * WolfWeatherMath.Smooth(frameTime, IndoorSmoothing);
        _indoorVolume = 1f + _indoor * (settings.IndoorVolume - 1f);
        _indoorMuffle = _indoor * settings.IndoorMuffle;

        var now = _timing.CurTime;
        foreach (var entry in weather.Weathers)
        {
            if (!_proto.TryIndex(entry.Weather, out var proto))
                continue;

            var strength = SharedWolfWeatherSystem.GetStrength(entry, proto, now);
            for (var i = 0; i < proto.Sounds.Count; i++)
            {
                var key = (entry.Weather, i);
                if (!_targets.TryGetValue(key, out var old) || old < strength)
                    _targets[key] = strength;
            }
        }
    }

    /// <summary>Starts, fades and stops the weather loops toward their target strengths.</summary>
    private void UpdateLoops(float frameTime)
    {
        var blend = WolfWeatherMath.Smooth(frameTime, StrengthSmoothing);

        _finished.Clear();
        foreach (var (key, loop) in _loops)
        {
            var target = _targets.GetValueOrDefault(key);
            loop.Strength += (target - loop.Strength) * blend;

            if (!_proto.TryIndex(key.Item1, out var proto)
                || key.Item2 >= proto.Sounds.Count
                || target <= 0f && loop.Strength < StopBelow)
            {
                _audio.Stop(loop.Stream);
                _finished.Add(key);
                continue;
            }

            SetGain(loop, proto.Sounds[key.Item2].Volume);
        }

        foreach (var key in _finished)
        {
            _loops.Remove(key);
        }

        foreach (var (key, target) in _targets)
        {
            if (target <= 0f || _loops.ContainsKey(key) || !_proto.TryIndex(key.Item1, out var proto))
                continue;

            var sound = proto.Sounds[key.Item2];
            _files.Clear();
            _files.AddRange(sound.Files);
            if (sound.Collection is { } collection && _proto.TryIndex(collection, out var files))
                _files.AddRange(files.PickFiles);

            if (_files.Count == 0)
                continue;

            var loop = new Loop { Stream = StartLoop(_random.Pick(_files)) };
            _loops[key] = loop;
            SetGain(loop, sound.Volume);
        }
    }

    /// <summary>Starts a file looping from a random point, silent until its gain is set.</summary>
    private EntityUid? StartLoop(ResPath file)
    {
        var resolved = _audio.ResolveSound(new SoundPathSpecifier(file));
        var length = (float) _audio.GetAudioLength(resolved).TotalSeconds;
        var audioParams = AudioParams.Default
            .WithLoop(true)
            .WithVolume(-100f)
            .WithPlayOffset(length > 1f ? _random.NextFloat(0f, length) : 0f);

        return _audio.PlayGlobal(resolved, Filter.Local(), false, audioParams)?.Entity;
    }

    private void SetGain(Loop loop, float volume)
    {
        if (loop.Stream == null || !_audioQuery.TryComp(loop.Stream, out var audio))
            return;

        _audio.SetGain(loop.Stream, loop.Strength * volume * _ambienceGain * _indoorVolume, audio);
        audio.Occlusion = _indoorMuffle;
    }

    private float FindIndoor(TransformComponent xform, WolfWeatherSettingsPrototype weather)
    {
        if (xform.GridUid is not { } gridUid || !_gridQuery.TryComp(gridUid, out var grid))
            return 0f;

        _roofQuery.TryComp(gridUid, out var roof);
        var origin = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var depth = Math.Clamp(weather.IndoorSearch, 0, MaxSearch);

        for (var ring = 0; ring <= depth; ring++)
        {
            for (var i = RingStarts[ring]; i < RingStarts[ring + 1]; i++)
            {
                var tile = _map.GetTileRef(gridUid, grid, origin + SearchOffsets[i]);
                if (!_wolfWeather.IsExposed((gridUid, grid, roof), tile))
                    continue;

                if (ring == 0)
                    return 0f;

                return ring <= weather.IndoorNear ? weather.IndoorNearLevel : 1f;
            }
        }

        return 1f;
    }

    private static Vector2i[] BuildOffsets()
    {
        var list = new List<Vector2i>();
        for (var ring = 0; ring <= MaxSearch; ring++)
        {
            for (var x = -ring; x <= ring; x++)
            {
                var y = ring - Math.Abs(x);
                list.Add(new Vector2i(x, y));
                if (y != 0)
                    list.Add(new Vector2i(x, -y));
            }
        }

        return list.ToArray();
    }

    private static int[] BuildRingStarts()
    {
        var starts = new int[MaxSearch + 2];
        var index = 0;
        for (var ring = 0; ring <= MaxSearch; ring++)
        {
            starts[ring] = index;
            index += ring == 0 ? 1 : 4 * ring;
        }

        starts[MaxSearch + 1] = index;
        return starts;
    }

    /// <summary>One playing weather sound and its smoothed strength.</summary>
    private sealed class Loop
    {
        public EntityUid? Stream;
        public float Strength;
    }
}
