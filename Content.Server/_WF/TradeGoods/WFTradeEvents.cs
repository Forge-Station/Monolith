namespace Content.Server._WF.TradeGoods;

/// <summary>A change to a quote: a factor and the reason players see.</summary>
public readonly record struct WFTradeModifier(float Factor, LocId Reason);

/// <summary>
/// Raised broadcast when a chute quotes a good. Any module adds modifiers; those above 1 are capped together, those
/// below 1 apply after.
/// </summary>
public sealed class WFTradeQuoteEvent(EntityUid grid, EntityUid chute, string good, EntityUid? crate, EntityUid? seller) : EntityEventArgs
{
    public readonly EntityUid Grid = grid;
    public readonly EntityUid Chute = chute;
    public readonly string Good = good;

    /// <summary>The crate being sold, or null for a listed price.</summary>
    public readonly EntityUid? Crate = crate;

    /// <summary>Who is selling it, or null for a listed price.</summary>
    public readonly EntityUid? Seller = seller;

    public readonly List<WFTradeModifier> Modifiers = new();
}

/// <summary>
/// Raised broadcast when a station's trade vendor prices a good. Modules whose conditions make goods scarce there, such
/// as shortages, blockades and raids, add factors above 1; they are capped together like a chute's.
/// </summary>
public sealed class WFTradeBuyQuoteEvent(EntityUid grid, string good) : EntityEventArgs
{
    public readonly EntityUid Grid = grid;
    public readonly string Good = good;
    public readonly List<WFTradeModifier> Modifiers = new();
}

/// <summary>Raised broadcast when a chute buys a crate, before the crate is deleted.</summary>
public readonly record struct WFTradeSaleEvent(
    EntityUid Grid,
    EntityUid Chute,
    string Good,
    int Amount,
    EntityUid Seller,
    EntityUid? Ship,
    EntityUid Crate);
