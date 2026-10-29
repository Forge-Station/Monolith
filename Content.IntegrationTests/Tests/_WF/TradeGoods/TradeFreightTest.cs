#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.TradeGoods.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.TradeGoods;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// NPC freight in the market: docking at a stop supplies it, a lost run leaves its stops short, sealed crates pay less
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

    /// <summary>Most pressure one freighter's dock adds to one good (WFTradeFreightSystem.SupplyCap).</summary>
    private const float SupplyCap = 0.3f;

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

    /// <summary>Seals a crate as freight bound for a station.</summary>
    private static void Seal(IEntityManager entMan, EntityUid crate, EntityUid destination)
    {
        var origin = entMan.GetComponent<WFTradeOriginComponent>(crate);
        origin.Run = "WF test freighter";
        origin.Destination = destination;
    }

    [Test]
    public async Task DockingAtAStopSuppliesIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var chute = TradeTestWorld.MarketChute(entMan, map);
            var stop = map.Grid.Owner;
            var (elsewhere, _) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WF test elsewhere");
            var profile = market.Profile(stop);

            // A crewed freighter with six crates of fabric aboard: enough supply to show as the reason.
            const int crates = 6;
            var ship = TradeTestWorld.Grid(entMan, map, new Vector2(0f, -60f), "WF test freighter", crates);
            for (var i = 0; i < crates; i++)
            {
                entMan.SpawnEntity(Fabric, new EntityCoordinates(ship, i + 0.5f, 0.5f));
            }

            var run = Run(entMan, GeneralManifest, stop);
            Board(entMan, run, ship);

            var notAStop = new WFPilotDockedEvent(EntityUid.Invalid, ship, elsewhere);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref notAStop);
            Assert.That(market.Pressure(elsewhere, Fabric), Is.Zero, "A dock at a station off the route supplied it.");

            var docked = new WFPilotDockedEvent(EntityUid.Invalid, ship, stop);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref docked);
            var supplied = System.Math.Min(SupplyCap, crates * profile.PerSale);
            Assert.That(market.Pressure(stop, Fabric), Is.EqualTo(supplied).Within(0.001f), "A crewed freighter supplies what is aboard.");
            Assert.That(market.Pressure(stop, Supplies), Is.Zero, "A crewed freighter supplies only what is still aboard, not its manifest.");
            Assert.That(market.Quote(chute, Fabric)!.Value.Reason, Is.EqualTo((LocId) "wf-trade-reason-supplied"));

            entMan.EventBus.RaiseEvent(EventSource.Local, ref docked);
            Assert.That(market.Pressure(stop, Fabric), Is.EqualTo(supplied).Within(0.001f), "A second dock at the same stop supplied it again.");

            // A simulated freighter has no hold to look in; its manifest says what it brings.
            var standIn = entMan.SpawnEntity(null, new MapCoordinates(new Vector2(0f, -300f), map.MapId));
            var simulated = Run(entMan, MedicalManifest, stop);
            Board(entMan, simulated, standIn);
            var virtualDock = new WFCrewVirtualDockedEvent(standIn, stop);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref virtualDock);

            var listed = protos.Index<WFEncounterManifestPrototype>(MedicalManifest).Crates.Count(crate => crate.Id == Medical);
            Assert.That(market.Pressure(stop, Medical), Is.EqualTo(System.Math.Min(SupplyCap, listed * profile.PerSale)).Within(0.001f),
                "A simulated freighter supplies what its manifest lists.");

            Drop(entMan, run, ship);
            Drop(entMan, simulated, standIn);
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

            encounters.Resolve(run, WFEncounterResolution.Destroyed);
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
