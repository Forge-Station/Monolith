using Robust.Shared.GameStates;

namespace Content.Shared._HL.Silicons.Synths.Surgery;

[RegisterComponent, NetworkedComponent]
public sealed partial class SynthSurgeryStepBatteryInsertComponent : Component
{
    [DataField(required: true)]
    public string Slot;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class SynthSurgeryStepBatteryExtractComponent : Component
{
    [DataField(required: true)]
    public string Slot;
}
