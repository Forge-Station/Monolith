#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.Parallax;
using Content.Server.Decals;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>Verifies authored sites and their roads survive the terrain preload.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetPoiSystem))]
public sealed class PlanetPoiNetworkTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: wfPlanetPoiTable
  id: WFPoiNetworkTest
  minCount: 3
  maxCount: 3
  spacing: 36
  edgeMargin: 24
  centreClear: 24
  footprint: 12
  roads:
    tile: FloorAsphalt
    width: 3
    shoulderTile: FloorConcrete
    shoulderWidth: 1
  entries:
  - mapPath: /Maps/_WF/PlanetPois/Tests/through_service.yml
    name: wf-poi-asclepiu-research
    footprint: 0,0,16,12
    minCount: 1
    maxCount: 1
    connection: Through
    sockets:
    - offset: 0,6
      direction: West
    - offset: 15,6
      direction: East
  - mapPath: /Maps/_WF/PlanetPois/Tests/through_service.yml
    name: wf-poi-asclepiu-clinic
    footprint: 0,0,16,12
    minCount: 1
    maxCount: 1
    connection: Roadside
    sockets:
    - offset: 0,6
      direction: West
  - mapPath: /Maps/_WF/PlanetPois/Tests/through_service.yml
    name: wf-poi-asclepiu-dorms
    footprint: 0,0,16,12
    minCount: 1
    maxCount: 1
    connection: Isolated
";

    [Test]
    public async Task AuthoredRoofsOutdoorsAndConnectedRoadsSurvivePreload()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, 128);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WFPoiNetworkTest");
            var surface = server.ResolveDependency<IPrototypeManager>().Index<WFPlanetSurfacePrototype>(PlanetFixture.SurfaceProto);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Asclepiu", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
        });
        var ground = layers[0];
        try
        {
            for (var i = 0; i < 300 && !entMan.HasComponent<WFPlanetPreloadedComponent>(ground); i++)
                await pair.RunTicksSync(10);
            await server.WaitPost(() =>
            {
                Assert.That(entMan.HasComponent<WFPlanetPreloadedComponent>(ground), Is.True);
                var record = entMan.GetComponent<WFPlanetPoiComponent>(ground);
                Assert.That(record.GenerationError, Is.Null);
                Assert.That(record.NetworkReady, Is.True);
                Assert.That(record.Sites, Has.Count.EqualTo(3));
                Assert.That(record.RoadTiles, Is.Not.Empty);
                var grid = entMan.GetComponent<MapGridComponent>(ground);
                var biome = entMan.GetComponent<BiomeComponent>(ground);
                var roof = entMan.GetComponent<RoofComponent>(ground);
                var roofs = server.System<SharedRoofSystem>();
                var biomes = server.System<BiomeSystem>();
                var maps = server.System<SharedMapSystem>();
                var tileDefinitions = server.ResolveDependency<ITileDefinitionManager>();
                Assert.That(record.ShoulderTiles, Is.Not.Empty);
                var reached = Flood(record.RoadTiles);
                Assert.That(reached.Count, Is.EqualTo(record.RoadTiles.Count), "The paved network is disconnected.");
                Assert.That(record.Sites.Select(s => s.Connection), Is.EquivalentTo(new[]
                {
                    WFPlanetPoiConnection.Through, WFPlanetPoiConnection.Roadside, WFPlanetPoiConnection.Isolated,
                }));
                foreach (var site in record.Sites)
                {
                    Assert.That(site.TileCount, Is.EqualTo(192));
                    var decalPosition = (Vector2) site.Origin + new Vector2(8, 6);
                    var decals = server.System<DecalSystem>().GetDecalsIntersecting(ground, Box2.CenteredAround(decalPosition, Vector2.One));
                    Assert.That(decals.Any(value => value.Decal.Id == "RoadStop" && value.Decal.Coordinates == decalPosition), Is.True,
                        "The authored road marking was lost during merge.");
                    var expectedSockets = site.Connection == WFPlanetPoiConnection.Through ? 2
                        : site.Connection == WFPlanetPoiConnection.Roadside ? 1 : 0;
                    Assert.That(site.Sockets, Has.Count.EqualTo(expectedSockets));
                    foreach (var socket in site.Sockets)
                        Assert.That(reached, Does.Contain(socket.Position));
                    var wallTile = site.Origin + new Vector2i(6, 2);
                    var wall = maps.GetAnchoredEntities((ground, grid), wallTile).Single(uid =>
                        entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "WallSolid");
                    var metadata = entMan.GetComponent<MetaDataComponent>(wall);
                    Assert.That(metadata.EntityLifeStage, Is.EqualTo(EntityLifeStage.MapInitialized));
                    Assert.That(entMan.GetComponent<TransformComponent>(wall).MapID,
                        Is.EqualTo(entMan.GetComponent<TransformComponent>(ground).MapID), "Transferred entities retained their staging map.");
                    Assert.That(metadata.EntityPaused, Is.EqualTo(maps.IsPaused(ground)));
                    if (site.Connection == WFPlanetPoiConnection.Isolated)
                        Assert.That(record.RoadTiles.Any(t => t.X >= site.Bounds.Left && t.X < site.Bounds.Right
                            && t.Y >= site.Bounds.Bottom && t.Y < site.Bounds.Top), Is.False);
                    for (var x = site.Bounds.Left; x < site.Bounds.Right; x++)
                    for (var y = site.Bounds.Bottom; y < site.Bounds.Top; y++)
                        Assert.That(biomes.WfIsPinned((ground, biome), new Vector2i(x, y)), Is.True);
                    Assert.That(roofs.IsRooved((ground, grid, roof), site.Origin + new Vector2i(5, 1)), Is.True, "Authored roof was lost during merge.");
                    Assert.That(roofs.IsRooved((ground, grid, roof), site.Origin + new Vector2i(8, 6)), Is.False, "Outdoor through corridor was roofed.");
                    Assert.That(biomes.WfIsPinned((ground, biome), site.Origin + new Vector2i(8, 6)), Is.True);
                }
                foreach (var tile in record.RoadTiles.Concat(record.ShoulderTiles))
                {
                    Assert.That(biomes.WfIsPinned((ground, biome), tile), Is.True);
                    Assert.That(maps.GetTileRef(ground, grid, tile).Tile.TypeId,
                        Is.EqualTo(tileDefinitions[record.RoadTiles.Contains(tile) ? "FloorAsphalt" : "FloorConcrete"].TileId));
                    Assert.That(roofs.IsRooved((ground, grid, roof), tile), Is.False, "A road should remain open to the sky.");
                }
            });
        }
        finally { await PlanetFixture.Teardown(pair, layers); }
        await pair.CleanReturnAsync();
    }

    /// <summary>Finds the road tiles reachable without crossing unpaved ground.</summary>
    private static HashSet<Vector2i> Flood(HashSet<Vector2i> tiles)
    {
        var found = new HashSet<Vector2i>();
        var pending = new Queue<Vector2i>();
        pending.Enqueue(tiles.First());
        while (pending.TryDequeue(out var tile))
        {
            if (!found.Add(tile))
                continue;
            foreach (var step in new[] { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right })
                if (tiles.Contains(tile + step) && !found.Contains(tile + step))
                    pending.Enqueue(tile + step);
        }
        return found;
    }

}
