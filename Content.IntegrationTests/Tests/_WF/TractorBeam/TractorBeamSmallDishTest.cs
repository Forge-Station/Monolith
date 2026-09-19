using System.Numerics;
using Content.Server._WF.TractorBeam;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.TractorBeam;
using Content.Shared.Friction;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed class TractorBeamSmallDishTest
{
    [Test]
    public async Task SmallDishRestrainsWithinItsPowerBudgetAndReleasesBeyondItsShorterRange()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId,
                sourcePosition: new Vector2(25, 0), emitterPrototype: "WFTractorBeamEmitterSmall");
            var system = entities.System<TractorBeamSystem>();
            var physics = entities.System<SharedPhysicsSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var power = entities.GetComponent<PowerConsumerComponent>(emitter);
            var targetBody = entities.GetComponent<PhysicsComponent>(target);

            Assert.Multiple(() =>
            {
                Assert.That(power.Voltage, Is.EqualTo(Voltage.Medium));
                Assert.That(entities.GetComponent<NodeContainerComponent>(emitter).Nodes["input"].NodeGroupID,
                    Is.EqualTo(NodeGroupID.MVPower), "The small dish must override its parent's HV cable node.");
                Assert.That(beam.MaxRange, Is.EqualTo(80f));
                Assert.That(beam.MaxForce, Is.EqualTo(6000f));
                Assert.That(beam.IdlePower, Is.EqualTo(10000f));
                Assert.That(beam.HoldingPower, Is.EqualTo(30000f));
                Assert.That(beam.MaxPower, Is.EqualTo(150000f));
            });

            system.UpdateBeforeSolve(false, 1f / 60f);
            Assert.That(beam.Active, Is.True, "The small prototype must operate inside its 80 m cone.");
            Assert.That(beam.Visual, Is.Not.Null);
            var visual = entities.GetComponent<TractorBeamVisualComponent>(beam.Visual!.Value);
            Assert.That(visual.WidthScale, Is.EqualTo(0.5f),
                "The smaller field must use the narrower visual rather than inherit the full-size effect.");

            physics.SetLinearVelocity(target, new Vector2(20, 0));
            system.UpdateBeforeSolve(false, 1f / 60f);
            Assert.That(targetBody.LinearVelocity.X, Is.LessThan(20f));
            Assert.That(power.DrawRate, Is.GreaterThan(beam.HoldingPower));
            Assert.That(power.DrawRate, Is.LessThanOrEqualTo(150000f));
            var poweredVelocity = targetBody.LinearVelocity.X;

            physics.SetLinearVelocity(source, Vector2.Zero);
            physics.SetLinearVelocity(target, new Vector2(20, 0));
            power.NetworkLoad.ReceivingPower = beam.HoldingPower;
            system.UpdateBeforeSolve(false, 1f / 60f);
            Assert.That(targetBody.LinearVelocity.X, Is.GreaterThan(poweredVelocity),
                "The small dish must still lose authority when its power supply cannot cover strain.");

            power.NetworkLoad.ReceivingPower = beam.MaxPower;
            transform.SetWorldPosition(target, transform.GetWorldPosition(source) + new Vector2(beam.MaxRange + 10f, 0));
            system.UpdateBeforeSolve(false, 1f / 60f);
            Assert.Multiple(() =>
            {
                Assert.That(beam.Active, Is.False);
                Assert.That(beam.Target, Is.Null);
                Assert.That(beam.Visual, Is.Null);
                Assert.That(power.DrawRate, Is.EqualTo(10000f));
            });

            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PoweredSmallDishHoldsSmallerCraftAgainstSustainedThrust()
    {
        const float step = 1f / 60f;
        const float targetThrust = 2000f;
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var physics = entities.System<SharedPhysicsSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var smallGrid = maps.CreateGridEntity(map.MapId);
            for (var x = 0; x < 2; x++)
            {
                for (var y = 0; y < 2; y++)
                    entities.System<SharedMapSystem>().SetTile(smallGrid.Owner, smallGrid.Comp,
                        new Vector2i(x, y), new Tile(1));
            }
            physics.SetBodyType(smallGrid.Owner, BodyType.Dynamic);
            transform.SetWorldPosition(smallGrid.Owner, new Vector2(50, 0));
            var (source, target, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId,
                existingTarget: smallGrid.Owner, sourcePosition: new Vector2(25, 0),
                emitterPrototype: "WFTractorBeamEmitterSmall");
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var power = entities.GetComponent<PowerConsumerComponent>(emitter);
            var sourceBody = entities.GetComponent<PhysicsComponent>(source);
            var targetBody = entities.GetComponent<PhysicsComponent>(target);
            // Bare grid tiles weigh only 0.5 kg each. Model equipped craft rather than
            // a two-kilogram target under 2 kN, whose soft-hold spring legitimately deflects.
            SetHullMass(source, 8000f);
            SetHullMass(target, 1000f);
            Assert.That(sourceBody.Mass, Is.GreaterThan(targetBody.Mass));

            // Supply actual shuttle-controller thrust capacity while isolating the restraint
            // from ambient friction, which must not secretly stop the escaping craft.
            var shuttle = entities.GetComponent<ShuttleComponent>(source);
            Array.Fill(shuttle.LinearThrust, 10000f);
            shuttle.AngularThrust = 10000f;
            foreach (var uid in new[] { source, target })
            {
                var friction = entities.EnsureComponent<TileFrictionModifierComponent>(uid);
                entities.System<TileFrictionController>().SetModifier(uid, 0f, friction);
                var body = entities.GetComponent<PhysicsComponent>(uid);
                physics.SetLinearDamping(uid, body, 0f);
                physics.SetAngularDamping(uid, body, 0f);
            }

            var startSeparation = Center(target) - Center(source);
            var thrustDirection = Vector2.Normalize(startSeparation);
            var peakDisplacement = 0f;
            var peakDraw = 0f;
            for (var tick = 0; tick < 360; tick++)
            {
                physics.ApplyForce(target, thrustDirection * targetThrust);
                physics.Update(step);
                Assert.That(beam.Active, Is.True);
                peakDisplacement = MathF.Max(peakDisplacement,
                    Vector2.Distance(Center(target) - Center(source), startSeparation));
                peakDraw = MathF.Max(peakDraw, power.DrawRate);
            }

            Assert.That(peakDisplacement, Is.LessThan(1f),
                "Six seconds of continuous 2 kN escape thrust should remain restrained within one tile.");
            Assert.That((targetBody.LinearVelocity - sourceBody.LinearVelocity).Length(), Is.LessThan(0.2f),
                "The captured craft must settle rather than continue slipping away.");
            Assert.That(peakDraw, Is.GreaterThan(beam.HoldingPower));
            Assert.That(peakDraw, Is.LessThanOrEqualTo(150000f));
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);

            Vector2 Center(EntityUid uid) => transform.ToMapCoordinates(new EntityCoordinates(uid,
                entities.GetComponent<PhysicsComponent>(uid).LocalCenter)).Position;

            void SetHullMass(EntityUid uid, float mass)
            {
                var multiplier = mass / entities.GetComponent<PhysicsComponent>(uid).Mass;
                var fixtures = entities.GetComponent<FixturesComponent>(uid);
                foreach (var (id, fixture) in fixtures.Fixtures)
                    physics.SetDensity(uid, id, fixture, fixture.Density * multiplier);
                Assert.That(entities.GetComponent<PhysicsComponent>(uid).Mass, Is.EqualTo(mass).Within(0.1f));
            }
        });

        await pair.CleanReturnAsync();
    }
}
