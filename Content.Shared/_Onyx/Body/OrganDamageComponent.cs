// WOLFGATE(Wolfmed): ported from Onyx for Wolfmed.
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Onyx.Body;

/// <summary>
/// Defines how damage to a containing body part is routed into this organ.
/// </summary>
[RegisterComponent]
public sealed partial class OrganDamageComponent : Component
{
    [DataField]
    public float HitChance = 1f;

    [DataField]
    public float SelectionWeight = 1f;

    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, float> DamageMultipliers = new();

    [DataField]
    public float MaxDamageFraction = 0.3f;
}
