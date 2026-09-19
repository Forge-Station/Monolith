using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.TractorBeam;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.TractorBeam;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed class TractorBeamConsoleTest
{
    [Test]
    public async Task ReleasedDishCannotRecaptureUntilCooldownExpires()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, console, actor, _) = CreateConsole(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var controller = entities.GetComponent<TractorBeamConsoleComponent>(console);
            var system = entities.System<TractorBeamSystem>();
            Send(entities, console, actor, emitter, target);
            Assert.That(beam.Target, Is.EqualTo(target));
            Send(entities, console, actor, emitter, null);
            Assert.That(beam.CooldownRemaining, Is.EqualTo(12));
            for (var i = 0; i < 11; i++)
                system.UpdateBeforeSolve(false, 1f);
            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target);
            Assert.That(beam.Target, Is.Null, "The server must reject recapture even if the client bypasses disabled controls.");
            Send(entities, console, actor, emitter, null);
            Assert.That(beam.CooldownRemaining, Is.EqualTo(1), "Idle releases cannot restart the countdown.");
            system.UpdateBeforeSolve(false, 1f);
            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target);
            Assert.That(beam.Target, Is.EqualTo(target), "Capture must become available when the cooldown expires.");
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShieldsBlockAcquisitionButPreserveExistingCapturePinAndRangeControls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, console, actor, _) = CreateConsole(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var controller = entities.GetComponent<TractorBeamConsoleComponent>(console);
            var system = entities.System<TractorBeamSystem>();

            // The shield system owns this grid marker. Drive its boundary directly to isolate
            // acquisition policy from the generator's separate power and recharge simulation.
            entities.AddComponent<ShipShieldedComponent>(target);
            Send(entities, console, actor, emitter, target);
            Assert.That(beam.Target, Is.Null, "A raised shield must prevent a new capture.");

            entities.RemoveComponent<ShipShieldedComponent>(target);
            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target);
            system.UpdateBeforeSolve(false, 1f / 60f);
            Assert.That(beam.Target, Is.EqualTo(target));
            Assert.That(beam.Active, Is.True, "Dropping the shield must permit this same valid target to be captured.");
            var visual = beam.Visual;

            entities.AddComponent<ShipShieldedComponent>(target);
            system.UpdateBeforeSolve(false, 1f / 60f);
            Assert.That(beam.Active, Is.True, "Raising shields cannot sever an existing capture.");
            Assert.That(beam.Target, Is.EqualTo(target));
            Assert.That(beam.Visual, Is.EqualTo(visual), "The existing effect must survive without reacquisition.");
            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target, lockInPlace: true);
            Assert.That(beam.LockedInPlace, Is.True, "A shield raised after capture must not block pinning.");

            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target, desiredRange: 30f);
            Assert.That(beam.RequestedDistance, Is.EqualTo(30f));
            Assert.That(beam.Pulling, Is.True, "The existing capture must still accept a shorter range.");
            system.UpdateBeforeSolve(false, 1f / 60f);
            Assert.That(beam.Active, Is.True);

            Send(entities, console, actor, emitter, null);
            Assert.That(beam.Target, Is.Null);
            Assert.That(beam.Active, Is.False);
            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target);
            Assert.That(beam.Target, Is.Null, "After release, the still-raised shield must block reacquisition.");
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(1.005f, true)]
    [TestCase(1.03f, false)]
    public async Task StoppedPinAvailabilityToleratesOnlySmallPowerAllocationLag(float demandMultiplier, bool available)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, console, actor, _) = CreateConsole(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var controller = entities.GetComponent<TractorBeamConsoleComponent>(console);
            Send(entities, console, actor, emitter, target);
            entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, 1f / 60f);
            var maintenance = beam.RequestedPower;
            beam.RequestedPower = maintenance * demandMultiplier;
            entities.GetComponent<PowerConsumerComponent>(emitter).NetworkLoad.ReceivingPower = maintenance;
            // Refresh through the actual console command path, preserving an existing capture.
            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target);
            Assert.That(entities.System<SharedUserInterfaceSystem>().TryGetUiState<TractorBeamConsoleBoundUserInterfaceState>(
                console, TractorBeamUiKey.Key, out var state), Is.True);
            var displayed = state!.Emitters.Single(entry => entry.Entity == entities.GetNetEntity(emitter));
            Assert.That(displayed.CanLockInPlace, Is.EqualTo(available));
            Assert.That(displayed.PinStatus, Is.EqualTo(available ? TractorBeamPinStatus.Ready : TractorBeamPinStatus.InsufficientPower));
            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target, lockInPlace: true);
            Assert.That(beam.LockedInPlace, Is.EqualTo(available), "Displayed availability and server validation must agree.");
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("source-drift", TractorBeamPinStatus.StopSource)]
    [TestCase("source-spin", TractorBeamPinStatus.StopSource)]
    [TestCase("target-drift", TractorBeamPinStatus.StopTarget)]
    [TestCase("target-spin", TractorBeamPinStatus.StopTarget)]
    [TestCase("waiting", TractorBeamPinStatus.WaitingForBeam)]
    [TestCase("no-power", TractorBeamPinStatus.InsufficientPower)]
    public async Task PinAvailabilityExplainsTheActualStopOrPowerRequirement(string reason, TractorBeamPinStatus expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, console, actor, _) = CreateConsole(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var controller = entities.GetComponent<TractorBeamConsoleComponent>(console);
            var physics = entities.System<SharedPhysicsSystem>();
            Send(entities, console, actor, emitter, target);
            if (reason != "waiting")
                entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, 1f / 60f);
            switch (reason)
            {
                case "source-drift": physics.SetLinearVelocity(source, new Vector2(0.21f, 0)); break;
                case "source-spin": physics.SetAngularVelocity(source, 0.051f); break;
                case "target-drift": physics.SetLinearVelocity(target, new Vector2(0.21f, 0)); break;
                case "target-spin": physics.SetAngularVelocity(target, 0.051f); break;
                case "no-power":
                    entities.GetComponent<PowerConsumerComponent>(emitter).NetworkLoad.ReceivingPower = beam.HoldingPower * 0.5f;
                    break;
            }
            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target);
            Assert.That(entities.System<SharedUserInterfaceSystem>().TryGetUiState<TractorBeamConsoleBoundUserInterfaceState>(
                console, TractorBeamUiKey.Key, out var state), Is.True);
            var displayed = state!.Emitters.Single(entry => entry.Entity == entities.GetNetEntity(emitter));
            Assert.That(displayed.CanLockInPlace, Is.False);
            Assert.That(displayed.PinStatus, Is.EqualTo(expected));
            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target, lockInPlace: true);
            Assert.That(beam.LockedInPlace, Is.False);
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StoppedCaptureCanPinWithoutRecapturingPoseAndHoldExitsPin()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, console, actor, _) = CreateConsole(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var controller = entities.GetComponent<TractorBeamConsoleComponent>(console);
            Send(entities, console, actor, emitter, target);
            entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, 1f / 60f);
            Assert.That(beam.Active, Is.True);
            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target, lockInPlace: true);
            Assert.That(beam.LockedInPlace, Is.True);
            Assert.That(beam.Pulling, Is.False);
            var capturedSeparation = beam.LockedSeparation;
            var capturedAngle = beam.LockedAngle;
            var grace = beam.PowerGraceUntil;
            entities.System<SharedTransformSystem>().SetWorldPosition(target, new Vector2(70, 3));
            entities.System<SharedTransformSystem>().SetWorldRotation(target, Angle.FromDegrees(30));
            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target, lockInPlace: true);
            Assert.That(beam.LockedSeparation, Is.EqualTo(capturedSeparation));
            Assert.That(beam.LockedAngle, Is.EqualTo(capturedAngle));
            Assert.That(beam.PowerGraceUntil, Is.EqualTo(grace));
            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target);
            Assert.That(beam.LockedInPlace, Is.False);
            Send(entities, console, actor, emitter, null);
            Assert.That(beam.LockedSeparation, Is.EqualTo(Vector2.Zero));
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("uncaptured")]
    [TestCase("inactive")]
    [TestCase("target-moving")]
    [TestCase("source-moving")]
    [TestCase("target-spinning")]
    [TestCase("source-spinning")]
    [TestCase("brownout")]
    [TestCase("pull-and-pin")]
    public async Task PinRejectsTargetsThatAreNotCapturedStoppedAndFullyPowered(string reason)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, console, actor, _) = CreateConsole(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var controller = entities.GetComponent<TractorBeamConsoleComponent>(console);
            var physics = entities.System<SharedPhysicsSystem>();
            if (reason != "uncaptured")
            {
                Send(entities, console, actor, emitter, target);
                entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, 1f / 60f);
                Assert.That(beam.Active, Is.True);
            }
            switch (reason)
            {
                case "inactive": beam.Active = false; break;
                case "target-moving": physics.SetLinearVelocity(target, Vector2.One); break;
                case "source-moving": physics.SetLinearVelocity(source, Vector2.One); break;
                case "target-spinning": physics.SetAngularVelocity(target, 0.5f); break;
                case "source-spinning": physics.SetAngularVelocity(source, 0.5f); break;
                case "brownout":
                    beam.RequestedPower = beam.MaxPower;
                    entities.GetComponent<PowerConsumerComponent>(emitter).NetworkLoad.ReceivingPower = beam.HoldingPower;
                    break;
            }
            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target, pulling: reason == "pull-and-pin", lockInPlace: true);
            Assert.That(beam.LockedInPlace, Is.False);
            Assert.That(beam.Target, Is.EqualTo(reason == "uncaptured" ? (EntityUid?) null : target),
                "An invalid pin cannot replace or create a capture.");
            Assert.That(beam.LockedSeparation, Is.EqualTo(Vector2.Zero));
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsoleSwitchesHoldAndPullWithoutRefreshingLockGrace()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, console, actor, _) = CreateConsole(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var controller = entities.GetComponent<TractorBeamConsoleComponent>(console);
            Send(entities, console, actor, emitter, target);
            entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, 1f / 60f);
            Assert.That(beam.Target, Is.EqualTo(target));
            Assert.That(beam.Pulling, Is.False);
            var initialDistance = beam.HoldDistance;
            beam.PowerGraceUntil -= TimeSpan.FromSeconds(0.5);
            var initialGrace = beam.PowerGraceUntil;
            entities.System<SharedTransformSystem>().SetWorldPosition(target, new Vector2(70, 0));

            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target, true);
            Assert.Multiple(() =>
            {
                Assert.That(beam.Pulling, Is.True, "A hold lock can switch to pulling the same target.");
                Assert.That(beam.Target, Is.EqualTo(target));
                Assert.That(beam.HoldDistance, Is.EqualTo(initialDistance), "Switching modes cannot ratchet the lock distance.");
                Assert.That(beam.PowerGraceUntil, Is.EqualTo(initialGrace));
            });

            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target);
            Assert.Multiple(() =>
            {
                Assert.That(beam.Pulling, Is.False, "Pull mode can return to holding the same target.");
                Assert.That(beam.HoldDistance, Is.EqualTo(initialDistance));
                Assert.That(beam.PowerGraceUntil, Is.EqualTo(initialGrace),
                    "Changing modes cannot extend the startup power allowance.");
            });
            Send(entities, console, actor, emitter, null);
            Assert.That(beam.Target, Is.Null);
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsoleLocksWithoutRatchetAndReleasesDuringCooldown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, console, actor, _) = CreateConsole(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var controller = entities.GetComponent<TractorBeamConsoleComponent>(console);
            var power = entities.GetComponent<PowerConsumerComponent>(emitter);

            Send(entities, console, actor, emitter, target);
            Assert.Multiple(() =>
            {
                Assert.That(beam.Target, Is.EqualTo(target));
                Assert.That(beam.SourceGrid, Is.EqualTo(source));
                Assert.That(beam.Controller, Is.EqualTo(console));
                Assert.That(beam.HoldDistance, Is.GreaterThan(0));
                Assert.That(power.DrawRate, Is.EqualTo(TractorBeamPhysics.CalculatePower(0,
                    beam.MaxForce, beam.HoldingPower, beam.MaxPower, beam.DistanceStrain)));
            });

            var initialDistance = beam.HoldDistance;
            // Simulate an older lock so a refreshed grace period is observable within this tick.
            beam.PowerGraceUntil -= TimeSpan.FromSeconds(0.5);
            var initialGrace = beam.PowerGraceUntil;
            entities.System<SharedTransformSystem>().SetWorldPosition(target, new Vector2(70, 0));
            // Let the second command past the cooldown without ticking unrelated machinery.
            // This specifically tests the no-ratcheting guard, rather than the rate limiter.
            controller.NextCommand = TimeSpan.Zero;
            Send(entities, console, actor, emitter, target);
            Assert.Multiple(() =>
            {
                Assert.That(beam.HoldDistance, Is.EqualTo(initialDistance),
                    "Repeated lock commands cannot recapture the target's new distance.");
                Assert.That(beam.PowerGraceUntil, Is.EqualTo(initialGrace),
                    "Repeated locks cannot renew the power-loss grace period.");
            });

            // A release sent immediately after locking must bypass the lock-command cooldown.
            Send(entities, console, actor, emitter, null);
            Assert.Multiple(() =>
            {
                Assert.That(beam.Target, Is.Null);
                Assert.That(beam.SourceGrid, Is.Null);
                Assert.That(beam.Controller, Is.Null);
                Assert.That(power.DrawRate, Is.EqualTo(beam.IdlePower));
            });

            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("foreign-emitter")]
    [TestCase("self-target")]
    [TestCase("hidden-target")]
    [TestCase("dish-range")]
    [TestCase("outside-cone")]
    [TestCase("behind-dish")]
    [TestCase("console-range")]
    [TestCase("closed-ui")]
    [TestCase("unpowered-console")]
    [TestCase("unpowered-emitter")]
    [TestCase("static-target")]
    [TestCase("disabled-target-shuttle")]
    public async Task ConsoleRejectsInvalidCommands(string reason)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, console, actor, foreignEmitter) = CreateConsole(entities, maps, map.MapId);
            var commandEmitter = emitter;
            var commandTarget = target;
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            // Establish that this actor, UI and machinery can lock before introducing the
            // invalid condition, so a broken fixture or message dispatch cannot pass vacuously.
            Send(entities, console, actor, emitter, target);
            Assert.That(beam.Target, Is.EqualTo(target), "The unmodified fixture must accept a valid lock.");
            Send(entities, console, actor, emitter, null);
            Assert.That(beam.Target, Is.Null);
            entities.GetComponent<TractorBeamConsoleComponent>(console).NextCommand = TimeSpan.Zero;
            switch (reason)
            {
                case "foreign-emitter":
                    commandEmitter = foreignEmitter;
                    break;
                case "self-target":
                    commandTarget = source;
                    break;
                case "hidden-target":
                    entities.System<SharedShuttleSystem>().AddIFFFlag(target, IFFFlags.Hide);
                    break;
                case "dish-range":
                    beam.MaxRange = 10;
                    break;
                case "outside-cone":
                    entities.System<SharedTransformSystem>().SetWorldPosition(target, new Vector2(20, 50));
                    break;
                case "behind-dish":
                    entities.System<SharedTransformSystem>().SetWorldPosition(target, new Vector2(-50, 0));
                    break;
                case "console-range":
                    entities.GetComponent<TractorBeamConsoleComponent>(console).Range = 10;
                    break;
                case "closed-ui":
                    entities.System<SharedUserInterfaceSystem>().CloseUi(console, TractorBeamUiKey.Key, actor);
                    break;
                case "unpowered-console":
                    entities.GetComponent<ApcPowerReceiverComponent>(console).Powered = false;
                    break;
                case "unpowered-emitter":
                    entities.GetComponent<PowerConsumerComponent>(emitter).NetworkLoad.ReceivingPower = 0;
                    break;
                case "static-target":
                    entities.System<SharedPhysicsSystem>().SetBodyType(target, BodyType.Static);
                    break;
                case "disabled-target-shuttle":
                    entities.GetComponent<ShuttleComponent>(target).Enabled = false;
                    break;
                default:
                    Assert.Fail($"Unknown invalid command: {reason}");
                    break;
            }

            Send(entities, console, actor, commandEmitter, commandTarget);
            Assert.Multiple(() =>
            {
                Assert.That(beam.Target, Is.Null, "Invalid requests must not create a local lock.");
                Assert.That(beam.SourceGrid, Is.Null);
                Assert.That(beam.Controller, Is.Null);
                Assert.That(entities.GetComponent<TractorBeamEmitterComponent>(foreignEmitter).Target,
                    Is.Null, "A console cannot control another ship's dish.");
            });

            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    internal static void Send(IEntityManager entities, EntityUid console, EntityUid actor, EntityUid emitter, EntityUid? target,
        bool pulling = false, bool lockInPlace = false, float? desiredRange = null)
    {
        // Match SharedUserInterfaceSystem's object-based directed dispatch, including broadcast
        // subscribers. No production lock fields or private methods are used to create the lock.
        var message = new TractorBeamConsoleMessage(entities.GetNetEntity(emitter),
            target is { } targetEntity ? entities.GetNetEntity(targetEntity) : null, pulling, lockInPlace, desiredRange)
        {
            Actor = actor,
            Entity = entities.GetNetEntity(console),
            UiKey = TractorBeamUiKey.Key,
        };
        entities.EventBus.RaiseLocalEvent(console, (object) message, true);
    }

    internal static (EntityUid Source, EntityUid Target, EntityUid Emitter, EntityUid Console, EntityUid Actor, EntityUid ForeignEmitter)
        CreateConsole(IEntityManager entities, IMapManager maps, MapId map)
    {
        var mapSystem = entities.System<SharedMapSystem>();
        var transform = entities.System<SharedTransformSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var source = MakeGrid();
        var target = MakeGrid();
        transform.SetWorldPosition(target, new Vector2(50, 0));
        var emitter = SpawnPoweredEmitter(source);
        var foreignEmitter = SpawnPoweredEmitter(target);
        var coordinates = new EntityCoordinates(source, new Vector2(1.5f, 0.5f));
        var console = entities.SpawnEntity("WFComputerTractorBeam", coordinates);
        var actor = entities.SpawnEntity("MobHuman", coordinates);
        entities.GetComponent<ApcPowerReceiverComponent>(console).Powered = true;
        Assert.That(entities.GetComponent<TransformComponent>(console).Anchored, Is.True);

        var ui = entities.System<SharedUserInterfaceSystem>();
        ui.OpenUi(console, TractorBeamUiKey.Key, actor);
        Assert.That(ui.IsUiOpen(console, TractorBeamUiKey.Key, actor), Is.True,
            "The command actor must have the real console UI open before sending requests.");
        return (source, target, emitter, console, actor, foreignEmitter);

        EntityUid SpawnPoweredEmitter(EntityUid grid)
        {
            var dish = entities.SpawnEntity("WFTractorBeamEmitter", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            transform.SetWorldRotation(dish, Angle.FromDegrees(-90));
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(dish);
            // Supply the power solver result directly, matching the physics integration fixture.
            entities.GetComponent<PowerConsumerComponent>(dish).NetworkLoad.ReceivingPower = beam.MaxPower;
            Assert.That(entities.GetComponent<TransformComponent>(dish).Anchored, Is.True);
            return dish;
        }

        EntityUid MakeGrid()
        {
            var grid = maps.CreateGridEntity(map);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
            for (var x = 0; x < 4; x++)
            {
                for (var y = 0; y < 4; y++)
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
            }
            mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);
            physics.SetBodyType(grid.Owner, BodyType.Dynamic);
            Assert.That(entities.GetComponent<PhysicsComponent>(grid.Owner).Mass, Is.GreaterThan(0));
            return grid.Owner;
        }
    }
}
