using System.Linq;
using Content.Server._WF.TradeGoods.Components;
using Content.Server.Cargo.Systems;
using Content.Shared._Crescent.Dispenser;
using Content.Shared._Mono.VendingMachine;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.TradeGoods;
using Content.Shared.VendingMachines;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>A trade vendor's live price for one good: its base price, what it charges now and the reason players see.</summary>
public readonly record struct WFTradeBuyQuote(int Base, int Amount, float Factor, LocId Reason);

/// <summary>
/// Buy prices at station trade vendors. A vendor sells from its station's ledger and prices by how full the shelf is, a
/// few minutes behind the stock; station conditions add modifiers through <see cref="WFTradeBuyQuoteEvent"/>. A vendor
/// never charges less than its base price, so the sell floors still cover a bought crate.
/// </summary>
public sealed partial class WFTradeMarketSystem
{
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WFTradeRushSystem _rush = default!;

    private static readonly TimeSpan VendorInterval = TimeSpan.FromSeconds(5);

    private TimeSpan _nextVendorRefresh;

    /// <summary>The best price any open chute table lists per good, worked out once per tick.</summary>
    private readonly Dictionary<string, int> _openBest = new();

    private GameTick _openBestTick;

    private void InitializeBuy()
    {
        SubscribeLocalEvent<VendingMachinePurchaseComponent, ComponentStartup>(OnPurchased);
    }

    /// <summary>A random wait before a purchase or sale moves a console's material prices.</summary>
    public TimeSpan PriceDelay(EntityUid? grid)
    {
        var profile = Profile(grid);
        return _random.Next(profile.PriceDelayMin, profile.PriceDelayMax);
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

        var stock = MathF.Max(1f, LevelFactor(profile, PricedLevel(grid, good)));

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

        reason = raiseReason ?? (stock >= 1f + SteadyBand ? "wf-trade-reason-scarce" : "wf-trade-reason-steady");
        return MathF.Min(stock * Math.Min(raise, profile.ModifierCap), profile.PriceCap);
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

    /// <summary>A trade good bought from a vendor comes off its station's shelf now and reaches its price later; contraband does neither.</summary>
    private void OnPurchased(Entity<VendingMachinePurchaseComponent> ent, ref ComponentStartup args)
    {
        if (!HasComp<WFTradeOriginComponent>(ent)
            || Transform(ent).GridUid is not { } grid
            || MetaData(ent).EntityPrototype?.ID is not { } good
            || Profile(grid).ContrabandGoods.Contains(good))
            return;

        AddStock(grid, good, -1f);
        RefreshVendors(grid);
    }

    /// <summary>Moves prices toward stock and reprices every trade vendor every few seconds, so purchases and events show in their menus.</summary>
    private void UpdateVendors()
    {
        if (_timing.CurTime < _nextVendorRefresh)
            return;

        _nextVendorRefresh = _timing.CurTime + VendorInterval;
        SmoothPrices(VendorInterval);
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

            SyncStock(uid, machine, xform.GridUid);
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

    /// <summary>Whether a vendor's stock of a good is finite: every trade good but contraband, which never runs out.</summary>
    private bool HasLimitedStock(string good)
    {
        return _proto.TryIndex<EntityPrototype>(good, out var proto)
               && proto.HasComp<WFTradeOriginComponent>(_factory)
               && !_proto.Index(DefaultProfile).ContrabandGoods.Contains(good);
    }

    /// <summary>Shows a station's shared stock on one of its vendors, replacing the machine's infinite trade goods.</summary>
    private void SyncStock(EntityUid vendor, VendingMachineComponent machine, EntityUid? grid)
    {
        if (grid is not { } onGrid)
            return;

        var changed = false;
        foreach (var (good, entry) in machine.Inventory)
        {
            if (!HasLimitedStock(good))
                continue;

            var stock = (uint) VendorStock(onGrid, good);
            if (entry.Amount == stock)
                continue;

            entry.Amount = stock;
            changed = true;
        }

        if (changed)
            Dirty(vendor, machine);
    }

    /// <summary>Whether a station is scarce of a good, so its chutes take it to fill the shelf.</summary>
    public bool Restocking(EntityUid grid, string good)
    {
        var profile = Profile(grid);
        return !profile.ContrabandGoods.Contains(good)
               && TryComp<WFTradeMarketComponent>(grid, out var market)
               && market.Ledger.TryGetValue(good, out var row)
               && row.Stock < profile.StockTarget * profile.ScarceLevel;
    }

    /// <summary>
    /// What a chute pays for a good its table doesn't list but its station is scarce of, or has an open order for: the
    /// best any open table lists. An order keeps the chute taking the good after the first crates end the shortage.
    /// </summary>
    private int? RestockBase(EntityUid grid, string good)
    {
        if (!Restocking(grid, good) && !_rush.Wants(grid, good))
            return null;

        if (_openBestTick != _timing.CurTick)
        {
            _openBestTick = _timing.CurTick;
            _openBest.Clear();
        }

        if (!_openBest.TryGetValue(good, out var best))
        {
            best = Chutes().Where(chute => !IsHiddenChute(chute.Chute)).Select(chute => BasePrice(chute.Dispenser, good) ?? 0).DefaultIfEmpty(0).Max();
            _openBest[good] = best;
        }

        return best > 0 ? best : null;
    }

    /// <summary>Whether a chute would take a crate only because its station's vendors are scarce of it.</summary>
    public bool TakesToRestock(EntityUid chute, EntityUid crate)
    {
        return TryComp<DispenserComponent>(chute, out var dispenser)
               && Transform(chute).GridUid is { } grid
               && MetaData(crate).EntityPrototype?.ID is { } good
               && BasePrice(dispenser, good) == null
               && RestockBase(grid, good) != null;
    }

    /// <summary>The trade goods with finite stock that a station's vendors sell.</summary>
    public HashSet<string> StockedGoods(EntityUid grid)
    {
        var goods = new HashSet<string>();
        var query = EntityQueryEnumerator<WFTradeVendorComponent, VendingMachineComponent, TransformComponent>();
        while (query.MoveNext(out _, out var machine, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            foreach (var good in machine.Inventory.Keys)
            {
                if (HasLimitedStock(good))
                    goods.Add(good);
            }
        }

        return goods;
    }

    /// <summary>The live buy prices of every trade vendor on a grid, one per good at the lowest. Sold-out goods aren't listed.</summary>
    public Dictionary<string, WFTradeBuyQuote> StationBuyQuotes(EntityUid grid)
    {
        var quotes = new Dictionary<string, WFTradeBuyQuote>();
        var query = EntityQueryEnumerator<WFTradeVendorComponent, VendingMachineComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var machine, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            foreach (var (good, entry) in machine.Inventory)
            {
                if (entry.Amount > 0
                    && BuyQuote(uid, good) is { } quote
                    && (!quotes.TryGetValue(good, out var current) || quote.Amount < current.Amount))
                    quotes[good] = quote;
            }
        }

        return quotes;
    }
}
