using System.Numerics;
using Content.Shared._WF.WolfWeather;
using Content.Client.Light;
using Content.Shared.Light.Components;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.WolfWeather;

/// <summary>Moves weather layers with the wind, tints map light by the weather and flashes it for lightning.</summary>
public sealed partial class WolfWeatherSystem : SharedWolfWeatherSystem
{
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private EntityQuery<LightCycleComponent> _cycleQuery = default!;

    private readonly Dictionary<EntityUid, WolfWeatherMapState> _states = new();
    private readonly Dictionary<EntityUid, WolfWeatherLight> _lights = new();

    public int Quality { get; private set; }

    public Vector2 Down { get; private set; } = new(0f, -1f);

    /// <summary>Goes up whenever a tile changes, so the overlay knows to redraw its mask.</summary>
    public int MaskVersion { get; private set; }

    private const float FlashLength = 0.35f;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(LightCycleSystem));
        _overlay.AddOverlay(new WolfWeatherOverlay());
        Subs.CVar(_cfg, WolfWeatherCVars.WeatherQuality, q => Quality = q, true);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        MaskVersion++;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<WolfWeatherOverlay>();
        _states.Clear();
        _lights.Clear();
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<WolfWeatherMapComponent> ent, ref ComponentShutdown args)
    {
        _states.Remove(ent.Owner);
        _lights.Remove(ent.Owner);
    }

    public bool TryGetState(EntityUid map, out WolfWeatherMapState state)
    {
        return _states.TryGetValue(map, out state!);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!Timing.IsFirstTimePredicted)
            return;

        var now = Timing.CurTime;
        var real = Timing.RealTime;
        var query = EntityQueryEnumerator<WolfWeatherMapComponent, MapLightComponent>();
        while (query.MoveNext(out var uid, out var weather, out var light))
        {
            if (!_lights.TryGetValue(uid, out var state))
            {
                state = new WolfWeatherLight();
                _lights[uid] = state;
            }

            var cycle = _cycleQuery.TryComp(uid, out var lightCycle) && lightCycle.Running;
            if (cycle || state.Written != light.AmbientLightColor)
                state.Base = light.AmbientLightColor;

            var color = state.Base;
            foreach (var entry in weather.Weathers)
            {
                if (Proto.TryIndex(entry.Weather, out var proto))
                    color *= Color.InterpolateBetween(Color.White, proto.AmbientTint, GetStrength(entry, proto, now));
            }

            var flash = FlashLevel(state, real);
            if (flash > 0f)
                color = Color.InterpolateBetween(color, state.FlashColor, flash);

            if (color == light.AmbientLightColor)
                continue;

            light.AmbientLightColor = color;
            state.Written = color;
        }
    }

    /// <summary>Starts a lightning flash on a map, two quick pulses toward the colour.</summary>
    public void Flash(EntityUid map, Color color, float strength)
    {
        if (!_lights.TryGetValue(map, out var state))
        {
            state = new WolfWeatherLight();
            _lights[map] = state;
        }

        state.FlashStart = Timing.RealTime;
        state.FlashColor = color;
        state.FlashStrength = Math.Clamp(strength, 0f, 1f);
    }

    private static float FlashLevel(WolfWeatherLight state, TimeSpan now)
    {
        var t = (float) (now - state.FlashStart).TotalSeconds;
        if (state.FlashStrength <= 0f || t < 0f || t >= FlashLength)
            return 0f;

        var pulse = t < FlashLength * 0.3f
            ? 1f - t / (FlashLength * 0.3f)
            : 0.7f * MathF.Max(0f, 1f - MathF.Abs(t - FlashLength * 0.55f) / (FlashLength * 0.45f));
        return state.FlashStrength * pulse;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var time = (float) Timing.RealTime.TotalSeconds;
        Down = (-_eye.CurrentEye.Rotation).RotateVec(new Vector2(0f, -1f));
        var query = EntityQueryEnumerator<WolfWeatherMapComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!_states.TryGetValue(uid, out var state))
            {
                state = new WolfWeatherMapState { Wind = comp.Wind };
                _states[uid] = state;
            }

            var gust = 1f;
            var response = 6f;
            if (GetCurrent(comp) is { } current && Proto.TryIndex(current.Weather, out var proto))
            {
                response = proto.WindResponse;
                if (proto.GustPeriod > 0f)
                {
                    gust += proto.Gust * (MathF.Sin(time * MathF.Tau / proto.GustPeriod + current.Seed) * 0.6f
                                          + MathF.Sin(time * MathF.Tau / (proto.GustPeriod * 0.61f) + current.Seed * 2f) * 0.4f);
                }
            }
            else if (Proto.TryIndex(comp.Settings, out var settings))
            {
                response = settings.CalmWindResponse;
            }

            state.Wind = Vector2.Lerp(state.Wind, comp.Wind * gust, WolfWeatherMath.Smooth(frameTime, MathF.Max(response, 0.05f)));
            Advance(state, comp, frameTime);
        }
    }

    /// <summary>Scrolls each weather's layers with the wind, and drops offsets of weathers that are gone.</summary>
    private void Advance(WolfWeatherMapState state, WolfWeatherMapComponent comp, float frameTime)
    {
        state.Seen.Clear();
        foreach (var entry in comp.Weathers)
        {
            if (!Proto.TryIndex(entry.Weather, out var proto))
                continue;

            var key = (entry.Weather, entry.Start);
            state.Seen.Add(key);
            if (!state.Offsets.TryGetValue(key, out var offsets))
            {
                offsets = new WolfWeatherLayerOffsets();
                state.Offsets[key] = offsets;
            }

            var layers = proto.FinalLayers;
            if (offsets.X.Length != layers.Count)
            {
                offsets.X = new double[layers.Count];
                offsets.Y = new double[layers.Count];
            }

            for (var i = 0; i < layers.Count; i++)
            {
                var velocity = GetVelocity(layers[i], state.Wind, Down);
                offsets.X[i] += velocity.X * frameTime;
                offsets.Y[i] += velocity.Y * frameTime;
            }
        }

        if (state.Offsets.Count == state.Seen.Count)
            return;

        state.Gone.Clear();
        foreach (var key in state.Offsets.Keys)
        {
            if (!state.Seen.Contains(key))
                state.Gone.Add(key);
        }

        foreach (var key in state.Gone)
        {
            state.Offsets.Remove(key);
        }
    }

    public static Vector2 GetVelocity(WolfWeatherLayer layer, Vector2 wind, Vector2 down)
    {
        if (layer.Kind == WolfWeatherLayerKind.Splash)
            return Vector2.Zero;

        if (layer.Kind == WolfWeatherLayerKind.Fog)
            return wind * layer.WindScale;

        return wind * layer.WindScale + down * layer.FallSpeed;
    }
}

/// <summary>Ambient light we wrote on one map, the colour under it and the current lightning flash.</summary>
public sealed class WolfWeatherLight
{
    public Color? Written;
    public Color Base;
    public TimeSpan FlashStart;
    public Color FlashColor;
    public float FlashStrength;
}

/// <summary>Client-side wind and layer offsets for one map.</summary>
public sealed class WolfWeatherMapState
{
    public Vector2 Wind;

    /// <summary>Layer offsets per weather on the map, keyed by weather and start time.</summary>
    public readonly Dictionary<(ProtoId<WolfWeatherPrototype>, TimeSpan), WolfWeatherLayerOffsets> Offsets = new();

    public readonly HashSet<(ProtoId<WolfWeatherPrototype>, TimeSpan)> Seen = new();
    public readonly List<(ProtoId<WolfWeatherPrototype>, TimeSpan)> Gone = new();
}

/// <summary>Per-layer scroll offsets for one weather.</summary>
public sealed class WolfWeatherLayerOffsets
{
    public double[] X = Array.Empty<double>();
    public double[] Y = Array.Empty<double>();
}
