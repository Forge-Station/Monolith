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

public sealed class TractorBeamArcTest
{
    private const float Step = 1f / 60f;

    [TestCase("hold")]
    [TestCase("pull")]
    [TestCase("pin")]
    public async Task SourceRotationDrivesTargetAlongArcWithReciprocalMomentumAndPowerCost(string mode)
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
            var power = entities.GetComponent<PowerConsumerComponent>(emitter);
            var sourceBody = entities.GetComponent<PhysicsComponent>(source);
            var targetBody = entities.GetComponent<PhysicsComponent>(target);
            system.UpdateBeforeSolve(false, Step);
            ConfigureMode(mode, beam, source, target, sourceBody, targetBody, transform);
            var side = Tangent(Center(target, targetBody, transform) - Center(source, sourceBody, transform));
            const float rotation = 0.05f;
            physics.SetAngularVelocity(source, rotation);
            var initialAngularMomentum = AngularMomentum(source, sourceBody, target, targetBody, transform);
            system.UpdateBeforeSolve(false, Step);

            var poweredTangentialSpeed = Vector2.Dot(targetBody.LinearVelocity, side);
            Assert.That(poweredTangentialSpeed, Is.GreaterThan(0f), "Turning the arrestor must sweep the target along its captured arc.");
            Assert.That(sourceBody.AngularVelocity, Is.LessThan(rotation), "The target's inertia must resist turning the arrestor.");
            Assert.That(power.DrawRate, Is.GreaterThan(TractorBeamPhysics.CalculatePower(0,
                beam.MaxForce, beam.HoldingPower, beam.MaxPower, beam.DistanceStrain)), "Sweeping a target consumes mechanical power above field maintenance.");
            AssertMomentum(entities, source, target, Vector2.Zero, initialAngularMomentum);

