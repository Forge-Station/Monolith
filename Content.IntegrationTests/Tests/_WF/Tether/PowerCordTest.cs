using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.Tether;
using Content.Server.Power.Components;
using Content.Shared._WF.Tether;
using Content.Shared._WF.Tether.PowerCord;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.Tether;

/// <summary>
/// Power cords across two hulls: the cord merges the nets, parting it separates them again, and a
/// cord only clamps onto its own voltage.
/// </summary>
[TestFixture]
public sealed class PowerCordTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFTestCordGenerator
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      output:
        !type:CableDeviceNode
        nodeGroupID: HVPower
  - type: PowerSupplier

- type: entity
  id: WFTestCordConsumer
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      input:
        !type:CableDeviceNode
        nodeGroupID: HVPower
  - type: PowerConsumer
";

    private const float Load = 200f;

    /// <summary>Ticks the power solver needs to settle after the nets change.</summary>
    private const int SettleTicks = 30;

    [Test]
    public async Task CordCarriesPowerBetweenHullsUntilItParts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid gridA = default, gridB = default, clampA = default, clampB = default, rope = default;
        PowerConsumerComponent consumer = default!;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            (gridA, gridB) = CreatePair(entities, maps, map.MapId);
            // A cable run under each hull's machine, reaching the tile its clamp sits on.
            entities.SpawnEntity("CableHV", Tile(gridA, 1, 1));
            entities.SpawnEntity("CableHV", Tile(gridA, 2, 1));
            entities.SpawnEntity("CableHV", Tile(gridB, 0, 1));
            entities.SpawnEntity("CableHV", Tile(gridB, 1, 1));

            var supplier = entities.GetComponent<PowerSupplierComponent>(
                entities.SpawnEntity("WFTestCordGenerator", Tile(gridA, 1, 1)));
            supplier.MaxSupply = Load * 4f;
            supplier.SupplyRampTolerance = Load * 4f;

            consumer = entities.GetComponent<PowerConsumerComponent>(
                entities.SpawnEntity("WFTestCordConsumer", Tile(gridB, 1, 1)));
            consumer.DrawRate = Load;

            clampA = entities.SpawnEntity("PowerCordClampHV", Tile(gridA, 2, 1));
            clampB = entities.SpawnEntity("PowerCordClampHV", Tile(gridB, 0, 1));
        });

        await server.WaitRunTicks(SettleTicks);
        await server.WaitAssertion(() =>
        {
            Assert.That(consumer.ReceivedPower, Is.EqualTo(0f).Within(0.1f),
                "Two separate hulls must not share power before the cord is run.");

            Assert.That(entities.System<RopeSystem>()
                .TryCreateRope(clampA, clampB, "PowerCordHV", 14f, out var created), Is.True);
            rope = created!.Value;
        });

        await server.WaitRunTicks(SettleTicks);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<PowerCordClampComponent>(clampA).Partner,
                    Is.EqualTo(entities.GetNetEntity(clampB)), "Both clamps must name each other.");
                Assert.That(entities.GetComponent<PowerCordClampComponent>(clampB).Partner,
                    Is.EqualTo(entities.GetNetEntity(clampA)));
                Assert.That(consumer.ReceivedPower, Is.EqualTo(Load).Within(0.1f),
                    "The cord merges the two nets, so the far load draws from the generator.");
            });

            entities.System<RopeSystem>().BreakRope(rope);
        });

        await server.WaitRunTicks(SettleTicks);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(consumer.ReceivedPower, Is.EqualTo(0f).Within(0.1f),
                    "A parted cord separates the nets again.");
                Assert.That(entities.Deleted(clampA), Is.True, "A clamp with no cord left is removed.");
                Assert.That(entities.Deleted(clampB), Is.True);
            });

            entities.DeleteEntity(gridA);
            entities.DeleteEntity(gridB);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CoilClicksBoltClampsOntoPoweredTiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (gridA, gridB) = CreatePair(entities, maps, map.MapId);
            var cableA = entities.SpawnEntity("CableHV", Tile(gridA, 2, 1));
            var cableB = entities.SpawnEntity("CableHV", Tile(gridB, 0, 1));
            var user = entities.SpawnEntity(null, Tile(gridA, 1, 1));
            var coil = entities.SpawnEntity("PowerCordCoilHV", Tile(gridA, 1, 1));

            // Both clicks happen inside one tick, so the carried end cannot be dropped in between.
            Interact(entities, user, coil, cableA);
            Assert.That(entities.HasComponent<RopeCarrierComponent>(user), Is.True,
                "Clicking powered cable bolts a clamp down and takes the loose end off the coil.");

            Interact(entities, user, coil, cableB);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<RopeCarrierComponent>(user), Is.False);
                Assert.That(CountClamps(entities), Is.EqualTo(2), "One clamp per end, and no more.");
            });

            var clamp = FindClamp(entities, gridA);
            Assert.That(entities.GetComponent<RopeAttachPointComponent>(clamp).Ropes, Has.Count.EqualTo(1));
            Assert.That(entities.GetComponent<PowerCordClampComponent>(clamp).Partner, Is.Not.Null);

            entities.DeleteEntity(gridA);
            entities.DeleteEntity(gridB);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CordRefusesTheWrongVoltage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (gridA, gridB) = CreatePair(entities, maps, map.MapId);
            var cable = entities.SpawnEntity("CableHV", Tile(gridA, 2, 1));
            var user = entities.SpawnEntity(null, Tile(gridA, 1, 1));
            var coil = entities.SpawnEntity("PowerCordCoilMV", Tile(gridA, 1, 1));

            Interact(entities, user, coil, cable);
            Assert.Multiple(() =>
            {
                Assert.That(CountClamps(entities), Is.Zero, "An MV cord finds nothing to clamp to on HV cable.");
                Assert.That(entities.HasComponent<RopeCarrierComponent>(user), Is.False);
            });

            // The same cord is equally refused by a clamp of another voltage.
            var clamp = entities.SpawnEntity("PowerCordClampHV", Tile(gridA, 2, 1));
            Interact(entities, user, coil, clamp);
            Assert.That(entities.HasComponent<RopeCarrierComponent>(user), Is.False,
                "A clamp only takes its own cord.");

            // ... and a clamp refuses plain rope.
            var rope = entities.SpawnEntity("RopeCoilDebug", Tile(gridA, 1, 1));
            Interact(entities, user, rope, clamp);
            Assert.That(entities.HasComponent<RopeCarrierComponent>(user), Is.False,
                "Plain rope does not belong on a power clamp.");

            entities.DeleteEntity(gridA);
            entities.DeleteEntity(gridB);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CancellingTheFirstClickLeavesNoClampBehind()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid gridA = default, gridB = default;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            (gridA, gridB) = CreatePair(entities, maps, map.MapId);
            var cable = entities.SpawnEntity("CableHV", Tile(gridA, 2, 1));
            var user = entities.SpawnEntity(null, Tile(gridA, 1, 1));
            var coil = entities.SpawnEntity("PowerCordCoilHV", Tile(gridA, 1, 1));

            Interact(entities, user, coil, cable);
            Assert.That(CountClamps(entities), Is.EqualTo(1), "The first click bolts a clamp down.");

            // Using the coil in hand drops the loose end before it is ever tied off.
            entities.EventBus.RaiseLocalEvent(coil, new UseInHandEvent(user));
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(CountClamps(entities), Is.Zero,
                "A cancelled carry must take the clamp its first click bolted down with it.");

            entities.DeleteEntity(gridA);
            entities.DeleteEntity(gridB);
        });

        await pair.CleanReturnAsync();
    }

    private static void Interact(IEntityManager entities, EntityUid user, EntityUid used, EntityUid target)
    {
        var ev = new AfterInteractEvent(user, used, target,
            entities.GetComponent<TransformComponent>(target).Coordinates, true);
        entities.EventBus.RaiseLocalEvent(used, ev);
    }

    private static int CountClamps(IEntityManager entities)
    {
        var total = 0;
        var query = entities.EntityQueryEnumerator<PowerCordClampComponent>();
        while (query.MoveNext(out _, out _))
        {
            total++;
        }

        return total;
    }

    private static EntityUid FindClamp(IEntityManager entities, EntityUid grid)
    {
        var query = entities.EntityQueryEnumerator<PowerCordClampComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (entities.GetComponent<TransformComponent>(uid).GridUid == grid)
                return uid;
        }

        Assert.Fail("No clamp was bolted onto the grid.");
        return default;
    }

    private static EntityCoordinates Tile(EntityUid grid, int x, int y)
    {
        return new EntityCoordinates(grid, new Vector2(x, y));
    }

    /// <summary>Two floating 3x3 hulls on one map, ten metres apart.</summary>
    private static (EntityUid GridA, EntityUid GridB) CreatePair(IEntityManager entities, IMapManager maps, MapId map)
    {
        var mapSystem = entities.System<SharedMapSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var transform = entities.System<SharedTransformSystem>();

        return (MakeGrid(Vector2.Zero), MakeGrid(new Vector2(10f, 0f)));

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
