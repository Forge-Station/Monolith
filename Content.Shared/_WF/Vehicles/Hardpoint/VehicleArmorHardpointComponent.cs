using System.Collections.Generic;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Vehicles.Hardpoint;

/// <summary>An armour part: the damage modifier sets it adds to the vehicle and its explosion coefficient.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VehicleArmorHardpointComponent : Component
{
    [DataField]
    public List<ProtoId<DamageModifierSetPrototype>> ModifierSets = new();

    [DataField]
    public float? ExplosionCoefficient;
}
