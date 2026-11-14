using Robust.Shared.Configuration;

namespace Content.Shared._WF.WolfWeather;

/// <summary>Settings for weather.</summary>
[CVarDefs]
public sealed class WolfWeatherCVars
{
    /// <summary>Client weather detail: 0 off, 1 reduced, 2 full with splashes.</summary>
    public static readonly CVarDef<int> WeatherQuality =
        CVarDef.Create("wf.wolf_weather.quality", 2, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Whether weather schedules roll new weather on their maps.</summary>
    public static readonly CVarDef<bool> WeatherSchedule =
        CVarDef.Create("wf.wolf_weather.schedule", true, CVar.SERVERONLY);
}
