#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._NF.Market.Components;
using Content.Server._NF.Market.Systems;
using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Station.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// NPC freight in the market: docking at a stop unloads what it is owed, a lost run writes that off, sealed crates pay less
/// at lawful chutes and more at their destination, every load is sealed, and announced runs show as inbound.
/// </summary>
[TestFixture]
[TestOf(typeof(WFTradeFreightSystem))]
public sealed class TradeFreightTest
{
    private const string Fabric = TradeTestWorld.Fabric;
    private const string Fence = TradeTestWorld.Fence;
    private const string Medical = "TradeGoodMedicalSupplies";
    private const string Supplies = "TradeGoodSupplies";
    private const string GeneralManifest = "WFManifestGeneral";
    private const string MedicalManifest = "WFManifestMedical";

    /// <summary>More crates of a good than any hold can bring.</summary>
    private const float Plenty = 1000f;

    /// <summary>A bare freight run calling at some stations, with no ships yet.</summary>
    private static EntityUid Run(IEntityManager entMan, string manifest, params EntityUid[] stops)
    {
        var run = entMan.Spawn();
        var comp = entMan.AddComponent<WFEncounterComponent>(run);
        comp.Manifest = new ProtoId<WFEncounterManifestPrototype>(manifest);
        comp.Stops.AddRange(stops);
        return run;
    }

    /// <summary>Puts a hull, or a simulated ship's stand-in, in a run as its lead ship.</summary>
    private static void Board(IEntityManager entMan, EntityUid run, EntityUid ship)
    {
        var marker = entMan.EnsureComponent<WFEncounterGridComponent>(ship);
        marker.Encounter = run;
        marker.Key = "lead";
        var comp = entMan.GetComponent<WFEncounterComponent>(run);
        comp.Ships["lead"] = new WFEncounterShipState { Grid = ship, Name = TradeTestWorld.Name(entMan, ship) };
        comp.Announcer = "lead";
    }

    /// <summary>Ends a hand-built run and takes its marker off the ship, so nothing polls a run that is gone.</summary>
    private static void Drop(IEntityManager entMan, EntityUid run, EntityUid ship)
    {
        entMan.System<WFEncounterSystem>().Resolve(run, WFEncounterResolution.Ended);
        entMan.DeleteEntity(run);
        if (entMan.EntityExists(ship))
            entMan.RemoveComponent<WFEncounterGridComponent>(ship);
    }

    /// <summary>Spawns a crate in a hull's hold as cargo loaded for its run.</summary>
    private static EntityUid Load(IEntityManager entMan, string good, EntityUid ship, int slot)
    {
        var crate = entMan.SpawnEntity(good, new EntityCoordinates(ship, slot + 0.5f, 0.5f));
        entMan.GetComponent<WFTradeOriginComponent>(crate).Run = TradeTestWorld.Name(entMan, ship);
        return crate;
    }

    /// <summary>Seals a crate as freight bound for a station.</summary>
    private static void Seal(IEntityManager entMan, EntityUid crate, EntityUid destination)
    {
        var origin = entMan.GetComponent<WFTradeOriginComponent>(crate);
        origin.Run = "WF test freighter";
        origin.Destination = destination;
    }

    [Test]
    public async Task ADockRestocksWhatTheFreighterCarries()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var shortages = entMan.System<WFTradeShortageSystem>();
        var stock = entMan.System<MarketSystem>();

