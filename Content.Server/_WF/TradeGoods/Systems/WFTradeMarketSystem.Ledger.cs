using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._WF.TradeGoods.Components;
using Content.Shared._WF.TradeGoods;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>
/// Every station's ledger: a row per good holding the crates on hand, the level prices see and the crates owed to it. One
/// price curve reads it for chutes and vendors. <see cref="WFTradeEconomySystem"/> runs the sector on top of it.
/// </summary>
public sealed partial class WFTradeMarketSystem
{
    /// <summary>What a level on the shelf does to a price: steep near empty, near normal for small dips, falling through a glut.</summary>
    public static float LevelFactor(WFTradeMarketPrototype profile, float level)
    {
        if (level < 1f)
        {
            var missing = 1f - MathF.Max(level, 0f);
            return 1f + profile.ScarcityPremium * missing * missing;
        }

        return 1f - profile.GlutSlope * MathF.Min(level - 1f, profile.GlutSpan);
    }

    /// <summary>The role a station has in the sector economy, or null for one outside it.</summary>
    public WFTradeStationPrototype? Role(EntityUid grid)
    {
        if (!TryComp<WFTradeStationComponent>(grid, out var station))
            return null;

        if (_roles == null)
        {
            _roles = new Dictionary<string, WFTradeStationPrototype>();
            foreach (var role in _proto.EnumeratePrototypes<WFTradeStationPrototype>())
            {
                foreach (var id in role.Stations)
                {
                    _roles.TryAdd(id, role);
                }
            }
        }

        return _roles.GetValueOrDefault(station.Id);
    }

    /// <summary>A good's row at a station if it keeps one. Unlike <see cref="Row"/>, never opens one.</summary>
    public bool TryGetRow(EntityUid grid, string good, [NotNullWhen(true)] out WFTradeRow? row)
    {
        row = null;
        return TryComp<WFTradeMarketComponent>(grid, out var market) && market.Ledger.TryGetValue(good, out row);
    }

    /// <summary>A good's level where the station keeps a row for it, or normal where it doesn't.</summary>
    public float KnownLevel(EntityUid grid, string good)
    {
        return TryGetRow(grid, good, out var row) ? row.Stock / Profile(grid).StockTarget : 1f;
    }

    /// <summary>A good's row at a station, opening it the first time it's asked for: full outside the economy, a rolled share of target in it.</summary>
    public WFTradeRow Row(EntityUid grid, string good)
    {
        var market = EnsureComp<WFTradeMarketComponent>(grid);
        if (market.Ledger.TryGetValue(good, out var row))
            return row;

        var profile = Profile(grid);
        var share = Role(grid) == null ? 1f : _random.NextFloat(profile.VendorStockStartMin, profile.VendorStockStartMax);
        row = new WFTradeRow { Stock = profile.StockTarget * share, Priced = share };
        market.Ledger[good] = row;
        return row;
    }

    /// <summary>A good's stock against its target at a station: 1 is normal.</summary>
    public float Level(EntityUid grid, string good)
    {
        return Row(grid, good).Stock / Profile(grid).StockTarget;
    }

    /// <summary>The level vendors price by: stock, a few minutes behind.</summary>
    public float PricedLevel(EntityUid grid, string good)
    {
        return Row(grid, good).Priced;
    }

    /// <summary>
    /// The level a chute pays by. A sale lowers its pay at once, or a big haul would all fetch the first crate's price, but
    /// stock bought away only raises it after the delay.
    /// </summary>
    public float ChuteLevel(EntityUid grid, string good)
    {
        return MathF.Max(Level(grid, good), PricedLevel(grid, good));
    }

    /// <summary>Crates of a good a station has on its shelf, whole.</summary>
    public int VendorStock(EntityUid grid, string good)
    {
        return (int) MathF.Floor(Row(grid, good).Stock + 0.001f);
    }

