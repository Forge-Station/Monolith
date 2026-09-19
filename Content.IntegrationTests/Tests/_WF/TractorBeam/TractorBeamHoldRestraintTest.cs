using System.Numerics;
using Content.Server._WF.TractorBeam;
using Content.Server.Power.Components;
using Content.Shared._WF.TractorBeam;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed class TractorBeamHoldRestraintTest
{
    private const float Step = 1f / 60f;

    [TestCase(false)]
    [TestCase(true)]
    public async Task HoldResistsClosingVelocityAndPendingThrustWithPowerLimitedReaction(bool queuedThrust)
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
            var system = entities.System<TractorBeamSystem>();
            var physics = entities.System<SharedPhysicsSystem>();
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var power = entities.GetComponent<PowerConsumerComponent>(emitter);
            var sourceBody = entities.GetComponent<PhysicsComponent>(source);
            var targetBody = entities.GetComponent<PhysicsComponent>(target);
            system.UpdateBeforeSolve(false, Step);
            var bearing = beam.HoldDirection;
            if (queuedThrust)
                physics.ApplyForce(target, -bearing * beam.MaxForce * 0.1f);
            else
                physics.SetLinearVelocity(target, -bearing * 10f);
            var initialVelocity = targetBody.LinearVelocity;
            var initialMomentum = initialVelocity * targetBody.Mass;
            var unrestrained = initialVelocity + targetBody.Force * targetBody.InvMass * Step;
            system.UpdateBeforeSolve(false, Step);
            var poweredVelocity = targetBody.LinearVelocity + targetBody.Force * targetBody.InvMass * Step;
            Assert.Multiple(() =>
            {
                Assert.That(beam.LockedInPlace, Is.False, "Ordinary Hold must restrain inward movement.");
                Assert.That(Vector2.Dot(poweredVelocity - unrestrained, bearing), Is.GreaterThan(0f));
                Assert.That(Vector2.Dot(sourceBody.LinearVelocity, bearing), Is.LessThan(0f),
                    "Pushing the target back transfers an opposite reaction to the arrestor.");
                Assert.That(Vector2.Distance(initialMomentum, targetBody.LinearVelocity * targetBody.Mass +
                    sourceBody.LinearVelocity * sourceBody.Mass), Is.LessThan(0.1f));
                Assert.That(power.DrawRate, Is.GreaterThan(beam.HoldingPower));
                Assert.That(beam.Strain, Is.GreaterThan(0f));
            });

            physics.SetLinearVelocity(source, Vector2.Zero);
            physics.SetLinearVelocity(target, initialVelocity);
            power.NetworkLoad.ReceivingPower = beam.HoldingPower;
            system.UpdateBeforeSolve(false, Step);
            var brownoutVelocity = targetBody.LinearVelocity + targetBody.Force * targetBody.InvMass * Step;
            Assert.That(Vector2.Dot(brownoutVelocity, bearing), Is.LessThan(Vector2.Dot(poweredVelocity, bearing)),
                "Inward restraint must weaken when resistance power is unavailable.");
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false, false, -1f)]
    [TestCase(false, false, 1f)]
    [TestCase(false, true, -1f)]
    [TestCase(false, true, 1f)]
    [TestCase(true, false, -1f)]
    [TestCase(true, false, 1f)]
    [TestCase(true, true, -1f)]
    [TestCase(true, true, 1f)]
    public async Task HoldAndPullResistBothRotationDirectionsAndQueuedGyros(bool pulling, bool queuedTorque, float sign)
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
            var system = entities.System<TractorBeamSystem>();
            var physics = entities.System<SharedPhysicsSystem>();
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var power = entities.GetComponent<PowerConsumerComponent>(emitter);
            var sourceBody = entities.GetComponent<PhysicsComponent>(source);
            var targetBody = entities.GetComponent<PhysicsComponent>(target);
            system.UpdateBeforeSolve(false, Step);
            var initialHoldDistance = beam.HoldDistance;
            beam.Pulling = pulling;
            Assert.That(sourceBody.InvI, Is.GreaterThan(0f));
            Assert.That(targetBody.InvI, Is.GreaterThan(0f));
            if (queuedTorque)
                physics.ApplyTorque(target, sign * beam.MaxForce * 0.05f);
            else
                physics.SetAngularVelocity(target, sign * 0.5f);
            var initialAngularVelocity = targetBody.AngularVelocity;
            var initialAngularMomentum = initialAngularVelocity / targetBody.InvI;
            var unrestrained = initialAngularVelocity + targetBody.Torque * targetBody.InvI * Step;
            system.UpdateBeforeSolve(false, Step);
            var poweredVelocity = targetBody.AngularVelocity + targetBody.Torque * targetBody.InvI * Step;
            var transform = entities.System<SharedTransformSystem>();
            var sourceCenter = transform.ToMapCoordinates(new EntityCoordinates(source, sourceBody.LocalCenter)).Position;
            var targetCenter = transform.ToMapCoordinates(new EntityCoordinates(target, targetBody.LocalCenter)).Position;
            var sourceMomentum = sourceBody.LinearVelocity * sourceBody.Mass;
            var targetMomentum = targetBody.LinearVelocity * targetBody.Mass;
            var orbitalMomentum = sourceCenter.X * sourceMomentum.Y - sourceCenter.Y * sourceMomentum.X +
                targetCenter.X * targetMomentum.Y - targetCenter.Y * targetMomentum.X;
            Assert.Multiple(() =>
            {
                Assert.That(beam.LockedInPlace, Is.False);
                Assert.That(sign * (poweredVelocity - unrestrained), Is.LessThan(0f));
                Assert.That(sign * sourceBody.AngularVelocity, Is.GreaterThan(0f));
                Assert.That(sourceBody.AngularVelocity / sourceBody.InvI + targetBody.AngularVelocity / targetBody.InvI + orbitalMomentum,
                    Is.EqualTo(initialAngularMomentum).Within(MathF.Abs(initialAngularMomentum) * 0.00001f + 0.1f),
                    "Angular restraint conserves combined spin and orbital angular momentum.");
                Assert.That(power.DrawRate, Is.GreaterThan(beam.HoldingPower));
                Assert.That(power.DrawRate, Is.LessThanOrEqualTo(beam.MaxPower));
                Assert.That(beam.Strain, Is.GreaterThan(0f));
            });
            if (pulling)
            {
                Assert.That(beam.HoldDistance, Is.LessThan(initialHoldDistance));
                Assert.That(Vector2.Dot(targetBody.LinearVelocity, beam.HoldDirection), Is.LessThan(0f),
                    "Rotational restraint must not disable commanded reeling.");
            }

            physics.SetLinearVelocity(source, Vector2.Zero);
            physics.SetLinearVelocity(target, Vector2.Zero);
            physics.SetAngularVelocity(source, 0);
            physics.SetAngularVelocity(target, initialAngularVelocity);
            beam.HoldDistance = initialHoldDistance;
            power.NetworkLoad.ReceivingPower = beam.HoldingPower;
            system.UpdateBeforeSolve(false, Step);
            var brownoutVelocity = targetBody.AngularVelocity + targetBody.Torque * targetBody.InvI * Step;
            Assert.That(sign * brownoutVelocity, Is.GreaterThan(sign * poweredVelocity),
                "Rotation cannot be restrained for free when variable power is exhausted.");
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(-1f)]
    [TestCase(1f)]
    public async Task HoldRestoresCapturedAngleAcrossWraparoundWithoutRecapturingDrift(float sign)
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
            var system = entities.System<TractorBeamSystem>();
            var physics = entities.System<SharedPhysicsSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var sourceBody = entities.GetComponent<PhysicsComponent>(source);
            var targetBody = entities.GetComponent<PhysicsComponent>(target);
            var center = transform.ToMapCoordinates(new EntityCoordinates(target, targetBody.LocalCenter)).Position;
            SetAnglePreservingCenter(sign * 179f);
            system.UpdateBeforeSolve(false, Step);
            Assert.That(targetBody.AngularVelocity, Is.EqualTo(0f).Within(0.0001f),
                "A nonzero initial orientation is captured, not forced to the arrestor's orientation.");
            SetAnglePreservingCenter(-sign * 179f);
            for (var repeat = 0; repeat < 2; repeat++)
            {
                physics.SetLinearVelocity(source, Vector2.Zero);
                physics.SetLinearVelocity(target, Vector2.Zero);
                physics.SetAngularVelocity(source, 0);
                physics.SetAngularVelocity(target, 0);
                system.UpdateBeforeSolve(false, Step);
                Assert.That(sign * targetBody.AngularVelocity, Is.LessThan(0f),
                    "Correct the two-degree drift through the shortest angle on every update.");
                Assert.That(sign * sourceBody.AngularVelocity, Is.GreaterThan(0f));
                Assert.That(beam.LockedInPlace, Is.False);
            }
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);

            void SetAnglePreservingCenter(float degrees)
            {
                transform.SetWorldRotation(target, Angle.FromDegrees(degrees));
                var movedCenter = transform.ToMapCoordinates(new EntityCoordinates(target, targetBody.LocalCenter)).Position;
                transform.SetWorldPosition(target, transform.GetWorldPosition(target) + center - movedCenter);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HoldRestoresOriginalDistanceAfterTargetDriftsCloser()
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
            var system = entities.System<TractorBeamSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            system.UpdateBeforeSolve(false, Step);
            var holdDistance = beam.HoldDistance;
            transform.SetWorldPosition(target, transform.GetWorldPosition(target) - beam.HoldDirection * 5f);
            system.UpdateBeforeSolve(false, Step);
            Assert.That(beam.HoldDistance, Is.EqualTo(holdDistance));
            Assert.That(Vector2.Dot(entities.GetComponent<PhysicsComponent>(target).LinearVelocity, beam.HoldDirection),
                Is.GreaterThan(0f), "A stopped target inside the captured distance must be pushed back out.");
            Assert.That(entities.GetComponent<PowerConsumerComponent>(emitter).DrawRate, Is.GreaterThan(beam.HoldingPower));
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }
}
