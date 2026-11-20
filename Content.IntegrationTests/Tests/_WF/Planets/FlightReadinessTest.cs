#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.Planets.Flight;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>The console's flight checklist and the vertical keys standing in for the liftoff and descent buttons.</summary>
[TestFixture]
public sealed class FlightReadinessTest
{
    // The console readout is swept once a second.
    private const float Refresh = 1.5f;

    /// <summary>A grounded hull reports what holds it and what it lacks, then lifts off on the ascend key once it can.</summary>
    [Test]
    public async Task AscendKeyLiftsOffOnceTheChecklistPasses()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-8, -8), new Vector2i(32, 32));
        var hull = await BuildHull(pair, await MapIdOf(pair, ground));
        await MapInitHull(pair, hull);
        await RemoveOrdinaryThrusters(pair, hull);
        await server.WaitRunTicks(pair.SecondsToTicks(Refresh));

        var console = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            console = FindShuttleConsole(entMan, hull);
            var readout = entMan.GetComponent<WFConsoleOrbitTargetComponent>(console);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(readout.LiftoffAvailable, Is.True, "Precondition: the hull is not grounded.");
                Assert.That(readout.LiftRatio, Is.Zero);
                Assert.That(readout.Hold, Is.Null);
                Assert.That(readout.ConvertAdvice, Is.Zero, "There is nothing to convert.");
                Assert.That(readout.AddAdvice, Is.GreaterThan(0), "A hull with no thrusters must be told to add some.");
                Assert.That(readout.AtmospherePowerDeficit, Is.False);
            }

            entMan.EnsureComponent<ForceAnchorComponent>(hull);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(Refresh));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFConsoleOrbitTargetComponent>(console).Hold, Is.Not.Null,
                "A force-anchored hull must say what holds it.");
            entMan.RemoveComponent<ForceAnchorComponent>(hull);
        });

        // Ascend without lift is refused.
        await HoldVertical(pair, hull, ShuttleButtons.AscendZ);
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFLiftoffComponent>(hull), Is.False, "Ascend lifted off a hull with no lift.");
            Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(ground));
        });

        // 150 lift against 225 half-mass tiles at 1 g. The key is still held.
        await AddLandingThrusters(pair, hull, 3);
        await server.WaitRunTicks(pair.SecondsToTicks(Refresh));
        await server.WaitAssertion(() =>
        {
            var readout = entMan.GetComponent<WFConsoleOrbitTargetComponent>(console);
            var lifting = entMan.HasComponent<WFLiftoffComponent>(hull)
                          || entMan.GetComponent<TransformComponent>(hull).MapUid != ground;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(lifting, Is.True, "The held ascend key did not lift off a hull with full lift.");
                Assert.That(readout.LiftRatio, Is.GreaterThanOrEqualTo(1f));
                Assert.That(readout.ConvertAdvice + readout.AddAdvice, Is.Zero, "A hull with full lift was given advice.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>With full lift and power the descend key leaves orbit, the same as the button without a confirm.</summary>
    [Test]
    public async Task DescendKeyEntersAtmosphereWithFullLift()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var hull = await BuildHull(pair, await MapIdOf(pair, orbit));
        await MapInitHull(pair, hull);
        await RemoveOrdinaryThrusters(pair, hull);
        await AddLandingThrusters(pair, hull, 3);

        await HoldDescend(pair, hull);
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var map = entMan.GetComponent<TransformComponent>(hull).MapUid;
            Assert.That(map, Is.Not.EqualTo(orbit), "The descend key left a hull with full lift in orbit.");
            Assert.That(entMan.HasComponent<CEZTransitMapComponent>(map) || layers.Contains(map!.Value), Is.True,
                "The hull left orbit for somewhere other than the planet below.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    private static async Task<MapId> MapIdOf(TestPair pair, EntityUid layer)
    {
        var mapId = MapId.Nullspace;
        await pair.Server.WaitPost(() => mapId = pair.Server.EntMan.GetComponent<MapComponent>(layer).MapId);
        return mapId;
    }
}
