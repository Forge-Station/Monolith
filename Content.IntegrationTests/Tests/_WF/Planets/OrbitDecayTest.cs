#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.Planets;
using Content.Server._WF.ShipPa;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Planets.Flight;
using Content.Shared._WF.Planets;
using Content.Shared._WF.ShipPa;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Orbit decay: only station-keeping grids stay in orbit; anything else is warned and then falls.</summary>
[TestFixture]
[TestOf(typeof(WFOrbitDecaySystem))]
public sealed class OrbitDecayTest
{
    /// <summary>The ship code the warning borrows and hands back.</summary>
    private const string PriorCode = "WFShipCodeYellow";

    /// <summary>Seconds the countdown is shortened to; the sweep is 1 Hz.</summary>
    private const float TestGrace = 1f;

    /// <summary>Seconds to wait for a stamp: ten seconds of arrival settle plus two sweeps.</summary>
    private const float StampWait = 13f;

    /// <summary>One powered thruster keeps station, even at a lift ratio far short of flying.</summary>
    [Test]
    public async Task PoweredThrusterHoldsOrbit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMap = await MapIdOf(pair, orbit);

        var hull = await BuildHull(pair, orbitMap);
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, 1);

        // Three sweeps; a stamp would land on the first.
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(hull), Is.False,
                    "A hull with a running thruster is decaying anyway.");
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                    "The hull left the orbit layer without being told to.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A fresh arrival whose thrusters are still off from the FTL hop is not stamped.</summary>
    [Test]
    public async Task FreshArrivalWithPoweredThrustersIsNotStamped()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMap = await MapIdOf(pair, orbit);

        var hull = await BuildHull(pair, orbitMap);
        await MapInitHull(pair, hull);
        var thrusters = await AddLandingThrusters(pair, hull, 1);

        // The arrival gap: powered thrusters that are off until their own next update.
        await SetThrusters(pair, hull, false);
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(thrusters, Is.Not.Empty, "Precondition: the hull was given a landing thruster.");
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(hull), Is.False,
                    "A hull was stamped as decaying inside its arrival settle.");
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                    "The hull left the orbit layer without being told to.");
            }
        });

        await SetThrusters(pair, hull, true);
        await server.WaitRunTicks(pair.SecondsToTicks(StampWait));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(hull), Is.False,
                    "The hull is decaying with its thrusters back and running.");
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                    "The hull fell out of orbit after its thrusters came back.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A hull with all thrusters off takes the warning code, holds for the grace, then falls.</summary>
    [Test]
    public async Task LostStationKeepingDecaysAndFalls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMap = await MapIdOf(pair, orbit);

        var hull = await BuildHull(pair, orbitMap);
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, 1);

        await SetThrusters(pair, hull, false);
        await server.WaitRunTicks(pair.SecondsToTicks(StampWait));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(hull), Is.True,
                    "A hull with no station-keeping is not decaying.");
                Assert.That(entMan.GetComponent<ShipAlertComponent>(hull).Code.Id,
                    Is.EqualTo(WFOrbitDecaySystem.AlertOrbitDecay),
                    "The decay warning never reached the ship's situation code.");
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                    "The hull fell before its grace was up.");
            }
        });

        await Hasten(pair, hull, TestGrace);
        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.Not.EqualTo(orbit),
                    "The grace ran out and the hull is still parked in orbit.");
                Assert.That(entMan.HasComponent<WFLiftLostComponent>(hull), Is.True,
                    "The hull left orbit without entering lift lost, so no GPWS sequence runs on the way down.");
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(hull), Is.False,
                    "The countdown is still on a hull that has already left orbit.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Thrust back inside the grace cancels the decay and silently restores the prior code.</summary>
    [Test]
    public async Task ThrustBackInsideGraceCancels()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var alerts = server.System<ShipAlertSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMap = await MapIdOf(pair, orbit);

        var hull = await BuildHull(pair, orbitMap);
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, 1);

        await server.WaitPost(() => alerts.SetCode(hull, PriorCode));

        await SetThrusters(pair, hull, false);
        await server.WaitRunTicks(pair.SecondsToTicks(StampWait));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ShipAlertComponent>(hull).Code.Id,
                Is.EqualTo(WFOrbitDecaySystem.AlertOrbitDecay),
                "Precondition: the warning took the ship's code over.");
        });

        await SetThrusters(pair, hull, true);
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(hull), Is.False,
                    "The countdown survived the thrusters coming back.");
                Assert.That(entMan.GetComponent<ShipAlertComponent>(hull).Code.Id, Is.EqualTo(PriorCode),
                    "The ship was not handed its own situation code back.");
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                    "The hull fell anyway.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Bare debris falls on its own and stays on the ground as a wreck.</summary>
    [Test]
    public async Task ThrusterlessDebrisFallsAndStaysAWreck()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var orbit = layers[^1];
        var orbitMap = await MapIdOf(pair, orbit);

        // Ground to land on; no player means no biome tiles.
        await LayTiles(pair, ground, new Vector2i(-16, -16), new Vector2i(16, 16));

        var debris = await BuildDebris(pair, orbitMap, 3);

        await server.WaitRunTicks(pair.SecondsToTicks(StampWait));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(debris), Is.True,
                "A thrusterless fragment is holding an orbit for free.");
        });

        await Hasten(pair, debris, TestGrace);

        // Poll through the whole free fall.
        for (var i = 0; i < 200 && !await OnGround(pair, debris, ground); i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(0.25f));
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(debris).MapUid, Is.EqualTo(ground),
                "The debris never reached the ground layer.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(30f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.EntityExists(debris), Is.True, "The wreck was cleaned up; wrecks are forever.");
                Assert.That(entMan.GetComponent<TransformComponent>(debris).MapUid, Is.EqualTo(ground),
                    "The wreck did not stay where it landed.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A dead hull docked to a station-keeping one holds orbit with it.</summary>
    [Test]
    public async Task DockedToStationKeepingHoldsOrbit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var docking = server.System<DockingSystem>();
        var shuttles = server.System<ShuttleSystem>();
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMap = await MapIdOf(pair, orbit);

        var hull = await BuildHull(pair, orbitMap);
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, 1);

        var lander = await BuildLander(pair, orbitMap, new Vector2(400f, 0f));
        await MapInitHull(pair, lander);
        await SetThrusters(pair, lander, false);

        // Align the docking ports before docking so the weld starts at rest.
        await server.WaitPost(() =>
        {
            var hullDock = FindDock(entMan, hull);
            var landerDock = FindDock(entMan, lander);

            Assert.That(hullDock, Is.Not.EqualTo(EntityUid.Invalid), "The hull has no docking port.");
            Assert.That(landerDock, Is.Not.EqualTo(EntityUid.Invalid), "The lander has no docking port.");

            var wanted = transform.GetWorldPosition(hullDock) - transform.GetWorldPosition(landerDock);
            transform.SetWorldPosition(lander, transform.GetWorldPosition(lander) + wanted);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitPost(() =>
        {
            var hullDock = FindDock(entMan, hull);
            var landerDock = FindDock(entMan, lander);

            docking.Dock(
                (hullDock, entMan.GetComponent<DockingComponent>(hullDock)),
                (landerDock, entMan.GetComponent<DockingComponent>(landerDock)));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var docked = new HashSet<EntityUid>();
            shuttles.GetAllDockedShuttles(hull, docked);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(docked, Does.Contain(lander), "Precondition: the lander is docked to the hull.");
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(lander), Is.False,
                    "A thrusterless grid docked to a hull that is keeping station is decaying anyway.");
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(hull), Is.False,
                    "The hull that is keeping station is decaying.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Force-anchored grids and detached terrain are never stamped.</summary>
    [Test]
    public async Task ForceAnchoredAndDetachedTerrainNeverDecay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMap = await MapIdOf(pair, orbit);

        var anchored = await BuildDebris(pair, orbitMap, 3, new Vector2(30f, 0f));
        var terrain = await BuildDebris(pair, orbitMap, 3, new Vector2(60f, 0f));

        await server.WaitPost(() =>
        {
            entMan.AddComponent<ForceAnchorComponent>(anchored);
            entMan.AddComponent<WFDetachedTerrainComponent>(terrain);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(StampWait));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(anchored), Is.False,
                    "A force-anchored grid is decaying.");
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(terrain), Is.False,
                    "Detached terrain is decaying.");
                Assert.That(entMan.GetComponent<TransformComponent>(anchored).MapUid, Is.EqualTo(orbit),
                    "The force-anchored grid left the orbit layer.");
                Assert.That(entMan.GetComponent<TransformComponent>(terrain).MapUid, Is.EqualTo(orbit),
                    "The detached terrain left the orbit layer.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Switches every thruster on a grid on or off through ThrusterSystem, as a power cut would.</summary>
    private static async Task SetThrusters(TestPair pair, EntityUid grid, bool enabled)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var thrusters = server.System<ThrusterSystem>();

        await server.WaitPost(() =>
        {
            foreach (var uid in Children(entMan, grid))
            {
                if (!entMan.TryGetComponent<ThrusterComponent>(uid, out var thruster))
                    continue;

                if (!enabled)
                    thrusters.DisableThruster(uid, thruster);
                else if (thrusters.CanEnable(uid, thruster))
                    thrusters.EnableThruster(uid, thruster);
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
    }

    /// <summary>Shortens a running countdown, re-stamping its deadline since the sweep stamps it only once.</summary>
    private static async Task Hasten(TestPair pair, EntityUid grid, float seconds)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();

        await server.WaitPost(() =>
        {
            var decay = entMan.GetComponent<WFOrbitDecayComponent>(grid);
            decay.Grace = TimeSpan.FromSeconds(seconds);
            decay.DecayAt = timing.CurTime + decay.Grace;
        });

        await server.WaitRunTicks(1);
    }

    /// <summary>The map id of a layer, which is what a grid is built on.</summary>
    private static async Task<MapId> MapIdOf(TestPair pair, EntityUid layer)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapId = MapId.Nullspace;

        await server.WaitPost(() => mapId = entMan.GetComponent<MapComponent>(layer).MapId);
        return mapId;
    }

    /// <summary>True once a grid has arrived on the ground layer, however it got there.</summary>
    private static async Task<bool> OnGround(TestPair pair, EntityUid grid, EntityUid ground)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var landed = false;

        await server.WaitPost(() => landed = entMan.GetComponent<TransformComponent>(grid).MapUid == ground);
        return landed;
    }

    /// <summary>The first docking port resting on a grid, or Invalid.</summary>
    private static EntityUid FindDock(IEntityManager entMan, EntityUid grid)
    {
        foreach (var uid in Children(entMan, grid))
        {
            if (entMan.HasComponent<DockingComponent>(uid))
                return uid;
        }

        return EntityUid.Invalid;
    }

    /// <summary>Z-layer maps are grids too, but no flight sweep treats one as a hull.</summary>
    [Test]
    public async Task LayerMapsAreNeverStampedAsHulls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);

        await server.WaitRunTicks(pair.SecondsToTicks(StampWait));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var layer in layers)
                {
                    Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(layer), Is.False,
                        $"Layer map {entMan.ToPrettyString(layer)} was stamped as a decaying orbiter.");
                    Assert.That(entMan.HasComponent<Content.Server._WF.Planets.Flight.WFFlightAmbienceComponent>(layer), Is.False,
                        $"Layer map {entMan.ToPrettyString(layer)} was treated as a hull in flight.");
                    Assert.That(entMan.HasComponent<Content.Server._WF.Planets.Flight.WFPlanetDragComponent>(layer), Is.False,
                        $"Layer map {entMan.ToPrettyString(layer)} was given planet drag.");
                }
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
