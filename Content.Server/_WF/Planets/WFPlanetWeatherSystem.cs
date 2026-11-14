using System.Globalization;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Server._WF.WolfWeather;
using Content.Shared._WF.Planets;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Weather;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Planets;

/// <summary>Bounded planet-wide weather cycles. Orbit remains dry; the timepiece reports surface conditions.</summary>
public sealed partial class WFPlanetWeatherSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedWeatherSystem _weather = default!;
    [Dependency] private WolfWeatherSystem _wolfWeather = default!;
    private TimeSpan _nextUpdate;

    public void Configure(EntityUid network, WFPlanetSurfacePrototype surface, string name)
    {
        foreach (var profile in _proto.EnumeratePrototypes<WFPlanetWeatherPrototype>())
        {
            if (profile.PlanetType != surface.PlanetType)
                continue;
            var comp = EnsureComp<WFPlanetWeatherComponent>(network);
            comp.Profile = profile.ID;
            comp.PlanetName = name;
            comp.Epoch = _timing.CurTime;
            // A world with seasons starts each round in a random one.
            StartSeason(comp, profile, profile.Seasons.Count > 1 ? _random.Next(profile.Seasons.Count) : 0);
            var clearScale = GetSeason(comp, profile, _timing.CurTime)?.ClearScale ?? 1f;
            comp.NextChange = _timing.CurTime + TimeSpan.FromSeconds(
                _random.NextFloat(profile.ClearMinSeconds, profile.ClearMaxSeconds) * clearScale);
            PublishNetwork(Comp<WFPlanetNetworkComponent>(network), comp, profile);
            return;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        var worlds = EntityQueryEnumerator<WFPlanetNetworkComponent, WFPlanetWeatherComponent>();
        while (worlds.MoveNext(out var uid, out var network, out var state))
        {
            if (MetaData(uid).EntityPaused || !_proto.TryIndex(state.Profile, out var profile))
                continue;
            if (state.AdminHeld && _timing.CurTime >= state.NextChange)
                state.AdminHeld = false;
            // An admin's weather holds even while the cycle is off, until its time is up.
            if (!_cfg.GetCVar(PlanetCVars.PlanetWeather) && !state.AdminHeld)
            {
                // Held clear: anything already falling stops, and nothing new is scheduled.
                state.Current = null;
                state.Phase = WFStormPhase.None;
                state.NextChange = _timing.CurTime + TimeSpan.FromSeconds(profile.ClearMinSeconds);
                state.Upcoming.Clear();
            }
            else if (_timing.CurTime >= state.NextChange)
            {
                Advance(state, profile);
            }
            else
            {
                Fill(state, profile);
            }
            // Weather falls on the surface only; the air above, the orbit and the levels below stay clear.
            if (!TerminatingOrDeleted(network.GroundMap))
                Apply(network.GroundMap, state);
            PublishNetwork(network, state, profile);
        }
        // Transit maps are created on demand while a ship changes altitude.
        var transits = EntityQueryEnumerator<CEZTransitMapComponent>();
        while (transits.MoveNext(out var uid, out var transit))
        {
            if (transit.LowerMap is { } lower && TryGetWorld(lower, out var world) &&
                TryComp<WFPlanetWeatherComponent>(world, out var state) && !MetaData(world).EntityPaused)
            {
                if (_proto.TryIndex(state.Profile, out var profile) && TryGetReport(lower, out var name, out _, out var weather))
                {
                    PublishMap(uid, name, GetMinuteOfDay(state, profile), weather);
                    SynchronizeDaylight(uid, state, profile, lower);
                }
                // Keep soundscape continuity while ships cross gaps between atmospheric layers.
                if (TryComp<WFPlanetAmbienceComponent>(lower, out var source))
                {
                    var target = EnsureComp<WFPlanetAmbienceComponent>(uid);
                    if (target.Profile != source.Profile || target.VolumeOffset != source.VolumeOffset || target.Occlusion != source.Occlusion)
                    {
                        target.Profile = source.Profile;
                        target.VolumeOffset = source.VolumeOffset;
                        target.Occlusion = source.Occlusion;
                        Dirty(uid, target);
                    }
                }
            }
        }
    }

    /// <summary>The world's local minute of the day now, or at a given time.</summary>
    public int GetMinuteOfDay(WFPlanetWeatherComponent state, WFPlanetWeatherPrototype profile, TimeSpan? at = null)
    {
        var elapsed = ((at ?? _timing.CurTime) - state.Epoch).TotalSeconds;
        var minutes = Math.Floor(profile.InitialHour * 60 + elapsed * 1440 / Math.Max(1, profile.DaySeconds));
        return (int) ((minutes % 1440 + 1440) % 1440);
    }

    private void PublishNetwork(WFPlanetNetworkComponent network, WFPlanetWeatherComponent state, WFPlanetWeatherPrototype profile)
    {
        if (!TryGetReport(network.GroundMap, out var name, out _, out var weather))
            return;
        var minute = GetMinuteOfDay(state, profile);
        foreach (var layer in network.Layers)
        {
            if (!TerminatingOrDeleted(layer))
            {
                PublishMap(layer, name, minute, weather);
                if (layer != network.OrbitMap)
                    SynchronizeDaylight(layer, state, profile);
            }
        }
    }

    private void PublishMap(EntityUid map, string name, int minute, string weather)
    {
        var environment = EnsureComp<WFPlanetEnvironmentComponent>(map);
        if (environment.PlanetName == name && environment.MinuteOfDay == minute && environment.Weather == weather)
            return;
        environment.PlanetName = name;
        environment.MinuteOfDay = minute;
        environment.Weather = weather;
        Dirty(map, environment);
    }

    private void Apply(EntityUid map, WFPlanetWeatherComponent state)
    {
        if (!TryComp<MapComponent>(map, out var mapComp) || HasComp<WFOrbitLayerComponent>(map))
            return;
        var applied = EnsureComp<WFPlanetWeatherAppliedComponent>(map);
        var wolf = ResolveWolfWeather(state.Profile, state.Current);
        var hasWolf = HasComp<WolfWeatherMapComponent>(map);
        if (applied.Current == state.Current && applied.EndTime == state.NextChange && applied.WolfWeather == wolf && hasWolf)
            return;
        // Clear on a virgin map needs no WeatherComponent or network update.
        if (state.Current != null || applied.Current != null)
            _weather.SetWeather(mapComp.MapId, state.Current is { } id ? _proto.Index(id) : null,
                state.Current != null ? state.NextChange : null);
        // WolfWeather draws, sounds, blows and strikes for the stock weather; its state on a layer, even clear, keeps the
        // stock overlay and loop off there. Open-ended, so each phase cross-fades into the next when the scheduler moves.
        if (applied.WolfWeather != wolf || !hasWolf)
            _wolfWeather.SetWeather(map, wolf, null);
        applied.Current = state.Current;
        applied.EndTime = state.NextChange;
        applied.WolfWeather = wolf;
    }

    /// <summary>The WolfWeather that shows a stock weather on a world: its own table's, else any world's, else none.</summary>
    public ProtoId<WolfWeatherPrototype>? ResolveWolfWeather(ProtoId<WFPlanetWeatherPrototype> profile, ProtoId<WeatherPrototype>? weather)
    {
        if (weather is not { } id)
            return null;
        if (_proto.TryIndex(profile, out var own) && own.WolfWeathers.TryGetValue(id, out var wolf))
            return wolf;
        foreach (var other in _proto.EnumeratePrototypes<WFPlanetWeatherPrototype>())
        {
            if (other.WolfWeathers.TryGetValue(id, out wolf))
                return wolf;
        }
        return null;
    }

    /// <summary>The world entity a planet layer, or a transit map between two, belongs to.</summary>
    public bool TryGetWorld(EntityUid map, out EntityUid world)
    {
        world = default;
        if (TryComp<CEZTransitMapComponent>(map, out var transit) && transit.LowerMap is { } lower)
            map = lower;
        if (TryComp<WFPlanetLayerComponent>(map, out var layer) && layer.Network is { } net &&
            TryGetEntity(net, out var resolved) && resolved is { } uid && HasComp<WFPlanetNetworkComponent>(uid))
        {
            world = uid;
            return true;
        }
        return false;
    }

    /// <summary>Uses the containing planet even in orbit/transit; elsewhere reports no planetary signal.</summary>
    public bool TryGetReport(EntityUid uid, out string planet, out string time, out string weather)
    {
        planet = time = weather = string.Empty;
        if (Transform(uid).MapUid is not { } map)
            return false;
        if (TryComp<CEZTransitMapComponent>(map, out var transit) && transit.LowerMap is { } lower)
            map = lower;
        if (!TryGetWorld(map, out var world) || !TryComp<WFPlanetWeatherComponent>(world, out var state) ||
            !_proto.TryIndex(state.Profile, out var profile))
            return false;
        var network = Comp<WFPlanetNetworkComponent>(world);
        planet = state.PlanetName;
        time = TimeSpan.FromMinutes(GetMinuteOfDay(state, profile)).ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        weather = Loc.GetString("wf-planet-weather-clear");
        // Below ground the sky is out of sight: the layer's own environment says what it reports.
        if (network.LowerLayers.Contains(map))
        {
            if (TryComp<WFPlanetEnvironmentComponent>(map, out var below) && !string.IsNullOrEmpty(below.Weather))
                weather = below.Weather;
            return true;
        }
        // Read real surface weather, including admin overrides, rather than merely the scheduler's intent.
        if (!TryComp<WeatherComponent>(network.GroundMap, out var actual))
            return true;
        var conditions = new List<string>();
        foreach (var (id, data) in actual.Weather)
        {
            if (data.EndTime is { } end && end <= _timing.CurTime)
                continue;
            var key = "wf-planet-weather-" + id.Id;
            conditions.Add(Loc.TryGetString(key, out var label) ? label : id.Id);
        }
        if (conditions.Count > 0)
            weather = string.Join(", ", conditions);
        return true;
    }
}
