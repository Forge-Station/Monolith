using Robust.Shared.GameObjects;

namespace Content.Shared._WF.Vehicles.Hardpoint;

/// <summary>Scales the damage a weapon or a charge deals to vehicles.</summary>
[RegisterComponent]
public sealed partial class VehicleDamageMultiplierComponent : Component
{
    [DataField]
    public float Multiplier = 1f;
}
