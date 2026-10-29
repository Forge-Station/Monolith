using System.Linq;
using Content.Server._WF.TradeGoods.Components;
using Content.Server.Cargo.Systems;
using Content.Shared._Mono.VendingMachine;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.TradeGoods;
using Content.Shared.VendingMachines;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>A trade vendor's live price for one good: its base price, what it charges now and the reason players see.</summary>
public readonly record struct WFTradeBuyQuote(int Base, int Amount, float Factor, LocId Reason);

/// <summary>
/// Buy prices at station trade vendors. Each trade good bought raises its scarcity at that station, which decays like
/// sale pressure; station conditions add modifiers through <see cref="WFTradeBuyQuoteEvent"/>. A vendor never charges
/// less than its base price, so the sell floors still cover a bought crate.
/// </summary>
public sealed partial class WFTradeMarketSystem
{
    [Dependency] private PricingSystem _pricing = default!;

    private static readonly TimeSpan VendorInterval = TimeSpan.FromSeconds(5);

    /// <summary>Scarcity above which a vendor names it as the reason for a higher price.</summary>
    private const float ScarceReason = 0.1f;

    private TimeSpan _nextVendorRefresh;

    private void InitializeBuy()
    {
        SubscribeLocalEvent<VendingMachinePurchaseComponent, ComponentStartup>(OnPurchased);
    }

    /// <summary>A good's scarcity at a station now, decayed since it was last touched.</summary>
    public float Scarcity(EntityUid grid, string good)
    {
        if (!TryComp<WFTradeMarketComponent>(grid, out var market) || !market.Scarcity.TryGetValue(good, out var entry))
            return 0f;

        var elapsed = (_timing.CurTime - entry.Touched).TotalSeconds;
        var halfLife = Math.Max(1.0, Profile(grid).HalfLife.TotalSeconds);
        var scarcity = (float) (entry.Scarcity * Math.Pow(0.5, elapsed / halfLife));
        market.Scarcity[good] = (scarcity, _timing.CurTime);
        return scarcity;
    }

    /// <summary>Adds scarcity to a good at a station, or eases it with a negative amount, and reprices its vendors.</summary>
    public void AddScarcity(EntityUid grid, string good, float amount)
    {
        var market = EnsureComp<WFTradeMarketComponent>(grid);
        market.Scarcity[good] = (Math.Max(0f, Scarcity(grid, good) + amount), _timing.CurTime);
        RefreshVendors(grid);
    }

    /// <summary>What a vendor charges for a good before the market moves it, as the vending machine works it out.</summary>
    public int? VendorBasePrice(EntityUid vendor, EntityPrototype proto)
    {
        var vend = _pricing.GetEstimatedVendPrice(proto);
        if (vend > 0.0)
            return (int) vend;

        var price = _pricing.GetEstimatedPrice(proto);
        if (price == 0)
            price = 20;

        if (TryComp<MarketModifierComponent>(vendor, out var modifier))
            price *= modifier.Mod;

        return price > 0 ? (int) price : null;
    }

    /// <summary>What a trade vendor charges for a trade good now, or null if it isn't a trade vendor or good.</summary>
    public WFTradeBuyQuote? BuyQuote(EntityUid vendor, string good)
    {
        if (!HasComp<WFTradeVendorComponent>(vendor)
            || Transform(vendor).GridUid is not { } grid
            || !_proto.TryIndex<EntityPrototype>(good, out var proto)
            || !proto.HasComp<WFTradeOriginComponent>(_factory)
            || VendorBasePrice(vendor, proto) is not { } basePrice)
            return null;

        var factor = BuyFactor(grid, good, out var reason);
        return new WFTradeBuyQuote(basePrice, (int) MathF.Round(basePrice * factor), factor, reason);
    }

