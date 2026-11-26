#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Popups;
using Content.IntegrationTests.Pair;
using Content.Shared._WF.RMCCore.Actions;
using Content.Shared._WF.Xenonids.Construction;
using Content.Shared._WF.Xenonids.Construction.Events;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared._WF.Xenonids.Plasma;
using Content.Shared.Actions;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>
/// Secrete Resin through the client's action request: a drone on weeds builds on the tiles around it with the
/// coordinates the client sends, and the client predicts no "can't reach" popup.
/// </summary>
[TestFixture]
public sealed class XenoConstructionReachTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: CMXenoDrone
  id: WFTestXenoReachBuilder
  components:
  - type: XenoConstruction
    buildDelay: 0.5
    canBuild:
    - WallXenoResin
    buildChoice: WallXenoResin
";

    /// <summary>Where the drone stands in its tile, and the tile it builds on.</summary>
    private static readonly (Vector2 Drone, Vector2i Tile, bool MapRelative)[] Cases =
    {
        (new Vector2(0.5f, 0.5f), new Vector2i(1, 0), false),
        (new Vector2(0.5f, 0.5f), new Vector2i(0, 1), true),
        (new Vector2(0.8f, 0.8f), new Vector2i(1, 1), false),
        (new Vector2(0.2f, 0.5f), new Vector2i(-1, 0), true),
        (new Vector2(0.75f, 0.3f), new Vector2i(-1, -1), false),
    };

    [Test]
    public async Task ClientSecreteOnAdjacentTileBuilds()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntities = server.EntMan;
        var cEntities = client.EntMan;
        var map = await pair.CreateTestMap();
        var cantReach = client.ResolveDependency<ILocalizationManager>().GetString("cm-xeno-cant-reach-there");

        EntityUid hive = default;
        await server.WaitPost(() =>
        {
            var maps = sEntities.System<SharedMapSystem>();
            for (var x = -2; x <= 2; x++)
            for (var y = -2; y <= 2; y++)
                maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);

            hive = sEntities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            {
                var weeds = sEntities.SpawnEntity("XenoWeeds", new EntityCoordinates(map.Grid, x + 0.5f, y + 0.5f));
                sEntities.System<SharedXenoHiveSystem>().SetHive(weeds, hive);
            }
        });

        foreach (var (dronePos, tile, mapRelative) in Cases)
        {
            EntityUid drone = default;
            await server.WaitPost(() =>
            {
                drone = sEntities.SpawnEntity("WFTestXenoReachBuilder", new EntityCoordinates(map.Grid, dronePos));
                sEntities.System<SharedXenoHiveSystem>().SetHive(drone, hive);
                var plasma = sEntities.GetComponent<XenoPlasmaComponent>(drone);
                sEntities.System<XenoPlasmaSystem>().SetPlasma((drone, plasma), plasma.MaxPlasma);
                server.PlayerMan.SetAttachedEntity(pair.Player!, drone);
            });
            await pair.RunTicksSync(20);

            await client.WaitPost(() =>
            {
                var cDrone = cEntities.GetEntity(sEntities.GetNetEntity(drone));
                var grid = cEntities.GetEntity(sEntities.GetNetEntity(map.Grid));
                var transform = cEntities.System<SharedTransformSystem>();
                var gridClick = new EntityCoordinates(grid, tile.X + 0.3f, tile.Y + 0.7f);
                var click = mapRelative ? transform.WithEntityId(gridClick, transform.GetMap(grid)!.Value) : gridClick;

                var action = cEntities.System<SharedRMCActionsSystem>().GetActionsWithEvent<XenoSecreteStructureActionEvent>(cDrone).First();
                cEntities.RaisePredictiveEvent(new RequestPerformActionEvent(cEntities.GetNetEntity(action), cEntities.GetNetCoordinates(click)));
            });
            await pair.RunSeconds(1.5f);

            // covers the predicted request and the predicted end of the build doafter
            await client.WaitAssertion(() =>
            {
                var popups = cEntities.System<PopupSystem>().WorldLabels.Select(l => l.Text).ToList();
                Assert.That(popups, Has.None.EqualTo(cantReach),
                    $"Drone at {dronePos} building on {tile} (map-relative: {mapRelative}) predicted a reach failure.");
            });

            await server.WaitAssertion(() =>
            {
                var built = new List<Vector2i>();
                var query = sEntities.EntityQueryEnumerator<XenoConstructComponent, MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out _, out _, out var meta, out var xform))
                {
                    if (meta.EntityPrototype?.ID == "WallXenoResin")
                        built.Add(sEntities.System<SharedMapSystem>().TileIndicesFor(map.Grid, xform.Coordinates));
                }

                Assert.That(built, Does.Contain(tile),
                    $"Drone at {dronePos} should build on {tile} (map-relative: {mapRelative}).");

                server.PlayerMan.SetAttachedEntity(pair.Player!, null);
                sEntities.DeleteEntity(drone);
            });
        }

        await pair.CleanReturnAsync();
    }
}