    /// <summary>Puts crates on a station's shelf or takes them off, up to what it can hold. Prices catch up over a few minutes.</summary>
    public void AddStock(EntityUid grid, string good, float amount)
    {
        var row = Row(grid, good);
        row.Stock = Math.Clamp(row.Stock + amount, 0f, Profile(grid).VendorStock);
    }

    /// <summary>Sets a station's stock of a good outright, prices included, and shows it on all its vendors.</summary>
    public void SetVendorStock(EntityUid grid, string good, int amount)
    {
        var profile = Profile(grid);
        var row = Row(grid, good);
        row.Stock = Math.Clamp(amount, 0, profile.VendorStock);
        row.Priced = row.Stock / profile.StockTarget;
        RefreshVendors(grid);
    }

    /// <summary>Unloads up to some crates of what a station is owed onto its shelf, and returns how many. Freight moves prices at once.</summary>
    public float Deliver(EntityUid grid, string good, float most)
    {
        if (!TryComp<WFTradeMarketComponent>(grid, out var market) || !market.Ledger.TryGetValue(good, out var row))
            return 0f;

        // Only what fits on the shelf comes off; the rest stays owed.
        var amount = MathF.Min(MathF.Min(row.Inbound, most), Profile(grid).VendorStock - row.Stock);
        if (amount <= 0f)
            return 0f;

        row.Inbound -= amount;
        AddStock(grid, good, amount);
        row.Priced = MathF.Max(row.Priced, Level(grid, good));
        RefreshVendors(grid);
        return amount;
    }

    /// <summary>Writes off crates owed to a station because the ship bringing them was lost.</summary>
    public void LoseInbound(EntityUid grid, string good, float amount)
    {
        if (TryComp<WFTradeMarketComponent>(grid, out var market) && market.Ledger.TryGetValue(good, out var row))
            row.Inbound = MathF.Max(0f, row.Inbound - amount);
    }

    /// <summary>Crates owed to a station, in all or of some goods.</summary>
    public float Owed(EntityUid grid, IEnumerable<string>? goods = null)
    {
        if (!TryComp<WFTradeMarketComponent>(grid, out var market))
            return 0f;

        if (goods == null)
            return market.Ledger.Values.Sum(row => row.Inbound);

        return goods.Sum(good => market.Ledger.GetValueOrDefault(good)?.Inbound ?? 0f);
    }

    /// <summary>
    /// What a service costs at a station against normal: the curve at the lowest of its goods, never under 1. A station
    /// outside the economy charges what it always has.
    /// </summary>
    public float ServicePrice(EntityUid grid, ProtoId<WFTradeServicePrototype> id)
    {
        if (Role(grid) == null)
            return 1f;

        var profile = Profile(grid);
        var service = _proto.Index(id);
        return MathF.Max(1f, service.Goods.Max(good => TryGetRow(grid, good, out var row) ? LevelFactor(profile, row.Priced) : 1f));
    }

    /// <summary>The goods a station's chutes would take only because it is scarce of them.</summary>
    public IEnumerable<string> ScarceGoods(EntityUid grid)
    {
        return TryComp<WFTradeMarketComponent>(grid, out var market)
            ? market.Ledger.Keys.Where(good => Restocking(grid, good))
            : Enumerable.Empty<string>();
    }

    /// <summary>Moves every station's priced level toward its stock.</summary>
    private void SmoothPrices(TimeSpan elapsed)
    {
        var query = EntityQueryEnumerator<WFTradeMarketComponent>();
        while (query.MoveNext(out var grid, out var market))
        {
            var profile = Profile(grid);
            var keep = (float) Math.Pow(0.5, elapsed.TotalSeconds / Math.Max(profile.PriceSmoothing.TotalSeconds, 1.0));
            foreach (var row in market.Ledger.Values)
            {
                row.Priced = row.Stock / profile.StockTarget + (row.Priced - row.Stock / profile.StockTarget) * keep;
            }
        }
    }
}
