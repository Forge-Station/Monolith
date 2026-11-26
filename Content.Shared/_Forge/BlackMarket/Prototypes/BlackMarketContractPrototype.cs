using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.BlackMarket.Prototypes;

[Prototype]
public sealed partial class BlackMarketContractPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<BlackMarketCategoryPrototype> Category { get; private set; }

    [DataField(required: true)]
    public int Price { get; private set; }

    [DataField(required: true)]
    public LocId Name { get; private set; }

    [DataField]
    public LocId Description { get; private set; } = string.Empty;

    /// <summary>
    /// Entity prototype whose sprite is shown in the console UI.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Icon { get; private set; }

    [DataField(required: true)]
    public List<BlackMarketContractContentEntry> Contents { get; private set; } = new();

    /// <summary>
    /// Max purchases of this contract from a single console per round. 0 = unlimited.
    /// </summary>
    [DataField]
    public int PurchaseLimit;

    /// <summary>
    /// When enabled, price increases after each purchase of this contract on the same console.
    /// </summary>
    [DataField]
    public bool DynamicPrice;

    /// <summary>
    /// Price multiplier added per prior purchase this round. 0.15 = +15% of base price per purchase.
    /// </summary>
    [DataField]
    public float DynamicPriceIncreasePercent = 0.15f;

    /// <summary>
    /// Upper cap for dynamic price. 0 = no cap.
    /// </summary>
    [DataField]
    public int DynamicPriceMax;

    /// <summary>
    /// Placeholder contract shown when the pool is exhausted. Not purchasable.
    /// </summary>
    [DataField]
    public bool Stub;
}

[DataDefinition, Serializable, NetSerializable]
public readonly partial record struct BlackMarketContractContentEntry()
{
    [DataField(required: true)]
    public EntProtoId Proto { get; init; }

    [DataField]
    public int Amount { get; init; } = 1;
}
