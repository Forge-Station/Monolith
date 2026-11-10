using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Traders;

/// <summary>
/// Lets a trader sell the stock of a vending machine prototype, at that machine's prices.
/// </summary>
[RegisterComponent]
public sealed partial class TraderShopComponent : Component
{
    /// <summary>
    /// Most of any one item a single basket may hold.
    /// </summary>
    public const int MaxPerLine = 20;

    /// <summary>
    /// Most distinct items a single basket may hold.
    /// </summary>
    public const int MaxLines = 12;

    /// <summary>
    /// Vending machine entity prototypes whose packs, pricing and taxes are mirrored. Their
    /// stock is concatenated in order; an item listed twice keeps the first machine's price.
    /// </summary>
    [DataField(required: true)]
    public List<EntProtoId> Vendors = new();

    /// <summary>Multiplies every price. Below one is a discount.</summary>
    [DataField]
    public float PriceMultiplier = 1f;

    /// <summary>How many items a trader picks at random. Zero keeps the full mirrored stock.</summary>
    [DataField]
    public int RandomStock;

    [DataField]
    public int StockMin = 1;

    [DataField]
    public int StockMax = 4;

    [ViewVariables]
    public Dictionary<string, int>? Limited;

    [DataField]
    public int LootItems = 4;

    [DataField]
    public EntProtoId LootCrate = "CrateGenericSteel";

    [ViewVariables]
    public bool LootDropped;

    /// <summary>
    /// Carton an order is packed into when it runs to more than one item.
    /// </summary>
    [DataField]
    public EntProtoId Parcel = "WFTraderParcel";
}
