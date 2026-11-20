#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._NF.Market.Components;
using Content.Server._NF.Market.Systems;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Station.Systems;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>The sector economy, a rule at a time: stations use and make goods, surplus is owed to whoever is short, and prices follow the shelf.</summary>
[TestFixture]
[TestOf(typeof(WFTradeEconomySystem))]
public sealed class EconomyLedgerTest
{
    private const string Fertilizer = "TradeGoodFertilizer";
    private const string Fabric = "TradeGoodFabric";
    private const string Supplies = "TradeGoodSupplies";
    private const string Medical = "TradeGoodMedicalSupplies";
    private const string Chemicals = "TradeGoodChemicalSupplies";
    private const string Volatile = "TradeGoodChemicalsVolatile";
    private const string Metals = "TradeGoodMetals";
    private const string Ammo = "TradeGoodAmmo";
    private const string Fission = "TradeGoodFissionFuel";
    private const string Fusion = "TradeGoodFusionFuel";
    private const string Scrap = "TradeGoodScrap";
    private const string ShipComponents = "TradeGoodShipComponents";

    /// <summary>The always-spawned stations, one depot of each kind and the yard, as in a normal round.</summary>
    private static readonly string[] RoundStations = { "Frontier", "USSPCamelot", "TSFMCHalcyon", "Zvezda", "CargoDepot", "CargoDepotAlt", "WFDrydock" };

    /// <summary>A station grid of a role.</summary>
    private static EntityUid Station(IEntityManager entMan, TestMapData map, string id, float offset = 0f)
    {
        var grid = TradeTestWorld.Grid(entMan, map, new Vector2(offset, 200f), id);
        entMan.EnsureComponent<WFTradeStationComponent>(grid).Id = id;
        return grid;
    }

