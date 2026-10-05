using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared.Weather;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Planets;

/// <summary>Weather choices and the local clock for one planet type.</summary>
[Prototype("wfPlanetWeather")]
public sealed partial class WFPlanetWeatherPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public ProtoId<PlanetTypePrototype> PlanetType;
    /// <summary>Single-phase weather, picked from when <see cref="Storms"/> is empty.</summary>
    [DataField] public List<ProtoId<WeatherPrototype>> Weather = new();
    /// <summary>Phased storms, SS13 style: a light telegraph, the storm itself, then a wind-down.</summary>
    [DataField] public List<WFPlanetStorm> Storms = new();
    [DataField] public float ClearMinSeconds = 180f;
    [DataField] public float ClearMaxSeconds = 420f;
    [DataField] public float WeatherMinSeconds = 120f;
    [DataField] public float WeatherMaxSeconds = 240f;
    /// <summary>Real seconds per local day, shared by the watch, daylight, shadows and ambience.</summary>
    [DataField] public float DaySeconds = 3600f;
    [DataField] public float InitialHour = 8f;
}

/// <summary>One storm a world can have, as the three overlays it moves through.</summary>
[DataDefinition]
public sealed partial class WFPlanetStorm
{
    /// <summary>Light overlay that warns the storm is coming; null starts at full strength.</summary>
    [DataField] public ProtoId<WeatherPrototype>? Telegraph;
    [DataField(required: true)] public ProtoId<WeatherPrototype> Main;
    /// <summary>Light overlay the storm fades through on its way out.</summary>
    [DataField] public ProtoId<WeatherPrototype>? End;
    [DataField] public float TelegraphSeconds = 30f;
    [DataField] public float EndSeconds = 30f;
    /// <summary>Whether lightning strikes open ground near visitors while the main phase runs.</summary>
    [DataField] public bool Thunder;
}
