using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WF.Planets.Flight;

/// <summary>Unscaled engine ratings and the atmospheric power-recovery debounce.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WFAtmosphereThrusterComponent : Component
{
    [DataField] public float RatedThrust;
    [DataField] public float RatedLoad;
    [DataField] public bool Atmospheric;
    [DataField] public bool WasPowered;
    [DataField] public bool PowerLimited;
    [DataField] public bool Cooling;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan PulseAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan RecoverAt;
}
