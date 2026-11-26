using Robust.Shared.Serialization;

namespace Content.Shared._WF.Planets.Administration;

/// <summary>One world in the admin Planet Control window.</summary>
[Serializable, NetSerializable]
public sealed class PlanetControlInfo
{
    public NetEntity Planet;
    public string Name = string.Empty;

    /// <summary>False until the world's layer stack has been built; only the sanction can be set before that.</summary>
    public bool Built;

    public int MinuteOfDay;
    public float DaySeconds;

    /// <summary>The scheduled weather prototype, or empty for clear skies.</summary>
    public string Weather = string.Empty;

    /// <summary>The WolfWeather shown on the ground, or empty for none.</summary>
    public string WolfWeather = string.Empty;

    /// <summary>Seconds until the scheduler next changes the weather.</summary>
    public float WeatherSeconds;

    public float Gravity = 1f;
    public bool Sanctioned = true;

    /// <summary>The current season's id, or empty for a world without seasons.</summary>
    public string Season = string.Empty;
    public float SeasonSecondsLeft;

    /// <summary>Every season the world has, as ids and Fluent names in the same order.</summary>
    public List<string> SeasonIds = new();
    public List<string> SeasonNames = new();

    /// <summary>The outdoor air temperature in kelvin; NaN until the world has one.</summary>
    public float Temperature = float.NaN;
}

/// <summary>Client asks the server for the world list. Admin-only; ignored otherwise.</summary>
[Serializable, NetSerializable]
public sealed class PlanetControlListRequestEvent : EntityEventArgs;

/// <summary>Server reply carrying the world list.</summary>
[Serializable, NetSerializable]
public sealed class PlanetControlListEvent : EntityEventArgs
{
    public List<PlanetControlInfo> Planets;

    public PlanetControlListEvent(List<PlanetControlInfo> planets)
    {
        Planets = planets;
    }
}

/// <summary>One change to one world; every null field is left alone. Admin-only; ignored otherwise.</summary>
[Serializable, NetSerializable]
public sealed class PlanetControlSetEvent : EntityEventArgs
{
    public NetEntity Planet;
    public int? MinuteOfDay;

    /// <summary>True when <see cref="Weather"/> is meant, since null there is itself a value: clear skies.</summary>
    public bool SetWeather;
    public string? Weather;
    public float WeatherSeconds = 600f;

    public float? Gravity;
    public bool? Sanctioned;

    /// <summary>A season id to start now.</summary>
    public string? Season;
}