        await server.WaitAssertion(() =>
        {
            const string luxuries = "TradeGoodLuxuries";
            shortages.Clear();
            TradeTestWorld.MarketChute(entMan, map);
            var stop = map.Grid.Owner;
            var station = entMan.Spawn();
            entMan.EnsureComponent<StationDataComponent>(station);
            entMan.System<StationSystem>().AddGridToStation(station, stop);
            entMan.EnsureComponent<CargoMarketDataComponent>(station);
            entMan.SpawnEntity("VendingMachineTraderDepot", map.GridCoords);
            market.SetVendorStock(stop, Fabric, 0);
            market.SetVendorStock(stop, luxuries, 0);
            market.Row(stop, Fabric).Inbound = Plenty;
            market.Row(stop, luxuries).Inbound = Plenty;
            shortages.StartLocal(stop, new HashSet<string> { Fabric }, 1.5f, System.TimeSpan.FromMinutes(30), "fabric");
            shortages.StartLocal(stop, new HashSet<string> { luxuries }, 1.5f, System.TimeSpan.FromMinutes(30), "luxuries");

            var ship = TradeTestWorld.Grid(entMan, map, new Vector2(0f, -60f), "WF test freighter");
            var run = Run(entMan, GeneralManifest, stop);
            Board(entMan, run, ship);
            var docked = new WFPilotDockedEvent(EntityUid.Invalid, ship, stop);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref docked);

            var profile = market.Profile(stop);
            var listed = server.ProtoMan.Index<WFEncounterManifestPrototype>(GeneralManifest).Crates.Count(crate => crate.Id == Fabric);
            var restock = listed * profile.FreightCrateStock;
            Assert.That(market.VendorStock(stop, Fabric), Is.EqualTo(restock), "A dock should restock what the freighter carries.");
            Assert.That(market.VendorStock(stop, luxuries), Is.Zero, "A dock shouldn't restock what the freighter doesn't carry.");
            Assert.That(shortages.Active.Any(shortage => shortage.Goods?.Contains(Fabric) == true), Is.False, "A dock should end shortages of what it brings.");
            Assert.That(shortages.Active.Any(shortage => shortage.Goods?.Contains(luxuries) == true), Is.True, "A dock shouldn't end shortages of anything else.");
            Assert.That(TradeTestWorld.Stock(entMan, station, "SheetSteel1"), Is.GreaterThan(0), "A dock should bring its manifest's materials.");
            Assert.That(TradeTestWorld.Stock(entMan, station, "SheetPlasteel1"), Is.Zero, "A dock shouldn't bring materials off its manifest.");

            entMan.EventBus.RaiseEvent(EventSource.Local, ref docked);
            Assert.That(market.VendorStock(stop, Fabric), Is.EqualTo(restock), "A second dock on the same run shouldn't restock it again.");
            shortages.Clear();
            Drop(entMan, run, ship);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AFreighterSharesItsHoldAlongItsRoute()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            const string vendor = "VendingMachineTraderDepotAlt";
            TradeTestWorld.MarketChute(entMan, map);
            var first = map.Grid.Owner;
            var (second, _) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WF test second stop");
            entMan.SpawnEntity(vendor, map.GridCoords);
            entMan.SpawnEntity(vendor, new EntityCoordinates(second, 0.5f, 0.5f));
            market.SetVendorStock(first, Medical, 0);
            market.SetVendorStock(second, Medical, 0);
            market.Row(first, Medical).Inbound = Plenty;
            market.Row(second, Medical).Inbound = Plenty;

            var standIn = entMan.SpawnEntity(null, new MapCoordinates(new Vector2(0f, -300f), map.MapId));
            var run = Run(entMan, MedicalManifest, first, second);
            Board(entMan, run, standIn);

            var listed = server.ProtoMan.Index<WFEncounterManifestPrototype>(MedicalManifest).Crates.Count(crate => crate.Id == Medical);
            var share = (listed + 1) / 2 * market.Profile(first).FreightCrateStock;
            var dock = new WFCrewVirtualDockedEvent(standIn, first);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref dock);
            Assert.That(market.VendorStock(first, Medical), Is.EqualTo(share), "A stop should get its share of the hold, not all of it.");

