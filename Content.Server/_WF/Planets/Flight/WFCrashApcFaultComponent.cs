using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WF.Planets.Flight;

/// <summary>Impact-damaged regulator: an actual output interruption until repaired with a multitool.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WFCrashApcFaultComponent : Component
{
    [DataField] public bool Interrupted = true;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextFlicker;
}
