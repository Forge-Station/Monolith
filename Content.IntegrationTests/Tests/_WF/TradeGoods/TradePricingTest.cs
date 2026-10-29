#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._Mono.AlertLevel;
using Content.Server._NF.SectorServices;
using Content.Server._WF.CargoLoad;
using Content.Server._WF.SectorControl.Systems;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.GameTicking;
using Content.Shared._Crescent.Dispenser;
using Content.Shared._WF.SectorControl;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Interaction;
using Content.Shared.Paper;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// Live chute prices: recovery after a dump, pressure shared per station, the printed price sheet, a pending quote's
/// safety, war demand, shortage expiry and a blockade broken by traffic.
/// </summary>
[TestFixture]
[TestOf(typeof(WFTradeMarketSystem))]
public sealed class TradePricingTest
{
    private const string ChuteProto = "CargoChuteTradeMall";
    private const string TsfChute = "CargoChuteTSFMC";
    private const string CrateProto = "TradeGoodFabric";
    private const string Ammo = "TradeGoodAmmo";
    private const string Medical = "TradeGoodMedicalSupplies";
    private const string Blockader = "WFTestTradeBlockader";

    [TestPrototypes]
    private const string Prototypes = @"
- type: wfSectorFaction
  id: WFTestTradeBlockader
  name: cmd-wf_sector-hint-faction
  color: Red
  blockades: true
";

    /// <summary>A one-tile grid at a map position, optionally named.</summary>
    private static EntityUid Grid(IEntityManager entMan, TestMapData map, Vector2 position, string? name = null)
    {
        var mapSys = entMan.System<SharedMapSystem>();
        var grid = mapSys.CreateGridEntity(map.MapId);
        mapSys.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, map.Tile.Tile);
        entMan.System<SharedTransformSystem>().SetCoordinates(grid.Owner, new EntityCoordinates(map.MapUid, position));
        if (name != null)
            entMan.System<MetaDataSystem>().SetEntityName(grid.Owner, name);

