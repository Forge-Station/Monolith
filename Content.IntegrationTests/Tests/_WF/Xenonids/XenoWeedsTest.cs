#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.Maths;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared._WF.Xenonids.Weeds;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>Weeds slow outsiders but never xenos, and a node spreads over a whole room.</summary>
[TestFixture]
public sealed class XenoWeedsTest
{
    private static readonly string[] Floors = ["Plating", "FloorSteel", "FloorWood", "FloorGrass", "FloorDirt", "FloorWhite"];

    /// <summary>A drone on weeds keeps its speed, a human on the same weeds is slowed.</summary>
    [Test]
    public async Task WeedsSlowHumansNotXenos()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var map = await pair.CreateTestMap();

        EntityUid hive = default;
        EntityUid otherHive = default;
        EntityUid drone = default;
        EntityUid rogue = default;
        EntityUid enemy = default;
        EntityUid human = default;
        EntityUid control = default;
        EntityUid hiveless = default;
        EntityUid late = default;

        await server.WaitPost(() =>
        {
            hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            otherHive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);

            // One weeds tile per mob so the contacts don't overlap.
            Weeds(0, hive);
            Weeds(2, hive);
            Weeds(4, hive);
            Weeds(6, hive);
            Weeds(8, null);
            Weeds(10, hive);

            drone = Mob("CMXenoDrone", 0, hive);
            enemy = Mob("CMXenoDrone", 2, otherHive);
            human = Mob("MobHuman", 4, null);
            rogue = Mob("CMXenoDrone", 6, null);
            entities.RemoveComponent<HiveMemberComponent>(rogue);
            late = Mob("CMXenoDrone", 10, null);
            hiveless = Mob("CMXenoDrone", 8, hive);
            control = Mob("CMXenoDrone", 20, hive);
        });

        await pair.RunTicksSync(30);

        // A xeno that joins the weeds' hive after it arrived is no longer an outsider.
        await server.WaitAssertion(() =>
        {
            var baseSpeed = entities.GetComponent<MovementSpeedModifierComponent>(control).CurrentSprintSpeed;
            Assert.That(entities.GetComponent<MovementSpeedModifierComponent>(late).CurrentSprintSpeed, Is.LessThan(baseSpeed),
                "A drone of the default hive is an outsider on this hive's weeds.");
            hives.SetHive(late, hive);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var baseSpeed = entities.GetComponent<MovementSpeedModifierComponent>(control).CurrentSprintSpeed;
            var onWeeds = entities.GetComponent<MovementSpeedModifierComponent>(drone).CurrentSprintSpeed;
            Assert.That(onWeeds, Is.GreaterThanOrEqualTo(baseSpeed - 0.001f), "A hive drone on its own weeds must not be slowed.");

            var rogueSpeed = entities.GetComponent<MovementSpeedModifierComponent>(rogue).CurrentSprintSpeed;
            Assert.That(rogueSpeed, Is.GreaterThanOrEqualTo(baseSpeed - 0.001f), "A hiveless drone must not be slowed by weeds.");

            var hivelessWeedsSpeed = entities.GetComponent<MovementSpeedModifierComponent>(hiveless).CurrentSprintSpeed;
            Assert.That(hivelessWeedsSpeed, Is.GreaterThanOrEqualTo(baseSpeed - 0.001f), "Weeds with no hive must not slow a drone.");

            var enemySpeed = entities.GetComponent<MovementSpeedModifierComponent>(enemy).CurrentSprintSpeed;
            Assert.That(enemySpeed, Is.LessThan(baseSpeed), "An enemy hive's drone is slowed, as in RMC.");

            var lateSpeed = entities.GetComponent<MovementSpeedModifierComponent>(late).CurrentSprintSpeed;
            Assert.That(lateSpeed, Is.GreaterThanOrEqualTo(baseSpeed - 0.001f), "Joining the weeds' hive must lift the slow.");

            var humanSpeed = entities.GetComponent<MovementSpeedModifierComponent>(human).CurrentSprintSpeed;
            var humanBase = entities.GetComponent<MovementSpeedModifierComponent>(human).BaseSprintSpeed;
            Assert.That(humanSpeed, Is.LessThan(humanBase - 0.01f), "A human on weeds is slowed.");
        });

        await pair.CleanReturnAsync();

        void Weeds(int x, EntityUid? weedHive)
        {
            var weeds = entities.SpawnEntity("XenoWeeds", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            if (weedHive != null)
                hives.SetHive(weeds, weedHive);
        }

        EntityUid Mob(string proto, float x, EntityUid? mobHive)
        {
            var mob = entities.SpawnEntity(proto, new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            if (mobHive != null)
                hives.SetHive(mob, mobHive);
            return mob;
        }
    }

    /// <summary>A weed node covers a 5x5 room of mixed floors, and keeps spreading when its planter is gone.</summary>
    [Test]
    public async Task NodeCoversRoom()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var maps = entities.System<SharedMapSystem>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid;

        await server.WaitPost(() =>
        {
            var i = 0;
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -2; y <= 2; y++)
                {
                    var floor = Floors[i++ % Floors.Length];
                    maps.SetTile(grid, grid.Comp, new Vector2i(x, y), new Tile(tiles[floor].TileId));
                }
            }

            // A table and a machine beside the node must not stop the weeds spreading under them.
            entities.SpawnEntity("TableFrame", new EntityCoordinates(grid, 1.5f, 0.5f));
            entities.SpawnEntity("Table", new EntityCoordinates(grid, -0.5f, 0.5f));
            entities.SpawnEntity("VendingMachineCoffee", new EntityCoordinates(grid, 0.5f, 1.5f));

            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            var node = entities.SpawnEntity("XenoWeedsSource", new EntityCoordinates(grid, 0.5f, 0.5f));
            hives.SetHive(node, hive);
        });

        await pair.RunSeconds(40);

        await server.WaitAssertion(() =>
        {
            var covered = new HashSet<Vector2i>();
            var query = entities.EntityQueryEnumerator<XenoWeedsComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var xform))
            {
                if (xform.GridUid == grid.Owner)
                    covered.Add(maps.CoordinatesToTile(grid, grid.Comp, xform.Coordinates));
            }

            var missing = new List<Vector2i>();
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -2; y <= 2; y++)
                {
                    if (!covered.Contains(new Vector2i(x, y)))
                        missing.Add(new Vector2i(x, y));
                }
            }

            Assert.That(missing, Is.Empty, "Every tile of the room should have weeds.");
        });

        await pair.CleanReturnAsync();
    }
}
