using System.Numerics;
using Content.Server._WF.TractorBeam;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.TractorBeam;
using Content.Shared.Friction;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed class TractorBeamCollectionAccelerationTest
{
    private const float Step = 1f / 60f;

    [TestCase("SheetSteel1")]
    [TestCase("MobHuman")]
    public async Task LooseObjectsAndNormalMobsReceiveAmplifiedFieldPull(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var maps = pair.Server.ResolveDependency<IMapManager>();
        await pair.Server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            var caught = entities.SpawnEntity(prototype, new MapCoordinates(new Vector2(36, 3), map.MapId));
            var physics = entities.System<SharedPhysicsSystem>();
            var system = entities.System<TractorBeamSystem>();
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            if (prototype == "MobHuman")
                Assert.That(Body(entities, caught).BodyType, Is.EqualTo(BodyType.KinematicController));
            beam.MaxForce = 10000000f; // Isolate the multiplier from force-budget saturation.
            var multiplier = beam.LooseCollectionMultiplier;
            beam.LooseCollectionMultiplier = 1;
            system.UpdateBeforeSolve(false, Step);
            var normalSpeed = Body(entities, caught).LinearVelocity.Length();
            Assert.That(normalSpeed, Is.GreaterThan(0), "The actual mob physics type must be collected.");
            foreach (var uid in new[] { source, target, caught })
            {
                physics.SetLinearVelocity(uid, Vector2.Zero);
                physics.SetAngularVelocity(uid, 0);
            }
            beam.LooseCollectionMultiplier = multiplier;
            system.UpdateBeforeSolve(false, Step);
            Assert.That(Body(entities, caught).LinearVelocity.Length(), Is.EqualTo(normalSpeed * 50).Within(normalSpeed * 5),
                "Unsaturated loose-body pull should be approximately fifty times its former strength.");
            Assert.That(Vector2.Dot(Body(entities, caught).LinearVelocity,
                Center(entities, source) - Center(entities, caught)), Is.GreaterThan(0));
            entities.DeleteEntity(caught);
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false, 0f)]
    [TestCase(true, 0f)]
    [TestCase(false, 4f)]
    [TestCase(true, 4f)]
    public async Task CaughtObjectsKeepAcceleratingBeyondTheOldCollectionSpeed(bool grid, float initialSpeed)
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
            var sourceBody = Body(entities, source);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var shuttle = entities.GetComponent<ShuttleComponent>(source);
            Array.Fill(shuttle.LinearThrust, 100000f);
            shuttle.AngularThrust = 100000f;
            var caught = CreateCaughtObject(entities, maps, map.MapId, grid, new Vector2(44, 3));
            var caughtBody = Body(entities, caught);
            var sourceCenter = Center(entities, source);
            var outward = Vector2.Normalize(Center(entities, caught) - sourceCenter);
            physics.SetLinearVelocity(caught, -outward * initialSpeed);
            var initialDistance = Vector2.Distance(sourceCenter, Center(entities, caught));
            var previousSpeed = initialSpeed;

            // Loose objects now accelerate 50 times harder, so sample before they hit the dish.
            // Secondary grids retain the original four-second approach window.
            for (var tick = 0; tick < (grid ? 240 : 30); tick++)
            {
                physics.Update(Step);
                Assert.That(beam.Active, Is.True);
                outward = Vector2.Normalize(Center(entities, caught) - Center(entities, source));
                var inwardSpeed = -Vector2.Dot(caughtBody.LinearVelocity - sourceBody.LinearVelocity, outward);
                Assert.That(inwardSpeed, Is.GreaterThan(previousSpeed - 0.001f),
                    "Collection must continue accelerating inward, including an object already above the former 3 m/s limit.");
                previousSpeed = inwardSpeed;
            }

            Assert.That(previousSpeed, Is.GreaterThan(MathF.Max(3.5f, initialSpeed + 0.5f)),
                "Caught debris and grids must gain speed, not settle at the former collection speed.");
            Assert.That(Vector2.Distance(Center(entities, caught), Center(entities, source)), Is.LessThan(initialDistance - 1f));
            Assert.That(beam.RequiredForce, Is.GreaterThan(0), "The dish must still be actively accelerating the object.");
            Assert.That(transform.GetWorldPosition(source).Length(), Is.LessThan(0.01f),
                "The source's available thrusters should absorb collection recoil in this approach test.");
            entities.DeleteEntity(caught);
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UnpoweredCollectionDoesNotAccelerateAnAlreadyIncomingObject(bool grid)
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
            var caught = CreateCaughtObject(entities, maps, map.MapId, grid, new Vector2(36, 3));
            var physics = entities.System<SharedPhysicsSystem>();
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            beam.PowerGraceUntil = TimeSpan.MaxValue;
            entities.GetComponent<PowerConsumerComponent>(emitter).NetworkLoad.ReceivingPower = 0;
            var velocity = -Vector2.Normalize(Center(entities, caught) - Center(entities, source)) * 4f;
            physics.SetLinearVelocity(caught, velocity);
            var start = Center(entities, caught);

            for (var tick = 0; tick < 60; tick++)
                physics.Update(Step);

            Assert.That(beam.Active, Is.False);
            Assert.That(beam.Target, Is.EqualTo(target), "The grace period retains the request, not free tractor authority.");
            Assert.That(Vector2.Distance(Body(entities, caught).LinearVelocity, velocity), Is.LessThan(0.001f));
            Assert.That(Vector2.Distance(Center(entities, caught), start + velocity), Is.LessThan(0.01f),
                "The object keeps its physical incoming momentum without power from the dish.");
            Assert.That(Body(entities, source).LinearVelocity.Length(), Is.LessThan(0.001f));
            entities.DeleteEntity(caught);
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SustainedCollectionSharesTheDishForceBudgetAndConservesMomentum(bool isolateCollectionAngularReaction)
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
            var first = CreateCaughtObject(entities, maps, map.MapId, false, new Vector2(32, 2));
            var second = CreateCaughtObject(entities, maps, map.MapId, false, new Vector2(35, 3));
            var third = CreateCaughtObject(entities, maps, map.MapId, true, new Vector2(38, 3));
            EntityUid[] bodies = [source, target, first, second, third];
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            beam.MaxForce = isolateCollectionAngularReaction ? 100f : 0.1f;
            // The primary Hold servo also applies noncentral lateral restraint, whose orbital
            // couple is independent of collection. Disable that spring only in the angular
            // reaction case; the other case keeps the full primary/secondary force budget.
            if (isolateCollectionAngularReaction)
                beam.Frequency = 0;
            var physics = entities.System<SharedPhysicsSystem>();
            foreach (var uid in bodies)
            {
                DisableAmbientFriction(entities, uid);
                physics.SetLinearDamping(uid, Body(entities, uid), 0);
                physics.SetAngularDamping(uid, Body(entities, uid), 0);
                // The low-budget case produces velocities below the engine's
                // sleep threshold. Sleep would zero them and discard physical momentum.
                physics.SetSleepingAllowed(uid, Body(entities, uid), false);
            }

            // The pool runs at 1 Hz, but this test explicitly advances 60 Hz frames.
            // Avoid subdividing each frame another 60 times: otherwise tiny angular
            // impulses fall below SetAngularVelocity's absolute 1e-5 change threshold.
            var config = server.ResolveDependency<IConfigurationManager>();
            var oldMinimumTickrate = config.GetCVar(Robust.Shared.CVars.TargetMinimumTickrate);
            config.SetCVar(Robust.Shared.CVars.TargetMinimumTickrate, config.GetCVar(Robust.Shared.CVars.NetTickrate));
            try
            {
                var previous = new Vector2[bodies.Length];
                // Measure before the amplified off-center recoil can rotate this unpowered
                // source out of coverage. Losing that lock is valid physical behavior.
                for (var tick = 0; tick < 15; tick++)
                {
                    for (var i = 0; i < bodies.Length; i++)
                        previous[i] = Body(entities, bodies[i]).LinearVelocity;
                    physics.Update(Step);
                    var momentum = Vector2.Zero;
                    var absoluteMomentum = 0f;
                    var angularMomentum = 0f;
                    var absoluteAngularMomentum = 0f;
                    var deliveredImpulse = 0f;
                    for (var i = 0; i < bodies.Length; i++)
                    {
                        var body = Body(entities, bodies[i]);
                        var bodyMomentum = body.LinearVelocity * body.Mass;
                        momentum += bodyMomentum;
                        absoluteMomentum += bodyMomentum.Length();
                        var center = Center(entities, bodies[i]);
                        var orbital = center.X * bodyMomentum.Y - center.Y * bodyMomentum.X;
                        var spin = body.InvI > 0 ? body.AngularVelocity / body.InvI : 0f;
                        angularMomentum += orbital + spin;
                        absoluteAngularMomentum += MathF.Abs(orbital) + MathF.Abs(spin);
                        // The source receives the equal reaction. Count only target impulses
                        // when checking the single shared dish budget.
                        if (i > 0)
                            deliveredImpulse += (body.LinearVelocity - previous[i]).Length() * body.Mass;
                    }
                    Assert.That(deliveredImpulse, Is.LessThanOrEqualTo(beam.MaxForce * Step + 0.0001f), $"Impulse budget at tick {tick}");
                    Assert.That(momentum.Length(), Is.LessThan(absoluteMomentum * 0.00001f + 0.0001f));
                    if (isolateCollectionAngularReaction)
                    {
                        Assert.That(MathF.Abs(angularMomentum), Is.LessThan(absoluteAngularMomentum * 0.0001f + 0.001f),
                            $"Angular momentum at tick {tick}: source InvI={Body(entities, source).InvI}, w={Body(entities, source).AngularVelocity}; " +
                            "pulling toward an off-center dish must impart the matching angular reaction to its ship.");
                    }
                    Assert.That(beam.RequestedPower, Is.InRange(beam.HoldingPower, beam.MaxPower));
                }

                Assert.That(Body(entities, first).LinearVelocity.X, Is.LessThan(0));
                Assert.That(Body(entities, second).LinearVelocity.X, Is.LessThan(0));
                Assert.That(Body(entities, third).LinearVelocity.X, Is.LessThan(0));
                foreach (var uid in bodies)
                    entities.DeleteEntity(uid);
            }
            finally
            {
                config.SetCVar(Robust.Shared.CVars.TargetMinimumTickrate, oldMinimumTickrate);
            }
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid CreateCaughtObject(IEntityManager entities, IMapManager maps, MapId map, bool grid, Vector2 position)
    {
        EntityUid uid;
        var physics = entities.System<SharedPhysicsSystem>();
        if (grid)
        {
            var caught = maps.CreateGridEntity(map);
            entities.System<SharedMapSystem>().SetTiles(caught.Owner, caught.Comp, [(Vector2i.Zero, new Tile(1))]);
            entities.System<SharedTransformSystem>().SetWorldPosition(caught.Owner, position);
            uid = caught.Owner;
        }
        else
        {
            uid = entities.SpawnEntity("SheetSteel1", new MapCoordinates(position, map));
            Assert.That(entities.GetComponent<TransformComponent>(uid).GridUid, Is.Null);
        }

        physics.SetBodyType(uid, BodyType.Dynamic);
        DisableAmbientFriction(entities, uid);
        physics.SetLinearDamping(uid, Body(entities, uid), 0);
        return uid;
    }

    private static void DisableAmbientFriction(IEntityManager entities, EntityUid uid)
    {
        // TileFrictionController rewrites PhysicsComponent damping each solve, including
        // 0.05/s ambient space drag. Use its public modifier rather than overwriting the cache.
        var modifier = entities.EnsureComponent<TileFrictionModifierComponent>(uid);
        entities.System<TileFrictionController>().SetModifier(uid, 0f, modifier);
    }

    private static PhysicsComponent Body(IEntityManager entities, EntityUid uid) => entities.GetComponent<PhysicsComponent>(uid);

    private static Vector2 Center(IEntityManager entities, EntityUid uid)
    {
        return entities.System<SharedTransformSystem>().ToMapCoordinates(new EntityCoordinates(uid, Body(entities, uid).LocalCenter)).Position;
    }
}
