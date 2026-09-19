using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.TractorBeam;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.TractorBeam;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed class TractorBeamTest
{
    private const float Step = 1f / 60f;

    [Test]
    public async Task PinCountersQueuedThrustAndTorqueChargesPowerAndSlipsDuringBrownout()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, _) = CreateLock(entities, maps, map.MapId);
            var system = entities.System<TractorBeamSystem>();
            var physics = entities.System<SharedPhysicsSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var power = entities.GetComponent<PowerConsumerComponent>(emitter);
            var sourceBody = entities.GetComponent<PhysicsComponent>(source);
            var targetBody = entities.GetComponent<PhysicsComponent>(target);
            system.UpdateBeforeSolve(false, Step);
            beam.LockedInPlace = true;
            beam.LockedSeparation = transform.ToMapCoordinates(new EntityCoordinates(target, targetBody.LocalCenter)).Position -
                transform.ToMapCoordinates(new EntityCoordinates(source, sourceBody.LocalCenter)).Position;
            beam.LockedAngle = (float) (transform.GetWorldRotation(target).Theta - transform.GetWorldRotation(source).Theta);
            var radial = Vector2.Normalize(beam.LockedSeparation);
            var thrust = -radial * beam.MaxForce * 0.1f;
            physics.ApplyForce(target, thrust);
            Assert.That(targetBody.InvI, Is.GreaterThan(0f));
            physics.ApplyTorque(target, beam.MaxForce * 0.05f);
            system.UpdateBeforeSolve(false, Step);

            // Controllers run before queued engine forces are integrated. The beam precompensates
            // exactly once for those forces, rather than merely seeing zero velocity and no strain.
            var predictedTargetVelocity = targetBody.LinearVelocity + targetBody.Force * targetBody.InvMass * Step;
            var predictedTargetRotation = targetBody.AngularVelocity + targetBody.Torque * targetBody.InvI * Step;
            var predictedSourceVelocity = sourceBody.LinearVelocity + sourceBody.Force * sourceBody.InvMass * Step;
            var predictedSourceRotation = sourceBody.AngularVelocity + sourceBody.Torque * sourceBody.InvI * Step;
            var poweredRadialSlip = MathF.Abs(Vector2.Dot(predictedTargetVelocity - predictedSourceVelocity, radial));
            Assert.That(poweredRadialSlip, Is.LessThan(0.0001f), "A powered pin fixes relative separation while the unbraked pair can accelerate together.");
            Assert.That(MathF.Abs(predictedTargetRotation - predictedSourceRotation), Is.LessThan(0.0001f),
                "A powered pin fixes relative orientation, rather than anchoring either hull to space.");
            Assert.That(predictedTargetVelocity.Length(), Is.GreaterThan(0f), "The free pair must retain the external thrust's momentum.");
            Assert.That(power.DrawRate, Is.GreaterThan(TractorBeamPhysics.CalculatePower(0,
                beam.MaxForce, beam.HoldingPower, beam.MaxPower, beam.DistanceStrain)));
            Assert.That(beam.RequiredForce, Is.GreaterThan(0f));
            Assert.That(sourceBody.LinearVelocity.Length(), Is.GreaterThan(0f), "An unbraked source still takes recoil.");
            Assert.That((sourceBody.LinearVelocity * sourceBody.Mass + targetBody.LinearVelocity * targetBody.Mass).Length(),
                Is.LessThan(0.01f), "Restraint transfers momentum to the arrestor, never a free anchor.");

            physics.SetLinearVelocity(source, Vector2.Zero);
            physics.SetLinearVelocity(target, Vector2.Zero);
            physics.SetAngularVelocity(source, 0);
            physics.SetAngularVelocity(target, 0);
            power.NetworkLoad.ReceivingPower = beam.HoldingPower;
            system.UpdateBeforeSolve(false, Step);
            predictedTargetVelocity = targetBody.LinearVelocity + targetBody.Force * targetBody.InvMass * Step;
            predictedSourceVelocity = sourceBody.LinearVelocity + sourceBody.Force * sourceBody.InvMass * Step;
            Assert.That(MathF.Abs(Vector2.Dot(predictedTargetVelocity - predictedSourceVelocity, radial)),
                Is.GreaterThan(poweredRadialSlip + 0.0001f), "Pinning cannot retain relative restraint without strain power.");
            Assert.That(beam.LockedInPlace, Is.True, "A brownout softens the constraint without silently re-capturing its pose.");
            Assert.That(power.DrawRate, Is.GreaterThan(beam.HoldingPower));
            system.Release(emitter, beam);
            Assert.That(beam.LockedInPlace, Is.False);
            Assert.That(beam.LockedSeparation, Is.EqualTo(Vector2.Zero));
            Assert.That(beam.LockedAngle, Is.Zero);
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SidewaysRestraintPreservesLockBearingAndSharesPowerBudget()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, _) = CreateLock(entities, maps, map.MapId);
            var system = entities.System<TractorBeamSystem>();
            var physics = entities.System<SharedPhysicsSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var sourceBody = entities.GetComponent<PhysicsComponent>(source);
            var targetBody = entities.GetComponent<PhysicsComponent>(target);
            var power = entities.GetComponent<PowerConsumerComponent>(emitter);
            system.UpdateBeforeSolve(false, Step);
            var bearing = beam.HoldDirection;
            var side = new Vector2(-bearing.Y, bearing.X);
            physics.SetLinearVelocity(target, side * 20f);
            var momentum = targetBody.LinearVelocity * targetBody.Mass;
            system.UpdateBeforeSolve(false, Step);
            Assert.That(Vector2.Dot(targetBody.LinearVelocity, side), Is.LessThan(20f));
            Assert.That(Vector2.Dot(sourceBody.LinearVelocity, side), Is.GreaterThan(0f));
            Assert.That(Vector2.Distance(momentum, targetBody.LinearVelocity * targetBody.Mass +
                sourceBody.LinearVelocity * sourceBody.Mass), Is.LessThan(0.1f));
            Assert.That(power.DrawRate, Is.GreaterThan(beam.HoldingPower));
            Assert.That(power.DrawRate, Is.LessThanOrEqualTo(beam.MaxPower));

            // Stopping after a sideways displacement must not make the new bearing the rest position.
            physics.SetLinearVelocity(source, Vector2.Zero);
            physics.SetLinearVelocity(target, Vector2.Zero);
            transform.SetWorldPosition(target, transform.GetWorldPosition(target) + side * 5f);
            system.UpdateBeforeSolve(false, Step);
            Assert.That(beam.HoldDirection, Is.EqualTo(bearing));
            Assert.That(Vector2.Dot(targetBody.LinearVelocity, side), Is.LessThan(0f));
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CoordinatedShipsCombineAuthorityWithoutCreatingMomentum()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, _, _) = CreateLock(entities, maps, map.MapId);
            var system = entities.System<TractorBeamSystem>();
            var physics = entities.System<SharedPhysicsSystem>();
            var targetBody = entities.GetComponent<PhysicsComponent>(target);
            physics.SetLinearVelocity(target, new Vector2(20, 0));
            system.UpdateBeforeSolve(false, Step);
            var singleBeamVelocity = targetBody.LinearVelocity.X;

            physics.SetLinearVelocity(source, Vector2.Zero);
            physics.SetLinearVelocity(target, new Vector2(20, 0));
            var (secondSource, _, _, _) = CreateLock(entities, maps, map.MapId, target, new Vector2(-15, 0));
            var initialMomentum = targetBody.LinearVelocity * targetBody.Mass;
            system.UpdateBeforeSolve(false, Step);

            var firstBody = entities.GetComponent<PhysicsComponent>(source);
            var secondBody = entities.GetComponent<PhysicsComponent>(secondSource);
            Assert.That(targetBody.LinearVelocity.X, Is.LessThan(singleBeamVelocity),
                "Two cooperating ships must restrain the target more strongly than one.");
            Assert.That(firstBody.LinearVelocity.X, Is.GreaterThan(0));
            Assert.That(secondBody.LinearVelocity.X, Is.GreaterThan(0));
            var finalMomentum = targetBody.LinearVelocity * targetBody.Mass +
                                firstBody.LinearVelocity * firstBody.Mass +
                                secondBody.LinearVelocity * secondBody.Mass;
            Assert.That(Vector2.Distance(initialMomentum, finalMomentum),
                Is.LessThan(initialMomentum.Length() * 0.00001f + 0.01f));

            entities.DeleteEntity(source);
            entities.DeleteEntity(secondSource);
            entities.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PoweredBeamConservesMomentumAndRespondsToResistance()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, _) = CreateLock(entities, maps, map.MapId);
            var system = entities.System<TractorBeamSystem>();
            var physics = entities.System<SharedPhysicsSystem>();
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var power = entities.GetComponent<PowerConsumerComponent>(emitter);
            var sourceBody = entities.GetComponent<PhysicsComponent>(source);
            var targetBody = entities.GetComponent<PhysicsComponent>(target);

            Assert.That(targetBody.Mass, Is.GreaterThan(sourceBody.Mass), "The target is the larger grid.");
            system.UpdateBeforeSolve(false, Step);
            Assert.That(beam.Active, Is.True);
            Assert.That(power.DrawRate, Is.EqualTo(TractorBeamPhysics.CalculatePower(0,
                beam.MaxForce, beam.HoldingPower, beam.MaxPower, beam.DistanceStrain)).Within(1f),
                "A stationary pair pays the range-dependent field-maintenance cost.");

            physics.SetLinearVelocity(target, new Vector2(20, 0));
            var momentumBefore = targetBody.LinearVelocity * targetBody.Mass;
            system.UpdateBeforeSolve(false, Step);

            Assert.Multiple(() =>
            {
                Assert.That(targetBody.LinearVelocity.X, Is.LessThan(20), "The target is restrained.");
                Assert.That(sourceBody.LinearVelocity.X, Is.GreaterThan(0), "The source feels the reaction force.");
                Assert.That(sourceBody.LinearVelocity.X, Is.GreaterThan(20 - targetBody.LinearVelocity.X),
                    "The smaller ship changes velocity more than the larger target.");
                Assert.That(beam.RequiredForce, Is.GreaterThan(0));
                Assert.That(beam.Strain, Is.InRange(0f, 1f));
                Assert.That(power.DrawRate, Is.GreaterThan(beam.HoldingPower),
                    "A resisting target increases the power demand.");
                Assert.That(power.DrawRate, Is.LessThanOrEqualTo(beam.MaxPower));
            });
            var momentumAfter = sourceBody.LinearVelocity * sourceBody.Mass + targetBody.LinearVelocity * targetBody.Mass;
            Assert.That(Vector2.Distance(momentumBefore, momentumAfter), Is.LessThan(momentumBefore.Length() * 0.00001f + 0.01f),
                "The beam cannot create momentum or anchor either ship to space.");

            var poweredTargetVelocity = targetBody.LinearVelocity.X;
            physics.SetLinearVelocity(source, Vector2.Zero);
            physics.SetLinearVelocity(target, new Vector2(20, 0));
            power.NetworkLoad.ReceivingPower = beam.HoldingPower;
            system.UpdateBeforeSolve(false, Step);
            Assert.That(targetBody.LinearVelocity.X, Is.GreaterThan(poweredTargetVelocity),
                "Power below the requested strain demand must reduce restraining authority.");

            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("range")]
    [TestCase("target-outside-cone")]
    [TestCase("dish-rotated-away")]
    [TestCase("source-rotated-away")]
    [TestCase("unanchor")]
    [TestCase("source-ftl")]
    [TestCase("target-ftl")]
    [TestCase("source-split")]
    [TestCase("target-split")]
    [TestCase("target-deleted")]
    [TestCase("controller-deleted")]
    [TestCase("power-loss")]
    [TestCase("static-target")]
    [TestCase("disabled-target-shuttle")]
    [TestCase("target-docked-to-static-station")]
    public async Task InvalidLocksReleaseAndResetPower(string reason)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, console) = CreateLock(entities, maps, map.MapId);
            var system = entities.System<TractorBeamSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var power = entities.GetComponent<PowerConsumerComponent>(emitter);
            EntityUid? dockedStation = null;
            system.UpdateBeforeSolve(false, Step);
            Assert.That(beam.Active, Is.True, "The test must start with a functioning lock.");
            Assert.That(beam.Visual, Is.Not.Null, "Active beams have a separately replicated visual.");
            var visual = beam.Visual!.Value;
            Assert.That(entities.HasComponent<TractorBeamVisualComponent>(visual), Is.True);
            Assert.That(entities.GetComponent<TransformComponent>(visual).ParentUid, Is.EqualTo(source),
                "The visual must depend on the grid, not reveal its dish through a parent visibility override.");

            switch (reason)
            {
                case "range":
                    transform.SetWorldPosition(target, new Vector2(beam.MaxRange + 100, 0));
                    break;
                case "target-outside-cone":
                    transform.SetWorldPosition(target, new Vector2(0, 50));
                    break;
                case "dish-rotated-away":
                    transform.SetWorldRotation(emitter, Angle.Zero);
                    break;
                case "source-rotated-away":
                    transform.SetWorldRotation(source, Angle.FromDegrees(90));
                    break;
                case "unanchor":
                    transform.Unanchor(emitter);
                    break;
                case "source-ftl":
                    entities.AddComponent<FTLComponent>(source);
                    break;
                case "target-ftl":
                    entities.AddComponent<FTLComponent>(target);
                    break;
                case "source-split":
                case "target-split":
                    var splitGrid = reason == "source-split" ? source : target;
                    var split = new GridSplitEvent(Array.Empty<EntityUid>(), splitGrid);
                    // Match the engine's directed event plus broadcast delivery. Fragment geometry
                    // belongs to the engine; this verifies the beam's reaction to that lifecycle event.
                    entities.EventBus.RaiseLocalEvent(splitGrid, ref split, true);
                    break;
                case "target-deleted":
                    entities.DeleteEntity(target);
                    break;
                case "controller-deleted":
                    entities.DeleteEntity(console);
                    break;
                case "power-loss":
                    power.NetworkLoad.ReceivingPower = 0;
                    beam.PowerGraceUntil = TimeSpan.Zero;
                    break;
                case "static-target":
                    entities.System<SharedPhysicsSystem>().SetBodyType(target, BodyType.Static);
                    break;
                case "disabled-target-shuttle":
                    entities.GetComponent<ShuttleComponent>(target).Enabled = false;
                    break;
                case "target-docked-to-static-station":
                    var station = maps.CreateGridEntity(map.MapId);
                    dockedStation = station.Owner;
                    entities.System<SharedMapSystem>().SetTile(station, Vector2i.Zero, new Tile(1));
                    transform.SetWorldPosition(station.Owner, new Vector2(58, 0));
                    entities.System<SharedPhysicsSystem>().SetBodyType(station.Owner, BodyType.Static);
                    var targetDock = entities.SpawnEntity("AirlockShuttle",
                        new EntityCoordinates(target, new Vector2(7.5f, 0.5f)));
                    var stationDock = entities.SpawnEntity("AirlockShuttle",
                        new EntityCoordinates(station.Owner, new Vector2(0.5f, 0.5f)));
                    entities.System<DockingSystem>().Dock(
                        (targetDock, entities.GetComponent<DockingComponent>(targetDock)),
                        (stationDock, entities.GetComponent<DockingComponent>(stationDock)));
                    Assert.That(entities.System<DockingSystem>().AreGridsDocked(target, station.Owner), Is.True);
                    break;
                default:
                    Assert.Fail($"Unknown invalidation case: {reason}");
                    break;
            }

            system.UpdateBeforeSolve(false, Step);
            Assert.Multiple(() =>
            {
                Assert.That(beam.Target, Is.Null);
                Assert.That(beam.SourceGrid, Is.Null);
                Assert.That(beam.Controller, Is.Null);
                Assert.That(beam.Active, Is.False);
                Assert.That(beam.Strain, Is.Zero);
                Assert.That(beam.RequiredForce, Is.Zero);
                Assert.That(beam.Visual, Is.Null);
                Assert.That(entities.Deleted(visual), Is.True, "Released beams must remove their visual entity.");
                Assert.That(power.DrawRate, Is.EqualTo(reason == "unanchor" ? 0 : beam.IdlePower));
            });

            entities.DeleteEntity(source);
            if (!entities.Deleted(target))
                entities.DeleteEntity(target);
            if (dockedStation is { } stationUid)
                entities.DeleteEntity(stationUid);
        });

        await pair.CleanReturnAsync();
    }

    internal static (EntityUid Source, EntityUid Target, EntityUid Emitter, EntityUid Console) CreateLock(
        IEntityManager entities, IMapManager maps, MapId map, EntityUid? existingTarget = null, Vector2 sourcePosition = default,
        string emitterPrototype = "WFTractorBeamEmitter")
    {
        var mapSystem = entities.System<SharedMapSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var transform = entities.System<SharedTransformSystem>();
        var source = MakeGrid(4);
        var target = existingTarget ?? MakeGrid(8);
        transform.SetWorldPosition(source, sourcePosition);
        if (existingTarget == null)
            transform.SetWorldPosition(target, new Vector2(50, 0));

        // Spawn real machinery prototypes so component registrations, sprite references, circuitboards,
        // BUI registration and anchoring participate in the same integration test.
        var emitter = entities.SpawnEntity(emitterPrototype, new EntityCoordinates(source, new Vector2(0.5f, 0.5f)));
        transform.SetWorldRotation(emitter, Angle.FromDegrees(-90));
        var console = entities.SpawnEntity("WFComputerTractorBeam", new EntityCoordinates(source, new Vector2(1.5f, 0.5f)));
        Assert.That(entities.GetComponent<TransformComponent>(emitter).Anchored, Is.True);
        Assert.That(entities.GetComponent<TransformComponent>(console).Anchored, Is.True);

        // Supply the solver's output directly: this tests the beam's power contract without requiring
        // a separate station power network or allowing unrelated machinery updates between assertions.
        entities.GetComponent<ApcPowerReceiverComponent>(console).Powered = true;
        var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
        entities.GetComponent<PowerConsumerComponent>(emitter).NetworkLoad.ReceivingPower = beam.MaxPower;
        var sourceBody = entities.GetComponent<PhysicsComponent>(source);
        var targetBody = entities.GetComponent<PhysicsComponent>(target);
        Assert.That(sourceBody.Mass, Is.GreaterThan(0));
        Assert.That(targetBody.Mass, Is.GreaterThan(0));
        beam.SourceGrid = source;
        beam.Controller = console;
        beam.Target = target;
        beam.TargetOffset = targetBody.LocalCenter;
        beam.HoldDistance = Vector2.Distance(
            transform.ToMapCoordinates(new EntityCoordinates(source, sourceBody.LocalCenter)).Position,
            transform.ToMapCoordinates(new EntityCoordinates(target, targetBody.LocalCenter)).Position);
        beam.PowerGraceUntil = TimeSpan.Zero;
        return (source, target, emitter, console);

        EntityUid MakeGrid(int side)
        {
            var grid = maps.CreateGridEntity(map);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
            for (var x = 0; x < side; x++)
            {
                for (var y = 0; y < side; y++)
                {
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                }
            }

            mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);
            physics.SetBodyType(grid.Owner, BodyType.Dynamic);
            return grid.Owner;
        }
    }
}
