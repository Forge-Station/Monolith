using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Components;

/// <summary>
/// A crewman posted where his ship has no air: he is spawned in this pressure kit, and the tank he wears never runs
/// down while he does.
/// </summary>
[RegisterComponent]
public sealed partial class WFCrewVoidKitComponent : Component
{
    /// <summary>The suit, helmet, mask and tank put on him, replacing whatever his loadout had in those slots.</summary>
    [DataField]
    public ProtoId<StartingGearPrototype> Gear = "WFCrewVoidKitGear";

    /// <summary>The pressure, in kPa, the worn tank is kept topped up to.</summary>
    [DataField]
    public float TankPressure = 1000f;
}
