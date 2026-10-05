#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.CCVar;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._WF.Planets;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>The shuttle console's enter and leave orbit actions, which need no FTL drive.</summary>
[TestFixture]
[TestOf(typeof(WFOrbitEntrySystem))]
public sealed class OrbitEntryTest
{
    /// <summary>The shuttle console prototype; the orbit action is a message on its own BUI key.</summary>
    private const string ConsoleProto = "ComputerShuttle";

    /// <summary>WFSurfaceAsclepiu's orbitRange, which is the band the console action is gated on.</summary>
    private const float OrbitRange = 2000f;

    /// <summary>Seconds to wait for a ten second hop before giving up.</summary>
    private const float HopTimeout = 20f;

    /// <summary>The star system stamped on the test sector map, so hops carry the approach mark.</summary>
    private const string SystemProto = "SystemKyphrus";

    /// <summary>The docking airlock fitted to the hull for a tender to dock with.</summary>
    private const string DockProto = "AirlockShuttle";

    /// <summary>Where the hull and its docked tender stood, tick by tick, as the hop ended.</summary>
    private sealed class HopTrace
    {
        public bool Arrived;
        public bool CameFromFtlMap;
        public TimeSpan End;
        public TimeSpan ArrivalTime;
        public bool HullMarkedOnLastFtlTick;
        public bool TenderMarkedOnLastFtlTick;
        public bool HullMarkedOnArrival;
        public bool TenderMarkedOnArrival;
        public bool TenderArrived;
        public bool LeftBehindMarked;
    }

    /// <summary>A sector body with its stack, plus a driveless hull carrying one shuttle console.</summary>
    private sealed class Site
    {
        public List<EntityUid> Layers = new();
        public EntityUid Body;
        public EntityUid SectorMap;
        public EntityUid Hull;
        public EntityUid Console;

        public EntityUid Air => Layers[1];
        public EntityUid Orbit => Layers[^1];
    }

    /// <summary>Out of range is refused, and the console offers nothing to press.</summary>
    [Test]
    public async Task RefusedOutOfRange()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var orbits = server.System<WFOrbitEntrySystem>();

        var site = await BuildSite(pair);
        await MoveTo(pair, site.Hull, site.SectorMap, new Vector2(OrbitRange + 500f, 0f));
        await Sweep(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.False,
                    "A hull well outside the orbit range entered orbit anyway.");
                Assert.That(reason,
                    Is.EqualTo(loc.GetString("wf-orbit-out-of-range", ("planet", Name(entMan, site.Body)))),
                    "Out of range was refused for some other reason.");
                Assert.That(entMan.GetComponent<TransformComponent>(site.Hull).MapUid, Is.EqualTo(site.SectorMap),
                    "The refused hull moved anyway.");
                Assert.That(OrbitTarget(entMan, site.Console)?.Planet, Is.Null,
                    "The console still offers a planet to a hull outside the range band.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>A hull on another map entirely is refused, even parked at the body's own coordinates.</summary>
    [Test]
    public async Task RefusedFromAnotherMap()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var orbits = server.System<WFOrbitEntrySystem>();

        var site = await BuildSite(pair);
        var elsewhere = await pair.CreateTestMap();

        await MoveTo(pair, site.Hull, elsewhere.MapUid, Vector2.Zero);
        await Sweep(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.False,
                    "A hull on a different map entered orbit.");
                Assert.That(reason,
                    Is.EqualTo(loc.GetString("wf-orbit-not-in-sector", ("planet", Name(entMan, site.Body)))),
                    "Being off the sector map was refused for some other reason.");
                Assert.That(OrbitTarget(entMan, site.Console)?.Planet, Is.Null,
                    "The console offers a planet that is not even in this system.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>In range and with no FTL drive aboard, the hull and its docked tender reach the orbit layer.</summary>
    [Test]
    public async Task EntersOrbitWithoutAnFTLDrive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var orbits = server.System<WFOrbitEntrySystem>();
        var shuttles = server.System<ShuttleSystem>();

        var site = await BuildSite(pair);
        await MoveTo(pair, site.Hull, site.SectorMap, new Vector2(400f, 0f));
        var tender = await DockTender(pair, site);
        await Sweep(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(shuttles.TryGetFTLDrive(site.Hull, out _, out _), Is.False,
                    "Precondition: the hull carries no FTL drive.");
                Assert.That(shuttles.GetFTLRange(site.Hull), Is.EqualTo(0f),
                    "Precondition: a driveless hull has no FTL range at all, which is why the orbit button exists.");
            }

            var target = OrbitTarget(entMan, site.Console);
            Assert.That(target, Is.Not.Null, "The console offers nothing to a hull parked in range.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(target!.Planet, Is.EqualTo(entMan.GetNetEntity(site.Body)), "The console names the wrong body.");
                Assert.That(target.PlanetName, Is.EqualTo(Name(entMan, site.Body)), "The button could not label itself.");
                Assert.That(target.InOrbit, Is.False, "A hull on the sector map is not in orbit.");
            }
        });

