using Content.Shared._WF.Planets;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Weather;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WF.Planets;

/// <summary>One weather scheduler and clock epoch per world, not per tile or player.</summary>
[RegisterComponent, UnsavedComponent, AutoGenerateComponentPause]
public sealed partial class WFPlanetWeatherComponent : Component
{
    [DataField] public ProtoId<WFPlanetWeatherPrototype> Profile;
    [DataField] public string PlanetName = string.Empty;
    [DataField] public ProtoId<WeatherPrototype>? Current;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan Epoch;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextChange;

    /// <summary>Where a phased storm has got to; <see cref="WFStormPhase.None"/> under clear skies.</summary>
    [DataField] public WFStormPhase Phase;
    /// <summary>An admin set this weather; it holds until <see cref="NextChange"/> even while planet weather is off.</summary>
    [DataField] public bool AdminHeld;

    /// <summary>Index into the profile's storms of the one running.</summary>
    [DataField] public int Storm;

    /// <summary>The phases rolled to follow the current one, in order; the first starts at <see cref="NextChange"/>.</summary>
    [DataField] public List<WFWeatherPhase> Upcoming = new();

    /// <summary>When the first season of the profile's cycle last began.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan SeasonEpoch;

    /// <summary>The world's outdoor air temperature before seasons, weather and the hour, in kelvin.</summary>
    [DataField] public float BaseTemperature = float.NaN;
    /// <summary>The outdoor air temperature the world's layers hold now, in kelvin; NaN until first set.</summary>
    [DataField] public float Temperature = float.NaN;
    /// <summary>The temperature last written to the layers' air.</summary>
    [DataField] public float AppliedTemperature = float.NaN;
}

/// <summary>One scheduled weather phase: what falls, as which storm phase, for how long.</summary>
[DataDefinition]
public sealed partial class WFWeatherPhase
{
    [DataField] public ProtoId<WeatherPrototype>? Weather;
    [DataField] public WFStormPhase Phase;
    [DataField] public float Seconds;
}

/// <summary>Phases of a scheduled storm.</summary>
public enum WFStormPhase : byte
{
    None,
    Telegraph,
    Main,
    End,
}

/// <summary>Remembers the state copied onto a static or newly-created transit map.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPlanetWeatherAppliedComponent : Component
{
    public ProtoId<WeatherPrototype>? Current;
    public TimeSpan EndTime;

    /// <summary>The WolfWeather last set on the map for <see cref="Current"/>.</summary>
    public ProtoId<WolfWeatherPrototype>? WolfWeather;
}
