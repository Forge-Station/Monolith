using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._WF.Vehicles.Hardpoint;

/// <summary>Marks a slot type prototype.</summary>
[RegisterComponent]
public sealed partial class HardpointSlotTypeComponent : Component
{
    [DataField]
    public float RepairRate = 0.05f;
}
