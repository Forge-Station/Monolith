using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.Tether;
using Content.Shared._WF.Tether;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.Tether;

/// <summary>
/// Stage 2A content: the real rope types and coils, the anchor eye and the installer gun.
/// </summary>
[TestFixture]
public sealed class TetherContentTest
{
    private static readonly string[] CoilEntities =
    {
        "RopeHempCoil", "RopeHempCoil1",
        "RopeSyntheticCoil", "RopeSyntheticCoil1",
        "RopeBungeeCoil", "RopeBungeeCoil1",
        "RopeSteelCableCoil", "RopeSteelCableCoil1",
        "RopeTowCableCoil", "RopeTowCableCoil1",
    };

    private static readonly string[] RopeTypes =
    {
        "RopeHemp", "RopeSynthetic", "RopeBungee", "RopeSteelCable", "RopeTowCable",
    };

    [Test]
    public async Task AllContentPrototypesSpawnWithoutError()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            foreach (var ropeType in RopeTypes)
            {
                Assert.That(protos.HasIndex<RopeTypePrototype>(ropeType), Is.True, $"{ropeType} must exist.");
            }

            foreach (var id in CoilEntities)
            {
                var uid = entities.SpawnEntity(id, map.GridCoords);
                Assert.That(entities.Deleted(uid), Is.False, $"{id} failed to spawn.");
                Assert.That(entities.HasComponent<RopeCoilComponent>(uid), Is.True);
                Assert.That(entities.HasComponent<StackComponent>(uid), Is.True);
                entities.DeleteEntity(uid);
            }

            var eye = entities.SpawnEntity("TetherAnchorEye", map.GridCoords);
            Assert.That(entities.Deleted(eye), Is.False, "TetherAnchorEye failed to spawn.");
            Assert.That(entities.HasComponent<RopeAttachPointComponent>(eye), Is.True);
            Assert.That(entities.GetComponent<RopeAttachPointComponent>(eye).MaxRopes, Is.EqualTo(2));
            entities.DeleteEntity(eye);

            var installer = entities.SpawnEntity("TetherInstaller", map.GridCoords);
            Assert.That(entities.Deleted(installer), Is.False, "TetherInstaller failed to spawn.");
            Assert.That(entities.HasComponent<TetherInstallerComponent>(installer), Is.True);
            Assert.That(entities.HasComponent<MaterialStorageComponent>(installer), Is.True);
            entities.DeleteEntity(installer);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InstallerPlacesAnEyeAndConsumesMaterial()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var materials = entities.System<SharedMaterialStorageSystem>();

        EntityUid installer = default, user = default;
        EntityCoordinates target = default;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            var grid = MakeFloorGrid(server, map.MapId);
            var origin = new EntityCoordinates(grid, new Vector2(2.5f, 2.5f));
            target = new EntityCoordinates(grid, new Vector2(3.5f, 2.5f));
            user = entities.SpawnEntity(null, origin);
            entities.EnsureComponent<HandsComponent>(user);
            entities.EnsureComponent<DoAfterComponent>(user);
            entities.System<SharedHandsSystem>().AddHand(user, "hand", HandLocation.Right);
            installer = entities.SpawnEntity("TetherInstaller", origin);
            Assert.That(materials.TryChangeMaterialAmount(installer, "Steel", 400), Is.True,
                "Test setup must be able to load steel into the installer.");

