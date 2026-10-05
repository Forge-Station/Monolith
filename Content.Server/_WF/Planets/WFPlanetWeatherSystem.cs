using System.Globalization;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;
using Content.Shared.Weather;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Planets;

/// <summary>Bounded planet-wide weather cycles. Orbit remains dry; the timepiece reports surface conditions.</summary>
public sealed partial class WFPlanetWeatherSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedWeatherSystem _weather = default!;
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
            comp.NextChange = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(profile.ClearMinSeconds, profile.ClearMaxSeconds));
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
            if (_timing.CurTime >= state.NextChange)
            {
                if (profile.Storms.Count > 0 || state.Phase != WFStormPhase.None)
                {
                    AdvanceStorm(state, profile);
                }
                else
                {
                    state.Current = state.Current == null && profile.Weather.Count > 0
                        ? (ProtoId<WeatherPrototype>?) _random.Pick(profile.Weather) : null;
                    var seconds = state.Current == null
                        ? _random.NextFloat(profile.ClearMinSeconds, profile.ClearMaxSeconds)
                        : _random.NextFloat(profile.WeatherMinSeconds, profile.WeatherMaxSeconds);
                    state.NextChange = _timing.CurTime + TimeSpan.FromSeconds(seconds);
                }
            }
            UpdateThunder(network, state, profile);
            foreach (var layer in network.Layers)
            {
                if (layer != network.OrbitMap && !TerminatingOrDeleted(layer))
                    Apply(layer, state);
            }
            PublishNetwork(network, state, profile);
        }
        // Transit maps are created on demand while a ship changes altitude.
        var transits = EntityQueryEnumerator<CEZTransitMapComponent>();
        while (transits.MoveNext(out var uid, out var transit))
        {
            if (transit.LowerMap is { } lower && TryGetWorld(lower, out var world) &&
                TryComp<WFPlanetWeatherComponent>(world, out var state) && !MetaData(world).EntityPaused)
            {
                Apply(uid, state);
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

    private int GetMinuteOfDay(WFPlanetWeatherComponent state, WFPlanetWeatherPrototype profile)
    {
        var elapsed = (_timing.CurTime - state.Epoch).TotalSeconds;
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
        if (applied.Current == state.Current && applied.EndTime == state.NextChange)
            return;
        // Clear on a virgin map needs no WeatherComponent or network update.
        if (state.Current != null || applied.Current != null)
            _weather.SetWeather(mapComp.MapId, state.Current is { } id ? _proto.Index(id) : null,
                state.Current != null ? state.NextChange : null);
        applied.Current = state.Current;
        applied.EndTime = state.NextChange;
    }

    private bool TryGetWorld(EntityUid map, out EntityUid world)
    {
        world = default;
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
