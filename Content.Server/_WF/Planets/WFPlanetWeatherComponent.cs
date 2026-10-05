using Content.Shared._WF.Planets;
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

    /// <summary>Index into the profile's storms of the one running.</summary>
    [DataField] public int Storm;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextThunder;
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
}
