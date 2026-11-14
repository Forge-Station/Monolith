#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.CargoLoad;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Cargo.Systems;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// Chute markets: the shelf sets the price and sales move it, the chute pays what it quoted, crates made anywhere
/// sell but only a haul counts as one, raises are capped, and consignments and rush orders pay their premiums.
/// </summary>
[TestFixture]
[TestOf(typeof(WFTradeMarketSystem))]
public sealed class TradeMarketTest
{
    private const string ChuteProto = "CargoChuteTradeMall";
    private const string CrateProto = "TradeGoodFabric";

    /// <summary>A market map with a chute, and a far grid where crates are made before they're carried in.</summary>
    private sealed class Setup
    {
        public EntityUid Chute;
        public EntityUid User;
        public EntityUid Far;
        public EntityUid Near;
    }

    private static Setup Build(IEntityManager entMan, TestMapData map)
    {
        var mapSys = entMan.System<SharedMapSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        entMan.EnsureComponent<WFTradeMarketMapComponent>(map.MapUid);

        var setup = new Setup
        {
            Chute = entMan.SpawnEntity(ChuteProto, map.GridCoords),
            User = entMan.SpawnEntity(null, map.GridCoords),
        };

        var far = mapSys.CreateGridEntity(map.MapId);
        mapSys.SetTile(far.Owner, far.Comp, Vector2i.Zero, map.Tile.Tile);
        xformSys.SetLocalPosition(far.Owner, new Vector2(5000f, 0f));
        setup.Far = far.Owner;

        var near = mapSys.CreateGridEntity(map.MapId);
        mapSys.SetTile(near.Owner, near.Comp, Vector2i.Zero, map.Tile.Tile);
        xformSys.SetLocalPosition(near.Owner, new Vector2(30f, 0f));
        setup.Near = near.Owner;
        return setup;
    }

    /// <summary>A crate made on a grid, then carried to the chute.</summary>
    private static EntityUid Crate(IEntityManager entMan, EntityUid madeOn, EntityCoordinates chute, string proto = CrateProto)
    {
        var crate = entMan.SpawnEntity(proto, new EntityCoordinates(madeOn, 0.5f, 0.5f));
        entMan.System<SharedTransformSystem>().SetCoordinates(crate, chute);
        return crate;
    }

    [Test]
    public async Task PricesFallWithSalesAndRecover()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var setup = Build(entMan, map);
            var grid = map.Grid.Owner;
            var first = market.Quote(setup.Chute, CrateProto)!.Value;
            Assert.That(first.Amount, Is.EqualTo(first.Base), "A fresh market should pay the chute's base price.");

            // Each crate walks down the curve at once, however many come in together.
            var profile = market.Profile(grid);
            var crates = (int) (profile.StockTarget * 0.25f);
            for (var i = 0; i < crates; i++)
            {
                market.Sell(setup.Chute, Crate(entMan, setup.Far, map.GridCoords), setup.User);
            }

            var glutted = market.Quote(setup.Chute, CrateProto)!.Value;
            Assert.That(glutted.Factor, Is.EqualTo(1f - profile.GlutSlope * 0.25f).Within(0.02f), "Sales should lower the price as they land.");
            Assert.That(glutted.Reason, Is.EqualTo((LocId) "wf-trade-reason-glutted"));
            Assert.That(market.Delivered(grid, CrateProto), Is.EqualTo(crates));

            // A short shelf pays over the table.
            market.SetVendorStock(grid, CrateProto, (int) (profile.StockTarget * 0.6f));
            var wanted = market.Quote(setup.Chute, CrateProto)!.Value;
            Assert.That(wanted.Factor, Is.EqualTo(1f + profile.ScarcityPremium * 0.16f).Within(0.02f));
            Assert.That(wanted.Reason, Is.EqualTo((LocId) "wf-trade-reason-wanted"));

            // Stock bought away only raises what a chute pays once prices catch up, so a sale can't outrun them.
            market.SetVendorStock(grid, CrateProto, (int) profile.StockTarget);
            market.Row(grid, CrateProto).Stock = profile.StockTarget * 0.2f;
            Assert.That(market.Quote(setup.Chute, CrateProto)!.Value.Amount, Is.EqualTo(first.Base));

            // A glutted market never pays below the good's floor.
            market.SetVendorStock(grid, CrateProto, profile.VendorStock);
            var floor = market.Quote(setup.Chute, CrateProto)!.Value;
            var expected = profile.FloorPrices.TryGetValue(CrateProto, out var floorPrice) ? floorPrice : (int) (floor.Base * profile.Floor);
            Assert.That(floor.Amount, Is.GreaterThanOrEqualTo(System.Math.Min(expected, floor.Base)));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChutePaysTheQuoteAndCountsOnlyRealHauls()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var interaction = entMan.System<SharedInteractionSystem>();

        Setup setup = default!;
        EntityUid madeHere = default, madeNear = default, hauled = default, unknown = default;
        var quoted = 0;

