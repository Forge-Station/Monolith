using Content.Shared.Weather;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Planets;

public sealed partial class WFPlanetWeatherSystem
{
    /// <summary>A world's clock and scheduled weather, for the admin panel.</summary>
    public bool TryGetState(EntityUid world, out int minuteOfDay, out float daySeconds, out string weather, out float weatherSeconds)
    {
        minuteOfDay = 0;
        daySeconds = 0f;
        weather = string.Empty;
        weatherSeconds = 0f;

        if (!TryComp<WFPlanetWeatherComponent>(world, out var state) || !_proto.TryIndex(state.Profile, out var profile))
            return false;

        minuteOfDay = GetMinuteOfDay(state, profile);
        daySeconds = profile.DaySeconds;
        weather = state.Current?.Id ?? string.Empty;
        weatherSeconds = MathF.Max(0f, (float) (state.NextChange - _timing.CurTime).TotalSeconds);
        return true;
    }

    /// <summary>Moves a world's clock by shifting its epoch, so the watch, the daylight and the soundscape all follow.</summary>
    public bool SetMinuteOfDay(EntityUid world, int minuteOfDay)
    {
        if (!TryComp<WFPlanetWeatherComponent>(world, out var state) || !_proto.TryIndex(state.Profile, out var profile))
            return false;

        var minutes = ((minuteOfDay - profile.InitialHour * 60) % 1440 + 1440) % 1440;
        state.Epoch = _timing.CurTime - TimeSpan.FromSeconds(minutes * Math.Max(1, profile.DaySeconds) / 1440.0);
        _nextUpdate = TimeSpan.Zero;
        return true;
    }

    /// <summary>Overrides the scheduler: this weather (null for clear) holds for the duration, then the cycle resumes.</summary>
    public bool SetWeather(EntityUid world, ProtoId<WeatherPrototype>? weather, TimeSpan duration)
    {
        if (!TryComp<WFPlanetWeatherComponent>(world, out var state))
            return false;

        // Held as a storm's last phase, so the scheduler's next step from it is clear skies and its own cycle.
        state.Current = weather;
        state.Phase = WFStormPhase.End;
        state.NextChange = _timing.CurTime + duration;
        _nextUpdate = TimeSpan.Zero;
        return true;
    }
}
