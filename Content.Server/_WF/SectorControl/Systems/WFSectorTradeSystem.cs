using Content.Shared._Mono.Company;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.SectorControl.Systems;

/// <summary>
/// The prices a held hex will apply once outposts can be moved onto one. Nothing reads this into a sale yet:
/// <see cref="Quote"/> stays even. When that move exists, a station in the hex should use
/// <see cref="PlannedOwnBuy"/> for its holder and <see cref="PlannedOtherBuy"/> for everyone else.
/// </summary>
public sealed class WFSectorTradeSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;

    /// <summary>What the holding company will pay, as a fraction of the list price.</summary>
    public const float PlannedOwnBuy = 0.9f;

    /// <summary>What everyone else will pay in that hex.</summary>
    public const float PlannedOtherBuy = 1.15f;

    /// <summary>What the holding company will be paid when it sells there.</summary>
    public const float PlannedOwnSell = 1.1f;

    /// <summary>What everyone else will be paid when they sell there.</summary>
    public const float PlannedOtherSell = 0.9f;

    /// <summary>Today's prices: even. The planned rates stay on the constants until outposts can stand in a claimed hex.</summary>
    public WFSectorTradeQuote Quote(MapId map, WFSectorCell cell, ProtoId<CompanyPrototype>? visitor)
    {
        var quote = new WFSectorTradeQuote(1f, 1f);
        if (_territory.Owner(map, cell) is not { } faction
            || !_prototypes.TryIndex(faction, out var proto))
            return quote;

        var ev = new WFSectorTradeQueryEvent(map, cell, proto.Company, visitor, quote.Buy, quote.Sell);
        RaiseLocalEvent(ref ev);
        return new WFSectorTradeQuote(ev.Buy, ev.Sell);
    }
}

/// <summary>Buy is what a visitor pays. Sell is what they are paid. Both are 1 until a listener changes them.</summary>
public readonly record struct WFSectorTradeQuote(float Buy, float Sell);

/// <summary>Raised by <see cref="WFSectorTradeSystem.Quote"/> so a later trade pass can tilt the rates.</summary>
[ByRefEvent]
public record struct WFSectorTradeQueryEvent(
    MapId Map,
    WFSectorCell Cell,
    ProtoId<CompanyPrototype>? Holder,
    ProtoId<CompanyPrototype>? Visitor,
    float Buy,
    float Sell);