        await server.WaitAssertion(() =>
        {
            setup = Build(entMan, map);
            madeHere = entMan.SpawnEntity(CrateProto, map.GridCoords);
            madeNear = Crate(entMan, setup.Near, map.GridCoords);
            hauled = Crate(entMan, setup.Far, map.GridCoords);

            // A crate made off the market map has no known origin until it shows up on one.
            var elsewhere = entMan.System<SharedMapSystem>().CreateMap(out var otherMap);
            unknown = entMan.SpawnEntity(CrateProto, new MapCoordinates(Vector2.Zero, otherMap));
            Assert.That(entMan.GetComponent<WFTradeOriginComponent>(unknown).Pending, Is.True);
            entMan.System<SharedTransformSystem>().SetCoordinates(unknown, map.GridCoords);
            entMan.DeleteEntity(elsewhere);

            foreach (var crate in new[] { madeHere, madeNear, hauled, unknown })
            {
                Assert.That(market.CheckSeal(setup.Chute, crate), Is.Null, "An open chute refused an unsealed crate.");
            }

            Assert.That(market.WasHauled(setup.Chute, madeHere), Is.False, "A crate made on the chute's station counted as a haul.");
            Assert.That(market.WasHauled(setup.Chute, madeNear), Is.False, "A crate made nearby counted as a haul.");
            Assert.That(market.WasHauled(setup.Chute, unknown), Is.False, "A crate of unknown origin counted as a haul.");
            Assert.That(market.WasHauled(setup.Chute, hauled), Is.True);

            // A crate made on the chute's own station sells there, as a lathe print does.
            quoted = market.Quote(setup.Chute, CrateProto)!.Value.Amount;
            Assert.That(interaction.InteractUsing(setup.User, madeHere, setup.Chute, map.GridCoords, false, false), Is.True);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(madeHere), Is.True, "The chute refused a crate made on its station.");
            Assert.That(TradeTestHelpers.CashOnMap(entMan, map.MapId), Is.EqualTo(quoted), "The chute didn't pay what it quoted.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RaisesAreCappedAndPremiumsPay()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var shortages = entMan.System<WFTradeShortageSystem>();
        var special = entMan.System<WFTradeSpecialCrateSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            var setup = Build(entMan, map);
            var grid = map.Grid.Owner;
            var profile = market.Profile(grid);

            shortages.StartLocal(grid, new HashSet<string> { CrateProto }, 4f, System.TimeSpan.FromMinutes(5), "wf-trade-goods-textiles");
            shortages.StartLocal(grid, new HashSet<string> { CrateProto }, 4f, System.TimeSpan.FromMinutes(5), "wf-trade-goods-textiles");
            var capped = market.Quote(setup.Chute, CrateProto)!.Value;
            Assert.That(capped.Factor, Is.EqualTo(profile.ModifierCap).Within(0.01f), "Stacked raises should stop at the cap.");
            Assert.That(capped.Reason, Is.EqualTo((LocId) "wf-trade-reason-short"));

            // A consignment pays its value and bonus at its destination on time, and little anywhere else.
            var consignment = entMan.SpawnEntity("WFTradeConsignment", map.GridCoords);
            var comp = entMan.GetComponent<WFConsignmentComponent>(consignment);
            comp.Destination = grid;
            comp.Deadline = timing.CurTime + System.TimeSpan.FromMinutes(10);
            Assert.That(special.IsSpecial(consignment), Is.True);
            Assert.That(special.Price(setup.Chute, consignment), Is.EqualTo(comp.Value + comp.OnTimeBonus));

            comp.Destination = setup.Far;
            Assert.That(special.Price(setup.Chute, consignment), Is.EqualTo(comp.Elsewhere));

            // Any other crate is priced from the chute's own table.
            Assert.That(special.IsSpecial(entMan.SpawnEntity(CrateProto, map.GridCoords)), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsoleTradeCratesWeighAndFollowDemand()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var cargo = entMan.System<WFCargoLoadSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var profile = market.Profile(grid);
            var crate = entMan.SpawnEntity("CrateTradeSecureHigh", map.GridCoords);
            Assert.That(cargo.Weight(crate), Is.GreaterThan(0f), "An express trading crate should load the ship carrying it.");

            var full = PalletPrice(entMan, crate);
            Assert.That(full, Is.GreaterThan(0));

            // Each crate sold at a station lowers what the next one fetches there.
            var sold = new EntitySoldEvent(new HashSet<EntityUid> { crate }, grid);
            for (var i = 0; i < 4; i++)
            {
                entMan.EventBus.RaiseEvent(EventSource.Local, ref sold);
            }

            Assert.That(PalletPrice(entMan, crate), Is.EqualTo(full * (1 - 4 * profile.TradeCratePerSale)).Within(full * 0.01));

            // A glutted station still pays the floor.
            market.AddPressure(grid, entMan.GetComponent<WFTradeCrateMarketComponent>(crate).Good, 10f);
            Assert.That(PalletPrice(entMan, crate), Is.EqualTo(full * profile.TradeCrateFloor).Within(1));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A pallet sale prices all its crates in one tick: each one as if those priced before it had sold.</summary>
    [Test]
    public async Task PalletBatchPricesEachCrateAfterTheOnesBefore()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var profile = market.Profile(map.Grid.Owner);
            var crates = Enumerable.Range(0, 3).Select(_ => entMan.SpawnEntity("CrateTradeSecureHigh", map.GridCoords)).ToList();
            var prices = crates.Select(crate => PalletPrice(entMan, crate)).ToList();
            Assert.That(PalletPrice(entMan, crates[0]), Is.EqualTo(prices[0]), "Pricing a crate twice in a tick moved it in line.");
            for (var i = 1; i < prices.Count; i++)
            {
                Assert.That(prices[i], Is.EqualTo(prices[0] * (1 - i * profile.TradeCratePerSale)).Within(prices[0] * 0.01),
                    "A batch on the pallets skipped the saturation.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The price the pallet's pricing event gives a trading crate.</summary>
    private static double PalletPrice(IEntityManager entMan, EntityUid crate)
    {
        var ev = new PriceCalculationEvent();
        entMan.EventBus.RaiseLocalEvent(crate, ref ev);
        return ev.Price;
    }
}
