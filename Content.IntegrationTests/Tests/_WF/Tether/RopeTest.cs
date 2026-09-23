using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.Tether;
using Content.Shared._WF.Tether;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.Tether;

/// <summary>
/// Rope physics against real grids: the hard limit, one-sided force, breaking, severing and the
/// coil's unit accounting.
/// </summary>
[TestFixture]
public sealed class RopeTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: ropeType
  id: WFTestRopeStiff
  stiffness: 400000
  dampingRatio: 0.7
  maxStretch: 0.05
  breakForce: 0
  maxLength: 100
  stackType: WFTestRopeStack

- type: ropeType
  id: WFTestRopeWeak
  stiffness: 400000
  dampingRatio: 0.7
  maxStretch: 0.05
  breakForce: 200
  maxLength: 100
  stackType: WFTestRopeStack

- type: stack
  id: WFTestRopeStack
  name: test rope
  spawn: WFTestRopeCoil1
  maxCount: 60

- type: entity
  id: WFTestRopeCoil
  parent: BaseItem
  name: test rope coil
  components:
  - type: Stack
    stackType: WFTestRopeStack
    count: 40
  - type: RopeCoil
    ropeType: WFTestRopeStiff
    metresPerUnit: 1
    slack: 1.15

- type: entity
  id: WFTestRopeCoil1
  parent: WFTestRopeCoil
  components:
  - type: Stack
    count: 1
