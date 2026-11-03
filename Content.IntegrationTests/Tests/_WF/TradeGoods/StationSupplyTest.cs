#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._NF.Market.Components;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Cargo.Systems;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Systems;
using Content.Shared._Crescent.Dispenser;
using Content.Shared._Mono.VendingMachine;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.TradeGoods;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Station.Components;
using Content.Shared.VendingMachines;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>Stations start stocked, vendors hold finite stock, and freighters run on their own schedule.</summary>
[TestFixture]
[TestOf(typeof(WFTradeSupplySystem))]
public sealed class StationSupplyTest
{
    private const string Vendor = "VendingMachineTraderTradeMall";
    private const string Fence = "WFVendingMachineFence";
    private const string Luxuries = "TradeGoodLuxuries";
    private const string Contraband = "WFTradeGoodContraband";
    private const string Medicine = "TradeGoodMedicalSupplies";
    private const string Steel = "SheetSteel1";

    /// <summary>A vendor on a grid at full stock of luxuries, repriced so its stock is finite.</summary>
    private static (EntityUid Vendor, VendingMachineInventoryEntry Entry) StockedVendor(IEntityManager entMan, EntityUid grid)
    {
        var vendor = entMan.SpawnEntity(Vendor, new EntityCoordinates(grid, 0.5f, 0.5f));
        var market = entMan.System<WFTradeMarketSystem>();
        market.SetVendorStock(grid, Luxuries, market.Profile(grid).VendorStock);
        return (vendor, entMan.GetComponent<VendingMachineComponent>(vendor).Inventory[Luxuries]);
    }

    [Test]
    public async Task VendorsRunOutAndRestock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var (_, entry) = StockedVendor(entMan, grid);
            var full = market.Profile(grid).VendorStock;
            Assert.That(entry.Amount, Is.EqualTo((uint) full), "A trade vendor's stock should be finite.");

            market.SetVendorStock(grid, Luxuries, 0);
            Assert.That(entry.Amount, Is.Zero);
            Assert.That(market.StationBuyQuotes(grid).ContainsKey(Luxuries), Is.False, "A sold-out good should drop off the app.");

            market.Row(grid, Luxuries).Inbound = 1f;
            Assert.That(market.Deliver(grid, Luxuries, 5f), Is.EqualTo(1f), "A delivery should stop at what's owed.");
            Assert.That(entry.Amount, Is.EqualTo(1u));
            Assert.That(market.StationBuyQuotes(grid).ContainsKey(Luxuries), Is.True);

            market.Row(grid, Luxuries).Inbound = full * 2f;
            market.Deliver(grid, Luxuries, full * 2f);
            Assert.That(entry.Amount, Is.EqualTo((uint) full), "A delivery should stop at what the station can hold.");