            var last = new WFCrewVirtualDockedEvent(standIn, second);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref last);
            var rest = (listed - (listed + 1) / 2) * market.Profile(first).FreightCrateStock;
            Assert.That(market.VendorStock(second, Medical), Is.EqualTo(rest), "The last stop should get the rest of the hold.");

            Drop(entMan, run, standIn);
            entMan.DeleteEntity(standIn);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ACrewedFreighterUnloadsRealCrates()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        EntityUid ship = default;
        EntityUid run = default;
        EntityUid stowed = default;
        var crates = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            TradeTestWorld.MarketChute(entMan, map);
            var (second, _) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WF test second stop");
            ship = TradeTestWorld.Grid(entMan, map, new Vector2(0f, -60f), "WF test freighter", 5);
            run = Run(entMan, GeneralManifest, map.Grid.Owner, second);
            Board(entMan, run, ship);
            for (var i = 0; i < 4; i++)
            {
                crates.Add(Load(entMan, Fabric, ship, i));
            }

            stowed = entMan.SpawnEntity(Fabric, new EntityCoordinates(ship, 4.5f, 0.5f));

            entMan.System<WFTradeMarketSystem>().Row(map.Grid.Owner, Fabric).Inbound = Plenty;
            var docked = new WFPilotDockedEvent(EntityUid.Invalid, ship, map.Grid.Owner);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref docked);
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(crates.Count(entMan.EntityExists), Is.EqualTo(2), "A stop should take its share off the hull, leaving the rest to steal.");
            Assert.That(entMan.EntityExists(stowed), "A crate a player put aboard isn't the run's freight.");
            Drop(entMan, run, ship);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AFreighterStrippedOfItsCargoBringsNothing()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            TradeTestWorld.MarketChute(entMan, map);
            var stop = map.Grid.Owner;
            var station = entMan.Spawn();
            entMan.EnsureComponent<StationDataComponent>(station);
            entMan.System<StationSystem>().AddGridToStation(station, stop);
            entMan.EnsureComponent<CargoMarketDataComponent>(station);
            entMan.SpawnEntity("VendingMachineTraderDepot", map.GridCoords);
            market.SetVendorStock(stop, Fabric, 0);
            market.Row(stop, Fabric).Inbound = Plenty;

            var ship = TradeTestWorld.Grid(entMan, map, new Vector2(0f, -60f), "WF test robbed freighter");
            var run = Run(entMan, GeneralManifest, stop);
            Board(entMan, run, ship);
            var crate = entMan.SpawnEntity(Fabric, new EntityCoordinates(ship, 0.5f, 0.5f));
            var loaded = new WFEncounterCargoLoadedEvent(run, ship, crate);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref loaded);
            entMan.DeleteEntity(crate);

            var docked = new WFPilotDockedEvent(EntityUid.Invalid, ship, stop);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref docked);
            Assert.That(market.VendorStock(stop, Fabric), Is.Zero, "An empty hold shouldn't restock anything.");
            Assert.That(TradeTestWorld.Stock(entMan, station, "SheetSteel1"), Is.Zero, "An empty hold shouldn't bring materials.");
            Drop(entMan, run, ship);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADockUnloadsOnlyWhatsOwedAndWhatsAboard()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        EntityUid ship = default, run = default, stop = default;
        var crates = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            TradeTestWorld.MarketChute(entMan, map);
            stop = map.Grid.Owner;
            var (elsewhere, _) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WF test elsewhere");
            var profile = market.Profile(stop);
            market.SetVendorStock(stop, Fabric, 0);
            market.SetVendorStock(elsewhere, Fabric, 0);
            market.SetVendorStock(stop, Supplies, 0);
            market.Row(stop, Fabric).Inbound = 2.5f * profile.FreightCrateStock;
            market.Row(elsewhere, Fabric).Inbound = Plenty;
            market.Row(stop, Supplies).Inbound = Plenty;

            const int aboard = 6;
            ship = TradeTestWorld.Grid(entMan, map, new Vector2(0f, -60f), "WF test freighter", aboard);
            for (var i = 0; i < aboard; i++)
            {
                crates.Add(Load(entMan, Fabric, ship, i));
            }

            run = Run(entMan, GeneralManifest, stop);
            Board(entMan, run, ship);

            var notAStop = new WFPilotDockedEvent(EntityUid.Invalid, ship, elsewhere);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref notAStop);
            Assert.That(market.VendorStock(elsewhere, Fabric), Is.Zero, "A dock at a station off the route supplied it.");

            var docked = new WFPilotDockedEvent(EntityUid.Invalid, ship, stop);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref docked);
            Assert.That(market.Row(stop, Fabric).Stock, Is.EqualTo(2.5f * profile.FreightCrateStock).Within(0.01f), "A dock should stop at what the station is owed.");
            Assert.That(market.Row(stop, Fabric).Inbound, Is.Zero);
            Assert.That(market.PricedLevel(stop, Fabric), Is.EqualTo(market.Level(stop, Fabric)).Within(0.001f), "Freight should move prices at once.");
            Assert.That(market.VendorStock(stop, Supplies), Is.Zero, "A crewed freighter supplies only what is still aboard, not its manifest.");

            // A simulated freighter has no hold to look in; its manifest says what it brings.
            market.SetVendorStock(stop, Medical, 0);
            market.Row(stop, Medical).Inbound = Plenty;
            var standIn = entMan.SpawnEntity(null, new MapCoordinates(new Vector2(0f, -300f), map.MapId));
            var simulated = Run(entMan, MedicalManifest, stop);
            Board(entMan, simulated, standIn);
            var virtualDock = new WFCrewVirtualDockedEvent(standIn, stop);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref virtualDock);

            var listed = protos.Index<WFEncounterManifestPrototype>(MedicalManifest).Crates.Count(crate => crate.Id == Medical);
            Assert.That(market.VendorStock(stop, Medical), Is.EqualTo(listed * profile.FreightCrateStock), "A simulated freighter supplies what its manifest lists.");

            Drop(entMan, simulated, standIn);
            entMan.DeleteEntity(standIn);
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(crates.Count(entMan.EntityExists), Is.EqualTo(3), "Only the crates delivered should leave the hold.");
            Drop(entMan, run, ship);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ABlockadedStopGetsNothingAndKeepsWhatItsOwed()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var sector = entMan.System<WFTradeSectorSystem>();

        await server.WaitAssertion(() =>
        {
            TradeTestWorld.MarketChute(entMan, map);
            var stop = map.Grid.Owner;
            market.SetVendorStock(stop, Medical, 0);
            market.Row(stop, Medical).Inbound = Plenty;

            var raiders = entMan.Spawn();
            var comp = entMan.AddComponent<WFEncounterComponent>(raiders);
            comp.Blockades = true;
            comp.Begun = true;
            comp.Stops.Add(stop);
            Assert.That(sector.IsBlockaded(stop), Is.True);

            var standIn = entMan.SpawnEntity(null, new MapCoordinates(new Vector2(0f, -300f), map.MapId));
            var run = Run(entMan, MedicalManifest, stop);
            Board(entMan, run, standIn);
            var dock = new WFCrewVirtualDockedEvent(standIn, stop);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref dock);

            Assert.That(market.VendorStock(stop, Medical), Is.Zero, "A freighter shouldn't reach a blockaded station.");
            Assert.That(market.Row(stop, Medical).Inbound, Is.EqualTo(Plenty), "What's owed should wait for the blockade to lift.");

            entMan.DeleteEntity(raiders);
            Drop(entMan, run, standIn);
            entMan.DeleteEntity(standIn);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LostRunLeavesItsStopsShort()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var shortages = entMan.System<WFTradeShortageSystem>();
        var encounters = entMan.System<WFEncounterSystem>();

        await server.WaitAssertion(() =>
        {
            TradeTestWorld.MarketChute(entMan, map);
            var reached = map.Grid.Owner;
            var (missed, missedChute) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WF test missed stop");
            var bare = TradeTestWorld.Grid(entMan, map, new Vector2(0f, 120f), "WF test bare stop");
            var (finished, _) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 180f), "WF test finished stop");

            var standIn = entMan.SpawnEntity(null, new MapCoordinates(new Vector2(0f, -300f), map.MapId));
            var run = Run(entMan, MedicalManifest, reached, missed, bare);
            Board(entMan, run, standIn);
            var dock = new WFCrewVirtualDockedEvent(standIn, reached);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref dock);

            market.Row(missed, Medical).Inbound = Plenty;
            encounters.Resolve(run, WFEncounterResolution.Destroyed);
            Assert.That(market.Row(missed, Medical).Inbound, Is.LessThan(Plenty), "A lost run should write off what it was bringing.");
            Assert.That(shortages.Active.Any(shortage => shortage.Grid == missed && shortage.Goods?.Contains(Medical) == true), Is.True,
                "A stop the lost run never reached should be short of its goods.");
            Assert.That(shortages.Active.Any(shortage => shortage.Grid == reached), Is.False, "A stop the run reached went short.");
            Assert.That(shortages.Active.Any(shortage => shortage.Grid == bare), Is.False, "A stop with no chute for the goods went short.");

            var quote = market.Quote(missedChute, Medical)!.Value;
            Assert.That(quote.Reason, Is.EqualTo((LocId) "wf-trade-reason-short"));
            Assert.That(quote.Factor, Is.GreaterThan(1f));

            // A run that arrives leaves no station short.
            var completed = Run(entMan, MedicalManifest, finished);
            encounters.Resolve(completed, WFEncounterResolution.Completed);
            Assert.That(shortages.Active.Any(shortage => shortage.Grid == finished), Is.False, "A completed run left its stop short.");

            Drop(entMan, run, standIn);
            entMan.DeleteEntity(completed);
            entMan.DeleteEntity(standIn);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SealedFreightQuotes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var chute = TradeTestWorld.MarketChute(entMan, map);
            var station = map.Grid.Owner;
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WF test far yard");
            var near = TradeTestWorld.Grid(entMan, map, new Vector2(30f, 0f), "WF test near yard");
            var (_, fence) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WF test fence", Fence);
            var profile = market.Profile(station);
            var basePrice = market.Quote(chute, Fabric)!.Value.Base;

            // Bound for another station: a lawful chute pays the sealed share.
            var sealedCrate = TradeTestWorld.Hauled(entMan, far, map.GridCoords);
            Seal(entMan, sealedCrate, near);
            var lawful = market.Quote(chute, Fabric, sealedCrate)!.Value;
            Assert.That(lawful.Reason, Is.EqualTo((LocId) "wf-trade-reason-sealed"));
            Assert.That(lawful.Amount, Is.EqualTo((int) System.MathF.Round(basePrice * profile.HotCargoLawful)).Within(1));

            // The fence pays full price for sealed freight.
            var fenced = market.Quote(fence, Fabric, sealedCrate)!.Value;
            Assert.That(fenced.Amount, Is.EqualTo(fenced.Base), $"{Fence} should pay full price for sealed freight.");
            Assert.That(fenced.Reason, Is.Not.EqualTo((LocId) "wf-trade-reason-sealed"));

            // Hauled to its own destination: the salvage bonus.
            var bound = TradeTestWorld.Hauled(entMan, far, map.GridCoords);
            Seal(entMan, bound, station);
            var salvage = market.Quote(chute, Fabric, bound)!.Value;
            Assert.That(salvage.Reason, Is.EqualTo((LocId) "wf-trade-reason-salvage"));
            Assert.That(salvage.Amount,
                Is.EqualTo((int) System.MathF.Round(basePrice * System.MathF.Min(profile.SalvageBonus, profile.ModifierCap))).Within(1));

            // At its destination but never hauled there: no bonus, and no seal either.
            var unhauled = TradeTestWorld.Hauled(entMan, near, map.GridCoords);
            Seal(entMan, unhauled, station);
            Assert.That(market.Quote(chute, Fabric, unhauled)!.Value.Amount, Is.EqualTo(basePrice));
        });

        await pair.CleanReturnAsync();
    }

    /// <remarks>
    /// The full despawn-and-rebuild of a simulated freighter needs a crewed vessel spawn (the InteractionTest fixture in
    /// WFEncounterTest); a rebuild loads its hold through the same cargo-loaded event, which is what this drives.
    /// </remarks>
    [Test]
    public async Task EveryLoadIsSealed()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            TradeTestWorld.MarketChute(entMan, map);
            var first = map.Grid.Owner;
            var (second, _) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WF test second stop");
            var stops = new[] { first, second };
            var ship = TradeTestWorld.Grid(entMan, map, new Vector2(0f, -60f), "WF test hauler", 2);
            var run = Run(entMan, GeneralManifest, stops);
            Board(entMan, run, ship);

            var crate = entMan.SpawnEntity(Fabric, new EntityCoordinates(ship, 0.5f, 0.5f));
            var loaded = new WFEncounterCargoLoadedEvent(run, ship, crate);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref loaded);
            var origin = entMan.GetComponent<WFTradeOriginComponent>(crate);
            Assert.That(origin.Run, Is.EqualTo("WF test hauler"));
            Assert.That(stops, Does.Contain(origin.Destination));

            // The hull rebuilt from the simulation, same name and run: its fresh hold is sealed the same way.
            var rebuilt = TradeTestWorld.Grid(entMan, map, new Vector2(0f, -120f), "WF test hauler", 2);
            var reloaded = entMan.SpawnEntity(Fabric, new EntityCoordinates(rebuilt, 0.5f, 0.5f));
            var again = new WFEncounterCargoLoadedEvent(run, rebuilt, reloaded);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref again);
            var reloadedOrigin = entMan.GetComponent<WFTradeOriginComponent>(reloaded);
            Assert.That(reloadedOrigin.Run, Is.EqualTo(origin.Run));
            Assert.That(stops, Does.Contain(reloadedOrigin.Destination));

            // A run with no route seals nothing.
            var nowhere = Run(entMan, GeneralManifest);
            var loose = entMan.SpawnEntity(Fabric, new EntityCoordinates(ship, 1.5f, 0.5f));
            var unrouted = new WFEncounterCargoLoadedEvent(nowhere, ship, loose);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref unrouted);
            Assert.That(entMan.GetComponent<WFTradeOriginComponent>(loose).Run, Is.Null);

            Drop(entMan, run, ship);
            entMan.DeleteEntity(nowhere);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnnouncedRunsShowAsInbound()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var freight = entMan.System<WFTradeFreightSystem>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var loc = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            TradeTestWorld.MarketChute(entMan, map);
            var first = map.Grid.Owner;
            var (second, _) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WF test second stop");
            var ship = TradeTestWorld.Grid(entMan, map, new Vector2(0f, -60f), "WF test inbound freighter");
            var run = Run(entMan, MedicalManifest, first, second);
            Board(entMan, run, ship);
            var comp = entMan.GetComponent<WFEncounterComponent>(run);
            var goods = loc.GetString(protos.Index<WFEncounterManifestPrototype>(MedicalManifest).Name.Id);

            Assert.That(freight.Inbound().Any(entry => entry.Ship == "WF test inbound freighter"), Is.False,
                "A run that has not begun is not inbound yet.");

            comp.Begun = true;
            Assert.That(freight.Inbound(), Does.Contain(("WF test inbound freighter", TradeTestWorld.Name(entMan, first), goods)));

            var dock = new WFCrewVirtualDockedEvent(ship, first);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref dock);
            Assert.That(freight.Inbound(), Does.Contain(("WF test inbound freighter", TradeTestWorld.Name(entMan, second), goods)),
                "After its first stop a run is inbound to the next.");

            // Its radio call is still to come: players haven't heard of it yet.
            comp.Announcement = "wf-test-pending";
            Assert.That(freight.Inbound().Any(entry => entry.Ship == "WF test inbound freighter"), Is.False);

            Drop(entMan, run, ship);
        });

        await pair.CleanReturnAsync();
    }
}