    /// <summary>The share of base a station's vendors charge for a good now, never below 1.</summary>
    private float BuyFactor(EntityUid grid, string good, out LocId reason)
    {
        var profile = Profile(grid);

        // Contraband has a fixed street price: buying it never makes it dearer.
        if (profile.ContrabandGoods.Contains(good))
        {
            reason = "wf-trade-reason-steady";
            return 1f;
        }

        var scarcity = Math.Min(Scarcity(grid, good), profile.BuyScarcityCap);

        var ev = new WFTradeBuyQuoteEvent(grid, good);
        RaiseLocalEvent(ev);

        var raise = 1f;
        var top = 1f;
        LocId? raiseReason = null;
        foreach (var modifier in ev.Modifiers)
        {
            if (modifier.Factor <= 1f)
                continue;

            raise *= modifier.Factor;
            if (modifier.Factor > top)
            {
                top = modifier.Factor;
                raiseReason = modifier.Reason;
            }
        }

        reason = raiseReason ?? (scarcity >= ScarceReason ? "wf-trade-reason-scarce" : "wf-trade-reason-steady");
        return (1f + scarcity) * Math.Min(raise, profile.ModifierCap);
    }

    /// <summary>
    /// What a vending machine charges for an item: the live price for a trade good at a trade vendor, scaling the price
    /// the machine worked out, or that price unchanged.
    /// </summary>
    public int BuyPrice(EntityUid vendor, EntityPrototype proto, int price)
    {
        if (price <= 0
            || !HasComp<WFTradeVendorComponent>(vendor)
            || Transform(vendor).GridUid is not { } grid
            || !proto.HasComp<WFTradeOriginComponent>(_factory))
            return price;

        return (int) MathF.Round(price * BuyFactor(grid, proto.ID, out _));
    }

    /// <summary>A trade good bought from a vendor makes it scarcer at the vendor's station; contraband doesn't.</summary>
    private void OnPurchased(Entity<VendingMachinePurchaseComponent> ent, ref ComponentStartup args)
    {
        if (!HasComp<WFTradeOriginComponent>(ent)
            || Transform(ent).GridUid is not { } grid
            || MetaData(ent).EntityPrototype?.ID is not { } good
            || Profile(grid).ContrabandGoods.Contains(good))
            return;

        AddScarcity(grid, good, Profile(grid).BuyPerPurchase);
    }

    /// <summary>Reprices every trade vendor every few seconds, so scarcity decay and events show in their menus.</summary>
    private void UpdateVendors()
    {
        if (_timing.CurTime < _nextVendorRefresh)
            return;

        _nextVendorRefresh = _timing.CurTime + VendorInterval;
        RefreshVendors(null);
    }

    /// <summary>Recomputes the live prices of the trade vendors on one grid, or on every grid, dirtying those that changed.</summary>
    private void RefreshVendors(EntityUid? onGrid)
    {
        var query = EntityQueryEnumerator<WFTradeVendorComponent, VendingMachineComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var vendor, out var machine, out var xform))
        {
            if (onGrid != null && xform.GridUid != onGrid)
                continue;

            var prices = new Dictionary<string, int>();
            foreach (var good in machine.Inventory.Keys)
            {
                if (BuyQuote(uid, good) is { } quote)
                    prices[good] = quote.Amount;
            }

            if (prices.Count == vendor.Prices.Count && prices.All(entry => vendor.Prices.GetValueOrDefault(entry.Key) == entry.Value))
                continue;

            vendor.Prices = prices;
            Dirty(uid, vendor);
        }
    }

    /// <summary>The live buy prices of every trade vendor on a grid, one per good at the lowest.</summary>
    public Dictionary<string, WFTradeBuyQuote> StationBuyQuotes(EntityUid grid)
    {
        var quotes = new Dictionary<string, WFTradeBuyQuote>();
        var query = EntityQueryEnumerator<WFTradeVendorComponent, VendingMachineComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var machine, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            foreach (var good in machine.Inventory.Keys)
            {
                if (BuyQuote(uid, good) is { } quote
                    && (!quotes.TryGetValue(good, out var current) || quote.Amount < current.Amount))
                    quotes[good] = quote;
            }
        }

        return quotes;
    }
}