            var fence = entMan.SpawnEntity(Fence, map.GridCoords);
            market.SetVendorStock(grid, Luxuries, full);
            var contraband = entMan.GetComponent<VendingMachineComponent>(fence).Inventory[Contraband];
            Assert.That(contraband.Amount, Is.EqualTo(uint.MaxValue), "Contraband should never run out.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EachStationRollsItsOwnOpeningStock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var profile = market.Profile(grid);
            Assert.That(market.VendorStock(grid, Luxuries), Is.EqualTo((int) profile.StockTarget), "A station outside the economy should open at its target.");

            entMan.EnsureComponent<WFTradeStationComponent>(grid).Id = "CargoDepot";
            entMan.SpawnEntity(Vendor, map.GridCoords);
            var goods = market.StockedGoods(grid).Where(good => good != Luxuries).ToList();
            var stocks = goods.ConvertAll(good => market.VendorStock(grid, good));

            Assert.That(stocks, Is.All.InRange(
                (int) System.MathF.Floor(profile.StockTarget * profile.VendorStockStartMin),
                (int) System.MathF.Ceiling(profile.StockTarget * profile.VendorStockStartMax)));
            Assert.That(stocks.Distinct().Count(), Is.GreaterThan(1), "Each good should roll its own stock.");
            Assert.That(goods.ConvertAll(good => market.VendorStock(grid, good)), Is.EqualTo(stocks), "A stock should only roll once.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StationsStartFull()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var profile = entMan.System<WFTradeMarketSystem>().Profile(null);
            var config = server.ProtoMan.Index<GameMapPrototype>("CargoDepot").Stations["CargoDepot"];
            var station = entMan.System<StationSystem>().InitializeNewStation(config, new[] { map.Grid.Owner }, "WF test depot");
            var value = entMan.System<PricingSystem>().GetEstimatedPrice(server.ProtoMan.Index<EntityPrototype>(Steel));

            Assert.That(TradeTestWorld.Stock(entMan, station, Steel), Is.EqualTo((int) (profile.MaterialStockTarget / value)).Within(1),
                "A station should open with a full stock of everyday materials.");
            Assert.That(TradeTestWorld.Stock(entMan, station, "SheetPlasma1"), Is.Zero, "Precious materials should only come from miners.");
            entMan.DeleteEntity(station);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AStationsVendorsShareOneStock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var (_, first) = StockedVendor(entMan, grid);
            var (_, second) = StockedVendor(entMan, grid);
            var full = (uint) market.Profile(grid).VendorStock;
            Assert.That(first.Amount, Is.EqualTo(full));
            Assert.That(second.Amount, Is.EqualTo(full));

            var crate = entMan.SpawnEntity(Luxuries, map.GridCoords);
            entMan.AddComponent<VendingMachinePurchaseComponent>(crate);
            Assert.That(market.VendorStock(grid, Luxuries), Is.EqualTo((int) full - 1), "A purchase should come off the station's stock.");
            Assert.That(first.Amount, Is.EqualTo(full - 1), "Every vendor should show the shared stock.");
            Assert.That(second.Amount, Is.EqualTo(full - 1), "Every vendor should show the shared stock.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADryStationPaysTheTopRateAndFillsItsShelves()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var rushes = entMan.System<WFTradeRushSystem>();

        await server.WaitAssertion(() =>
        {
            // The Trade Mall's chute doesn't list what its own vendor sells; Baikal's sets the rate.
            var chute = TradeTestWorld.MarketChute(entMan, map);
            var grid = map.Grid.Owner;
            var (far, payer) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WF test payer", "CargoChuteBaikal");
            entMan.SpawnEntity(Vendor, map.GridCoords);
            var profile = market.Profile(grid);
            var start = (int) MathF.Ceiling(profile.StockTarget * profile.ScarceLevel);

            market.SetVendorStock(grid, Luxuries, start);
            Assert.That(market.Quote(chute, Luxuries), Is.Null, "A chute shouldn't take a good its shelves aren't scarce of.");

            market.SetVendorStock(grid, Luxuries, start - 1);
            var quote = market.Quote(chute, Luxuries)!.Value;
            Assert.That(quote.Reason, Is.EqualTo((LocId) "wf-trade-reason-restock"));
            Assert.That(quote.Base, Is.EqualTo(market.Quote(payer, Luxuries)!.Value.Base), "A dry station should pay the best listed rate.");

            var crate = TradeTestWorld.Hauled(entMan, far, map.GridCoords, Luxuries);
            Assert.That(market.TakesToRestock(chute, crate), Is.True);

            var rush = rushes.Start(new ProtoId<WFTradeRushPrototype>("WFTradeRushLuxuries"));
            Assert.That(rush!.Grid, Is.EqualTo(grid), "An order should go to the station scarce of its goods.");

            market.Sell(chute, crate, entMan.SpawnEntity(null, map.GridCoords), quote.Amount);
            Assert.That(market.VendorStock(grid, Luxuries), Is.EqualTo(start), "A delivery should go on the shelf.");
            Assert.That(market.Quote(chute, Luxuries), Is.Not.Null, "An open order keeps taking its goods once the shortage ends.");

            rushes.Clear();
            Assert.That(market.Quote(chute, Luxuries), Is.Null, "A filled shelf should stop taking crates.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A good on no such manifest would run out for good at a quiet server.</summary>
    [Test]
    public async Task EveryVendorGoodComesByFreightAtAnyPlayerCount()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var factory = server.EntMan.ComponentFactory;

        await server.WaitAssertion(() =>
        {
            var carried = protoMan.EnumeratePrototypes<WFEncounterPrototype>()
                .Where(encounter => encounter.Freight && encounter.MinPlayers == 0)
                .SelectMany(encounter => encounter.Manifests)
                .SelectMany(manifest => protoMan.Index(manifest).Crates)
                .Select(crate => crate.Id)
                .ToHashSet();
            var contraband = protoMan.Index(WFTradeMarketSystem.DefaultProfile).ContrabandGoods;

            foreach (var pack in new[] { "TraderBaikal", "TraderTradeMall", "TraderDepot", "TraderDepotAlt" })
            {
                foreach (var good in protoMan.Index<VendingMachineInventoryPrototype>(pack).StartingInventory.Keys)
                {
                    if (protoMan.Index<EntityPrototype>(good).TryGetComponent<WFTradeOriginComponent>(out _, factory) && !contraband.Contains(good))
                        Assert.That(carried, Does.Contain(good), $"{pack} sells {good}, but no freighter brings it at any player count.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FreightRunsOutsideTheStorytellersCap()
    {
        // Dirty: the test makes its map the sector map in a round, so the pair needs a round restart before reuse.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var scheduler = entMan.System<WFEncounterSchedulerSystem>();
        var encounters = entMan.System<WFEncounterSystem>();

        await server.WaitAssertion(() =>
        {
            SetPrivate(entMan.System<GameTicker>(), nameof(GameTicker.DefaultMap), map.MapId);
            foreach (var (x, name) in new[] { (0f, "WF test a"), (9000f, "WF test b"), (18000f, "WF test c") })
            {
                var (grid, _) = Station(entMan, map, new Vector2(x, 0f), name);
                entMan.EnsureComponent<DockingComponent>(entMan.SpawnEntity(null, new EntityCoordinates(grid, 0.5f, 0.5f)));
            }

            var budget = encounters.ActiveCost();
            var count = encounters.ActiveCount();
            Assert.That(scheduler.TryScheduleFreight(out var run), Is.True, "A freight run should start on its own schedule.");
            var comp = entMan.GetComponent<WFEncounterComponent>(run);
            Assert.That(comp.Freight, Is.True);
            Assert.That(comp.Stops.Count, Is.GreaterThanOrEqualTo(2), "A freight run should call at stations.");
            Assert.That(encounters.ActiveFreight(), Is.EqualTo(1));
            Assert.That(encounters.ActiveCount(), Is.EqualTo(count), "A freight run shouldn't take a storyteller slot.");
            Assert.That(encounters.ActiveCost(), Is.EqualTo(budget), "A freight run shouldn't take storyteller budget.");

            for (var i = 0; i < 6; i++)
            {
                scheduler.TryScheduleFreight(out _);
            }

            var cap = server.CfgMan.GetCVar(Content.Shared._WF.CCVar.EncountersCVars.FreightMaxActive);
            Assert.That(encounters.ActiveFreight(), Is.LessThanOrEqualTo(cap), "Freight should keep to its own cap.");

            foreach (var uid in entMan.EntityQuery<WFEncounterComponent>().Select(running => running.Owner).ToList())
            {
                encounters.End(uid);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RaidersOnTheApproachesBlockadeAStation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var sector = entMan.System<WFTradeSectorSystem>();
        var encounters = entMan.System<WFEncounterSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var chute = TradeTestWorld.MarketChute(entMan, map);
            entMan.EnsureComponent<DockingComponent>(entMan.SpawnEntity(null, map.GridCoords));
            var profile = market.Profile(grid);
            var calm = market.Quote(chute, Medicine)!.Value.Amount;

            var run = entMan.Spawn();
            var comp = entMan.AddComponent<WFEncounterComponent>(run);
            comp.Prototype = "WFEncounterStationBlockade";
            comp.Blockades = true;
            comp.Stops.Add(grid);
            Assert.That(sector.IsBlockaded(grid), Is.False, "An encounter that hasn't begun blockades nothing.");

            comp.Begun = true;
            Assert.That(sector.IsBlockaded(grid), Is.True);
            Assert.That(sector.Blockaded(), Does.Contain(grid), "The Market app should list the blockade.");
            var scheduler = entMan.System<WFEncounterSchedulerSystem>();
            Assert.That(scheduler.Stations(map.MapId).Select(station => station.Owner), Does.Not.Contain(grid),
                "Freighters should stay away from a blockaded station.");
            Assert.That(entMan.System<WFSectorTerritorySystem>().Stations(map.MapId).Select(station => station.Owner), Does.Contain(grid),
                "Sector control's own station list should keep a blockaded station.");
            var quote = market.Quote(chute, Medicine)!.Value;
            Assert.That(quote.Amount, Is.EqualTo((int) (calm * profile.BlockadeFactor)).Within(1), "A blockaded station should pay relief prices.");
            Assert.That(quote.Reason, Is.EqualTo((LocId) "wf-trade-reason-blockade"));

            encounters.Resolve(run, WFEncounterResolution.Destroyed);
            Assert.That(sector.IsBlockaded(grid), Is.False, "Destroying the raiders should lift the blockade.");
            entMan.DeleteEntity(run);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A named station grid with a cargo market, and its station.</summary>
    private static (EntityUid Grid, EntityUid Station) Station(IEntityManager entMan, TestMapData map, Vector2 position, string name)
    {
        var grid = TradeTestWorld.Grid(entMan, map, position, name);
        var station = entMan.Spawn();
        entMan.EnsureComponent<StationDataComponent>(station);
        entMan.System<StationSystem>().AddGridToStation(station, grid);
        entMan.EnsureComponent<CargoMarketDataComponent>(station);
        return (grid, station);
    }

    private static void SetPrivate(GameTicker ticker, string property, object value)
    {
        typeof(GameTicker).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(ticker, new[] { value });
    }
}
