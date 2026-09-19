using System.Numerics;
using Content.Server._WF.TractorBeam;
using Content.Server.Power.Components;
using Content.Shared._WF.TractorBeam;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed class TractorBeamDistanceLoadTest
{
    private const float Step = 1f / 60f;

    [Test]
    public async Task StationaryDistanceConsumesPowerWithoutInventingMechanicalStrain()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var maps = pair.Server.ResolveDependency<IMapManager>();
        await pair.Server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var power = entities.GetComponent<PowerConsumerComponent>(emitter);
            var system = entities.System<TractorBeamSystem>();
            beam.CreakInitialMinimumDelay = beam.CreakInitialMaximumDelay = 0;
            var previousPower = beam.HoldingPower;
            var previousStrain = 0f;

            foreach (var fraction in new[] { 0.2f, 0.8f, 1f })
            {
                PlacePinnedTarget(entities, source, target, emitter, beam.MaxRange * fraction);
                for (var tick = 0; tick < 4; tick++)
                    system.UpdateBeforeSolve(false, Step);

                var expectedDistanceStrain = beam.RangeStrainAtMaxRange * fraction * fraction;
                Assert.That(beam.Active, Is.True, "The configured range boundary remains usable.");
                Assert.That(beam.DistanceStrain, Is.EqualTo(expectedDistanceStrain).Within(0.00001f));
                Assert.That(power.DrawRate, Is.GreaterThan(previousPower));
                Assert.That(power.DrawRate, Is.EqualTo(beam.HoldingPower +
                    (beam.MaxPower - beam.HoldingPower) * expectedDistanceStrain).Within(2f));
                Assert.That(beam.Strain, Is.GreaterThan(previousStrain));
                Assert.That(beam.RequiredForce, Is.EqualTo(0f).Within(0.001f),
                    "Distance is an electrical cost, not fictional resistance or force.");
                foreach (var uid in new[] { source, target })
                {
                    var body = entities.GetComponent<PhysicsComponent>(uid);
                    Assert.That(body.LinearVelocity.Length(), Is.LessThan(0.00001f));
                    Assert.That(MathF.Abs(body.AngularVelocity), Is.LessThan(0.00001f));
                }
                Assert.That(beam.Visual, Is.Not.Null);
                Assert.That(entities.GetComponent<TractorBeamVisualComponent>(beam.Visual!.Value).Strain,
                    Is.EqualTo(beam.Strain), "The beam effect reflects electrical distance load.");
                AssertNoCreaks(entities);
                previousPower = power.DrawRate;
                previousStrain = beam.Strain;
            }

            PlacePinnedTarget(entities, source, target, emitter, beam.MaxRange + 0.1f);
            system.UpdateBeforeSolve(false, Step);
            Assert.That(beam.Target, Is.Null);
            Assert.That(beam.DistanceStrain, Is.Zero);
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EqualResistanceCostsMoreAndHasLessAvailableAuthorityAtLongRange(bool partialPower)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var maps = pair.Server.ResolveDependency<IMapManager>();
        await pair.Server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var power = entities.GetComponent<PowerConsumerComponent>(emitter);
            var system = entities.System<TractorBeamSystem>();
            var physics = entities.System<SharedPhysicsSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var sourceBody = entities.GetComponent<PhysicsComponent>(source);
            var targetBody = entities.GetComponent<PhysicsComponent>(target);
            // A queued engine force persists through each beam substep. This exercises
            // sustained resistance, rather than a one-off velocity that may already stop.
            physics.ApplyForce(target, Vector2.UnitX * beam.MaxForce * 0.5f);
            if (partialPower)
            {
                var farDistanceStrain = beam.RangeStrainAtMaxRange * 0.8f * 0.8f;
                power.NetworkLoad.ReceivingPower = beam.HoldingPower +
                    (beam.MaxPower - beam.HoldingPower) * (farDistanceStrain + 0.1f);
            }

            PlaceRadialPinnedTarget(beam.MaxRange * 0.2f);
            system.UpdateBeforeSolve(false, Step);
            var nearPower = power.DrawRate;
            var nearForce = beam.RequiredForce;
            var nearResidual = targetBody.LinearVelocity.X + targetBody.Force.X * targetBody.InvMass * Step -
                sourceBody.LinearVelocity.X;
            PlaceRadialPinnedTarget(beam.MaxRange * 0.8f);
            system.UpdateBeforeSolve(false, Step);
            var farResidual = targetBody.LinearVelocity.X + targetBody.Force.X * targetBody.InvMass * Step -
                sourceBody.LinearVelocity.X;

            Assert.That(beam.Active, Is.True);
            if (partialPower)
            {
                // Unopposed thrust accumulates during a brownout, so either distance can
                // reach maximum demand even though their delivered restraint differs.
                Assert.That(power.DrawRate, Is.GreaterThanOrEqualTo(nearPower));
                Assert.That(power.DrawRate, Is.LessThanOrEqualTo(beam.MaxPower));
                Assert.That(nearResidual, Is.GreaterThanOrEqualTo(-0.0001f),
                    "The available supply may fully restrain the nearer target, but must not reverse its resistance.");
                Assert.That(farResidual, Is.GreaterThan(nearResidual + 0.0001f),
                    "The same supply leaves less force authority after paying the longer beam's baseline cost.");
            }
            else
            {
                Assert.That(power.DrawRate, Is.GreaterThan(nearPower));
                var expectedTransferredForce = beam.MaxForce * 0.5f * sourceBody.Mass / (sourceBody.Mass + targetBody.Mass);
                Assert.That(nearForce, Is.EqualTo(expectedTransferredForce).Within(0.1f),
                    "The beam shares the queued thrust between both free hulls according to their masses.");
                Assert.That(beam.RequiredForce, Is.EqualTo(nearForce).Within(0.1f),
                    "Identical engine resistance must not acquire phantom mechanical load with distance.");
                Assert.That(MathF.Abs(nearResidual), Is.LessThan(0.0001f));
                Assert.That(MathF.Abs(farResidual), Is.LessThan(0.0001f));
            }
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);

            void PlaceRadialPinnedTarget(float distance)
            {
                PlacePinnedTarget(entities, source, target, emitter, distance);
                // Isolate electrical distance load: keep thrust exactly along the COM-to-COM
                // axis so changing range cannot also change the rotational lever arm.
                var sourceCenter = transform.ToMapCoordinates(new EntityCoordinates(source, sourceBody.LocalCenter)).Position;
                var targetCenter = transform.ToMapCoordinates(new EntityCoordinates(target, targetBody.LocalCenter)).Position;
                transform.SetWorldPosition(target, transform.GetWorldPosition(target) +
                    new Vector2(0, sourceCenter.Y - targetCenter.Y));
                beam.LockedSeparation = new Vector2(targetCenter.X - sourceCenter.X, 0);
                beam.HoldDistance = beam.LockedSeparation.Length();
                beam.HoldDirection = Vector2.UnitX;
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnfundedDistanceBaselineHasNoAuthorityAndHardwarePowerLossReleasesAfterGrace()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var maps = pair.Server.ResolveDependency<IMapManager>();
        await pair.Server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var power = entities.GetComponent<PowerConsumerComponent>(emitter);
            var system = entities.System<TractorBeamSystem>();
            PlacePinnedTarget(entities, source, target, emitter, beam.MaxRange * 0.8f);
            system.UpdateBeforeSolve(false, Step);
            var distanceBaseline = power.DrawRate;
            Assert.That(distanceBaseline, Is.GreaterThan(beam.HoldingPower));
            power.NetworkLoad.ReceivingPower = (distanceBaseline + beam.HoldingPower) / 2f;
            entities.System<SharedPhysicsSystem>().ApplyForce(target, Vector2.UnitX * beam.MaxForce * 0.1f);
            system.UpdateBeforeSolve(false, Step);
            Assert.That(beam.Active, Is.True, "Hardware remains energized while its longer field lacks enough power to pull.");
            Assert.That(beam.Target, Is.EqualTo(target));
            Assert.That(power.DrawRate, Is.GreaterThan(distanceBaseline));
            Assert.That(entities.GetComponent<PhysicsComponent>(source).LinearVelocity, Is.EqualTo(Vector2.Zero));
            Assert.That(entities.GetComponent<PhysicsComponent>(target).LinearVelocity, Is.EqualTo(Vector2.Zero),
                "No counter-impulse may be granted before the electrical distance baseline is funded.");

            beam.PowerGraceUntil = TimeSpan.MaxValue;
            power.NetworkLoad.ReceivingPower = 0;
            system.UpdateBeforeSolve(false, Step);
            Assert.That(beam.Active, Is.False);
            Assert.That(beam.Target, Is.EqualTo(target));
            beam.PowerGraceUntil = TimeSpan.Zero;
            system.UpdateBeforeSolve(false, Step);
            Assert.That(beam.Target, Is.Null);
            Assert.That(beam.DistanceStrain, Is.Zero);
            Assert.That(power.DrawRate, Is.EqualTo(beam.IdlePower));
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    private static void PlacePinnedTarget(IEntityManager entities, EntityUid source, EntityUid target, EntityUid emitter, float distance)
    {
        var transform = entities.System<SharedTransformSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var sourceBody = entities.GetComponent<PhysicsComponent>(source);
        var targetBody = entities.GetComponent<PhysicsComponent>(target);
        var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
        var targetCenter = transform.ToMapCoordinates(new EntityCoordinates(target, targetBody.LocalCenter)).Position;
        var desiredCenter = transform.GetWorldPosition(emitter) + Vector2.UnitX * distance;
        transform.SetWorldPosition(target, transform.GetWorldPosition(target) + desiredCenter - targetCenter);
        var sourceCenter = transform.ToMapCoordinates(new EntityCoordinates(source, sourceBody.LocalCenter)).Position;
        beam.LockedInPlace = true;
        beam.LockedSeparation = desiredCenter - sourceCenter;
        beam.LockedAngle = (float) (transform.GetWorldRotation(target).Theta - transform.GetWorldRotation(source).Theta);
        beam.HoldDistance = beam.LockedSeparation.Length();
        beam.HoldDirection = Vector2.Normalize(beam.LockedSeparation);
        physics.SetLinearVelocity(source, Vector2.Zero);
        physics.SetLinearVelocity(target, Vector2.Zero);
        physics.SetAngularVelocity(source, 0f);
        physics.SetAngularVelocity(target, 0f);
    }

    private static void AssertNoCreaks(IEntityManager entities)
    {
        var query = entities.EntityQueryEnumerator<AudioComponent>();
        while (query.MoveNext(out var uid, out var audio))
        {
            if (!entities.IsQueuedForDeletion(uid))
                Assert.That(audio.FileName, Does.Not.Contain("creak"), "Distance load alone must never trigger hull resistance sounds.");
        }
    }
}