            // Reset velocities without recapturing the pose, then remove all power above field maintenance.
            physics.SetLinearVelocity(source, Vector2.Zero);
            physics.SetLinearVelocity(target, Vector2.Zero);
            physics.SetAngularVelocity(source, rotation);
            physics.SetAngularVelocity(target, 0f);
            power.NetworkLoad.ReceivingPower = TractorBeamPhysics.CalculatePower(0,
                beam.MaxForce, beam.HoldingPower, beam.MaxPower, beam.DistanceStrain);
            system.UpdateBeforeSolve(false, Step);
            Assert.That(Vector2.Dot(targetBody.LinearVelocity, side), Is.LessThan(poweredTangentialSpeed),
                "A brownout must reduce the authority used to sweep the target.");
            Assert.That(beam.Target, Is.EqualTo(target));
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("hold")]
    [TestCase("pull")]
    [TestCase("pin")]
    public async Task SidewaysTargetMotionTurnsArrestorAndConservesAngularMomentum(string mode)
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
            system.UpdateBeforeSolve(false, Step);
            ConfigureMode(mode, beam, source, target, sourceBody, targetBody, transform);
            var side = Tangent(Center(target, targetBody, transform) - Center(source, sourceBody, transform));
            physics.SetLinearVelocity(target, side * 0.1f);
            var linearMomentum = targetBody.LinearVelocity * targetBody.Mass;
            var angularMomentum = AngularMomentum(source, sourceBody, target, targetBody, transform);
            system.UpdateBeforeSolve(false, Step);

            Assert.That(Vector2.Dot(targetBody.LinearVelocity, side), Is.LessThan(0.1f));
            Assert.That(sourceBody.AngularVelocity, Is.GreaterThan(0f),
                "A sideways resisting target must turn the projector ship through the beam's lever arm.");
            AssertMomentum(entities, source, target, linearMomentum, angularMomentum);
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("hold")]
    [TestCase("pull")]
    [TestCase("pin")]
    public async Task CapturedBearingFollowsSourcePoseAfterRotationStops(string mode)
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
            var sourceBody = entities.GetComponent<PhysicsComponent>(source);
            var targetBody = entities.GetComponent<PhysicsComponent>(target);
            system.UpdateBeforeSolve(false, Step);
            ConfigureMode(mode, beam, source, target, sourceBody, targetBody, transform);
            var sourceCenter = Center(source, sourceBody, transform);
            var targetCenter = Center(target, targetBody, transform);
            var side = Tangent(targetCenter - sourceCenter);
            transform.SetWorldRotation(source, transform.GetWorldRotation(source) + Angle.FromDegrees(2));
            // Rotate around the center of mass, without introducing an unrelated translation.
            transform.SetWorldPosition(source, transform.GetWorldPosition(source) + sourceCenter - Center(source, sourceBody, transform));
            // Keep the captured relative hull orientation satisfied. Otherwise its separate
            // rotational constraint can turn the unbraked source back, making a target COM
            // impulse of either sign valid even though the source-relative arm is correct.
            transform.SetWorldRotation(target, transform.GetWorldRotation(target) + Angle.FromDegrees(2));
            transform.SetWorldPosition(target, transform.GetWorldPosition(target) + targetCenter - Center(target, targetBody, transform));
            system.UpdateBeforeSolve(false, Step);

            Assert.That(beam.Target, Is.EqualTo(target));
            Assert.That(Vector2.Dot(targetBody.LinearVelocity, side), Is.GreaterThan(0f),
                "The captured rest bearing must rotate with the projector, even after its angular velocity returns to zero.");
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    private static void ConfigureMode(string mode, TractorBeamEmitterComponent beam, EntityUid source, EntityUid target,
        PhysicsComponent sourceBody, PhysicsComponent targetBody, SharedTransformSystem transform)
    {
        beam.Pulling = mode == "pull";
        if (beam.Pulling)
            beam.RequestedDistance = beam.HoldDistance - 5f;
        beam.LockedInPlace = mode == "pin";
        if (!beam.LockedInPlace)
            return;
        beam.LockedSeparation = Center(target, targetBody, transform) - Center(source, sourceBody, transform);
        beam.LockedAngle = (float) (transform.GetWorldRotation(target).Theta - transform.GetWorldRotation(source).Theta);
    }

    private static Vector2 Center(EntityUid uid, PhysicsComponent body, SharedTransformSystem transform) =>
        transform.ToMapCoordinates(new EntityCoordinates(uid, body.LocalCenter)).Position;

    private static Vector2 Tangent(Vector2 separation) => Vector2.Normalize(new Vector2(-separation.Y, separation.X));

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static float AngularMomentum(EntityUid source, PhysicsComponent sourceBody, EntityUid target,
        PhysicsComponent targetBody, SharedTransformSystem transform) =>
        Cross(Center(source, sourceBody, transform), sourceBody.LinearVelocity * sourceBody.Mass) +
        Cross(Center(target, targetBody, transform), targetBody.LinearVelocity * targetBody.Mass) +
        sourceBody.AngularVelocity / sourceBody.InvI + targetBody.AngularVelocity / targetBody.InvI;

    private static void AssertMomentum(IEntityManager entities, EntityUid source, EntityUid target,
        Vector2 initialLinear, float initialAngular)
    {
        var sourceBody = entities.GetComponent<PhysicsComponent>(source);
        var targetBody = entities.GetComponent<PhysicsComponent>(target);
        var transform = entities.System<SharedTransformSystem>();
        Assert.That(Vector2.Distance(sourceBody.LinearVelocity * sourceBody.Mass + targetBody.LinearVelocity * targetBody.Mass,
            initialLinear), Is.LessThan(0.001f + initialLinear.Length() * 0.0001f));
        Assert.That(AngularMomentum(source, sourceBody, target, targetBody, transform),
            Is.EqualTo(initialAngular).Within(0.001f + MathF.Abs(initialAngular) * 0.0001f),
            "Orbital and spin angular momentum must balance; a tractor is not an external space anchor.");
    }
}
