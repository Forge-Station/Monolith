using Content.Shared.Random;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.BlackMarket.Prototypes;

[Prototype]
public sealed partial class BlackMarketCategoryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name { get; private set; }

    [DataField(required: true)]
    public ProtoId<WeightedRandomPrototype> ContractPool { get; private set; }

    [DataField]
    public TimeSpan PassiveRefreshDelay = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan PurchasedRefreshDelay = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Number of recently shown or purchased contracts per category excluded from the next roll on this console.
    /// </summary>
    [DataField]
    public int RecentHistorySize = 2;

    /// <summary>
    /// Fallback contract when no valid entries remain in the pool after exclusions.
    /// </summary>
    [DataField]
    public ProtoId<BlackMarketContractPrototype>? StubContract;
}
