using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._HL.Silicons.Synths.Body;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(Other = AccessPermissions.ReadWrite)]
public sealed partial class SynthBloodstreamComponent : Component
{
    /// <summary>
    /// Hunger level below which passive repair, bleed sealing, and blood regeneration stop.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MinHunger = 50f;

    /// <summary>
    /// Blood level below which passive repair and bleed sealing stop.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MinBloodLevel = 0.3f;

    /// <summary>
    /// Blood level at or above which passive repair and bleed sealing operate at full efficiency.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float FullEfficiencyBloodLevel = 0.9f;

    /// <summary>
    /// Passive repair and bleed sealing efficiency at minimum blood level.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MinBloodEfficiency = 0.1f;

    /// <summary>
    /// Hunger spent per point of damage repaired.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HungerCostPerRepair = 1.5f;

    /// <summary>
    /// Hunger spent per point of bleed amount sealed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HungerCostPerBleed = 1f;

    /// <summary>
    /// Passive repair applied each update. Negative damage values heal.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SynthBloodstreamDamageSpecifier Damage = new();

    [ViewVariables]
    public DamageSpecifier PassiveRepair = new();

    /// <summary>
    /// Bleed amount sealed each update before blood efficiency and hunger limits.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float BleedReductionAmount = 0.1f;

    /// <summary>
    /// Synth blood level regeneration settings, separate from passive repair.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SynthBloodstreamRegeneration BloodRegeneration = new();

}

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class SynthBloodstreamDamageSpecifier
{
    [DataField]
    public Dictionary<ProtoId<DamageGroupPrototype>, FixedPoint2> Groups = new();

    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2> Types = new();

    [ViewVariables]
    public bool Empty => Groups.Count == 0 && Types.Count == 0;
}

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class SynthBloodstreamRegeneration
{
    [DataField]
    public float TargetBloodLevel = 1f;

    [DataField]
    public FixedPoint2 BloodRefreshAmount = FixedPoint2.New(0.03f);

    [DataField]
    public float HungerCostPerUnit = 0.5f;
}