            var ev = new AfterInteractEvent(user, installer, null, target, true);
            entities.EventBus.RaiseLocalEvent(installer, ev);
        });

        // 1.5 s doafter plus slack for the tick it needs to notice the event.
        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            var before = materials.GetMaterialAmount(installer, "Steel");
            Assert.That(before, Is.EqualTo(200), "One install consumes exactly 200 units (two sheets) of steel.");

            // The eye has no Physics/Fixtures (same as the debug attach point), so it may not be
            // indexed by EntityLookupSystem's tile query. Search anchor points directly instead.
            var found = false;
            var targetMap = entities.System<SharedTransformSystem>().ToMapCoordinates(target);
            var query = entities.EntityQueryEnumerator<RopeAttachPointComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID != "TetherAnchorEye")
                    continue;

                var pos = entities.System<SharedTransformSystem>().ToMapCoordinates(xform.Coordinates);
                if (pos.MapId == targetMap.MapId && Vector2.Distance(pos.Position, targetMap.Position) < 0.5f)
                    found = true;
            }

            Assert.That(found, Is.True, "The installer must have bolted an anchor eye to the targeted tile.");

            entities.DeleteEntity(user);
            entities.DeleteEntity(installer);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EachRealRopeTypeTiesBetweenTwoAnchorEyesOnOneGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();

        var coilByType = new Dictionary<string, string>
        {
            ["RopeHemp"] = "RopeHempCoil",
            ["RopeSynthetic"] = "RopeSyntheticCoil",
            ["RopeBungee"] = "RopeBungeeCoil",
            ["RopeSteelCable"] = "RopeSteelCableCoil",
            ["RopeTowCable"] = "RopeTowCableCoil",
        };

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var grid = MakeFloorGrid(server, map.MapId);

            foreach (var (ropeType, coilId) in coilByType)
            {
                var pointA = entities.SpawnEntity("TetherAnchorEye", new EntityCoordinates(grid, new Vector2(2.5f, 2.5f)));
                var pointB = entities.SpawnEntity("TetherAnchorEye", new EntityCoordinates(grid, new Vector2(6.5f, 2.5f)));
                var user = entities.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(2.5f, 2.5f)));
                var coil = entities.SpawnEntity(coilId, new EntityCoordinates(grid, new Vector2(2.5f, 2.5f)));

                Interact(entities, user, coil, pointA);
                Assert.That(entities.HasComponent<RopeCarrierComponent>(user), Is.True,
                    $"{ropeType}: the first click must take the loose end off the coil.");

                Interact(entities, user, coil, pointB);
                Assert.That(entities.HasComponent<RopeCarrierComponent>(user), Is.False,
                    $"{ropeType}: the second click must finish the tie.");
                Assert.That(entities.GetComponent<RopeAttachPointComponent>(pointA).Ropes, Has.Count.EqualTo(1),
                    $"{ropeType}: point A must hold the rope.");
                Assert.That(entities.GetComponent<RopeAttachPointComponent>(pointB).Ropes, Has.Count.EqualTo(1),
                    $"{ropeType}: point B must hold the rope.");

                var net = entities.GetComponent<RopeAttachPointComponent>(pointA).Ropes[0];
                var rope = entities.GetEntity(net);
                Assert.That(entities.GetComponent<RopeComponent>(rope).RopeType.Id, Is.EqualTo(ropeType));

                entities.DeleteEntity(pointA);
                entities.DeleteEntity(pointB);
                entities.DeleteEntity(user);
                if (!entities.Deleted(coil))
                    entities.DeleteEntity(coil);
            }
        });

        await pair.CleanReturnAsync();
    }

    private static void Interact(IEntityManager entities, EntityUid user, EntityUid coil, EntityUid target)
    {
        var ev = new AfterInteractEvent(user, coil, target, entities.GetComponent<TransformComponent>(target).Coordinates, true);
        entities.EventBus.RaiseLocalEvent(coil, ev);
    }

    /// <summary>An 8x5 static plating grid, big enough to place two anchor eyes and an installer target apart.</summary>
    private static EntityUid MakeFloorGrid(RobustIntegrationTest.ServerIntegrationInstance server, MapId map)
    {
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        var mapSystem = entities.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        tileDefs.TryGetDefinition("Plating", out var platingDef);
        var plating = new Tile(platingDef?.TileId ?? 1);

        var grid = maps.CreateGridEntity(map);
        var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
        for (var x = 0; x < 8; x++)
        {
            for (var y = 0; y < 5; y++)
            {
                tiles.Add((new Vector2i(x, y), plating));
            }
        }

        mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);
        return grid.Owner;
    }
}
