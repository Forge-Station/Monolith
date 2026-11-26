#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.Xenonids.Construction;
using Content.Shared._WF.Xenonids.Construction.Events;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared._WF.Xenonids.Plasma;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>Resin construction: a builder xeno on its hive's weeds secretes a resin wall.</summary>
[TestFixture]
public sealed class XenoConstructionTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: CMXenoLarva
  id: WFTestXenoResinBuilder
  components:
  - type: XenoConstruction
    buildDelay: 0.5
    canBuild:
    - WallXenoResin
    buildChoice: WallXenoResin
  - type: XenoPlasma
    plasma: 500
    maxPlasma: 500
    plasmaRegenOnWeeds: 0
    plasmaRegenOffWeeds: 0
";

    /// <summary>Secreting a chosen resin wall on hive weeds spawns the wall and spends its plasma.</summary>
    [Test]
    public async Task SecretesResinWallOnWeeds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var mapSystem = entities.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();

        EntityUid xeno = default;
        EntityCoordinates wallCoords = default;
        await server.WaitPost(() =>
        {
            var grid = map.Grid;
            mapSystem.SetTile(grid, new Vector2i(1, 0), map.Tile.Tile);
            wallCoords = new EntityCoordinates(grid, 1.5f, 0.5f);

            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            var weeds = entities.SpawnEntity("XenoWeeds", wallCoords);
            hives.SetHive(weeds, hive);

            xeno = entities.SpawnEntity("WFTestXenoResinBuilder", new EntityCoordinates(grid, 0.5f, 0.5f));
            hives.SetHive(xeno, hive);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var ev = new XenoSecreteStructureActionEvent { Performer = xeno, Target = wallCoords };
            entities.EventBus.RaiseLocalEvent(xeno, ev);
            Assert.That(ev.Handled, "Secreting on weeds within reach should start building.");
        });

        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            var walls = new List<EntityUid>();
            var query = entities.EntityQueryEnumerator<XenoConstructComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out _, out var meta))
            {
                if (meta.EntityPrototype?.ID == "WallXenoResin")
                    walls.Add(uid);
            }

            Assert.That(walls, Has.Count.EqualTo(1), "The resin wall should have been built.");
            var wallTile = mapSystem.TileIndicesFor(map.Grid, entities.GetComponent<TransformComponent>(walls.First()).Coordinates);
            Assert.That(wallTile, Is.EqualTo(new Vector2i(1, 0)));
            Assert.That(entities.GetComponent<HiveMemberComponent>(walls.First()).Hive, Is.EqualTo(entities.GetComponent<HiveMemberComponent>(xeno).Hive));
            Assert.That(entities.GetComponent<XenoPlasmaComponent>(xeno).Plasma, Is.LessThan(FixedPoint2.New(500)), "Building should spend plasma.");
        });

        await pair.CleanReturnAsync();
    }
}
