using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;
using Content.Shared.Atmos;
using Content.Shared.Weather;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Planets;

/// <summary>
/// Each world's outdoor air temperature: its base air plus the season, the weather and the hour. Eased onto the
/// ground and air layers' map atmosphere, which bare ground shares; rooms keep their own air.
/// </summary>
public sealed partial class WFPlanetClimateSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private WFPlanetWeatherSystem _weather = default!;

    private const float UpdateSeconds = 2f;

    /// <summary>Kelvin the outdoor air may move in a second.</summary>
    public const float MaxRate = 0.1f;

    /// <summary>The smallest change written to the layers' air.</summary>
    public const float ApplyThreshold = 0.25f;

    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;
        _nextUpdate = now + TimeSpan.FromSeconds(UpdateSeconds);

        var worlds = EntityQueryEnumerator<WFPlanetNetworkComponent, WFPlanetWeatherComponent>();
        while (worlds.MoveNext(out var uid, out var network, out var state))
        {
            if (MetaData(uid).EntityPaused || !_proto.TryIndex(state.Profile, out var profile))
                continue;

            if (float.IsNaN(state.BaseTemperature))
            {
                if (!TryComp<MapAtmosphereComponent>(network.GroundMap, out var ground) || ground.Space)
                    continue;
                var air = ground.Mixture;
                state.BaseTemperature = air.Temperature;
            }

            var target = GetTarget(state, profile, now, state.Current);
            state.Temperature = float.IsNaN(state.Temperature)
                ? target
                : state.Temperature + Math.Clamp(target - state.Temperature, -MaxRate * UpdateSeconds, MaxRate * UpdateSeconds);

            if (float.IsNaN(state.AppliedTemperature) || MathF.Abs(state.Temperature - state.AppliedTemperature) >= ApplyThreshold)
            {
                ApplyAir(network, state.Temperature);
                state.AppliedTemperature = state.Temperature;
            }

            Publish(network, state, profile);
        }

        // Ships crossing between layers see the conditions of the layer below.
        var transits = EntityQueryEnumerator<CEZTransitMapComponent, WFPlanetEnvironmentComponent>();
        while (transits.MoveNext(out var uid, out var transit, out var environment))
        {
            if (transit.LowerMap is { } lower && TryComp<WFPlanetEnvironmentComponent>(lower, out var source))
                SetClimate((uid, environment), source.Season, source.SeasonColor, source.TemperatureCelsius);
        }
    }

    /// <summary>Moves a world's outdoor air to its target now, as an admin changing the hour, weather or season expects.</summary>
    public void Snap(EntityUid world)
    {
        if (!TryComp<WFPlanetWeatherComponent>(world, out var state))
            return;

        state.Temperature = float.NaN;
        _nextUpdate = TimeSpan.Zero;
    }

    /// <summary>
    /// The outdoor air temperature in kelvin of the world a map belongs to, now or at a later time (as forecast).
    /// Null for a map that isn't part of a world.
    /// </summary>
    public float? GetOutsideTemperature(EntityUid map, TimeSpan? at = null)
    {
        if (!TryGetState(map, out var state, out var profile))
            return null;

        var now = _timing.CurTime;
        if ((at == null || at <= now) && !float.IsNaN(state.Temperature))
            return state.Temperature;

        var time = at is { } later && later > now ? later : now;
        return GetTarget(state, profile, time, _weather.GetWeatherAt(state, time));
    }

    /// <summary>The season of the world a map belongs to, now or at a later time; null without seasons.</summary>
    public WFPlanetSeason? GetSeason(EntityUid map, TimeSpan? at = null)
    {
        return TryGetState(map, out var state, out var profile)
            ? _weather.GetSeason(state, profile, at ?? _timing.CurTime)
            : null;
    }

    /// <summary>The temperature, in kelvin, the world's air settles at for a time of day, season and weather.</summary>
    public float GetTarget(WFPlanetWeatherComponent state, WFPlanetWeatherPrototype profile, TimeSpan at,
        ProtoId<WeatherPrototype>? weather)
    {
        var temperature = float.IsNaN(state.BaseTemperature) ? Atmospherics.T20C : state.BaseTemperature;
        var day = DayCurve(_weather.GetMinuteOfDay(state, profile, at));
        var season = _weather.GetSeason(state, profile, at);

        temperature += profile.DayTemperatureSwing * day;
        if (season != null)
            temperature += season.Temperature;

        if (weather is { } id)
            temperature += profile.WeatherTemperature.GetValueOrDefault(id);
        else if (season != null)
            temperature += season.ClearDayTemperature * MathF.Max(0f, day);

        return MathF.Max(Atmospherics.TCMB, temperature);
    }

    /// <summary>
    /// Where the hour sits between the coldest, -1 at 05:00, and the warmest, 1 at 15:00: ten hours warming, fourteen
    /// cooling.
    /// </summary>
    public static float DayCurve(int minuteOfDay)
    {
        var hour = minuteOfDay / 60f;
        if (hour >= 5f && hour <= 15f)
            return -MathF.Cos(MathF.PI * (hour - 5f) / 10f);
        return MathF.Cos(MathF.PI * ((hour - 15f + 24f) % 24f) / 14f);
    }

    private bool TryGetState(EntityUid map, out WFPlanetWeatherComponent state, out WFPlanetWeatherPrototype profile)
    {
        state = default!;
        profile = default!;
        var world = map;
        if (!HasComp<WFPlanetWeatherComponent>(world) && !_weather.TryGetWorld(map, out world))
            return false;
        if (!TryComp(world, out WFPlanetWeatherComponent? comp) || !_proto.TryIndex(comp.Profile, out var proto))
            return false;
        state = comp;
        profile = proto;
        return true;
    }

    /// <summary>Writes the temperature into the outdoor air of every layer with a sky: the ground and the air layers.</summary>
    private void ApplyAir(WFPlanetNetworkComponent network, float temperature)
    {
        foreach (var layer in network.Layers)
        {
            if (layer == network.OrbitMap || TerminatingOrDeleted(layer) ||
                !TryComp<MapAtmosphereComponent>(layer, out var atmosphere) || atmosphere.Space)
            {
                continue;
            }

            var old = atmosphere.Mixture;
            var mixture = new GasMixture(old) { Temperature = temperature };
            _atmos.SetMapGasMixture(layer, mixture, atmosphere);
        }
    }

    private void Publish(WFPlanetNetworkComponent network, WFPlanetWeatherComponent state, WFPlanetWeatherPrototype profile)
    {
        var season = _weather.GetSeason(state, profile, _timing.CurTime);
        var celsius = (int) MathF.Round(state.Temperature - Atmospherics.T0C);
        foreach (var layer in network.Layers)
        {
            if (TerminatingOrDeleted(layer) || !TryComp<WFPlanetEnvironmentComponent>(layer, out var environment))
                continue;
            SetClimate((layer, environment), season?.Name, season?.Color ?? Color.White,
                layer == network.OrbitMap ? null : celsius);
        }
    }

    private void SetClimate(Entity<WFPlanetEnvironmentComponent> ent, LocId? season, Color color, int? celsius)
    {
        var environment = ent.Comp;
        if (environment.Season == season && environment.SeasonColor == color && environment.TemperatureCelsius == celsius)
            return;
        environment.Season = season;
        environment.SeasonColor = color;
        environment.TemperatureCelsius = celsius;
        Dirty(ent);
    }
}
