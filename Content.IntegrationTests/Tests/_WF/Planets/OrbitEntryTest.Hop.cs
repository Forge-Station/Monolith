#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Hop;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.FtlEffects;
using Content.Shared._WF.Planets;
using Content.Shared.CCVar;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Timing;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>The hop is its own state: it never uses FTL, whatever the drive is doing.</summary>
public sealed partial class OrbitEntryTest
{
    /// <summary>Hull and tender never carry an FTLComponent, and nothing is left behind once the hop ends.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task HopStartsNoCooldownAndLeavesNoStateBehind(bool leaving)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var orbits = server.System<WFOrbitEntrySystem>();

        var site = await BuildSite(pair);
        await MoveTo(pair, site.Hull, leaving ? site.Orbit : site.SectorMap, leaving ? new Vector2(60f, 0f) : new Vector2(400f, 0f));
        var tender = await DockTender(pair, site);
        await Sweep(pair);

        await server.WaitAssertion(() =>
        {
            if (leaving)
                Assert.That(orbits.TryLeaveOrbit(site.Console, out var reason), Is.True, $"Refused: {reason}");
            else
                Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.True, $"Refused: {reason}");
        });

        var trace = await TraceHop(pair, site.Hull, tender, leaving ? site.SectorMap : site.Orbit);
        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(trace.Arrived, Is.True, "The hop never finished.");
                Assert.That(trace.EverHadFtlComponent, Is.False, "A grid of the hop carried an FTLComponent.");
                Assert.That(trace.EverOnFtlMap, Is.False, "The hull went through the FTL map.");
                Assert.That(entMan.HasComponent<FTLComponent>(site.Hull), Is.False, "The hop started an FTL cooldown on the hull.");
                Assert.That(entMan.HasComponent<FTLComponent>(tender), Is.False, "The hop started an FTL cooldown on the tender.");
                Assert.That(entMan.HasComponent<WFPlanetHopComponent>(site.Hull), Is.False, "The hull still holds its hop.");
                Assert.That(entMan.HasComponent<WFPlanetHopComponent>(tender), Is.False, "The tender still holds the hop.");
                Assert.That(entMan.Count<WFPlanetHopMapComponent>(), Is.EqualTo(0), "The hold map outlived the hop.");
                Assert.That(entMan.Count<FtlVisualizerComponent>(), Is.EqualTo(0), "The arrival swirl outlived the hop.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>A drive cooling down doesn't stop a hop, and the hop leaves its cooldown exactly as it was.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task HopWorksWhileTheDriveCoolsDown(bool leaving)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var orbits = server.System<WFOrbitEntrySystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        var site = await BuildSite(pair);
        await MoveTo(pair, site.Hull, leaving ? site.Orbit : site.SectorMap, leaving ? new Vector2(60f, 0f) : new Vector2(400f, 0f));

        var cooldown = default(StartEndTime);

        await server.WaitPost(() =>
        {
            var ftl = entMan.AddComponent<FTLComponent>(site.Hull);
            ftl.State = FTLState.Cooldown;
            ftl.StateTime = StartEndTime.FromCurTime(timing, 600f);
            cooldown = ftl.StateTime;
        });

        await Sweep(pair);

        await server.WaitAssertion(() =>
        {
            Assert.That(OrbitTarget(entMan, site.Console)?.Busy, Is.False,
                "A cooling drive made the console say the hull was busy.");

            if (leaving)
                Assert.That(orbits.TryLeaveOrbit(site.Console, out var reason), Is.True, $"Refused with the drive cooling: {reason}");
            else
                Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.True, $"Refused with the drive cooling: {reason}");
        });

        var trace = await TraceHop(pair, site.Hull, null, leaving ? site.SectorMap : site.Orbit);

        await server.WaitAssertion(() =>
        {
            var ftl = entMan.GetComponent<FTLComponent>(site.Hull);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(trace.Arrived, Is.True, "The hop never finished.");
                Assert.That(trace.EverOnFtlMap, Is.False, "The hull went through the FTL map.");
                Assert.That(ftl.State, Is.EqualTo(FTLState.Cooldown), "The hop changed the drive's state.");
                Assert.That(ftl.StateTime, Is.EqualTo(cooldown), "The hop restarted or cut short the drive's cooldown.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>A second hop, an FTL departure and a docked ship's jump are all refused while a hop runs.</summary>
    [Test]
    public async Task NothingStartsMidHop()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var orbits = server.System<WFOrbitEntrySystem>();
        var shuttles = server.System<ShuttleSystem>();

        var site = await BuildSite(pair);
        await MoveTo(pair, site.Hull, site.SectorMap, new Vector2(400f, 0f));
        await DockTender(pair, site);
        await Sweep(pair);

        await server.WaitAssertion(() =>
            Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.True, $"Refused: {reason}"));

        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));

        await server.WaitAssertion(() =>
        {
            string? ftlReason = null;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var again), Is.False, "A second hop started mid-hop.");
                Assert.That(again, Is.EqualTo(loc.GetString("wf-orbit-hopping")), "The second hop was refused for another reason.");
                Assert.That(shuttles.WfRefusesFtlDeparture(site.Hull), Is.True, "An FTL departure was allowed mid-hop.");
                Assert.That(shuttles.CanFTL(site.Hull, out ftlReason), Is.False, "CanFTL allowed a hull mid-hop.");
                Assert.That(OrbitTarget(entMan, site.Console)?.Busy, Is.True, "The console doesn't read the hop as busy.");
            }

            Assert.That(ftlReason, Is.EqualTo(loc.GetString("wf-orbit-hopping")));
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>A hull in the middle of an FTL jump, or one that is anchored or protected, can't hop.</summary>
    [Test]
    public async Task PhysicalRefusalsStillApply()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var orbits = server.System<WFOrbitEntrySystem>();

        var site = await BuildSite(pair);
        await MoveTo(pair, site.Hull, site.SectorMap, new Vector2(400f, 0f));
        await Sweep(pair);

        await server.WaitAssertion(() =>
        {
            var ftl = entMan.AddComponent<FTLComponent>(site.Hull);
            ftl.State = FTLState.Travelling;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.False);
                Assert.That(reason, Is.EqualTo(loc.GetString("wf-orbit-in-ftl")), "A hull mid-jump was refused for another reason.");
            }

            entMan.RemoveComponent<FTLComponent>(site.Hull);

            entMan.AddComponent<PreventPilotComponent>(site.Hull);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.False);
                Assert.That(reason, Is.EqualTo(loc.GetString("wf-orbit-protected")), "A protected hull was refused for another reason.");
            }

            entMan.RemoveComponent<PreventPilotComponent>(site.Hull);

            entMan.AddComponent<ForceAnchorComponent>(site.Hull);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.False);
                Assert.That(reason, Is.EqualTo(loc.GetString("wf-orbit-anchored")), "An anchored hull was refused for another reason.");
                Assert.That(entMan.HasComponent<WFPlanetHopComponent>(site.Hull), Is.False, "A refused hop left its state on the hull.");
            }

            entMan.RemoveComponent<ForceAnchorComponent>(site.Hull);
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>Deleting the hull mid-hop leaves no hold map, swirl or mark, and the tender comes out on the target map.</summary>
    [TestCase(1f)]
    [TestCase(7f)]
    public async Task HullDeletedMidHopLeavesNothingBehind(float deleteAfter)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var orbits = server.System<WFOrbitEntrySystem>();

        var site = await BuildSite(pair);
        await MoveTo(pair, site.Hull, site.SectorMap, new Vector2(400f, 0f));
        var tender = await DockTender(pair, site);
        await Sweep(pair);

        await server.WaitAssertion(() =>
            Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.True, $"Refused: {reason}"));

        await server.WaitRunTicks(pair.SecondsToTicks(deleteAfter));

        var inTransit = deleteAfter > WFPlanetHopSystem.StartupTime;

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFPlanetHopMapComponent>(entMan.GetComponent<TransformComponent>(site.Hull).MapUid ?? EntityUid.Invalid),
                Is.EqualTo(inTransit), "Precondition: the hull is where the test thinks it is in the hop.");
            entMan.DeleteEntity(site.Hull);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var tenderMap = entMan.GetComponent<TransformComponent>(tender).MapUid;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.EntityExists(tender), Is.True, "The tender was lost with the hull.");
                Assert.That(entMan.Count<WFPlanetHopMapComponent>(), Is.EqualTo(0), "The hold map outlived the hull.");
                Assert.That(entMan.Count<FtlVisualizerComponent>(), Is.EqualTo(0), "The arrival swirl outlived the hull.");
                Assert.That(entMan.HasComponent<WFPlanetHopComponent>(tender), Is.False, "The tender kept the hop.");
                Assert.That(entMan.HasComponent<WFPlanetApproachComponent>(tender), Is.False, "The tender kept the approach mark.");
                Assert.That(entMan.HasComponent<FtlDepartureComponent>(tender), Is.False, "The tender kept the departure ripple.");
                Assert.That(entMan.HasComponent<FTLComponent>(tender), Is.False, "The tender gained an FTLComponent.");
                Assert.That(tenderMap, Is.EqualTo(inTransit ? site.Orbit : site.SectorMap),
                    "The tender didn't end on the map the hull was bound for, or stayed where the hull was.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>The tender docked to a hull lands clear of a grid parked at the arrival spot as well as the hull does.</summary>
    [Test]
    public async Task TenderArrivesClearOfAParkedGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var orbits = server.System<WFOrbitEntrySystem>();
        var transform = server.System<SharedTransformSystem>();

        var site = await BuildSite(pair);
        var centre = Vector2.Zero;
        var orbitId = MapId.Nullspace;

        await server.WaitPost(() =>
        {
            centre = transform.GetWorldPosition(site.Body);
            orbitId = entMan.GetComponent<MapComponent>(site.Orbit).MapId;
        });

        await MoveTo(pair, site.Hull, site.SectorMap, centre + new Vector2(400f, 0f));
        var tender = await DockTender(pair, site);
        var parked = await BuildHull(pair, orbitId, centre + new Vector2(402f, 0f));
        await Sweep(pair);

        await server.WaitAssertion(() =>
            Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.True, $"Refused: {reason}"));

        var trace = await TraceHop(pair, site.Hull, tender, site.Orbit);

        await server.WaitAssertion(() =>
        {
            var parkedBox = transform.GetWorldMatrix(parked).TransformBox(entMan.GetComponent<MapGridComponent>(parked).LocalAABB);
            var hullBox = transform.GetWorldMatrix(site.Hull).TransformBox(entMan.GetComponent<MapGridComponent>(site.Hull).LocalAABB);
            var tenderBox = transform.GetWorldMatrix(tender).TransformBox(entMan.GetComponent<MapGridComponent>(tender).LocalAABB);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(trace.Arrived, Is.True, "The hull never reached the orbit layer.");
                Assert.That(hullBox.Intersects(parkedBox), Is.False, "The hull came out on top of the parked grid.");
                Assert.That(tenderBox.Intersects(parkedBox), Is.False, "The tender came out on top of the parked grid.");
                Assert.That(entMan.GetComponent<TransformComponent>(tender).MapUid, Is.EqualTo(site.Orbit));
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>Crew are put on the floor as the hull leaves, and the spool-up ripple and its arrival flash reach the hull.</summary>
    [Test]
    public async Task CrewAreKnockedDownAndTheHopHasItsRipple()
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
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            var deck = new List<(Vector2i, Tile)>();

            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 5; y++)
            {
                deck.Add((new Vector2i(x, y), new Tile(plating)));
            }

            server.System<SharedMapSystem>().SetTiles(site.Hull, entMan.GetComponent<MapGridComponent>(site.Hull), deck);
            mob = entMan.SpawnEntity("MobHuman", entMan.GetComponent<TransformComponent>(site.Console).Coordinates);
            Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.True, $"Refused: {reason}");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var ripple = entMan.GetComponent<FtlDepartureComponent>(site.Hull);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<KnockedDownComponent>(mob), Is.False, "The crew went down before the hull left.");
                Assert.That(ripple.Entered, Is.False, "The ripple says the hull has left during the warm-up.");
                Assert.That(ripple.Arriving, Is.False);
            }
        });

        // Just past the end of the warm-up; the ripple fades under a second after the hull leaves.
        await server.WaitRunTicks(pair.SecondsToTicks(WFPlanetHopSystem.StartupTime - 0.8f));

        await server.WaitAssertion(() =>
        {
            var ripple = entMan.GetComponent<FtlDepartureComponent>(site.Hull);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<KnockedDownComponent>(mob), Is.True, "The crew stayed on their feet as the hull left.");
                Assert.That(ripple.Entered, Is.True, "The ripple never saw the hull leave.");
            }
        });

        // The flash fades out under a second after the landing, so catch the tick the hull lands.
        var landed = false;
        var flashed = false;

        for (var i = 0; i < pair.SecondsToTicks(WFPlanetHopSystem.TravelTime + 3f) && !landed; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() =>
            {
                landed = entMan.GetComponent<TransformComponent>(site.Hull).MapUid == site.Orbit;
                flashed = entMan.TryGetComponent(site.Hull, out FtlDepartureComponent? ripple) && ripple.Arriving;
            });
        }

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(landed, Is.True, "The hull never landed.");
                Assert.That(flashed, Is.True, "The arrival flash never reached the hull.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>A hull the world's gravity captures goes through the same hop, with no FTL on the way.</summary>
    [Test]
    public async Task GravityWellCaptureHops()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        var site = await BuildSite(pair);
        var centre = Vector2.Zero;

        await server.WaitPost(() => centre = transform.GetWorldPosition(site.Body));
        await MoveTo(pair, site.Hull, site.SectorMap, centre + new Vector2(150f, 0f));

        var hopped = false;
        var landed = false;
        var ftlSeen = false;
        var seconds = (int) (WFGravityWellSystem.CaptureDelay.TotalSeconds + 20f);

        for (var i = 0; i < seconds && !landed; i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f));
            await server.WaitPost(() =>
            {
                hopped |= entMan.HasComponent<WFPlanetHopComponent>(site.Hull);
                ftlSeen |= entMan.HasComponent<FTLComponent>(site.Hull);
                landed = hopped && entMan.GetComponent<TransformComponent>(site.Hull).MapUid == site.Orbit;
            });
        }

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(hopped, Is.True, "The well never started a hop on the adrift hull.");
                Assert.That(ftlSeen, Is.False, "The well's capture put an FTLComponent on the hull.");
                Assert.That(entMan.GetComponent<TransformComponent>(site.Hull).MapUid, Is.EqualTo(site.Orbit),
                    "The captured hull didn't end in orbit.");
                Assert.That(entMan.HasComponent<WFPlanetHopComponent>(site.Hull), Is.False);
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }
}
