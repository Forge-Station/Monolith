using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Weather;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Planets;

/// <summary>
/// Phased storms: telegraph, main, end. The phases are rolled ahead into a queue, so the forecast is what will happen.
/// Lightning comes from the world's WolfWeather for the phase.
/// </summary>
public sealed partial class WFPlanetWeatherSystem
{
    /// <summary>How far ahead each world's weather is rolled.</summary>
    public static readonly TimeSpan ForecastHorizon = TimeSpan.FromHours(3);

    private const int MaxUpcoming = 256;

    /// <summary>Moves a world on to its next rolled phase.</summary>
    private void Advance(WFPlanetWeatherComponent state, WFPlanetWeatherPrototype profile)
    {
        if (state.Upcoming.Count == 0)
            Fill(state, profile);

        if (state.Upcoming.Count == 0)
        {
            Enter(state, WFStormPhase.None, null, _random.NextFloat(profile.ClearMinSeconds, profile.ClearMaxSeconds));
            return;
        }

        var next = state.Upcoming[0];
        state.Upcoming.RemoveAt(0);
        Enter(state, next.Phase, next.Weather, next.Seconds);
        Fill(state, profile);
    }

    /// <summary>Rolls phases onto the queue until it reaches past the forecast horizon.</summary>
    private void Fill(WFPlanetWeatherComponent state, WFPlanetWeatherPrototype profile)
    {
        // With the cycle off nothing is coming: an admin's weather holds, then the world stays clear.
        if (!_cfg.GetCVar(PlanetCVars.PlanetWeather))
            return;

        var end = state.NextChange;
        foreach (var phase in state.Upcoming)
        {
            end += TimeSpan.FromSeconds(phase.Seconds);
        }

        var horizon = _timing.CurTime + ForecastHorizon;
        while (end < horizon && state.Upcoming.Count < MaxUpcoming)
        {
            var clear = state.Upcoming.Count > 0
                ? state.Upcoming[^1].Phase == WFStormPhase.None
                : state.Phase == WFStormPhase.None && state.Current == null;
            var season = GetSeason(state, profile, end);
            var count = state.Upcoming.Count;

            if (!clear || !RollWeather(profile, season, state.Upcoming))
            {
                var seconds = _random.NextFloat(profile.ClearMinSeconds, profile.ClearMaxSeconds) * (season?.ClearScale ?? 1f);
                state.Upcoming.Add(new WFWeatherPhase { Phase = WFStormPhase.None, Seconds = MathF.Max(1f, seconds) });
            }

            for (var i = count; i < state.Upcoming.Count; i++)
            {
                end += TimeSpan.FromSeconds(state.Upcoming[i].Seconds);
            }
        }
    }

    /// <summary>Drops what was rolled and rolls again; a storm under way keeps its remaining phases.</summary>
    public void Reroll(WFPlanetWeatherComponent state, WFPlanetWeatherPrototype profile)
    {
        var keep = 0;
        if (state.Phase is WFStormPhase.Telegraph or WFStormPhase.Main)
        {
            while (keep < state.Upcoming.Count && state.Upcoming[keep].Phase is WFStormPhase.Main or WFStormPhase.End)
            {
                keep++;
            }
        }

        state.Upcoming.RemoveRange(keep, state.Upcoming.Count - keep);
        Fill(state, profile);
    }

    /// <summary>Rolls the next storm of the season, or a single weather; false when the world has none.</summary>
    private bool RollWeather(WFPlanetWeatherPrototype profile, WFPlanetSeason? season, List<WFWeatherPhase> into)
    {
        var storms = season?.Storms ?? profile.Storms;
        var scale = season?.WeatherScale ?? 1f;
        var main = MathF.Max(1f, _random.NextFloat(profile.WeatherMinSeconds, profile.WeatherMaxSeconds) * scale);

        if (storms.Count == 0)
        {
            if (profile.Weather.Count == 0)
                return false;
            into.Add(new WFWeatherPhase { Weather = _random.Pick(profile.Weather), Phase = WFStormPhase.Main, Seconds = main });
            return true;
        }

        var total = 0f;
        foreach (var candidate in storms)
        {
            total += StormWeight(season, candidate);
        }

        var storm = storms[0];
        if (total <= 0f)
        {
            storm = _random.Pick(storms);
        }
        else
        {
            var roll = _random.NextFloat(total);
            foreach (var candidate in storms)
            {
                storm = candidate;
                roll -= StormWeight(season, candidate);
                if (roll < 0f)
                    break;
            }
        }

        if (storm.Telegraph is { } telegraph)
            into.Add(new WFWeatherPhase { Weather = telegraph, Phase = WFStormPhase.Telegraph, Seconds = storm.TelegraphSeconds });
        into.Add(new WFWeatherPhase { Weather = storm.Main, Phase = WFStormPhase.Main, Seconds = main });
        if (storm.End is { } end)
            into.Add(new WFWeatherPhase { Weather = end, Phase = WFStormPhase.End, Seconds = storm.EndSeconds });
        return true;
    }

    private static float StormWeight(WFPlanetSeason? season, WFPlanetStorm storm)
    {
        return season != null && season.StormWeights.TryGetValue(storm.Main, out var weight) ? MathF.Max(0f, weight) : 1f;
    }

    private void Enter(WFPlanetWeatherComponent state, WFStormPhase phase, ProtoId<WeatherPrototype>? weather, float seconds)
    {
        state.Phase = phase;
        state.Current = weather;
        state.NextChange = _timing.CurTime + TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// A world's weather from now to the forecast horizon: the current phase first, then each rolled one with its start
    /// and end. Empty while the cycle is off and nothing is held.
    /// </summary>
    public bool TryGetSchedule(EntityUid world, List<WFScheduledWeather> into)
    {
        into.Clear();
        if (!TryComp<WFPlanetWeatherComponent>(world, out var state))
            return false;

        var now = _timing.CurTime;
        into.Add(new WFScheduledWeather(now, state.NextChange, state.Current, state.Phase));
        var start = state.NextChange;
        foreach (var phase in state.Upcoming)
        {
            var end = start + TimeSpan.FromSeconds(phase.Seconds);
            into.Add(new WFScheduledWeather(start, end, phase.Weather, phase.Phase));
            start = end;
        }

        return true;
    }

    /// <summary>The weather the schedule has falling at a time; the current weather for any time already past.</summary>
    public ProtoId<WeatherPrototype>? GetWeatherAt(WFPlanetWeatherComponent state, TimeSpan at)
    {
        if (at < state.NextChange)
            return state.Current;

        var start = state.NextChange;
        foreach (var phase in state.Upcoming)
        {
            start += TimeSpan.FromSeconds(phase.Seconds);
            if (at < start)
                return phase.Weather;
        }

        return null;
    }
}

/// <summary>One stretch of a world's scheduled weather.</summary>
public readonly record struct WFScheduledWeather(TimeSpan Start, TimeSpan End, ProtoId<WeatherPrototype>? Weather, WFStormPhase Phase);
