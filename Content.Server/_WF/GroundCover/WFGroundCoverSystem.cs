using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.GroundCover;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Atmos;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.GroundCover;

/// <summary>
/// Wets open ground in rain and dries it after, and lays snow cover in snow weather that melts in warm air, worked
/// only near players and sent to them by chunk.
/// </summary>
public sealed partial class WFGroundCoverSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public const float Freezing = Atmospherics.T0C;

    /// <summary>Cover levels a minute that melt just above freezing; warmer air melts faster.</summary>
    public const float MeltPerMinute = 0.5f;

    /// <summary>Rain below this wets the ground but fills no puddles.</summary>
    public const float PuddleRain = 0.3f;

    /// <summary>Seconds the puddles' tint takes to ease most of the way to the rain now falling.</summary>
    private const float TintSeconds = 20f;

    private static readonly TimeSpan MapInterval = TimeSpan.FromSeconds(1);

    private readonly List<EntityUid> _newMaps = new();

    public override void Initialize()
    {
        base.Initialize();
        InitializeNetwork();
        InitializeShovel();
        InitializeIce();
        InitializeIceBreak();
        InitializeTracks();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ShutdownNetwork();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        _newMaps.Clear();
        var maps = EntityQueryEnumerator<WolfWeatherMapComponent>();
        while (maps.MoveNext(out var uid, out var weather))
        {
            if (weather.Weathers.Count > 0 && !HasComp<WFGroundWeatherComponent>(uid))
                _newMaps.Add(uid);
        }

        foreach (var uid in _newMaps)
        {
            EnsureComp<WFGroundWeatherComponent>(uid).NextUpdate = now;
        }

        var grounds = EntityQueryEnumerator<WFGroundWeatherComponent>();
        while (grounds.MoveNext(out var uid, out var ground))
        {
            if (now < ground.NextUpdate)
                continue;

            ground.NextUpdate = now + MapInterval;
            StepMap((uid, ground), (float) MapInterval.TotalSeconds);
        }

        UpdateSnow(now);
        UpdateHeat(now);
        UpdateFire(now);
        UpdateLiquids(now);
        UpdateDecals(now);
        UpdateIce(now);
        UpdateNetwork(now);
    }

    /// <summary>Rain as wetness, and snowfall in cover levels a minute, of a map's weathers at their current strength.</summary>
    public (float Rain, float Snow) GetPrecipitation(EntityUid map)
    {
        if (!TryComp<WolfWeatherMapComponent>(map, out var weather))
            return (0f, 0f);

        var now = _timing.CurTime;
        var rain = 0f;
        var snow = 0f;
        foreach (var entry in weather.Weathers)
        {
            if (!_proto.TryIndex(entry.Weather, out var proto))
                continue;

            var strength = SharedWolfWeatherSystem.GetStrength(entry, proto, now);
            rain += strength * proto.GroundWetness;
            snow += strength * proto.Snowfall;
        }

        return (MathF.Min(rain, 1f), snow);
    }

    /// <summary>The tint of the rain falling on a map, its weathers' tints weighted by how hard each rains; null when none does.</summary>
    public Color? GetRainTint(EntityUid map)
    {
        if (!TryComp<WolfWeatherMapComponent>(map, out var weather))
            return null;

        var now = _timing.CurTime;
        var sum = Vector3.Zero;
        var total = 0f;
        foreach (var entry in weather.Weathers)
        {
            if (!_proto.TryIndex(entry.Weather, out var proto) || proto.GroundWetness <= 0f)
                continue;

            var weight = SharedWolfWeatherSystem.GetStrength(entry, proto, now) * proto.GroundWetness;
            var tint = proto.GroundTint ?? WFGroundWeatherComponent.Water;
            sum += new Vector3(tint.R, tint.G, tint.B) * weight;
            total += weight;
        }

        return total > 0.001f ? new Color(sum.X / total, sum.Y / total, sum.Z / total) : null;
    }

    /// <summary>Moves a map's wetness, puddles and snow totals on by some seconds of its current weather.</summary>
    public void StepMap(Entity<WFGroundWeatherComponent> map, float seconds)
    {
        var comp = map.Comp;
        (comp.Rain, comp.Snow) = GetPrecipitation(map);

        comp.WetnessExact = Approach(comp.WetnessExact, comp.Rain, comp.WetRate * seconds, comp.DryRate * seconds);
        var puddles = Math.Clamp((comp.Rain - PuddleRain) / (1f - PuddleRain), 0f, 1f);
        comp.PuddlesExact = Approach(comp.PuddlesExact, puddles, comp.PuddleRate * seconds, comp.PuddleDryRate * seconds);

        if (comp.Snow > 0f)
        {
            var fallen = comp.Snow * seconds / 60f;
            comp.Fallen += fallen;
            comp.BaseDepth = MathF.Min(WFSnowCover.MaxLevels, comp.BaseDepth + fallen);
        }
        else
        {
            comp.MeltClock += seconds;
            if (comp.BaseDepth > 0f && MapTemperature(map) is { } temperature && temperature > Freezing)
                comp.BaseDepth = MathF.Max(0f, comp.BaseDepth - MeltRate(temperature) * seconds / 60f);
        }

        // The puddles keep the colour of the last rain while they dry.
        if (GetRainTint(map) is { } target)
        {
            var ease = MathF.Min(1f, seconds / TintSeconds);
            comp.TintExact = Color.InterpolateBetween(comp.TintExact, target, ease);
        }

        // Clients get wetness in fiftieths and the tint in thirty-seconds, so a slow change doesn't send the map every second.
        var wetness = MathF.Round(comp.WetnessExact * 50f) / 50f;
        var shown = MathF.Round(comp.PuddlesExact * 50f) / 50f;
        var tint = new Color(MathF.Round(comp.TintExact.R * 32f) / 32f, MathF.Round(comp.TintExact.G * 32f) / 32f,
            MathF.Round(comp.TintExact.B * 32f) / 32f);
        if (wetness == comp.Wetness && shown == comp.Puddles && tint == comp.Tint)
            return;

        comp.Wetness = wetness;
        comp.Puddles = shown;
        comp.Tint = tint;
        Dirty(map);
    }

    /// <summary>How wet a tile is: the map's wetness on open ground, none under cover.</summary>
    public float GetTileWetness(EntityUid grid, Vector2i tile)
    {
        if (!TryComp(grid, out TransformComponent? xform)
            || !TryComp<WFGroundWeatherComponent>(xform.MapUid, out var ground)
            || !IsOpen(grid, tile))
            return 0f;

        return ground.WetnessExact;
    }

    /// <summary>Cover levels a minute melting in air of a temperature.</summary>
    public static float MeltRate(float temperature)
    {
        return MeltPerMinute * Math.Clamp((temperature - Freezing) / 10f, 0.25f, 2f);
    }

    private float? MapTemperature(EntityUid map)
    {
        return _atmos.GetTileMixture(null, map, Vector2i.Zero)?.Temperature;
    }

    private static float Approach(float value, float target, float up, float down)
    {
        return value < target ? MathF.Min(target, value + up) : MathF.Max(target, value - down);
    }
}