        await server.WaitAssertion(() =>
            Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.True,
                $"A hull 400 from the body was refused orbit: {reason}"));

        var trace = await TraceHop(pair, site.Hull, tender, site.Orbit);

        await server.WaitAssertion(() =>
            Assert.That(trace.Arrived, Is.True,
                $"The hull never reached the orbit layer; it ended on {entMan.ToPrettyString(entMan.GetComponent<TransformComponent>(site.Hull).MapUid)}."));

        AssertApproachHeld(trace);

        // Make sure the arrival doesn't drop straight back through the stack.
        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<TransformComponent>(site.Hull).MapUid, Is.EqualTo(site.Orbit),
                "The hull arrived in orbit and then left the layer again."));

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>Crew on the deck ride the hop unharmed, without being thrown or dropped a level.</summary>
    [Test]
    public async Task CrewRideTheHopIntoOrbitUnharmed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var orbits = server.System<WFOrbitEntrySystem>();

        var site = await BuildSite(pair);
        await Sweep(pair);

        var mob = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            // Real deck, since the hop tosses anyone on a spaced tile.
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            var hullGrid = entMan.GetComponent<MapGridComponent>(site.Hull);
            var deck = new List<(Vector2i, Tile)>();

            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 5; y++)
            {
                deck.Add((new Vector2i(x, y), new Tile(plating)));
            }

            server.System<SharedMapSystem>().SetTiles(site.Hull, hullGrid, deck);

            mob = entMan.SpawnEntity("MobHuman", entMan.GetComponent<TransformComponent>(site.Console).Coordinates);

            // A throw made off-planet must not leave an unspent arc that launches the crew on arrival.
            server.System<Content.Shared.Throwing.ThrowingSystem>().TryThrow(mob, new Vector2(0.4f, 0f), 0.8f);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));

        await server.WaitPost(() =>
        {
            Assert.That(entMan.GetComponent<Content.Shared._CE.ZLevels.Core.Components.CEZPhysicsComponent>(mob).Velocity, Is.EqualTo(0f),
                "A throw off the z-network left vertical velocity on the body.");

            // The hull has no gravity, so put them back at the console.
            server.System<SharedTransformSystem>().SetCoordinates(mob, entMan.GetComponent<TransformComponent>(site.Console).Coordinates);
            server.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetLinearVelocity(mob, Vector2.Zero);

            Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.True, $"Refused orbit: {reason}");
        });

        var worstHeight = 0f;
        var trail = $"hull={site.Hull} orbit={site.Orbit}";

        // The whole hop and six seconds after it: spool-up, tunnel, arrival.
        for (var i = 0; i < 70; i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(0.25f));
            await server.WaitPost(() =>
            {
                if (!entMan.EntityExists(mob))
                {
                    trail += $" [{i}: deleted]";
                    return;
                }

                var z = entMan.GetComponent<Content.Shared._CE.ZLevels.Core.Components.CEZPhysicsComponent>(mob);
                var x = entMan.GetComponent<TransformComponent>(mob);
                worstHeight = MathF.Max(worstHeight, MathF.Abs(z.LocalPosition));
                trail += $" [{i}: map={x.MapUid} grid={x.GridUid} h={z.LocalPosition:F2} v={z.Velocity:F2} g={z.CachedGroundHeight:F2}]";
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(mob), Is.True, $"The crew member was deleted. {trail}");
            var xform = entMan.GetComponent<TransformComponent>(mob);
            var z = entMan.GetComponent<Content.Shared._CE.ZLevels.Core.Components.CEZPhysicsComponent>(mob);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.MapUid, Is.EqualTo(site.Orbit), $"The crew member did not end on the orbit layer. {trail}");
                Assert.That(xform.GridUid, Is.EqualTo(site.Hull), "The crew member is no longer aboard.");
                Assert.That(worstHeight, Is.LessThan(0.2f), $"The crew member left the deck: height {worstHeight}. {trail}");
                // Damage can't be checked: the airless hull deals blunt barotrauma.
                Assert.That(z.Velocity, Is.EqualTo(0f).Within(0.01f), $"The crew member is still moving vertically. {trail}");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>Leave orbit is the same hop in reverse: back onto the sector map the body sits on, tender and all.</summary>
    [Test]
    public async Task LeavesOrbitForTheSectorMap()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var orbits = server.System<WFOrbitEntrySystem>();

        var site = await BuildSite(pair);
        await MoveTo(pair, site.Hull, site.Orbit, new Vector2(60f, 0f));
        var tender = await DockTender(pair, site);
        await Sweep(pair);

        await server.WaitAssertion(() =>
        {
            var target = OrbitTarget(entMan, site.Console);
            Assert.That(target, Is.Not.Null, "The console offers nothing to a hull parked in orbit.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(target!.InOrbit, Is.True, "The console does not know the hull is in orbit.");
                Assert.That(target.PlanetName, Is.EqualTo(Name(entMan, site.Body)), "The button names the wrong world.");
            }
        });

        await server.WaitAssertion(() =>
            Assert.That(orbits.TryLeaveOrbit(site.Console, out var reason), Is.True,
                $"A hull in orbit was refused the climb out: {reason}"));

        var trace = await TraceHop(pair, site.Hull, tender, site.SectorMap);

        await server.WaitAssertion(() =>
            Assert.That(trace.Arrived, Is.True,
                $"The hull never returned to the sector map; it ended on {entMan.ToPrettyString(entMan.GetComponent<TransformComponent>(site.Hull).MapUid)}."));

        AssertApproachHeld(trace);

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>The mark follows the grids that actually ride the hop, not the ones docked when it was requested.</summary>
    [Test]
    public async Task DockChangesDuringSpoolUpFollowTheHop()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var orbits = server.System<WFOrbitEntrySystem>();
        var docking = server.System<DockingSystem>();
        var transform = server.System<SharedTransformSystem>();

        var site = await BuildSite(pair);
        await MoveTo(pair, site.Hull, site.SectorMap, new Vector2(400f, 0f));
        var leaving = await DockTender(pair, site);
        await Sweep(pair);

        await server.WaitAssertion(() =>
            Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.True, $"Refused orbit: {reason}"));

        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));

        // One tender casts off during the spool-up and parks outside the well; another takes its port.
        await server.WaitPost(() =>
        {
            foreach (var uid in Children(entMan, site.Hull))
            {
                if (entMan.TryGetComponent(uid, out DockingComponent? dock) && dock.DockedWith != null)
                    docking.Undock((uid, dock));
            }

            transform.SetCoordinates(leaving, new EntityCoordinates(site.SectorMap, new Vector2(OrbitRange + 1500f, 0f)));
        });

        var joining = await DockTender(pair, site);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<FTLComponent>(site.Hull).State, Is.EqualTo(FTLState.Starting),
                "Precondition: the dock changes land inside the hull's spool-up."));

        var trace = await TraceHop(pair, site.Hull, joining, site.Orbit, leaving);

        await server.WaitAssertion(() =>
        {
            Assert.That(trace.Arrived, Is.True, "The hull never reached the orbit layer.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(trace.LeftBehindMarked, Is.False,
                    "The tender that cast off during the spool-up kept the hull's mark, so its own next jump would show this planet.");
                Assert.That(entMan.GetComponent<TransformComponent>(leaving).MapUid, Is.EqualTo(site.SectorMap),
                    "The tender that cast off rode the hop anyway.");
            }
        });

        AssertApproachHeld(trace);

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>An arrival phase longer than the hop's travel leg still keeps the mark until the hull lands.</summary>
    [Test]
    public async Task ApproachOutlastsALongArrivalPhase()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var orbits = server.System<WFOrbitEntrySystem>();

        // Arrival comes at the startup plus the longer of the travel time and this; TestPair reverts it.
        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.FTLArrivalTime, ShuttleSystem.WfOrbitTravelTime + 3f));

        var site = await BuildSite(pair);
        await MoveTo(pair, site.Hull, site.SectorMap, new Vector2(400f, 0f));
        await Sweep(pair);

        await server.WaitAssertion(() =>
            Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.True, $"Refused orbit: {reason}"));

        var trace = await TraceHop(pair, site.Hull, null, site.Orbit);

        await server.WaitAssertion(() =>
        {
            Assert.That(trace.Arrived, Is.True, "The hull never reached the orbit layer.");
            Assert.That(trace.ArrivalTime - trace.End, Is.GreaterThan(TimeSpan.FromSeconds(2)),
                "Precondition: the hull stays on the FTL map past two one-second sweeps after the approach's end time.");
        });

        AssertApproachHeld(trace, tender: false);

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>Leaving orbit is refused outright from anywhere that is not an orbit layer.</summary>
    [Test]
    public async Task LeaveOrbitRefusedOffTheOrbitLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var orbits = server.System<WFOrbitEntrySystem>();

        var site = await BuildSite(pair);
        await MoveTo(pair, site.Hull, site.SectorMap, new Vector2(400f, 0f));
        await Sweep(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(orbits.TryLeaveOrbit(site.Console, out var reason), Is.False,
                    "A hull on the sector map left an orbit it was never in.");
                Assert.That(reason, Is.EqualTo(loc.GetString("wf-orbit-not-in-orbit")),
                    "Leaving orbit off an orbit layer was refused for some other reason.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>Orbit is not an FTL destination; an air-layer hull still cannot jump out, an orbit one can.</summary>
    [Test]
    public async Task OrbitIsNotADestinationAndAirLayersStillCannotFTL()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var shuttles = server.System<ShuttleSystem>();

        var site = await BuildSite(pair);

        var orbitMapId = entMan.GetComponent<MapComponent>(site.Orbit).MapId;
        var sectorMapId = entMan.GetComponent<MapComponent>(site.SectorMap).MapId;

        // The sector map is an ordinary open destination.
        await server.WaitPost(() => shuttles.TryAddFTLDestination(sectorMapId, true, false, false, out _));

        await MoveTo(pair, site.Hull, site.SectorMap, new Vector2(400f, 0f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<FTLDestinationComponent>(site.Orbit), Is.False,
                    "The orbit layer is still registered as an FTL destination.");
                Assert.That(shuttles.CanFTLTo(site.Hull, orbitMapId, site.Console), Is.False,
                    "The orbit layer is still offered as an FTL destination.");
            }
        });

        await MoveTo(pair, site.Hull, site.Air, Vector2.Zero);

        await server.WaitAssertion(() =>
            Assert.That(shuttles.CanFTLTo(site.Hull, sectorMapId, site.Console), Is.False,
                "A hull on an air layer must not be able to FTL off the planet; it climbs to orbit first."));

        await MoveTo(pair, site.Hull, site.Orbit, Vector2.Zero);

        await server.WaitAssertion(() =>
            Assert.That(shuttles.CanFTLTo(site.Hull, sectorMapId, site.Console), Is.True,
                "FTL out of orbit to elsewhere in the sector is the one jump off a planet that still works."));

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>An owned Asclepiu stack plus a driveless 5x5 hull carrying one shuttle console.</summary>
    private static async Task<Site> BuildSite(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapMan = server.ResolveDependency<IMapManager>();
        var maps = server.System<SharedMapSystem>();
        var receiver = server.System<SharedPowerReceiverSystem>();
        var shuttles = server.System<ShuttleSystem>();

        await EnableFeature(pair);

        var stack = await BuildOwnedStack(pair);
        var site = new Site { Layers = stack.Layers, Body = stack.Body, SectorMap = stack.BodyMap };

        await server.WaitPost(() =>
        {
            // A real sector map carries its star system, which is what the approach mark names.
            entMan.EnsureComponent<StarSystemMapComponent>(site.SectorMap).System = SystemProto;

            var grid = mapMan.CreateGridEntity(entMan.GetComponent<MapComponent>(site.SectorMap).MapId);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();

            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 5; y++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            }

            maps.SetTiles(grid.Owner, grid.Comp, tiles);
            entMan.EnsureComponent<ShuttleComponent>(grid.Owner);
            shuttles.Enable(grid.Owner, force: true);
            site.Hull = grid.Owner;

            // No cabling on a code-built hull, and no FTL drive.
            site.Console = entMan.SpawnEntity(ConsoleProto, new EntityCoordinates(grid.Owner, new Vector2(2.5f, 2.5f)));
            receiver.SetNeedsPower(site.Console, false);
        });

        await MapInitHull(pair, site.Hull);
        await server.WaitRunTicks(2);
        return site;
    }

    /// <summary>Parks the hull on a map at a world offset, the way an arrival would leave it.</summary>
    private static async Task MoveTo(TestPair pair, EntityUid hull, EntityUid map, Vector2 position)
    {
        var server = pair.Server;
        var transform = server.System<SharedTransformSystem>();

        await server.WaitPost(() => transform.SetCoordinates(hull, new EntityCoordinates(map, position)));
        await server.WaitRunTicks(1);
    }

    /// <summary>Runs past the orbit system's one-second sweep that fills the console component.</summary>
    private static Task Sweep(TestPair pair)
    {
        return pair.Server.WaitRunTicks(pair.SecondsToTicks(1.2f));
    }

    /// <summary>The console's current offer, or null when it has none.</summary>
    private static WFConsoleOrbitTargetComponent? OrbitTarget(IEntityManager entMan, EntityUid console)
    {
        return entMan.TryGetComponent(console, out WFConsoleOrbitTargetComponent? comp) ? comp : null;
    }

    /// <summary>The body's display name, which is what the button label substitutes.</summary>
    private static string Name(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<MetaDataComponent>(uid).EntityName;
    }

    /// <summary>Docks a lander face to face with the port on the hull's north edge, fitting one first if there is none.</summary>
    private static async Task<EntityUid> DockTender(TestPair pair, Site site)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var hullDock = EntityUid.Invalid;
        var mapId = MapId.Nullspace;
        var hullPos = Vector2.Zero;

        await server.WaitPost(() =>
        {
            foreach (var uid in Children(entMan, site.Hull))
            {
                if (entMan.TryGetComponent(uid, out DockingComponent? dock) && dock.DockedWith == null)
                    hullDock = uid;
            }

            if (hullDock == EntityUid.Invalid)
            {
                hullDock = entMan.SpawnEntity(DockProto, new EntityCoordinates(site.Hull, new Vector2(2.5f, 4.5f)));
                transform.SetLocalRotation(hullDock, Angle.FromDegrees(180));
                server.System<SharedPowerReceiverSystem>().SetNeedsPower(hullDock, false);
            }

            mapId = entMan.GetComponent<TransformComponent>(site.Hull).MapID;
            hullPos = transform.GetWorldPosition(site.Hull);
        });

        // The lander's port is on its tile (3, 0) facing south; this offset puts the two ports face to face.
        var tender = await BuildLander(pair, mapId, hullPos + new Vector2(-1f, 5f));

        await server.WaitPost(() =>
        {
            var tenderDock = EntityUid.Invalid;

            foreach (var uid in Children(entMan, tender))
            {
                if (entMan.HasComponent<DockingComponent>(uid))
                    tenderDock = uid;
            }

            Assert.That(tenderDock, Is.Not.EqualTo(EntityUid.Invalid), "The lander has no docking port.");

            server.System<DockingSystem>().Dock(
                (hullDock, entMan.GetComponent<DockingComponent>(hullDock)),
                (tenderDock, entMan.GetComponent<DockingComponent>(tenderDock)));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));

        await server.WaitAssertion(() =>
        {
            var docked = new HashSet<EntityUid>();
            server.System<ShuttleSystem>().GetAllDockedShuttles(site.Hull, docked);
            Assert.That(docked, Does.Contain(tender), "Precondition: the tender is docked to the hull.");
        });

        return tender;
    }

    /// <summary>Ticks one at a time until the hull lands on the map, noting the approach marks either side of the arrival.</summary>
    private static async Task<HopTrace> TraceHop(TestPair pair, EntityUid hull, EntityUid? tender, EntityUid map, EntityUid? leftBehind = null)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var trace = new HopTrace();
        var ticks = pair.SecondsToTicks(HopTimeout);
        var onFtl = false;
        var hullMarked = false;
        var tenderMarked = false;

        await server.WaitPost(() =>
        {
            Assert.That(entMan.TryGetComponent(hull, out WFPlanetApproachComponent? approach), Is.True,
                "The hop left no approach mark on the hull.");
            trace.End = approach!.End;
        });

        for (var i = 0; i < ticks && !trace.Arrived; i++)
        {
            await server.WaitRunTicks(1);

            await server.WaitPost(() =>
            {
                var hullMap = entMan.GetComponent<TransformComponent>(hull).MapUid;
                var hullMark = entMan.HasComponent<WFPlanetApproachComponent>(hull);
                var tenderMark = tender is { } docked && entMan.HasComponent<WFPlanetApproachComponent>(docked);

                if (leftBehind is { } other && entMan.HasComponent<WFPlanetApproachComponent>(other))
                    trace.LeftBehindMarked = true;

                if (hullMap == map)
                {
                    trace.Arrived = true;
                    trace.ArrivalTime = timing.CurTime;
                    trace.CameFromFtlMap = onFtl;
                    trace.HullMarkedOnLastFtlTick = hullMarked;
                    trace.TenderMarkedOnLastFtlTick = tenderMarked;
                    trace.HullMarkedOnArrival = hullMark;
                    trace.TenderMarkedOnArrival = tenderMark;
                    trace.TenderArrived = tender is { } riding && entMan.GetComponent<TransformComponent>(riding).MapUid == map;
                    return;
                }

                onFtl = hullMap is { } mapUid && entMan.HasComponent<FTLMapComponent>(mapUid);
                hullMarked = hullMark;
                tenderMarked = tenderMark;
            });
        }

        return trace;
    }

    /// <summary>The hull outlasts the approach's end time on the FTL map, and the marks bridge exactly that gap.</summary>
    private static void AssertApproachHeld(HopTrace trace, bool tender = true)
    {
        TestContext.Out.WriteLine($"The hull left the FTL map {(trace.ArrivalTime - trace.End).TotalMilliseconds:F0} ms after the approach's end time.");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(trace.CameFromFtlMap, Is.True, "The hop never passed through the FTL map.");
            Assert.That(trace.ArrivalTime, Is.GreaterThan(trace.End),
                "Precondition: the hull leaves the FTL map after the approach's end time, which is the gap clients must hold.");
            Assert.That(trace.HullMarkedOnLastFtlTick, Is.True,
                "The hull lost its approach mark while still on the FTL map, so its crew would see the FTL starfield.");
            Assert.That(trace.HullMarkedOnArrival, Is.False,
                "The hull still carries its approach mark after arriving, which a following jump could inherit.");
        }

        if (!tender)
            return;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(trace.TenderArrived, Is.True, "The docked tender did not arrive with the hull.");
            Assert.That(trace.TenderMarkedOnLastFtlTick, Is.True,
                "The docked tender carried no approach mark on the FTL map, so its crew would see the tunnel.");
            Assert.That(trace.TenderMarkedOnArrival, Is.False,
                "The docked tender still carries its approach mark after arriving.");
        }
    }

    /// <summary>Tears the stack down through its own network entity and lets any transit map settle.</summary>
    private static async Task Teardown(TestPair pair, Site site)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var networks = server.System<WFPlanetNetworkSystem>();

        await server.WaitPost(() =>
        {
            if (entMan.TryGetComponent(site.Body, out WFSectorPlanetComponent? sector)
                && sector.Network is { } network
                && entMan.TryGetEntity(network, out var networkUid))
            {
                networks.DeleteNetwork(networkUid.Value);
            }
        });

        await server.WaitRunTicks(5);
    }
}
