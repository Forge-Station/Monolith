using Content.Shared._WF.Planets;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Random.Helpers;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.WolfWeather;

/// <summary>Rolls weather on maps with a schedule.</summary>
public sealed partial class WeatherScheduleSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WolfWeatherSystem _wolfWeather = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    private readonly Dictionary<string, float> _weights = new();
    private TimeSpan _nextCheck;
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, WolfWeatherCVars.WeatherSchedule, OnEnabledChanged, true);
    }

    private void OnEnabledChanged(bool enabled)
    {
        if (_enabled && !enabled)
        {
            var query = EntityQueryEnumerator<WeatherScheduleComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                Reset((uid, comp));
                StartGap((uid, comp));
            }
        }

        _enabled = enabled;
    }

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<WeatherScheduleComponent> ent, ref MapInitEvent args)
    {
        StartGap(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + CheckInterval;

        var query = EntityQueryEnumerator<WeatherScheduleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            Tick((uid, comp), now);
        }
    }

    /// <summary>Whether a schedule may run on a map. A planet's layers follow its own weather instead.</summary>
    public bool CanSchedule(EntityUid map)
    {
        return !HasComp<WFPlanetLayerComponent>(map) && _wolfWeather.IsSurface(map);
    }

    /// <summary>Sets or clears a map's schedule. False, and nothing changed, on a map that can't have one.</summary>
    public bool SetSchedule(Entity<WeatherScheduleComponent?> map, ProtoId<WeatherSchedulePrototype>? schedule)
    {
        if (schedule == null)
        {
            if (!Resolve(map, ref map.Comp, false))
                return true;

            Reset((map.Owner, map.Comp));
            map.Comp.Schedule = null;
            return true;
        }

        if (!CanSchedule(map))
            return false;

        map.Comp ??= EnsureComp<WeatherScheduleComponent>(map);
        Reset((map.Owner, map.Comp));
        map.Comp.Schedule = schedule;
        StartGap((map.Owner, map.Comp));
        return true;
    }

    public bool RollNow(Entity<WeatherScheduleComponent?> map)
    {
        if (!Resolve(map, ref map.Comp, false)
            || !CanSchedule(map)
            || !_proto.TryIndex(map.Comp.Schedule, out var schedule))
            return false;

        map.Comp.Waiting = false;
        var entry = PickStart(schedule);
        if (entry == null)
        {
            StartGap((map.Owner, map.Comp));
            return false;
        }

        Start((map.Owner, map.Comp), entry);
        return true;
    }

    private void Tick(Entity<WeatherScheduleComponent> ent, TimeSpan now)
    {
        if (!CanSchedule(ent) || !_proto.TryIndex(ent.Comp.Schedule, out var schedule))
            return;

        TryComp(ent, out WolfWeatherMapComponent? mapWeather);
        var hasWeather = mapWeather != null && mapWeather.Weathers.Count > 0;

        if (ent.Comp.Waiting)
        {
            if (hasWeather)
                return;

            ent.Comp.Waiting = false;
            StartGap(ent);
            return;
        }

        if (ent.Comp.Current != null)
        {
            var owns = OwnsWeather(ent, mapWeather);
            if (now < ent.Comp.NextChange)
            {
                if (!owns)
                    TakenOver(ent);

                return;
            }

            if (owns)
                _wolfWeather.SetWeather(ent, null, null);

            Forget(ent);
            StartGap(ent);
            return;
        }

        if (hasWeather)
        {
            TakenOver(ent);
            return;
        }

        if (now < ent.Comp.NextChange)
            return;

        if (PickStart(schedule) is { } entry)
            Start(ent, entry);
        else
            StartGap(ent);
    }

    private void Start(Entity<WeatherScheduleComponent> ent, WeatherScheduleEntry entry)
    {
        var now = _timing.CurTime;
        var duration = Roll(entry.Duration);

        _wolfWeather.SetWeather(ent, entry.Weather, duration);

        var mapWeather = Comp<WolfWeatherMapComponent>(ent);
        ent.Comp.Current = entry.Weather;
        ent.Comp.CurrentStart = now;
        ent.Comp.NextChange = now + duration;

        if (entry.WindAngle is { } angle && _proto.TryIndex(entry.Weather, out var weather))
        {
            var wind = Angle.FromDegrees(_random.NextFloat(angle.Min, angle.Max)).ToVec() * weather.Wind.Length();
            _wolfWeather.SetWind((ent.Owner, mapWeather), wind);
        }
    }

    private void StartGap(Entity<WeatherScheduleComponent> ent)
    {
        if (!_proto.TryIndex(ent.Comp.Schedule, out var schedule))
            return;

        ent.Comp.NextChange = _timing.CurTime + Roll(schedule.Gap);
    }

    /// <summary>Ends the weather this schedule started, if it still runs, and forgets the schedule's state.</summary>
    private void Reset(Entity<WeatherScheduleComponent> ent)
    {
        if (TryComp(ent, out WolfWeatherMapComponent? mapWeather) && OwnsWeather(ent, mapWeather))
            _wolfWeather.SetWeather(ent, null, null);

        Forget(ent);
        ent.Comp.Waiting = false;
    }

    /// <summary>Whether the map's newest weather is the one this schedule started.</summary>
    private static bool OwnsWeather(Entity<WeatherScheduleComponent> ent, WolfWeatherMapComponent? mapWeather)
    {
        return ent.Comp.Current is { } current
               && mapWeather != null
               && SharedWolfWeatherSystem.GetCurrent(mapWeather) is { } newest
               && newest.Weather == current
               && newest.Start == ent.Comp.CurrentStart;
    }

    private void TakenOver(Entity<WeatherScheduleComponent> ent)
    {
        Forget(ent);
        ent.Comp.Waiting = true;
    }

    private void Forget(Entity<WeatherScheduleComponent> ent)
    {
        ent.Comp.Current = null;
    }

    private WeatherScheduleEntry? PickStart(WeatherSchedulePrototype schedule)
    {
        _weights.Clear();
        foreach (var entry in schedule.Weathers)
        {
            AddWeight(entry.Weather, entry.Weight);
        }

        return PickWeighted() is { } weather ? FindEntry(schedule, weather) : null;
    }

    private void AddWeight(string weather, float weight)
    {
        if (weight > 0f)
            _weights[weather] = _weights.GetValueOrDefault(weather) + weight;
    }

    /// <summary>Picks from the collected weights. Null when nothing has weight.</summary>
    private ProtoId<WolfWeatherPrototype>? PickWeighted()
    {
        if (_weights.Count == 0)
            return null;

        return _random.Pick(_weights);
    }

    private static WeatherScheduleEntry? FindEntry(WeatherSchedulePrototype schedule, ProtoId<WolfWeatherPrototype> weather)
    {
        foreach (var entry in schedule.Weathers)
        {
            if (entry.Weather == weather)
                return entry;
        }

        return null;
    }

    private TimeSpan Roll(ScheduleTimeRange range)
    {
        return _random.Next(range.Min, range.Max < range.Min ? range.Min : range.Max);
    }
}
