using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Planets.Flight;
using Content.Server._WF.Shuttles;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Interaction;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class CrashThrustTest
{
    [TestCase(6)]
    [TestCase(8)]
    public async Task SeveredFiringEnginePushesItsSectionAndObeysPowerAndManualShutdown(int cutX)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        await LayTiles(pair, layers[0], new Vector2i(-32, -32), new Vector2i(64, 64));
        var hull = await BuildHull(pair, em.GetComponent<MapComponent>(layers[0]).MapId);
        await MapInitHull(pair, hull);
        await server.WaitPost(() =>
        {
            var thrusters = server.System<ThrusterSystem>();
            var shuttle = em.GetComponent<ShuttleComponent>(hull);
            // Fire only the lower-right engine: opposed engines would correctly cancel their forces.
            foreach (var dir in new[] { DirectionFlag.East, DirectionFlag.West, DirectionFlag.North, DirectionFlag.South })
                if (shuttle.LinearThrusters[(int)Math.Log2((int)dir)].Any(uid =>
                    em.GetComponent<TransformComponent>(uid).LocalPosition is { X: > 6, Y: < 6 }))
                    thrusters.EnableLinearThrustDirection(shuttle, dir);
            thrusters.WfCaptureCrashThrust(hull);
            em.EnsureComponent<WFSkidComponent>(hull);
            em.EnsureComponent<WFCrashImpactComponent>(hull).NextImpact =
                server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromSeconds(1);
            var cut = new List<(Vector2i, Tile)>();
            for (var y = 0; y < 15; y++) cut.Add((new Vector2i(cutX, y), Tile.Empty));
            server.System<SharedMapSystem>().SetTiles(hull, em.GetComponent<MapGridComponent>(hull), cut);
        });
        await server.WaitRunTicks(2);
        await server.WaitPost(() =>
        {
            var query = em.EntityQueryEnumerator<WFCrashThrustComponent>();
            while (query.MoveNext(out var uid, out var command))
                if (command.Detached && em.GetComponent<TransformComponent>(uid).GridUid is { } grid)
                    server.System<SharedPhysicsSystem>().SetLinearVelocity(grid, Vector2.Zero);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        var engineUid = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            var query = em.EntityQueryEnumerator<WFCrashThrustComponent, ThrusterComponent>();
            while (query.MoveNext(out var uid, out var command, out var engine))
            {
                if (!command.Detached) continue;
                engineUid = uid;
                Assert.That(engine.Firing, Is.True);
                var grid = em.GetComponent<TransformComponent>(uid).GridUid!.Value;
                Assert.That(em.GetComponent<PhysicsComponent>(grid).LinearVelocity.Length(), Is.GreaterThan(0.01f));
                Assert.That(em.HasComponent<WFThrustAmbienceComponent>(grid), Is.True);
                // The console can move to a new grid while the engine stays on the original (largest) section.
                var consoles = em.EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
                while (consoles.MoveNext(out _, out _, out var consoleXform))
                    if (consoleXform.GridUid is { } controlledGrid)
                    {
                        Assert.That(controlledGrid, Is.Not.EqualTo(grid));
                        Assert.That(em.GetComponent<ShuttleComponent>(controlledGrid).LinearThrusters.Any(bank => bank.Contains(uid)), Is.False);
                    }
                break;
            }
            Assert.That(engineUid, Is.Not.EqualTo(EntityUid.Invalid));
            // DebugThruster normally requests zero watts; give it a real load before disconnecting supply.
            em.GetComponent<WFAtmosphereThrusterComponent>(engineUid).RatedLoad = 1000f;
            server.System<ThrusterSystem>().WfRefreshAtmosphereThruster(engineUid, em.GetComponent<ThrusterComponent>(engineUid));
            server.System<SharedPowerReceiverSystem>().SetNeedsPower(engineUid, true);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.75f));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ThrusterComponent>(engineUid).Firing, Is.False);
            Assert.That(em.HasComponent<WFCrashThrustComponent>(engineUid), Is.True);
            Assert.That(em.HasComponent<WFThrustAmbienceComponent>(em.GetComponent<TransformComponent>(engineUid).GridUid!.Value), Is.False);
            server.System<SharedPowerReceiverSystem>().SetNeedsPower(engineUid, false);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.75f));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ThrusterComponent>(engineUid).Firing, Is.True);
            em.EventBus.RaiseLocalEvent(engineUid, new ActivateInWorldEvent(engineUid, engineUid, true));
        });
        await server.WaitRunTicks(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFCrashThrustComponent>(engineUid), Is.False);
            Assert.That(em.GetComponent<ThrusterComponent>(engineUid).Firing, Is.False);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
