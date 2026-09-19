using System.Numerics;
using Content.Server._WF.TractorBeam;
using Content.Server.Physics.Controllers;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.TractorBeam;
using Content.Shared.Movement.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed class TractorBeamStationKeepingTest
{
    private const float Step = 1f / 60f;

    [TestCase("active", true)]
    [TestCase("released", false)]
    [TestCase("power-loss", false)]
    [TestCase("unanchored", false)]
    public async Task OnlyActivePoweredSourceRequestsActualShuttleBrakes(string state, bool expectedBraking)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            var beams = entities.System<TractorBeamSystem>();
            var stationKeeping = entities.System<TractorBeamStationKeepingSystem>();
            var mover = entities.System<MoverController>();
            var physics = entities.System<SharedPhysicsSystem>();
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var shuttle = entities.GetComponent<ShuttleComponent>(source);
            var body = entities.GetComponent<PhysicsComponent>(source);

            beams.UpdateBeforeSolve(false, Step);
            stationKeeping.UpdateBeforeSolve(false, Step);
            Assert.That(beam.Active, Is.True);
            Assert.That(entities.HasComponent<PilotedShuttleComponent>(source), Is.True,
                "Station keeping must work with nobody at the helm.");

            // The thruster system publishes available powered thrust here. Exercise the
            // real mover and its force limits without an unrelated power-network simulation.
            Array.Fill(shuttle.LinearThrust, 1000f);
            shuttle.AngularThrust = 1000f;
            physics.SetLinearVelocity(source, new Vector2(10, 0));
            physics.SetAngularVelocity(source, 1f);
            switch (state)
            {
                case "released":
                    beams.Release(emitter, beam);
                    break;
                case "power-loss":
                    entities.GetComponent<PowerConsumerComponent>(emitter).NetworkLoad.ReceivingPower = 0;
                    break;
                case "unanchored":
                    entities.System<SharedTransformSystem>().Unanchor(emitter);
                    break;
            }

            var forceBefore = body.Force;
            var torqueBefore = body.Torque;
            mover.UpdateBeforeSolve(false, Step);
            stationKeeping.UpdateBeforeSolve(false, Step);
            var appliedForce = body.Force - forceBefore;
            var appliedTorque = body.Torque - torqueBefore;
            if (expectedBraking)
            {
                Assert.That(appliedForce.X, Is.LessThan(0));
                Assert.That(appliedForce.Length(), Is.LessThanOrEqualTo(1500f));
                Assert.That(appliedTorque, Is.LessThan(0));
                Assert.That(shuttle.ThrustDirections, Is.Not.EqualTo(Robust.Shared.Maths.DirectionFlag.None));
            }
            else
            {
                Assert.That(appliedForce, Is.EqualTo(Vector2.Zero));
                Assert.That(appliedTorque, Is.Zero);
                Assert.That(shuttle.ThrustDirections, Is.EqualTo(Robust.Shared.Maths.DirectionFlag.None));
            }

            Assert.That(body.LinearVelocity.X, Is.EqualTo(10f),
                "Station keeping applies engine force; it must not directly set a ship's velocity.");
            Assert.That(entities.GetComponent<PhysicsComponent>(target).Force, Is.EqualTo(Vector2.Zero),
                "Being captured does not give the target automatic thruster control.");
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(0f, 1f, 0f, 3, DirectionFlag.West)]
    [TestCase(1000f, 1f, 0f, 3, DirectionFlag.West)]
    [TestCase(0f, -1f, 0f, 1, DirectionFlag.East)]
    [TestCase(1000f, -1f, 0f, 1, DirectionFlag.East)]
    [TestCase(0f, 0f, 1f, 0, DirectionFlag.South)]
    [TestCase(1000f, 0f, 1f, 0, DirectionFlag.South)]
    [TestCase(0f, 0f, -1f, 2, DirectionFlag.North)]
    [TestCase(1000f, 0f, -1f, 2, DirectionFlag.North)]
    public async Task BrakingRequiresThrustInTheOpposingDirection(float opposingThrust, float x, float y,
        int opposingIndex, DirectionFlag firingDirection)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, _, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, Step);
            entities.System<TractorBeamStationKeepingSystem>().UpdateBeforeSolve(false, Step);
            var shuttle = entities.GetComponent<ShuttleComponent>(source);
            var body = entities.GetComponent<PhysicsComponent>(source);
            // Missing or unpowered engines contribute zero to this cache and cannot
            // hold the ship still. Both the physical force and firing visuals must agree.
            var direction = new Vector2(x, y);
            Array.Clear(shuttle.LinearThrust);
            shuttle.LinearThrust[opposingIndex] = opposingThrust;
            entities.System<SharedPhysicsSystem>().SetLinearVelocity(source, direction * 10);
            var forceBefore = body.Force;
            entities.System<MoverController>().UpdateBeforeSolve(false, Step);
            entities.System<TractorBeamStationKeepingSystem>().UpdateBeforeSolve(false, Step);
            var expectedForce = -direction * MathF.Min(opposingThrust * ShuttleComponent.BrakeCoefficient, body.Mass * 10 / Step);
            Assert.That(Vector2.Distance(body.Force - forceBefore, expectedForce), Is.LessThan(0.01f));
            Assert.That(body.LinearVelocity, Is.EqualTo(direction * 10));
            Assert.That(shuttle.ThrustDirections, Is.EqualTo(opposingThrust > 0 ? firingDirection : DirectionFlag.None));
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task PilotOverridesTranslationAndRotationIndependently(bool translate)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, _, console) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, Step);
            entities.System<TractorBeamStationKeepingSystem>().UpdateBeforeSolve(false, Step);
            var mover = entities.System<MoverController>();
            var physics = entities.System<SharedPhysicsSystem>();
            var shuttle = entities.GetComponent<ShuttleComponent>(source);
            var body = entities.GetComponent<PhysicsComponent>(source);
            Array.Fill(shuttle.LinearThrust, 1000f);
            shuttle.AngularThrust = 1000f;
            physics.SetLinearVelocity(source, new Vector2(10, 0));
            physics.SetAngularVelocity(source, 1f);

            var pilotUid = entities.SpawnEntity(null, new EntityCoordinates(source, Vector2.One));
            var pilot = entities.AddComponent<PilotComponent>(pilotUid);
            pilot.Console = console;
            pilot.HeldButtons = translate ? ShuttleButtons.StrafeRight : ShuttleButtons.RotateLeft;
            mover.AddPilot(source, pilotUid);
            var forceBefore = body.Force;
            var torqueBefore = body.Torque;
            mover.UpdateBeforeSolve(false, Step);
            entities.System<TractorBeamStationKeepingSystem>().UpdateBeforeSolve(false, Step);

            if (translate)
            {
                Assert.That((body.Force - forceBefore).X, Is.GreaterThan(0), "Pilot translation wins over auto-braking.");
                Assert.That(body.Torque - torqueBefore, Is.LessThan(0), "Gyros still brake uncommanded rotation.");
            }
            else
            {
                Assert.That((body.Force - forceBefore).X, Is.LessThan(0), "Turning does not disable linear station keeping.");
                Assert.That(body.Torque - torqueBefore, Is.GreaterThan(0), "Pilot rotation wins over gyro braking.");
            }

            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(50000f, true)]
    [TestCase(0f, false)]
    public async Task SustainedPinnedResistanceNeedsRealCounterthrust(float engineThrust, bool holdsPosition)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            var physics = entities.System<SharedPhysicsSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var sourceBody = entities.GetComponent<PhysicsComponent>(source);
            var targetBody = entities.GetComponent<PhysicsComponent>(target);
            var shuttle = entities.GetComponent<ShuttleComponent>(source);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            Array.Fill(shuttle.LinearThrust, engineThrust);
            shuttle.AngularThrust = engineThrust;

            // Drive the target through the actual helm path on every physics substep.
            // Injecting a Force before Update would supply only its first substep, because
            // the engine clears forces after each solve and runs controllers again.
            var targetShuttle = entities.GetComponent<ShuttleComponent>(target);
            targetShuttle.LinearThrust[1] = 1500f;
            targetShuttle.LinearThrust[2] = 700f;
            targetShuttle.AngularThrust = 100f;
            var targetHelm = entities.SpawnEntity("ComputerShuttle", new EntityCoordinates(target, Vector2.One * 0.5f));
            var targetPilot = entities.SpawnEntity(null, new EntityCoordinates(target, Vector2.One));
            var pilot = entities.AddComponent<PilotComponent>(targetPilot);
            pilot.Console = targetHelm;
            pilot.HeldButtons = ShuttleButtons.StrafeRight | ShuttleButtons.StrafeUp | ShuttleButtons.RotateLeft;
            entities.System<MoverController>().AddPilot(target, targetPilot);
            var sourcePosition = transform.GetWorldPosition(source);
            var targetPosition = transform.GetWorldPosition(target);
            beam.LockedInPlace = true;
            beam.LockedSeparation =
                transform.ToMapCoordinates(new EntityCoordinates(target, targetBody.LocalCenter)).Position -
                transform.ToMapCoordinates(new EntityCoordinates(source, sourceBody.LocalCenter)).Position;
            beam.LockedAngle = 0;

            // Exercise the actual physics-controller ordering, force integration and position
            // updates for two seconds. Power/thrust caches stay supplied throughout this test;
            // no explicit beam or stationkeeping calls can conceal an ordering regression.
            var resistedWhileActive = false;
            var releasedOutsideCone = false;
            for (var tick = 0; tick < 120; tick++)
            {
                physics.Update(Step);
                resistedWhileActive |= beam.Active && beam.RequiredForce > 0 && beam.RequestedPower > beam.HoldingPower;
                if (beam.Target != null)
                    continue;

                var targetCenter = transform.ToMapCoordinates(new EntityCoordinates(target, targetBody.LocalCenter)).Position;
                var offset = targetCenter - transform.GetWorldPosition(emitter);
                var forward = transform.GetWorldRotation(emitter).RotateVec(Vector2.UnitY);
                releasedOutsideCone = offset.Length() < beam.MaxRange &&
                    !TractorBeamOperatingCone.Contains(offset, forward, beam.MaxRange, beam.ConeHalfAngle);
                break;
            }

            Assert.That(resistedWhileActive, Is.True,
                "The test must establish active restraint and increased power before any cone loss.");
            Assert.That(targetShuttle.LastThrust.Length(), Is.GreaterThan(0),
                "The target must still be actively firing its engines at the final physics substep.");
            if (holdsPosition)
            {
                Assert.That(beam.Active, Is.True);
                Assert.That(beam.RequiredForce, Is.GreaterThan(0));
                Assert.That(beam.RequestedPower, Is.GreaterThan(beam.HoldingPower));
                Assert.That(Vector2.Distance(transform.GetWorldPosition(source), sourcePosition), Is.LessThan(0.01f),
                    "Adequate powered engines must cancel this tick's recoil, without cumulative source drift.");
                Assert.That(Vector2.Distance(transform.GetWorldPosition(target), targetPosition), Is.LessThan(0.01f),
                    "The pinned target must not retain the common drift from a reduced-mass spring.");
                Assert.That(sourceBody.LinearVelocity.Length(), Is.LessThan(0.01f));
                Assert.That(targetBody.LinearVelocity.Length(), Is.LessThan(0.01f));
                Assert.That(MathF.Abs(sourceBody.AngularVelocity), Is.LessThan(0.01f));
                Assert.That(MathF.Abs(targetBody.AngularVelocity), Is.LessThan(0.01f));
                Assert.That(shuttle.ThrustDirections, Is.Not.EqualTo(DirectionFlag.None),
                    "Counterthrust must visibly fire even while the arrestor is held stationary.");
            }
            else
            {
                Assert.That(Vector2.Distance(transform.GetWorldPosition(source), sourcePosition), Is.GreaterThan(0.01f),
                    "Without engines, recoil must physically move the source instead of anchoring it to space.");
                Assert.That(shuttle.ThrustDirections, Is.EqualTo(DirectionFlag.None));
                if (beam.Target == null)
                {
                    Assert.That(beam.Active, Is.False);
                    Assert.That(beam.RequestedPower, Is.EqualTo(beam.IdlePower));
                    Assert.That(releasedOutsideCone || beam.CooldownRemaining > 0, Is.True,
                        "A released beam must either lose its operating cone or enter its release cooldown.");
                }
                else
                {
                    Assert.That(beam.Active, Is.True,
                        "A powerless arrestor can remain linked while both hulls move under recoil.");
                    Assert.That(beam.Target, Is.EqualTo(target));
                    Assert.That(targetBody.LinearVelocity.Length(), Is.GreaterThan(0.01f),
                        "An unbraked arrestor cannot keep the resisting target anchored to its original world position.");
                    var targetCenter = transform.ToMapCoordinates(new EntityCoordinates(target, targetBody.LocalCenter)).Position;
                    var offset = targetCenter - transform.GetWorldPosition(emitter);
                    var forward = transform.GetWorldRotation(emitter).RotateVec(Vector2.UnitY);
                    Assert.That(TractorBeamOperatingCone.Contains(offset, forward, beam.MaxRange, beam.ConeHalfAngle), Is.True,
                        "The linked target may follow the rotating dish and remain inside its cone.");
                }
            }

            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LateStationKeepingReplacesManualBrakingWithoutDoublingThrust()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, _, console) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, Step);
            var mover = entities.System<MoverController>();
            var physics = entities.System<SharedPhysicsSystem>();
            var shuttle = entities.GetComponent<ShuttleComponent>(source);
            var body = entities.GetComponent<PhysicsComponent>(source);
            Array.Fill(shuttle.LinearThrust, 1000f);
            physics.SetLinearVelocity(source, new Vector2(10, 0));
            var pilotUid = entities.SpawnEntity(null, new EntityCoordinates(source, Vector2.One));
            var pilot = entities.AddComponent<PilotComponent>(pilotUid);
            pilot.Console = console;
            pilot.HeldButtons = ShuttleButtons.Brake;
            mover.AddPilot(source, pilotUid);
            mover.UpdateBeforeSolve(false, Step);
            Assert.That(body.Force.X, Is.EqualTo(-1500).Within(0.01f));

            // A late external force must be included, but the already queued helm brake
            // consumes the same engines, not an independent second budget.
            physics.ApplyForce(source, new Vector2(3000, 0), body: body);
            entities.System<TractorBeamStationKeepingSystem>().UpdateBeforeSolve(false, Step);
            Assert.That(body.Force.X - 3000, Is.EqualTo(-1500).Within(0.01f));
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }
}
