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

    /// <summary>Seconds until the scheduler next changes the weather.</summary>
    public float WeatherSeconds;

    public float Gravity = 1f;
    public bool Sanctioned = true;
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
}