    [Test]
    public async Task TheCurveIsGentleAtFirstAndSteepNearEmpty()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var profile = server.ProtoMan.Index(WFTradeMarketSystem.DefaultProfile);
            Assert.That(WFTradeMarketSystem.LevelFactor(profile, 1f), Is.EqualTo(1f));
            Assert.That(WFTradeMarketSystem.LevelFactor(profile, 0.6f), Is.EqualTo(1.24f).Within(0.001f));
            Assert.That(WFTradeMarketSystem.LevelFactor(profile, 0.2f), Is.EqualTo(1.96f).Within(0.001f));
            Assert.That(WFTradeMarketSystem.LevelFactor(profile, 0f), Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(WFTradeMarketSystem.LevelFactor(profile, 1.5f), Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(WFTradeMarketSystem.LevelFactor(profile, 9f), Is.EqualTo(0.4f).Within(0.001f), "A glut should stop falling.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StationsUseGoodsAndIndustriesMakeThemFromTheirInput()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var economy = entMan.System<WFTradeEconomySystem>();

        await server.WaitAssertion(() =>
        {
            var depot = Station(entMan, map, "CargoDepotAlt");
            var profile = market.Profile(depot);
            var role = market.Role(depot)!;

            // Fed: an hour of Food makes its rate of Supplies and burns half that of Fertilizer.
            market.SetVendorStock(depot, Supplies, 0);
            market.SetVendorStock(depot, Fertilizer, 120);
            market.SetVendorStock(depot, Medical, 120);
            economy.Tick(1f);
            var food = server.ProtoMan.Index<WFTradeIndustryPrototype>("WFTradeIndustryFood");
            Assert.That(market.Row(depot, Supplies).Stock, Is.EqualTo(food.Rate).Within(0.01f), "A station alone in an industry should make all of its rate.");
            Assert.That(market.Row(depot, Fertilizer).Stock, Is.EqualTo(120f - food.Rate * profile.InputUse).Within(0.01f));
            Assert.That(market.Row(depot, Supplies).Made, Is.EqualTo(food.Rate).Within(0.01f), "The sector view should see the flow.");

            // Starved: it keeps a trickle, so a broken chain can restart, and output scales in between.
            market.SetVendorStock(depot, Supplies, 0);
            market.SetVendorStock(depot, Fertilizer, 0);
            economy.Tick(1f);
            Assert.That(market.Row(depot, Supplies).Stock, Is.EqualTo(food.Rate * profile.MinOutput).Within(0.01f));

            market.SetVendorStock(depot, Supplies, 0);
            market.SetVendorStock(depot, Fertilizer, 60);
            economy.Tick(1f);
            Assert.That(market.Row(depot, Supplies).Stock, Is.EqualTo(food.Rate * 0.5f).Within(0.01f), "A short input should slow an industry.");

            // A station uses what's on its list, and stops at empty.
            var used = profile.UseRate * role.Population;
            market.SetVendorStock(depot, ShipComponents, 40);
            economy.Tick(1f);
            Assert.That(market.Row(depot, ShipComponents).Stock, Is.EqualTo(40f - used).Within(0.01f));
            market.SetVendorStock(depot, ShipComponents, 0);
            economy.Tick(1f);
            Assert.That(market.Row(depot, ShipComponents).Stock, Is.Zero);

            // A full shelf stops the line and spares its input.
            market.SetVendorStock(depot, Supplies, profile.VendorStock);
            market.Row(depot, Fertilizer).Stock = 120f;
            economy.Tick(0.1f);
            Assert.That(market.Row(depot, Fertilizer).Stock, Is.EqualTo(120f).Within(0.01f));

            entMan.DeleteEntity(depot);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnIndustrysRateIsSharedSoARoundsDepotsDontChangeIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var economy = entMan.System<WFTradeEconomySystem>();

        await server.WaitAssertion(() =>
        {
            var first = Station(entMan, map, "CargoDepotAlt");
            var second = Station(entMan, map, "CargoDepotAlt", 200f);
            foreach (var grid in new[] { first, second })
            {
                market.SetVendorStock(grid, Fertilizer, 120);
                market.SetVendorStock(grid, Supplies, 0);
            }

            economy.Tick(1f);
            var food = server.ProtoMan.Index<WFTradeIndustryPrototype>("WFTradeIndustryFood");
            Assert.That(market.Row(first, Supplies).Stock + market.Row(second, Supplies).Stock, Is.EqualTo(food.Rate).Within(0.01f));
            entMan.DeleteEntity(first);
            entMan.DeleteEntity(second);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SurplusIsOwedToWhoeverIsShortAndOnlyFreightDeliversIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var economy = entMan.System<WFTradeEconomySystem>();

        await server.WaitAssertion(() =>
        {
            var maker = Station(entMan, map, "Zvezda");
            var buyer = Station(entMan, map, "TSFMCHalcyon", 200f);
            var profile = market.Profile(maker);
            market.SetVendorStock(buyer, Medical, 0);
            market.SetVendorStock(maker, Medical, profile.VendorStock);

            economy.Tick(1f);
            var row = market.Row(buyer, Medical);
            Assert.That(row.Inbound, Is.GreaterThan(0f), "A short station should be owed the sector's surplus.");
            Assert.That(row.Stock, Is.Zero, "Stock should only move when a freighter brings it.");
            Assert.That(market.Row(maker, Medical).Inbound, Is.Zero, "A maker shouldn't be owed its own goods.");
            Assert.That(market.Row(maker, Medical).Stock, Is.LessThan(profile.VendorStock), "Exports should leave the maker's shelf.");

            // Never more than the gap, so nothing ships past target.
            economy.Tick(12f);
            Assert.That(row.Inbound + row.Stock, Is.LessThanOrEqualTo(profile.StockTarget + 0.01f));

            var owed = row.Inbound;
            Assert.That(market.Deliver(buyer, Medical, owed + 50f), Is.EqualTo(owed).Within(0.01f));
            Assert.That(row.Stock, Is.EqualTo(owed).Within(0.01f));
            entMan.DeleteEntity(maker);
            entMan.DeleteEntity(buyer);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WhatNobodyMakesComesFromOutside()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var economy = entMan.System<WFTradeEconomySystem>();

        await server.WaitAssertion(() =>
        {
            var station = Station(entMan, map, "Frontier");
            var profile = market.Profile(station);
            market.SetVendorStock(station, Fission, 0);
            economy.Tick(1f);
            Assert.That(market.Row(station, Fission).Inbound, Is.GreaterThanOrEqualTo(profile.OutsideSupply - 0.01f));
            entMan.DeleteEntity(station);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ABlockadeBitesWithinHalfAnHourAndItsCratesWaitForRelief()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var economy = entMan.System<WFTradeEconomySystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            TradeTestWorld.MarketChute(entMan, map);
            entMan.EnsureComponent<WFTradeStationComponent>(grid).Id = "TSFMCHalcyon";
            var profile = market.Profile(grid);
            market.SetVendorStock(grid, Fusion, 80);
            market.SetVendorStock(grid, Ammo, 80);
            var start = market.Row(grid, Fusion).Stock;
            var floor = profile.BlockadeFloor * profile.StockTarget;

            var raiders = entMan.Spawn();
            var comp = entMan.AddComponent<WFEncounterComponent>(raiders);
            comp.Blockades = true;
            comp.Begun = true;
            comp.Stops.Add(grid);

            const float step = 5f / 60f;
            for (var i = 0; i < 6; i++)
            {
                economy.Tick(step);
            }

            var row = market.Row(grid, Fusion);
            var fallen = (start - row.Stock) / (start - floor);
            Assert.That(fallen, Is.GreaterThan(0.7f), "Most of a blockade's effect should land in half an hour.");
            Assert.That(row.Inbound, Is.GreaterThan(0f), "Hoarded stock should come back with the first ship through.");

            for (var i = 0; i < 36; i++)
            {
                economy.Tick(step);
            }

            Assert.That(row.Stock, Is.GreaterThanOrEqualTo(floor * 0.5f), "Hoarding should stop near the floor.");
            Assert.That(row.Inbound, Is.GreaterThan(start - floor - 1f), "What a blockade took should stay owed.");

            // Its own products stay on the shelf.
            Assert.That(market.Row(grid, Ammo).Stock, Is.GreaterThan(floor * 2f), "A station's own goods shouldn't go to hoarders.");
            entMan.DeleteEntity(raiders);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShortagesReportRealGapsAndEndWhenTheyRecover()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var economy = entMan.System<WFTradeEconomySystem>();
        var shortages = entMan.System<WFTradeShortageSystem>();

        await server.WaitAssertion(() =>
        {
            shortages.Clear();
            var chute = TradeTestWorld.MarketChute(entMan, map);
            var grid = map.Grid.Owner;
            entMan.EnsureComponent<WFTradeStationComponent>(grid).Id = "Zvezda";
            var profile = market.Profile(grid);
            var medical = new ProtoId<WFTradeShortagePrototype>("WFTradeShortageMedical");
            market.SetVendorStock(grid, Medical, 10);
            market.Row(grid, Chemicals).Stock = 0f;

            // Under the bar, but not for long enough yet.
            economy.Tick(0.01f);
            Assert.That(market.Row(grid, Medical).ScarceSince, Is.Not.Null);
            Assert.That(shortages.Start(), Is.Null.Or.Property(nameof(WFTradeShortage.Grid)).Null, "A fresh dip shouldn't be reported.");
            shortages.Clear();

            market.Row(grid, Medical).ScarceSince -= profile.ShortageDelay;
            var report = shortages.Start(medical)!;
            Assert.That(report.Report, Is.True);
            Assert.That(report.Grid, Is.EqualTo(grid));
            Assert.That(report.Cause, Does.Contain(market.GoodName(Chemicals)), "A report should name a missing input.");

            var price = market.Quote(chute, Medical)!.Value;
            Assert.That(price.Factor, Is.EqualTo(WFTradeMarketSystem.LevelFactor(profile, market.ChuteLevel(grid, Medical))).Within(0.02f),
                "A report shouldn't add to a price that already shows the gap.");

            // Part way back it stays open; past the bar it closes.
            market.Row(grid, Medical).Stock = profile.StockTarget * (profile.ScarceLevel + 0.05f);
            economy.Tick(0.001f);
            Assert.That(shortages.Active, Does.Contain(report));
            market.Row(grid, Medical).Stock = profile.StockTarget;
            economy.Tick(0.001f);
            Assert.That(shortages.Active, Does.Not.Contain(report), "A report should end with the gap.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShortagesReportTheGapThatIsDueNotAFreshDip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var shortages = entMan.System<WFTradeShortageSystem>();

        await server.WaitAssertion(() =>
        {
            shortages.Clear();
            TradeTestWorld.MarketChute(entMan, map);
            var grid = map.Grid.Owner;
            entMan.EnsureComponent<WFTradeStationComponent>(grid).Id = "Zvezda";
            var profile = market.Profile(grid);
            var now = server.Timing.CurTime;

            // Chemical supplies have been short long enough; volatiles sit lower but only just dipped.
            market.SetVendorStock(grid, Chemicals, 20);
            market.Row(grid, Chemicals).ScarceSince = now - profile.ShortageDelay;
            market.SetVendorStock(grid, Volatile, 0);
            market.Row(grid, Volatile).ScarceSince = now;

            var shortage = shortages.Start(new ProtoId<WFTradeShortagePrototype>("WFTradeShortageChemicals"))!;
            Assert.That(shortage.Report, Is.True, "A due gap should be reported even when a fresher dip sits lower.");
            Assert.That(shortage.Grid, Is.EqualTo(grid));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OrdersGoToTheStationFurthestUnderTarget()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var rushes = entMan.System<WFTradeRushSystem>();

        await server.WaitAssertion(() =>
        {
            TradeTestWorld.MarketChute(entMan, map);
            var (low, _) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WF test low");
            var (lowest, _) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 120f), "WF test lowest");
            market.SetVendorStock(low, Medical, 60);
            market.SetVendorStock(lowest, Medical, 20);

            var rush = rushes.Start(new ProtoId<WFTradeRushPrototype>("WFTradeRushMedical"))!;
            Assert.That(rush.Grid, Is.EqualTo(lowest));
            rushes.Clear();
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HiddenChutesStayOffAWorkingStationsBooks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var fence = TradeTestWorld.MarketChute(entMan, map, "CargoChuteHideout");
            var grid = map.Grid.Owner;
            var seller = entMan.SpawnEntity(null, map.GridCoords);
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WF test yard");

            market.Sell(fence, TradeTestWorld.Hauled(entMan, far, map.GridCoords, Medical), seller);
            Assert.That(market.Row(grid, Medical).Stock, Is.GreaterThan(market.Profile(grid).StockTarget), "A market off the books still fills its own shelf.");

            entMan.EnsureComponent<WFTradeStationComponent>(grid).Id = "Zvezda";
            market.SetVendorStock(grid, Medical, 10);
            market.Sell(fence, TradeTestWorld.Hauled(entMan, far, map.GridCoords, Medical), seller);
            Assert.That(market.VendorStock(grid, Medical), Is.EqualTo(10), "A fenced crate should never relieve a shortage.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task IndustriesWorkMaterialsMinersBringForMoreOutput()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var economy = entMan.System<WFTradeEconomySystem>();
        var stock = entMan.System<MarketSystem>();

        await server.WaitAssertion(() =>
        {
            const string ore = "SteelOre1";
            var grid = map.Grid.Owner;
            entMan.EnsureComponent<WFTradeStationComponent>(grid).Id = "CargoDepotAlt";
            var station = entMan.Spawn();
            entMan.EnsureComponent<StationDataComponent>(station);
            entMan.System<StationSystem>().AddGridToStation(station, grid);
            entMan.EnsureComponent<CargoMarketDataComponent>(station);
            var profile = market.Profile(grid);
            var refining = server.ProtoMan.Index<WFTradeIndustryPrototype>("WFTradeIndustryRefining");

            market.SetVendorStock(grid, Scrap, 120);
            market.SetVendorStock(grid, Metals, 0);
            economy.Tick(1f);
            var plain = market.Row(grid, Metals).Stock;
            Assert.That(plain, Is.EqualTo(refining.Rate).Within(0.01f));

            Assert.That(stock.WfDraw(grid, new List<EntProtoId> { ore }, 100f), Is.Zero, "No ore should be no help.");

            Assert.That(stock.WfRestock(grid, 1f, new List<EntProtoId> { ore }), Is.True);
            var mined = TradeTestWorld.Stock(entMan, station, ore);
            market.SetVendorStock(grid, Scrap, 120);
            market.SetVendorStock(grid, Metals, 0);
            economy.Tick(1f);
            Assert.That(market.Row(grid, Metals).Stock, Is.EqualTo(refining.Rate * (1f + profile.MaterialBoost)).Within(0.01f), "Mined ore should raise output.");
            Assert.That(TradeTestWorld.Stock(entMan, station, ore), Is.LessThan(mined), "An industry should work the stock down.");
            entMan.DeleteEntity(station);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ServicesRunOnTheirGoods()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var economy = entMan.System<WFTradeEconomySystem>();

        await server.WaitAssertion(() =>
        {
            var drydock = new ProtoId<WFTradeServicePrototype>("WFTradeServiceDrydock");
            var yard = map.Grid.Owner;
            Assert.That(market.ServicePrice(yard, drydock), Is.EqualTo(1f), "A station outside the economy should charge what it always has.");

            entMan.EnsureComponent<WFTradeStationComponent>(yard).Id = "WFDrydock";
            var profile = market.Profile(yard);
            market.SetVendorStock(yard, ShipComponents, (int) profile.StockTarget);
            Assert.That(market.ServicePrice(yard, drydock), Is.EqualTo(1f));
            market.SetVendorStock(yard, ShipComponents, 0);
            Assert.That(market.ServicePrice(yard, drydock), Is.EqualTo(1f + profile.ScarcityPremium).Within(0.01f), "A yard out of parts should cost more.");

            var halcyon = Station(entMan, map, "TSFMCHalcyon", 200f);
            market.SetVendorStock(halcyon, Ammo, (int) (profile.StockTarget * 0.7f));
            Assert.That(economy.ServiceState(WFTradeEconomySystem.Patrols), Is.EqualTo(1f).Within(0.01f), "A normal sector should run full patrols.");
            market.SetVendorStock(halcyon, Ammo, (int) (profile.StockTarget * 0.3f));
            Assert.That(economy.ServiceState(WFTradeEconomySystem.Patrols), Is.EqualTo(0.5f).Within(0.01f));
            market.SetVendorStock(halcyon, Ammo, 0);
            Assert.That(economy.ServiceState(WFTradeEconomySystem.Patrols), Is.EqualTo(0.25f).Within(0.01f), "Patrols should thin as ammunition runs out, not stop.");
            entMan.DeleteEntity(halcyon);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every good needs a maker that always spawns, every role a real station and every industry a station running it.</summary>
    [Test]
    public async Task EveryGoodIsMadeAtAStationThatAlwaysSpawns()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var protoMan = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var roles = protoMan.EnumeratePrototypes<WFTradeStationPrototype>().ToList();
            var ids = roles.SelectMany(role => role.Stations).ToList();
            Assert.That(ids, Is.Unique, "A station should have one role.");
            Assert.That(ids, Is.SupersetOf(RoundStations));

            var running = roles.SelectMany(role => role.Industries).Select(industry => industry.Id).ToHashSet();
            var made = new HashSet<string>();
            foreach (var industry in protoMan.EnumeratePrototypes<WFTradeIndustryPrototype>())
            {
                Assert.That(running, Does.Contain(industry.ID), $"No station runs {industry.ID}.");
                made.UnionWith(industry.Outputs.Select(good => good.Id));
            }

            var always = roles.Where(role => role.ID is not ("WFTradeStationDepot" or "WFTradeStationDepotAlt"))
                .SelectMany(role => role.Industries)
                .SelectMany(industry => protoMan.Index(industry).Outputs)
                .Select(good => good.Id)
                .ToHashSet();
            Assert.That(always, Is.EquivalentTo(made), "A round whose depots all rolled one way shouldn't lose a good.");

            var groups = protoMan.Index(WFTradeMarketSystem.DefaultProfile).Groups.Values.SelectMany(goods => goods).Select(good => good.Id).ToHashSet();
            groups.ExceptWith(made);
            Assert.That(groups, Is.EquivalentTo(new[] { Fission }), "Fission Fuel should be the only import.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The numbers settle: the opening run has to leave a sector with gaps, and no good dead, and it has to stay that way.</summary>
    [Test]
    public async Task TheSectorOpensSettledAndStaysThere()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var economy = entMan.System<WFTradeEconomySystem>();

        await server.WaitAssertion(() =>
        {
            var offset = 0f;
            var grids = new List<EntityUid>();
            foreach (var id in RoundStations)
            {
                grids.Add(Station(entMan, map, id, offset += 200f));
            }

            economy.Open(8f);
            List<float> Levels()
            {
                return grids.SelectMany(grid => entMan.GetComponent<WFTradeMarketComponent>(grid).Ledger.Keys.Select(good => market.Level(grid, good))).ToList();
            }

            var opened = Levels();
            Assert.That(opened.Average(), Is.InRange(0.4f, 1.1f), "The sector should open neither empty nor glutted.");
            Assert.That(opened.Count(level => level < 0.05f) / (float) opened.Count, Is.LessThan(0.25f), "A settled sector shouldn't open bare.");

            var sector = economy.Sector();
            Assert.That(sector.Select(good => good.Good), Does.Contain(Medical).And.Contain(Fission));
            Assert.That(sector.First(good => good.Good == Medical).Made, Is.GreaterThan(0));
            foreach (var grid in grids)
            {
                entMan.DeleteEntity(grid);
            }
        });

        await pair.CleanReturnAsync();
    }
}
