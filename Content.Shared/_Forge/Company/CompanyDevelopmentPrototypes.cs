using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Forge.Company;

public enum CompanyProjectEffect : byte
{
    None = 0,
    MemberSlots = 1,
    RelationCooldown = 2,
    SupplyDiscount = 3,
    ShipDiscount = 4,
    DiplomacyDiscount = 5,
}

/// <summary>
/// One-time treasury purchase that changes how the company works.
/// </summary>
[Prototype("companyProject")]
public sealed partial class CompanyProjectPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Purpose;

    [DataField(required: true)]
    public int Cost;

    [DataField(required: true)]
    public CompanyProjectEffect Effect;

    /// <summary>
    /// MemberSlots: extra members. RelationCooldown: seconds. Discounts: percent off.
    /// </summary>
    [DataField(required: true)]
    public int Amount;
}

/// <summary>
/// Repeatable crate the treasury can drop next to the buyer.
/// </summary>
[Prototype("companySupply")]
public sealed partial class CompanySupplyPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Purpose;

    [DataField(required: true)]
    public int Cost;

    [DataField(required: true)]
    public EntProtoId Crate;
}

/// <summary>
/// Ship voucher the treasury can buy. The price is the vessel prototype price.
/// </summary>
[Prototype("companyShipOffer")]
public sealed partial class CompanyShipOfferPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Vessel = string.Empty;
}

/// <summary>
/// Item delivery the company can take from the market or fund from the treasury.
/// </summary>
[Prototype("companyContract")]
public sealed partial class CompanyContractPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Purpose;

    /// <summary>Exact entity prototype to take from the member's inventory. Empty when <see cref="StackType"/> is set.</summary>
    [DataField]
    public EntProtoId? Item;

    /// <summary>When set, any stack of this type counts, whatever the entity id is.</summary>
    [DataField]
    public string? StackType;

    [DataField(required: true)]
    public int Count = 1;

    [DataField(required: true)]
    public int Reward;
}
