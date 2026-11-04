using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Weather;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Planets;

/// <summary>Weather choices, seasons, climate and the local clock for one planet type.</summary>
[Prototype("wfPlanetWeather")]
public sealed partial class WFPlanetWeatherPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public ProtoId<PlanetTypePrototype> PlanetType;
    /// <summary>Single-phase weather, picked from when <see cref="Storms"/> is empty.</summary>
    [DataField] public List<ProtoId<WeatherPrototype>> Weather = new();
    /// <summary>Phased storms, SS13 style: a light telegraph, the storm itself, then a wind-down.</summary>
    [DataField] public List<WFPlanetStorm> Storms = new();
    /// <summary>How each weather looks, sounds, blows and strikes on this world; a weather left out takes another world's, else shows nothing.</summary>
    [DataField] public Dictionary<ProtoId<WeatherPrototype>, ProtoId<WolfWeatherPrototype>> WolfWeathers = new();
    [DataField] public float ClearMinSeconds = 180f;
    [DataField] public float ClearMaxSeconds = 420f;
    [DataField] public float WeatherMinSeconds = 120f;
    [DataField] public float WeatherMaxSeconds = 240f;
    /// <summary>Real seconds per local day, shared by the watch, daylight, shadows and ambience.</summary>
    [DataField] public float DaySeconds = 3600f;
    [DataField] public float InitialHour = 8f;

    /// <summary>Seasons in order, looping; one is perpetual, none means the world has no seasons.</summary>
    [DataField] public List<WFPlanetSeason> Seasons = new();
    /// <summary>Kelvin added to the outdoor air while each weather falls.</summary>
    [DataField] public Dictionary<ProtoId<WeatherPrototype>, float> WeatherTemperature = new();
    /// <summary>Kelvin either side of the mean the day swings: warmest mid-afternoon, coldest before dawn.</summary>
    [DataField] public float DayTemperatureSwing = 4f;
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
}

/// <summary>A season of a world: how long it lasts, what weather it brings and how warm it is.</summary>
[DataDefinition]
public sealed partial class WFPlanetSeason
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public LocId Name;
    /// <summary>Real seconds the season lasts.</summary>
    [DataField] public float Length = 5400f;
    /// <summary>The season's own storms in place of the world's; null keeps the world's.</summary>
    [DataField] public List<WFPlanetStorm>? Storms;
    /// <summary>How often each storm comes, by its main weather; one when left out, zero never.</summary>
    [DataField] public Dictionary<ProtoId<WeatherPrototype>, float> StormWeights = new();
    /// <summary>Scales how long clear spells last.</summary>
    [DataField] public float ClearScale = 1f;
    /// <summary>Scales how long a storm's main phase lasts.</summary>
    [DataField] public float WeatherScale = 1f;
    /// <summary>Kelvin added to the world's outdoor air all season.</summary>
    [DataField] public float Temperature;
    /// <summary>Kelvin added under clear skies at the warmest hour, less towards night.</summary>
    [DataField] public float ClearDayTemperature;
    [DataField] public Color Color = Color.White;
}
