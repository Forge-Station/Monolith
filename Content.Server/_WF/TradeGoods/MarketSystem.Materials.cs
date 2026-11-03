using System.Linq;
using Content.Server._NF.Market.Components;
using Content.Server._NF.Market.Extensions;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Cargo.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Market;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._NF.Market.Systems;

/// <summary>What a station pays and charges per unit of a material now, zero where it can't; prices include console modifiers.</summary>
public readonly record struct WFMaterialQuote(string Good, EntityUid Station, string Name, double Pays, double Charges, int Stock);

/// <summary>
/// Station stock as a market for materials. Stations use goods up, anyone can bring more, and pallets pay and consoles
/// charge by how much of a material a station holds.
/// Materials in a console's cart stay the station's stock until they are bought, so a cart can't move prices or lock one.
/// Prices follow a purchase or sale after a delay, but a pallet cuts its pay at once: a delayed glut would let a split
/// sale fetch the top rate and be bought back cheaper once the console caught up.
/// Materials are appraised after <c>PriceCalculationEvent</c>, so a pallet adds the difference itself.
/// </summary>
public sealed partial class MarketSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFTradeMarketSystem _tradeMarket = default!;

    private static readonly TimeSpan WfRepriceInterval = TimeSpan.FromSeconds(5);

    private TimeSpan _wfNextReprice;

    /// <summary>Each entity's adjustment this tick, so an appraisal and a sale agree.</summary>
    private readonly Dictionary<EntityUid, double> _wfAdjusted = new();

    /// <summary>Units priced this tick per station and material: a sale walks down the curve instead of paying one rate.</summary>
    private readonly Dictionary<(EntityUid Station, string Good), int> _wfAhead = new();

    /// <summary>Each station's console carts this tick, so a pallet's items don't search the consoles one by one.</summary>
    private readonly Dictionary<EntityUid, List<List<MarketData>>> _wfCarts = new();

    private GameTick _wfTick;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _wfNextReprice)
            return;

        _wfNextReprice = _timing.CurTime + WfRepriceInterval;
        var query = EntityQueryEnumerator<CargoMarketDataComponent>();
        while (query.MoveNext(out var uid, out var market))
        {
            WfConsume((uid, market), WfRepriceInterval);
            WfReprice((uid, market));
        }
    }

    /// <summary>A station uses up some of each material it holds, which moves prices at once as it isn't a trade. Carts are left alone.</summary>
    public void WfConsume(Entity<CargoMarketDataComponent> ent, TimeSpan elapsed)
    {
        var profile = _tradeMarket.Profile(null);
        var lag = EnsureComp<WFMaterialPriceLagComponent>(ent);
        var carts = WfCarts(ent);
        var share = 1.0 - Math.Pow(0.5, elapsed.TotalSeconds / Math.Max(profile.MaterialHalfLife.TotalSeconds, 1.0));
        foreach (var material in profile.MaterialGoods)
        {
            var good = material.Id;
            if (ent.Comp.MarketDataList.FirstOrDefault(entry => entry.Prototype == good) is not { } stock)
                continue;

            WfPricedSupply(ent, carts, good);
            var used = lag.Used.GetValueOrDefault(good) + stock.Quantity * share;
            var whole = (int) Math.Min(used, stock.Quantity);
            lag.Used[good] = used - whole;
            if (whole <= 0)
                continue;

            ent.Comp.MarketDataList.Upsert(good, -whole, stock.Price);
            lag.Seen[good] = WfSupply(ent.Comp, carts, good);
        }
    }

    /// <summary>Sets a station's console prices for materials, in its stock and carts, by how much it holds.</summary>
    public void WfReprice(Entity<CargoMarketDataComponent> ent)
    {
        var profile = _tradeMarket.Profile(null);
        var carts = WfCarts(ent);
        // A material that sold out has no entry to reprice, but its delay still has to start.
        foreach (var good in profile.MaterialGoods)
        {
            WfPricedSupply(ent, carts, good);
        }

        WfReprice(ent.Comp.MarketDataList, ent, carts, profile);
        foreach (var cart in carts)
        {
            WfReprice(cart, ent, carts, profile);
        }
    }

    private void WfReprice(List<MarketData> stock, Entity<CargoMarketDataComponent> market, List<List<MarketData>> carts, WFTradeMarketPrototype profile)
    {
        foreach (var entry in stock)
        {
            if (!profile.MaterialGoods.Contains(entry.Prototype))
                continue;

            var unit = WfUnitPrice(entry.Prototype);
            entry.Price = unit * WfChargeFactor(profile, WfSaturation(profile, WfPricedSupply(market, carts, entry.Prototype), unit));
        }
    }

    /// <summary>What a sold entity and its contents gain or lose from the selling station's stock, before console modifiers.</summary>
    public double WfPalletAdjustment(EntityUid sold, EntityUid grid)
    {
        if (_station.GetOwningStation(grid) is not { } station || !TryComp<CargoMarketDataComponent>(station, out var market))
            return 0.0;

        if (_wfTick != _timing.CurTick)
        {
            _wfTick = _timing.CurTick;
            _wfAdjusted.Clear();
            _wfAhead.Clear();
            _wfCarts.Clear();
        }

        if (_wfAdjusted.TryGetValue(sold, out var known))
            return known;

        if (!_wfCarts.TryGetValue(station, out var carts))
            _wfCarts[station] = carts = WfCarts(station);

        var adjustment = WfAdjustment(sold, (station, market), carts, _tradeMarket.Profile(null));
        _wfAdjusted[sold] = adjustment;
        return adjustment;
    }

    private double WfAdjustment(EntityUid uid, Entity<CargoMarketDataComponent> market, List<List<MarketData>> carts,
        WFTradeMarketPrototype profile)
    {
        var adjustment = 0.0;
        if (TryComp<StackComponent>(uid, out var stack) && WfGood(stack) is { } good)
        {
            var unit = WfUnitPrice(good);
            var key = (market.Owner, good);
            var ahead = _wfAhead.GetValueOrDefault(key);
            _wfAhead[key] = ahead + stack.Count;
            // The curve is linear either side of the target, so its midpoint prices the whole stack.
            var supply = WfPalletSupply(market, carts, good) + ahead + stack.Count / 2.0;
            adjustment += unit * stack.Count * (WfPayFactor(profile, WfSaturation(profile, supply, unit)) - 1.0);
        }

        if (!TryComp<ContainerManagerComponent>(uid, out var containers))
            return adjustment;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
            {
                adjustment += WfAdjustment(contained, market, carts, profile);
            }
        }

        return adjustment;
    }

    /// <summary>What a pallet pays for a material at a stock level, as a share of its value.</summary>
    public static float WfPayFactor(WFTradeMarketPrototype profile, double saturation)
    {
        return WfCurve(profile, saturation, profile.MaterialPayMax, profile.MaterialPayMin);
    }

    /// <summary>What a console charges for a material at a stock level, as a share of its value.</summary>
    public static float WfChargeFactor(WFTradeMarketPrototype profile, double saturation)
    {
        return WfCurve(profile, saturation, profile.MaterialChargeMax, profile.MaterialChargeMin);
    }

    /// <summary>Falls from a high at no stock to 1 at the target, then to a low at a glut.</summary>
    private static float WfCurve(WFTradeMarketPrototype profile, double saturation, float high, float low)
    {
        if (saturation <= 1.0)
            return (float) (high + (1.0 - high) * Math.Max(saturation, 0.0));

        return (float) (1.0 + (low - 1.0) * Math.Min((saturation - 1.0) / Math.Max(profile.MaterialGlut - 1.0, 0.01), 1.0));
    }

    private static double WfSaturation(WFTradeMarketPrototype profile, double supply, double unit)
    {
        return supply * unit / Math.Max(profile.MaterialStockTarget, 1f);
    }

    /// <summary>Whether a stack trades through station stock.</summary>
    public bool WfIsMaterial(StackComponent stack)
    {
        return WfGood(stack) != null;
    }

    /// <summary>A material's single-unit prototype, or null for a stack that doesn't trade through station stock.</summary>
    private string? WfGood(StackComponent stack)
    {
        if (!_prototypeManager.TryIndex<StackPrototype>(stack.StackTypeId, out var proto))
            return null;

        return _tradeMarket.Profile(null).MaterialGoods.Contains(proto.Spawn) ? proto.Spawn.Id : null;
    }

    /// <summary>What one unit of a material appraises at.</summary>
    private double WfUnitPrice(string good)
    {
        return _prototypeManager.TryIndex<EntityPrototype>(good, out var proto) ? _pricingSystem.GetEstimatedPrice(proto) : 0.0;
    }

    private string? WfStackId(string good)
    {
        return _prototypeManager.TryIndex<EntityPrototype>(good, out var proto)
               && proto.TryGetComponent<StackComponent>(out var stack, EntityManager.ComponentFactory)
            ? stack.StackTypeId
            : null;
    }

    /// <summary>The carts of a station's market consoles: stock players have set aside but not bought.</summary>
    private List<List<MarketData>> WfCarts(EntityUid station)
    {
        var carts = new List<List<MarketData>>();
        var consoles = EntityQueryEnumerator<MarketConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out var console, out var xform))
        {
            if (console.CartDataList.Count > 0 && _station.GetOwningStation(uid, xform) == station)
                carts.Add(console.CartDataList);
        }

        return carts;
    }

    /// <summary>A station's stock of a material, counting what is in carts.</summary>
    private int WfSupply(CargoMarketDataComponent market, List<List<MarketData>> carts, string good)
    {
        var supply = market.MarketDataList.FirstOrDefault(entry => entry.Prototype == good)?.Quantity ?? 0;
        foreach (var cart in carts)
        {
            supply += cart.FirstOrDefault(entry => entry.Prototype == good)?.Quantity ?? 0;
        }

        return supply;
    }

    /// <summary>A material's stock as a console's price sees it, which counts purchases and sales only once their delay has passed.</summary>
    private int WfPricedSupply(Entity<CargoMarketDataComponent> market, List<List<MarketData>> carts, string good)
    {
        var lag = EnsureComp<WFMaterialPriceLagComponent>(market);
        var supply = WfSupply(market.Comp, carts, good);
        var now = _timing.CurTime;
        var change = supply - lag.Seen.GetValueOrDefault(good, supply);
        if (change != 0)
            lag.Pending.Add((good, change, now + _tradeMarket.PriceDelay(null)));

        lag.Seen[good] = supply;
        lag.Pending.RemoveAll(pending => pending.Due <= now);
        return Math.Max(supply - lag.Pending.Where(pending => pending.Good == good).Sum(pending => pending.Change), 0);
    }

    /// <summary>The stock a pallet pays by: a sale lowers its pay at once, but stock bought away only raises it after the delay.</summary>
    private int WfPalletSupply(Entity<CargoMarketDataComponent> market, List<List<MarketData>> carts, string good)
    {
        return Math.Max(WfSupply(market.Comp, carts, good), WfPricedSupply(market, carts, good));
    }

    /// <summary>Tops up some materials, by default those stations open with, by a share of the target, never past it. Returns whether it added any.</summary>
    public bool WfRestock(EntityUid grid, float share, List<EntProtoId>? goods = null)
    {
        if (_station.GetOwningStation(grid) is not { } station || !TryComp<CargoMarketDataComponent>(station, out var market))
            return false;

        var profile = _tradeMarket.Profile(null);
        var carts = WfCarts(station);
        var restocked = false;
        foreach (var good in goods ?? profile.SupplyMaterials)
        {
            var unit = WfUnitPrice(good);
            if (unit <= 0.0)
                continue;

            var target = profile.MaterialStockTarget / unit;
            var amount = (int) Math.Min(target * share, target - WfSupply(market, carts, good));
            if (amount <= 0)
                continue;

            // Freight isn't a purchase, so it moves prices at once.
            WfPricedSupply((station, market), carts, good);
            market.MarketDataList.Upsert(good, amount, unit, WfStackId(good));
            EnsureComp<WFMaterialPriceLagComponent>(station).Seen[good] = WfSupply(market, carts, good);
            restocked = true;
        }

        return restocked;
    }

    /// <summary>
    /// An industry works some spesos' worth of materials out of a station's stock, the first listed first, and returns the
    /// share it found. It isn't a trade, so it moves prices at once.
    /// </summary>
    public float WfDraw(EntityUid grid, List<EntProtoId> materials, float value)
    {
        if (value <= 0f || _station.GetOwningStation(grid) is not { } station || !TryComp<CargoMarketDataComponent>(station, out var market))
            return 0f;

        var carts = WfCarts(station);
        var lag = EnsureComp<WFMaterialPriceLagComponent>(station);
        var left = (double) value;
        foreach (var material in materials)
        {
            var good = material.Id;
            var unit = WfUnitPrice(good);
            if (unit <= 0.0 || market.MarketDataList.FirstOrDefault(entry => entry.Prototype == good) is not { } stock)
                continue;

            var units = (int) Math.Min(Math.Ceiling(left / unit), stock.Quantity);
            if (units <= 0)
                continue;

            WfPricedSupply((station, market), carts, good);
            market.MarketDataList.Upsert(good, -units, stock.Price);
            lag.Seen[good] = WfSupply(market, carts, good);
            left -= units * unit;
            if (left <= 0.0)
                return 1f;
        }

        return (float) Math.Clamp(1.0 - left / value, 0.0, 1.0);
    }

    /// <summary>Every market map station's prices for every material, at its best pallet and cheapest console.</summary>
    public List<WFMaterialQuote> WfQuotes()
    {
        var pays = new Dictionary<EntityUid, float>();
        var pallets = EntityQueryEnumerator<CargoPalletConsoleComponent, MarketModifierComponent, TransformComponent>();
        while (pallets.MoveNext(out var uid, out _, out var modifier, out var xform))
        {
            if (!modifier.Buy && _tradeMarket.IsMarketMap(xform.MapID) && _station.GetOwningStation(uid, xform) is { } station)
                pays[station] = Math.Max(pays.GetValueOrDefault(station), modifier.Mod);
        }

        var charges = new Dictionary<EntityUid, float>();
        var consoles = EntityQueryEnumerator<MarketConsoleComponent, MarketModifierComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out _, out var modifier, out var xform))
        {
            if (_tradeMarket.IsMarketMap(xform.MapID) && _station.GetOwningStation(uid, xform) is { } station)
                charges[station] = charges.TryGetValue(station, out var lowest) ? Math.Min(lowest, modifier.Mod) : modifier.Mod;
        }

        var profile = _tradeMarket.Profile(null);
        var quotes = new List<WFMaterialQuote>();
        var markets = EntityQueryEnumerator<CargoMarketDataComponent>();
        while (markets.MoveNext(out var uid, out var market))
        {
            if (!pays.TryGetValue(uid, out var pay) & !charges.TryGetValue(uid, out var charge))
                continue;

            var carts = WfCarts(uid);
            var name = Name(_station.GetLargestGrid(uid) ?? uid);
            foreach (var good in profile.MaterialGoods)
            {
                var unit = WfUnitPrice(good);
                var entry = market.MarketDataList.FirstOrDefault(data => data.Prototype == good.Id);
                var stock = entry?.Quantity ?? 0;
                quotes.Add(new WFMaterialQuote(good,
                    uid,
                    name,
                    unit * pay * WfPayFactor(profile, WfSaturation(profile, WfPalletSupply((uid, market), carts, good), unit)),
                    stock > 0 ? entry!.Price * charge : 0.0,
                    stock));
            }
        }

        return quotes;
    }
}
