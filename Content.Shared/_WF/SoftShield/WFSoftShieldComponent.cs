using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.SoftShield;

/// <summary>
/// Turns a personal shield into a soft shield: hits pass through instead of being blocked, one damage group is
/// dampened by a fixed share, and the wearer opens no new bleeds while the field is up.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFSoftShieldComponent : Component
{
    /// <summary>Damage group the field dampens; every other type passes to the suit's armour untouched.</summary>
    [DataField]
    public ProtoId<DamageGroupPrototype> DamageGroup = "Brute";

    /// <summary>Share of that damage the field soaks, drained from its charge. The rest bleeds through.</summary>
    [DataField]
    public float Reduction = 0.6f;
}
