using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.Tag; // Forge-change

namespace Content.Shared._NF.Medical.Prototypes;

/// <summary>
/// This is a prototype for a pirate bounty, a set of items
/// that must be sold together in a labeled container in order
/// to receive a reward in doubloons.
/// </summary>
[Prototype, Serializable, NetSerializable]
public sealed partial class MedicalBountyPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The base monetary reward for a bounty of this type
    /// </summary>
    [DataField(required: true)]
    public int BaseReward;

    /// <summary>
    /// Damage types to be added to a bountied entity and the bonus/penalties associated with them
    /// </summary>
    [DataField(required: true, customTypeSerializer: typeof(ProtoId<DamageTypePrototype>))]
    public Dictionary<string, RandomDamagePreset> DamageSets = new();

    /// <summary>
    /// Damage types to be added to a bountied entity and the bonus/penalties associated with them
    /// </summary>
    [DataField(customTypeSerializer: typeof(ProtoId<ReagentPrototype>))]
    public Dictionary<string, RandomReagentPreset> Reagents = new();

    /// <summary>
    /// Penalty for other damage types not in DamageSets on redemption.
    /// </summary>
    [DataField("otherPenalty")]
    public int PenaltyPerOtherPoint = 25;

    /// <summary>
    /// Maximum damage before bounty can be claimed.
    /// </summary>
    [DataField]
    public int MaximumDamageToRedeem = 99;

    // Forge-change-start
    /// <summary>
    /// Bonus reward prototype
    /// </summary>
    [DataField]
    public ProtoId<EntityPrototype>? BonusReward = "TTIMedicalCredit1";

    /// <summary>
    /// Bonus reward percentage. Name has a logic to it...
    /// </summary>
    [DataField]
    public float BonusRewardPercent = 0.01f;

    /// <summary>
    /// Bonus reward for turning in a body equipped with gear bearing the TTIEquip tag
    /// </summary>
    [DataField(customTypeSerializer: typeof(ProtoId<TagPrototype>))]
    public string BonusRewardTag = "TTIEquip";
    // Forge-change-end
}

[DataDefinition, Serializable, NetSerializable]
public partial record struct RandomDamagePreset
{
    /// <summary>
    /// The minimum amount of damage to receive.
    /// </summary>
    [DataField("min")]
    public int MinDamage;
    /// <summary>
    /// The maximum amount of damage to receive.
    /// </summary>
    [DataField("max")]
    public int MaxDamage;
    /// <summary>
    /// The maximum amount of damage to receive.
    /// </summary>
    [DataField("value")]
    public int ValuePerPoint;
    /// <summary>
    /// The base monetary reward
    /// </summary>
    [DataField("penalty")]
    public int PenaltyPerPoint;
}

[DataDefinition, Serializable, NetSerializable]
public partial record struct RandomReagentPreset
{
    /// <summary>
    /// The minimum amount of damage to receive.
    /// </summary>
    [DataField("min")]
    public int MinQuantity;
    /// <summary>
    /// The maximum amount of damage to receive.
    /// </summary>
    [DataField("max")]
    public int MaxQuantity;
    /// <summary>
    /// The maximum amount of damage to receive.
    /// </summary>
    [DataField("value")]
    public int ValuePerPoint;
}