        return grid.Owner;
    }

    /// <summary>An anchored chute on a grid's tile.</summary>
    private static EntityUid Chute(IEntityManager entMan, EntityUid grid, string proto = ChuteProto)
    {
        var chute = entMan.SpawnEntity(proto, new EntityCoordinates(grid, 0.5f, 0.5f));
        if (!entMan.GetComponent<TransformComponent>(chute).Anchored)
            entMan.System<SharedTransformSystem>().AnchorEntity(chute);

        return chute;
    }

    /// <summary>A crate made on a grid, then carried somewhere.</summary>
    private static EntityUid Crate(IEntityManager entMan, EntityUid madeOn, EntityCoordinates to, string proto = CrateProto)
    {
        var crate = entMan.SpawnEntity(proto, new EntityCoordinates(madeOn, 0.5f, 0.5f));
        entMan.System<SharedTransformSystem>().SetCoordinates(crate, to);
        return crate;
    }

    /// <summary>Papers of one prototype on a map.</summary>
    private static List<EntityUid> Papers(IEntityManager entMan, MapId map, string proto)
    {
        var papers = new List<EntityUid>();
        var query = entMan.AllEntityQueryEnumerator<PaperComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var xform, out var meta))
        {
            if (xform.MapID == map && meta.EntityPrototype?.ID == proto)
                papers.Add(uid);
        }

        return papers;
    }

    /// <summary>Puts the dummy ticker in a round, optionally on a map as the sector map, as the sector tests do.</summary>
    private static void StartRound(GameTicker ticker, MapId? sectorMap)
    {
        if (sectorMap is { } map)
            SetPrivate(ticker, nameof(GameTicker.DefaultMap), map);

        SetPrivate(ticker, nameof(GameTicker.RunLevel), GameRunLevel.InRound);
    }

    private static void SetPrivate(GameTicker ticker, string property, object value)
    {
        typeof(GameTicker).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(ticker, new[] { value });
    }

    [Test]
    public async Task DumpRecoversWithinTheRecoveryWindow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            entMan.EnsureComponent<WFTradeMarketMapComponent>(map.MapUid);
            var chute = entMan.SpawnEntity(ChuteProto, map.GridCoords);
            var user = entMan.SpawnEntity(null, map.GridCoords);
            var far = Grid(entMan, map, new Vector2(5000f, 0f));
            var grid = map.Grid.Owner;
            var profile = market.Profile(grid);
            var basePrice = market.Quote(chute, CrateProto)!.Value.Base;

            const int dump = 20;
            for (var i = 0; i < dump; i++)
            {
                market.Sell(chute, Crate(entMan, far, map.GridCoords), user);
            }

            // A 20-crate dump lands on the pressure price unless the good's floor is above it.
            var perSale = profile.PerSaleOverrides.GetValueOrDefault(CrateProto, profile.PerSale);
            var floor = profile.FloorPrices.TryGetValue(CrateProto, out var floorPrice)
                ? System.Math.Min(floorPrice, basePrice)
                : basePrice * profile.Floor;
            var dumped = market.Quote(chute, CrateProto)!.Value;
            Assert.That(dumped.Amount, Is.EqualTo(System.Math.Max(basePrice * (1f - dump * perSale), floor)).Within(1));
            Assert.That(dumped.Amount, Is.LessThan(basePrice * 0.9f), "The dump should push the price well below base.");

            var pressure = market.Pressure(grid, CrateProto);
            Assert.That(pressure, Is.LessThanOrEqualTo(1f), "Recovery to 0.9x in log2(10) half-lives needs pressure of at most 1.");

            // Stamp the pressure log2(10) half-lives ago: it has fallen to a tenth.
            var window = System.TimeSpan.FromSeconds(profile.HalfLife.TotalSeconds * System.Math.Log2(10));
            entMan.GetComponent<WFTradeMarketComponent>(grid).Pressure[CrateProto] = (pressure, timing.CurTime - window);
            var recovered = market.Quote(chute, CrateProto)!.Value;
            Assert.That(recovered.Amount, Is.GreaterThanOrEqualTo(basePrice * 0.9f),
                $"Price {recovered.Amount} hadn't recovered to 0.9x of {basePrice} after {window}.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChutesOnOneStationSharePressure()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            entMan.EnsureComponent<WFTradeMarketMapComponent>(map.MapUid);
            var first = entMan.SpawnEntity(ChuteProto, map.GridCoords);
            var second = entMan.SpawnEntity(ChuteProto, map.GridCoords);
            var otherStation = Grid(entMan, map, new Vector2(10000f, 0f));
            var elsewhere = Chute(entMan, otherStation);
            var user = entMan.SpawnEntity(null, map.GridCoords);
            var far = Grid(entMan, map, new Vector2(5000f, 0f));

            var basePrice = market.Quote(second, CrateProto)!.Value.Amount;
            for (var i = 0; i < 5; i++)
            {
                market.Sell(first, Crate(entMan, far, map.GridCoords), user);
            }

            var atFirst = market.Quote(first, CrateProto)!.Value;
            var atSecond = market.Quote(second, CrateProto)!.Value;
            Assert.That(atSecond.Amount, Is.LessThan(basePrice), "Sales at one chute should lower the other chute on the same station.");
            Assert.That(atSecond.Amount, Is.EqualTo(atFirst.Amount), "Two chutes on one station should quote alike.");
            Assert.That(market.Quote(elsewhere, CrateProto)!.Value.Amount, Is.EqualTo(market.Quote(elsewhere, CrateProto)!.Value.Base),
                "Sales on one station shouldn't move another station's price.");

            // And the other way round: selling at the other station leaves this one where it was.
            market.Sell(elsewhere, Crate(entMan, far, new EntityCoordinates(otherStation, 0.5f, 0.5f)), user);
            Assert.That(market.Quote(first, CrateProto)!.Value.Amount, Is.EqualTo(atFirst.Amount));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PaperPrintsLivePrices()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var cargo = entMan.System<WFCargoLoadSystem>();
        var loc = server.ResolveDependency<ILocalizationManager>();

        EntityUid chute = default, user = default;
        var printed = 0;
        string header = string.Empty;

        await server.WaitAssertion(() =>
        {
            entMan.EnsureComponent<WFTradeMarketMapComponent>(map.MapUid);
            chute = entMan.SpawnEntity(ChuteProto, map.GridCoords);
            user = entMan.SpawnEntity(null, map.GridCoords);
            var dispenser = entMan.GetComponent<DispenserComponent>(chute);

            // One good off its base, so the sheet has to be live rather than the static manifest. (Sell would leave a
            // pending quote, which the payout pays instead of printing.)
            market.AddPressure(map.Grid.Owner, CrateProto, 0.1f);
            Assert.That(market.Quote(chute, CrateProto)!.Value.Amount, Is.LessThan(market.Quote(chute, CrateProto)!.Value.Base));
            Assert.That(dispenser.Inventory.Keys.Any(good => cargo.GoodWeight(good) > 0f), Is.True,
                "No listed good has a weight, so the per-tonne figure isn't tested.");

            var before = Papers(entMan, map.MapId, dispenser.DefaultItem);
            Assert.That(market.Payout(chute, dispenser, dispenser.DefaultItem), Is.True, "The empty-hand paper should be handled.");
            var papers = Papers(entMan, map.MapId, dispenser.DefaultItem).Except(before).ToList();
            Assert.That(papers, Has.Count.EqualTo(1), "The empty-hand payout should print exactly one paper.");
            var content = entMan.GetComponent<PaperComponent>(papers[0]).Content;

            header = loc.GetString("wf-trade-paper-header", ("station", market.StationName(chute)));
            Assert.That(content, Does.Contain(header));
            foreach (var good in dispenser.Inventory.Keys)
            {
                var quote = market.Quote(chute, good)!.Value;
                var tonnes = cargo.GoodWeight(good);
                var line = loc.GetString("wf-trade-price-line",
                    ("good", market.GoodName(good)),
                    ("amount", quote.Amount),
                    ("trend", WFTradeMarketSystem.Trend(quote.Factor)),
                    ("reason", loc.GetString(quote.Reason)),
                    ("tonnes", tonnes),
                    ("perTonne", tonnes > 0f ? (int) (quote.Amount / tonnes) : 0));
                Assert.That(content, Does.Contain(line), $"The paper doesn't show {good} at its live price.");
            }

            // A real empty-hand press goes through the chute's dispense delay to the same sheet.
            printed = Papers(entMan, map.MapId, dispenser.DefaultItem).Count;
            var press = new ActivateInWorldEvent(user, chute, true);
            entMan.EventBus.RaiseLocalEvent(chute, press);
            Assert.That(press.Handled, Is.True, "An idle chute should answer an empty-hand press.");
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var dispenser = entMan.GetComponent<DispenserComponent>(chute);
            var papers = Papers(entMan, map.MapId, dispenser.DefaultItem);
            Assert.That(papers, Has.Count.EqualTo(printed + 1), "The empty-hand press didn't print a paper.");
            Assert.That(papers.Any(paper => entMan.GetComponent<PaperComponent>(paper).Content.Contains(header)), Is.True);
            Assert.That(TradeTestHelpers.CashOnMap(entMan, map.MapId), Is.Zero, "An empty-hand press paid out cash.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PendingQuoteIsNeitherPrintedOverNorPaidTwice()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var interaction = entMan.System<SharedInteractionSystem>();

        EntityUid chute = default, user = default, far = default, second = default;
        string paperProto = string.Empty;
        var quoted = 0;

        await server.WaitAssertion(() =>
        {
            entMan.EnsureComponent<WFTradeMarketMapComponent>(map.MapUid);
            chute = entMan.SpawnEntity(ChuteProto, map.GridCoords);
            user = entMan.SpawnEntity(null, map.GridCoords);
            far = Grid(entMan, map, new Vector2(5000f, 0f));
            var dispenser = entMan.GetComponent<DispenserComponent>(chute);
            paperProto = dispenser.DefaultItem;

            var crate = Crate(entMan, far, map.GridCoords);
            quoted = market.Quote(chute, CrateProto)!.Value.Amount;
            Assert.That(interaction.InteractUsing(user, crate, chute, map.GridCoords, false, false), Is.True);
            Assert.That(dispenser.Dispensing, Is.True);
            Assert.That(entMan.GetComponent<WFTradeChuteComponent>(chute).PendingAmount, Is.EqualTo(quoted));

            // An empty-hand press while the sale is in the chute is ignored: no paper, and the quote stays put.
            var press = new ActivateInWorldEvent(user, chute, true);
            entMan.EventBus.RaiseLocalEvent(chute, press);
            Assert.That(press.Handled, Is.False, "A busy chute shouldn't take an empty-hand press.");
            Assert.That(dispenser.DispensingItemId, Is.Not.EqualTo(paperProto), "The press replaced the sale being dispensed.");
            Assert.That(entMan.GetComponent<WFTradeChuteComponent>(chute).PendingAmount, Is.EqualTo(quoted));
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(TradeTestHelpers.CashOnMap(entMan, map.MapId), Is.EqualTo(quoted), "The sale wasn't paid exactly once.");
            Assert.That(Papers(entMan, map.MapId, paperProto), Is.Empty, "A paper was printed during a sale.");
            Assert.That(entMan.GetComponent<WFTradeChuteComponent>(chute).PendingAmount, Is.Null);

            // A chute that goes while a sale is pending pays nothing.
            second = Crate(entMan, far, map.GridCoords);
            Assert.That(interaction.InteractUsing(user, second, chute, map.GridCoords, false, false), Is.True);
            Assert.That(entMan.GetComponent<WFTradeChuteComponent>(chute).PendingAmount, Is.Not.Null);
            entMan.DeleteEntity(chute);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(second), Is.True);
            Assert.That(TradeTestHelpers.CashOnMap(entMan, map.MapId), Is.EqualTo(quoted), "A deleted chute paid its pending quote.");
            Assert.That(Papers(entMan, map.MapId, paperProto), Is.Empty);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WarDemandRaisesOnlyNavyChutes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var war = entMan.System<WarLevelSystem>();

        await server.WaitAssertion(() =>
        {
            entMan.EnsureComponent<WFTradeMarketMapComponent>(map.MapUid);
            var navy = entMan.SpawnEntity(TsfChute, map.GridCoords);
            var civil = Chute(entMan, Grid(entMan, map, new Vector2(10000f, 0f)));
            var profile = market.Profile(map.Grid.Owner);
            Assert.That(profile.WarDemandChutes.Contains(TsfChute), Is.True, $"{TsfChute} no longer pays war demand.");
            Assert.That(profile.WarDemandChutes.Contains(ChuteProto), Is.False, $"{ChuteProto} pays war demand; pick another civil chute.");

            // The war level lives on the sector service; a test map hosts one if the server has none.
            if (!entMan.EntityExists(entMan.System<SectorServiceSystem>().GetServiceEntity()))
                entMan.AddComponent<StationSectorServiceHostComponent>(map.MapUid);
            entMan.EnsureComponent<WarLevelComponent>(entMan.System<SectorServiceSystem>().GetServiceEntity());

            var peace = market.Quote(navy, Ammo)!.Value;
            Assert.That(peace.Reason, Is.Not.EqualTo((LocId) "wf-trade-reason-war"));
            war.SetLevel(true);
            try
            {
                Assert.That(war.GetWarLevel(navy), Is.True, "The test needs the war level to go hot.");

                var hot = market.Quote(navy, Ammo)!.Value;
                Assert.That(hot.Reason, Is.EqualTo((LocId) "wf-trade-reason-war"));
                Assert.That(hot.Amount, Is.EqualTo(peace.Amount * profile.WarFactor).Within(1));

                var civilQuote = market.Quote(civil, Ammo)!.Value;
                Assert.That(civilQuote.Reason, Is.Not.EqualTo((LocId) "wf-trade-reason-war"));
                Assert.That(civilQuote.Amount, Is.EqualTo(civilQuote.Base), "A civil chute shouldn't pay war demand.");
            }
            finally
            {
                // The pooled server is shared; leave the war cold for the next test.
                war.SetLevel(false);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShortageEndsAndPriceReturnsToBase()
    {
        // Dirty: the test puts the ticker in a round, so the pair needs a round restart before reuse.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var shortages = entMan.System<WFTradeShortageSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        EntityUid chute = default;
        var basePrice = 0;
        var ticks = 0;

        await server.WaitAssertion(() =>
        {
            // Shortages only expire while a round runs.
            StartRound(entMan.System<GameTicker>(), null);
            entMan.EnsureComponent<WFTradeMarketMapComponent>(map.MapUid);
            chute = entMan.SpawnEntity(ChuteProto, map.GridCoords);
            basePrice = market.Quote(chute, CrateProto)!.Value.Amount;

            shortages.StartLocal(map.Grid.Owner, new HashSet<string> { CrateProto }, 2f, System.TimeSpan.FromSeconds(1), "wf-trade-goods-textiles");
            var raised = market.Quote(chute, CrateProto)!.Value;
            Assert.That(raised.Reason, Is.EqualTo((LocId) "wf-trade-reason-short"));
            Assert.That(raised.Amount, Is.EqualTo(basePrice * 2));
            ticks = timing.TickRate * 3;
        });

        await pair.RunTicksSync(ticks);

        await server.WaitAssertion(() =>
        {
            Assert.That(shortages.Active.Any(shortage => shortage.Grid == map.Grid.Owner), Is.False, "The shortage outlived its end.");
            var after = market.Quote(chute, CrateProto)!.Value;
            Assert.That(after.Amount, Is.EqualTo(basePrice), "The price should return to base when the shortage ends.");
            Assert.That(after.Reason, Is.EqualTo((LocId) "wf-trade-reason-steady"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TrafficBreaksABlockadeOnlyWhenTheLaneIsBusy()
    {
        // Dirty: the test makes its map the sector map in a round, so the pair needs a round restart before reuse.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var sector = entMan.System<WFTradeSectorSystem>();
        var territory = entMan.System<WFSectorTerritorySystem>();
        var activity = entMan.System<WFSectorActivitySystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        var busy = new WFSectorCell(4, -2);
        var quiet = new WFSectorCell(-4, 2);
        EntityUid busyShip = default, quietShip = default, quietStation = default, quietChute = default;
        var ticks = 0;

        await server.WaitAssertion(() =>
        {
            // The blockade runs on the sector map: make the test map it, in a round.
            StartRound(entMan.System<GameTicker>(), map.MapId);
            Assert.That(territory.Enabled, Is.True, "Sector control is off.");
            Assert.That(territory.SectorMap, Is.EqualTo(map.MapId));

            var busyCentre = WFSectorHex.Centre(busy, territory.CellSize);
            var busyStation = Grid(entMan, map, busyCentre, "WF Trade Test Busy");
            quietStation = Grid(entMan, map, WFSectorHex.Centre(quiet, territory.CellSize), "WF Trade Test Quiet");
            var busyChute = Chute(entMan, busyStation);
            quietChute = Chute(entMan, quietStation);
            busyShip = Grid(entMan, map, busyCentre + new Vector2(200f, 0f), "WF Trade Test Runner");
            quietShip = Grid(entMan, map, WFSectorHex.Centre(quiet, territory.CellSize) + new Vector2(200f, 0f), "WF Trade Test Idler");
            var maker = Grid(entMan, map, busyCentre + new Vector2(5000f, 0f));
            var user = entMan.SpawnEntity(null, map.GridCoords);

            var deadline = timing.CurTime + System.TimeSpan.FromSeconds(2);
            Assert.That(territory.Contest(map.MapId, busy, Blockader, deadline), Is.True);
            Assert.That(territory.Contest(map.MapId, quiet, Blockader, deadline), Is.True);
            var stations = sector.Attempts().Select(attempt => attempt.Station).ToList();
            Assert.That(stations, Does.Contain(busyStation).And.Contain(quietStation), "The contests weren't seen as blockade attempts.");
            Assert.That(market.Quote(busyChute, CrateProto)!.Value.Reason, Is.EqualTo((LocId) "wf-trade-reason-relief"));
            var started = territory.ContestOf(map.MapId, busy)!.Started;

            // A real haul delivered at the busy station counts as traffic and names its ship.
            var hauled = Crate(entMan, maker, new EntityCoordinates(busyStation, 0.5f, 0.5f));
            entMan.GetComponent<WFTradeOriginComponent>(hauled).Carrier = busyShip;
            Assert.That(market.WasHauled(busyChute, hauled), Is.True);
            market.Sell(busyChute, hauled, user);
            Assert.That(activity.IsQuiet(map.MapId, busy, started), Is.False, "A hauled delivery should mark its cell as traffic.");

            // A crate made at the quiet station names a seller but isn't traffic.
            var local = entMan.SpawnEntity(CrateProto, new EntityCoordinates(quietStation, 0.5f, 0.5f));
            entMan.GetComponent<WFTradeOriginComponent>(local).Carrier = quietShip;
            market.Sell(quietChute, local, user);
            Assert.That(activity.IsQuiet(map.MapId, quiet, started), Is.True, "An unhauled sale shouldn't count as traffic.");

            ticks = timing.TickRate * 5;
        });

        await pair.RunTicksSync(ticks);

        await server.WaitAssertion(() =>
        {
            Assert.That(territory.ContestOf(map.MapId, busy), Is.Null, "The busy contest wasn't decided.");
            Assert.That(territory.ContestOf(map.MapId, quiet), Is.Null, "The quiet contest wasn't decided.");

            Assert.That(territory.Owner(map.MapId, busy), Is.Null, "Traffic should keep the busy cell free.");

            Assert.That(territory.Owner(map.MapId, quiet)?.Id, Is.EqualTo(Blockader), "A quiet cell should fall to the blockade.");
            Assert.That(sector.Blockaded(), Does.Contain(quietStation));
            Assert.That(market.Quote(quietChute, Medical)!.Value.Reason, Is.EqualTo((LocId) "wf-trade-reason-blockade"));
        });

        await pair.CleanReturnAsync();
    }
}
