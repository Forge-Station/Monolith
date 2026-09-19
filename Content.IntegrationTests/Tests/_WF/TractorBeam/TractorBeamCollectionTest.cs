using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.TractorBeam;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.TractorBeam;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed class TractorBeamCollectionTest
{
    private const float Step = 1f / 60f;

    [Test]
    public async Task TargetFanCannotCollectDebrisOrGridsOutsideDishOperatingSector()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter) = CreateLock(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            // A narrow configured sector contains the primary center, but clips the target fan's upper edge.
            beam.ConeHalfAngle = 0.08f;
            var inside = Debris(entities, map.MapId, new Vector2(25, 1));
            var outside = Debris(entities, map.MapId, new Vector2(25, 3.8f));
            var insideGrid = MakeGrid(entities, maps, map.MapId, 1, new Vector2(20, 0.5f));
            var outsideGrid = MakeGrid(entities, maps, map.MapId, 1, new Vector2(30, 3.4f));
            var transform = entities.System<SharedTransformSystem>();
            var start = transform.GetWorldPosition(emitter);
            var end = transform.ToMapCoordinates(new EntityCoordinates(target, Body(entities, target).LocalCenter)).Position;
            Assert.That(TractorBeamGeometry.ContainsPoint(start, end, transform.GetWorldPosition(outside), 4f), Is.True,
                "The excluded object must be inside the target fan, so this exercises the operating sector.");
            entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, Step);
            Assert.Multiple(() =>
            {
                Assert.That(beam.Active, Is.True);
                Assert.That(Body(entities, inside).LinearVelocity.X, Is.LessThan(0));
                Assert.That(Body(entities, insideGrid).LinearVelocity.X, Is.LessThan(0));
                Assert.That(Body(entities, outside).LinearVelocity, Is.EqualTo(Vector2.Zero));
                Assert.That(Body(entities, outsideGrid).LinearVelocity, Is.EqualTo(Vector2.Zero));
            });
            AssertMomentumConserved(entities, source, target, inside, insideGrid);
            foreach (var entity in new[] { inside, outside, insideGrid, outsideGrid, source, target })
                entities.DeleteEntity(entity);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ActiveConeCollectsOnlyLooseDebrisInsideItsFiniteFan()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter) = CreateLock(entities, maps, map.MapId);
            var inside = Debris(entities, map.MapId, new Vector2(25, 1));
            var outside = Debris(entities, map.MapId, new Vector2(25, 10));
            var behind = Debris(entities, map.MapId, new Vector2(-5, 0));
            var beyond = Debris(entities, map.MapId, new Vector2(65, 4));
            entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, Step);

            Assert.Multiple(() =>
            {
                Assert.That(Body(entities, inside).LinearVelocity.X, Is.LessThan(0),
                    "Loose debris between the dish and target must be collected while holding the target.");
                Assert.That(Body(entities, source).LinearVelocity.X, Is.GreaterThan(0),
                    "Collecting debris must apply a reaction to the emitter ship.");
                Assert.That(Body(entities, outside).LinearVelocity, Is.EqualTo(Vector2.Zero));
                Assert.That(Body(entities, behind).LinearVelocity, Is.EqualTo(Vector2.Zero));
                Assert.That(Body(entities, beyond).LinearVelocity, Is.EqualTo(Vector2.Zero),
                    "The effect ends at the selected target, even within emitter range.");
                Assert.That(entities.GetComponent<TractorBeamEmitterComponent>(emitter).RequiredForce, Is.GreaterThan(0));
            });
            AssertMomentumConserved(entities, source, target, inside);

            foreach (var uid in new[] { inside, outside, behind, beyond, source, target })
                entities.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConeCollectsDynamicGridsWithoutSeparatelyPullingTheirContents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, _) = CreateLock(entities, maps, map.MapId);
            var caughtGrid = MakeGrid(entities, maps, map.MapId, 2, new Vector2(24, 0));
            var staticGrid = MakeGrid(entities, maps, map.MapId, 2, new Vector2(34, 1));
            entities.System<SharedPhysicsSystem>().SetBodyType(staticGrid, BodyType.Static);
            var disabledGrid = MakeGrid(entities, maps, map.MapId, 2, new Vector2(39, 1));
            entities.GetComponent<ShuttleComponent>(disabledGrid).Enabled = false;
            var cargo = entities.SpawnEntity("SheetSteel1", new EntityCoordinates(caughtGrid, new Vector2(0.5f, 0.5f)));
            var anchored = entities.SpawnEntity("SheetSteel1", new EntityCoordinates(caughtGrid, new Vector2(1.5f, 0.5f)));
            Assert.That(entities.System<SharedTransformSystem>().AnchorEntity(anchored), Is.True);
            var ownCargo = entities.SpawnEntity("SheetSteel1", new EntityCoordinates(source, new Vector2(2.5f, 0.5f)));
            // A container with no physics prevents the holder itself from contributing a force.
            var holder = entities.SpawnEntity(null, new MapCoordinates(new Vector2(20, 1), map.MapId));
            var contained = Debris(entities, map.MapId, new Vector2(20, 1));
            var containers = entities.System<SharedContainerSystem>();
            var container = containers.EnsureContainer<Container>(holder, "tractor-test");
            Assert.That(containers.Insert(contained, container), Is.True);

            entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, Step);

            Assert.Multiple(() =>
            {
                Assert.That(Body(entities, caughtGrid).LinearVelocity.X, Is.LessThan(0),
                    "A separate dynamic grid inside the fan must be pulled toward the source.");
                Assert.That(Body(entities, staticGrid).LinearVelocity, Is.EqualTo(Vector2.Zero));
                Assert.That(Body(entities, disabledGrid).LinearVelocity, Is.EqualTo(Vector2.Zero),
                    "A station-anchored shuttle must not be dragged as secondary debris.");
                Assert.That(Body(entities, cargo).LinearVelocity, Is.EqualTo(Vector2.Zero),
                    "Cargo attached to a grid must not receive a separate collection impulse.");
                Assert.That(Body(entities, anchored).LinearVelocity, Is.EqualTo(Vector2.Zero));
                Assert.That(Body(entities, ownCargo).LinearVelocity, Is.EqualTo(Vector2.Zero));
                Assert.That(Body(entities, contained).LinearVelocity, Is.EqualTo(Vector2.Zero));
            });
            AssertMomentumConserved(entities, source, target, caughtGrid);

            foreach (var uid in new[] { holder, caughtGrid, staticGrid, disabledGrid, source, target })
                entities.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PrimaryAndCollectedBodiesShareOneForceAndPowerBudget()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter) = CreateLock(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            beam.Pulling = true;
            beam.MaxForce = 10;
            var debris = new List<EntityUid>();
            for (var i = 0; i < 10; i++)
                debris.Add(Debris(entities, map.MapId, new Vector2(15 + i, 1)));

            entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, Step);

            var impulse = Body(entities, target).LinearVelocity.Length() * Body(entities, target).Mass;
            foreach (var uid in debris)
                impulse += Body(entities, uid).LinearVelocity.Length() * Body(entities, uid).Mass;
            Assert.Multiple(() =>
            {
                Assert.That(impulse, Is.GreaterThan(0));
                Assert.That(impulse, Is.LessThanOrEqualTo(beam.MaxForce * Step + 0.001f),
                    "Adding collected bodies cannot multiply the dish's force budget.");
                Assert.That(entities.GetComponent<PowerConsumerComponent>(emitter).DrawRate,
                    Is.InRange(beam.HoldingPower, beam.MaxPower));
            });
            debris.Add(source);
            debris.Add(target);
            AssertMomentumConserved(entities, debris.ToArray());
            foreach (var uid in debris)
                entities.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GracePeriodDoesNotLetAnUnpoweredDishCollectDebris()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter) = CreateLock(entities, maps, map.MapId);
            var debris = Debris(entities, map.MapId, new Vector2(25, 1));
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            beam.PowerGraceUntil = TimeSpan.MaxValue;
            entities.GetComponent<PowerConsumerComponent>(emitter).NetworkLoad.ReceivingPower = 0;

            entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, Step);

            Assert.Multiple(() =>
            {
                Assert.That(beam.Target, Is.EqualTo(target), "The grace period preserves the requested lock.");
                Assert.That(Body(entities, debris).LinearVelocity, Is.EqualTo(Vector2.Zero));
                Assert.That(Body(entities, source).LinearVelocity, Is.EqualTo(Vector2.Zero));
                Assert.That(Body(entities, target).LinearVelocity, Is.EqualTo(Vector2.Zero));
            });
            entities.DeleteEntity(debris);
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PullReelsThePrimaryTargetWhileHoldRetainsSeparation(bool pulling)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter) = CreateLock(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            beam.Pulling = pulling;
            var distance = beam.HoldDistance;
            entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, Step);

            if (pulling)
            {
                Assert.That(beam.HoldDistance, Is.LessThan(distance));
                Assert.That(Body(entities, target).LinearVelocity.X - Body(entities, source).LinearVelocity.X,
                    Is.LessThan(0), "Pull mode must close the separation even when both ships begin at rest.");
            }
            else
            {
                Assert.That(beam.HoldDistance, Is.EqualTo(distance));
                Assert.That(Body(entities, target).LinearVelocity.Length(), Is.LessThan(0.0001f));
                Assert.That(Body(entities, source).LinearVelocity.Length(), Is.LessThan(0.0001f));
            }
            AssertMomentumConserved(entities, source, target);
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    private static PhysicsComponent Body(IEntityManager entities, EntityUid uid) => entities.GetComponent<PhysicsComponent>(uid);

    private static void AssertMomentumConserved(IEntityManager entities, params EntityUid[] bodies)
    {
        var total = Vector2.Zero;
        var magnitude = 0f;
        foreach (var uid in bodies)
        {
            var momentum = Body(entities, uid).LinearVelocity * Body(entities, uid).Mass;
            total += momentum;
            magnitude += momentum.Length();
        }
        Assert.That(total.Length(), Is.LessThan(magnitude * 0.00001f + 0.001f),
            "Collection and primary target forces must conserve the initially zero total momentum.");
    }

    private static EntityUid Debris(IEntityManager entities, MapId map, Vector2 position)
    {
        var uid = entities.SpawnEntity("SheetSteel1", new MapCoordinates(position, map));
        entities.System<SharedPhysicsSystem>().SetBodyType(uid, BodyType.Dynamic);
        Assert.That(entities.GetComponent<TransformComponent>(uid).GridUid, Is.Null,
            "Loose test debris must begin in space, rather than on a ship.");
        Assert.That(Body(entities, uid).Mass, Is.GreaterThan(0));
        return uid;
    }

    private static (EntityUid Source, EntityUid Target, EntityUid Emitter) CreateLock(
        IEntityManager entities, IMapManager maps, MapId map)
    {
        var (source, target, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map);
        return (source, target, emitter);
    }

    private static EntityUid MakeGrid(IEntityManager entities, IMapManager maps, MapId map, int side, Vector2 position)
    {
        var grid = maps.CreateGridEntity(map);
        var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
        for (var x = 0; x < side; x++)
        {
            for (var y = 0; y < side; y++)
                tiles.Add((new Vector2i(x, y), new Tile(1)));
        }
        entities.System<SharedMapSystem>().SetTiles(grid.Owner, grid.Comp, tiles);
        entities.System<SharedPhysicsSystem>().SetBodyType(grid.Owner, BodyType.Dynamic);
        entities.System<SharedTransformSystem>().SetWorldPosition(grid.Owner, position);
        return grid.Owner;
    }
}