";

    private const string AttachPoint = "RopeAttachPointDebug";
    private const float Stretch = 0.05f;

    [Test]
    public async Task HardLimitHoldsAndDragsThePartnerGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid gridA = default, gridB = default, pointA = default, pointB = default, rope = default;
        const float length = 9f;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            (gridA, gridB, pointA, pointB) = CreatePair(entities, maps, map.MapId);
            Assert.That(entities.System<RopeSystem>()
                .TryCreateRope(pointA, pointB, "WFTestRopeStiff", length, out var created), Is.True);
            rope = created!.Value;
            // Shove the far grid straight out along the rope.
            entities.System<SharedPhysicsSystem>().ApplyLinearImpulse(gridB, new Vector2(400000f, 0f));
        });

        var maximum = 0f;
        var dragged = 0f;
        var tension = 0f;
        for (var i = 0; i < 90; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() =>
            {
                if (entities.Deleted(rope))
                    return;

                var system = entities.System<RopeSystem>();
                maximum = MathF.Max(maximum,
                    Vector2.Distance(system.GetAnchorPosition(pointA), system.GetAnchorPosition(pointB)));
                dragged = MathF.Max(dragged, entities.GetComponent<PhysicsComponent>(gridA).LinearVelocity.X);
                tension = MathF.Max(tension, system.GetTension(rope));
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(rope), Is.False, "An unbreakable rope must survive the pull.");
            Assert.That(maximum, Is.LessThanOrEqualTo(length * (1f + Stretch) + 0.35f),
                "The joint's upper bound is the rope's inextensible limit.");
            Assert.That(dragged, Is.GreaterThan(0.5f), "The near grid must be towed along behind it.");
            Assert.That(tension, Is.GreaterThan(0f), "A rope under load reports tension.");
            entities.DeleteEntity(gridA);
            entities.DeleteEntity(gridB);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SlackRopeAppliesNoForce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid gridA = default, gridB = default, rope = default;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (a, b, pointA, pointB) = CreatePair(entities, maps, map.MapId);
            gridA = a;
            gridB = b;
            // Well beyond the ends' separation, so the rope lies slack for the whole run.
            Assert.That(entities.System<RopeSystem>()
                .TryCreateRope(pointA, pointB, "WFTestRopeStiff", 30f, out var created), Is.True);
            rope = created!.Value;
        });

        await server.WaitRunTicks(30);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<PhysicsComponent>(gridA).LinearVelocity.Length(), Is.LessThan(0.001f));
                Assert.That(entities.GetComponent<PhysicsComponent>(gridB).LinearVelocity.Length(), Is.LessThan(0.001f));
                Assert.That(entities.GetComponent<RopeComponent>(rope).Strain, Is.Zero);
                Assert.That(entities.System<RopeSystem>().GetTension(rope), Is.Zero);
            });

            entities.DeleteEntity(gridA);
            entities.DeleteEntity(gridB);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BreakForceSnapsTheRope()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid gridA = default, gridB = default, pointA = default, pointB = default, rope = default;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            (gridA, gridB, pointA, pointB) = CreatePair(entities, maps, map.MapId);
            Assert.That(entities.System<RopeSystem>()
                .TryCreateRope(pointA, pointB, "WFTestRopeWeak", 9f, out var created), Is.True);
            rope = created!.Value;
            entities.System<SharedPhysicsSystem>().ApplyLinearImpulse(gridB, new Vector2(400000f, 0f));
        });

        await server.WaitRunTicks(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(rope), Is.True, "A weak rope parts under a full shove.");
            Assert.That(entities.GetComponent<RopeAttachPointComponent>(pointA).Ropes, Is.Empty,
                "A snapped rope must leave no dangling reference on its attach point.");
            entities.DeleteEntity(gridA);
            entities.DeleteEntity(gridB);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeletingAnEndRemovesTheRope()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (gridA, gridB, pointA, pointB) = CreatePair(entities, maps, map.MapId);
            var system = entities.System<RopeSystem>();
            Assert.That(system.TryCreateRope(pointA, pointB, "WFTestRopeStiff", 9f, out var rope), Is.True);
            Assert.That(entities.GetComponent<RopeAttachPointComponent>(pointA).Ropes, Has.Count.EqualTo(1));

            entities.DeleteEntity(pointB);
            Assert.Multiple(() =>
            {
                Assert.That(entities.Deleted(rope!.Value), Is.True);
                Assert.That(entities.GetComponent<RopeAttachPointComponent>(pointA).Ropes, Is.Empty);
            });

            entities.DeleteEntity(gridA);
            entities.DeleteEntity(gridB);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CoilConsumesUnitsAndRefundsThemOnUntie()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (gridA, gridB, pointA, pointB) = CreatePair(entities, maps, map.MapId);
            var system = entities.System<RopeSystem>();
            var user = entities.SpawnEntity(null, new EntityCoordinates(gridA, new Vector2(1.5f, 1.5f)));
            var coil = entities.SpawnEntity("WFTestRopeCoil", new EntityCoordinates(gridA, new Vector2(1.5f, 1.5f)));
            var stack = entities.GetComponent<StackComponent>(coil);
            var before = stack.Count;

            // Both clicks happen inside one tick, so the carry cannot be cancelled in between.
            Interact(entities, user, coil, pointA);
            Assert.That(entities.HasComponent<RopeCarrierComponent>(user), Is.True,
                "The first click takes the loose end off the coil.");

            Interact(entities, user, coil, pointB);
            var separation = Vector2.Distance(system.GetAnchorPosition(pointA), system.GetAnchorPosition(pointB));
            var expected = (int) MathF.Ceiling(MathF.Max(separation * 1.15f, 1f));
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<RopeCarrierComponent>(user), Is.False);
                Assert.That(stack.Count, Is.EqualTo(before - expected), "Whole units of rope are consumed.");
                Assert.That(entities.GetComponent<RopeAttachPointComponent>(pointA).Ropes, Has.Count.EqualTo(1));
                Assert.That(entities.GetComponent<RopeAttachPointComponent>(pointB).Ropes, Has.Count.EqualTo(1));
            });

            var net = entities.GetComponent<RopeAttachPointComponent>(pointA).Ropes[0];
            var rope = entities.GetEntity(net);
            Assert.That(entities.GetComponent<RopeComponent>(rope).Length, Is.EqualTo((float) expected).Within(0.001f));

            // Untying returns exactly what was paid out, so no rope is created or lost overall.
            system.BreakRope(rope, true);
            Assert.That(entities.Deleted(rope), Is.True);
            Assert.That(CountLooseRope(entities, gridA), Is.EqualTo(before), "Untying refunds every consumed unit.");

            entities.DeleteEntity(gridA);
            entities.DeleteEntity(gridB);
        });

        await pair.CleanReturnAsync();
    }

    private static void Interact(IEntityManager entities, EntityUid user, EntityUid coil, EntityUid target)
    {
        var ev = new AfterInteractEvent(user, coil, target, entities.GetComponent<TransformComponent>(target).Coordinates, true);
        entities.EventBus.RaiseLocalEvent(coil, ev);
    }

    private static int CountLooseRope(IEntityManager entities, EntityUid grid)
    {
        var total = 0;
        var query = entities.EntityQueryEnumerator<RopeCoilComponent, StackComponent>();
        while (query.MoveNext(out var uid, out _, out var stack))
        {
            if (entities.GetComponent<TransformComponent>(uid).GridUid == grid)
                total += stack.Count;
        }

        return total;
    }

    /// <summary>Two free-floating square grids with one anchored attach point facing each other.</summary>
    private static (EntityUid GridA, EntityUid GridB, EntityUid PointA, EntityUid PointB) CreatePair(
        IEntityManager entities, IMapManager maps, MapId map)
    {
        var mapSystem = entities.System<SharedMapSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var transform = entities.System<SharedTransformSystem>();

        var gridA = MakeGrid(Vector2.Zero);
        var gridB = MakeGrid(new Vector2(10f, 0f));
        var pointA = entities.SpawnEntity(AttachPoint, new EntityCoordinates(gridA, new Vector2(2.5f, 1.5f)));
        var pointB = entities.SpawnEntity(AttachPoint, new EntityCoordinates(gridB, new Vector2(0.5f, 1.5f)));
        Assert.That(entities.GetComponent<TransformComponent>(pointA).Anchored, Is.True);
        Assert.That(entities.GetComponent<TransformComponent>(pointB).Anchored, Is.True);
        Assert.That(entities.GetComponent<PhysicsComponent>(gridA).Mass, Is.GreaterThan(0f));
        Assert.That(entities.GetComponent<PhysicsComponent>(gridB).Mass, Is.GreaterThan(0f));
        return (gridA, gridB, pointA, pointB);

        EntityUid MakeGrid(Vector2 position)
        {
            var grid = maps.CreateGridEntity(map);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
            for (var x = 0; x < 3; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                }
            }

            mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);
            physics.SetBodyType(grid.Owner, BodyType.Dynamic);
            transform.SetWorldPosition(grid.Owner, position);
            return grid.Owner;
        }
    }
}
