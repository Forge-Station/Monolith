using Robust.Shared.GameStates;

namespace Content.Shared._HL.Silicons.Synths.Surgery;

[RegisterComponent, NetworkedComponent]
public sealed partial class SynthSurgeryBatterySlotEmptyConditionComponent : Component
{
    [DataField(required: true)]
    public string Slot;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class SynthSurgeryBatterySlotFullConditionComponent : Component
{
    [DataField(required: true)]
    public string Slot;
}
