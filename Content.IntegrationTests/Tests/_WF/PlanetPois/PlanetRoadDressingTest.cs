#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.Decals;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>
/// A world's roads are furnished before its ground loads: kerbs face the road, lights stand on the shoulder, scattered
/// traffic keeps off the middle of the road, and all of it is still there once the terrain has grown round it.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetPoiSystem))]
public sealed class PlanetRoadDressingTest
{
    private static readonly string[] Lights = { "RMCStreetlight", "RMCStreetlightBroken" };

    private const int Radius = 160;

    /// <summary>A box round the whole test world, for asking after its decals.</summary>
    private static readonly Box2 World = new(-Radius - 8, -Radius - 8, Radius + 8, Radius + 8);

    [TestPrototypes]
    private const string Prototypes = @"
- type: wfRoadDressing
  id: WFRoadDressingTest
  kerbs:
    North: RMCPlanetHybrisaSidewalkNorth
    South: RMCPlanetHybrisaSidewalkSouth
    East: RMCPlanetHybrisaSidewalkEast
    West: RMCPlanetHybrisaSidewalkWest
    NorthEast: RMCPlanetHybrisaSidewalkNorthEast
    NorthWest: RMCPlanetHybrisaSidewalkNorthWest
    SouthEast: RMCPlanetHybrisaSidewalkSouthEast
    SouthWest: RMCPlanetHybrisaSidewalkSouthWest
    InnerNorthEast: RMCPlanetHybrisaSidewalkCornerNorthEast
    InnerNorthWest: RMCPlanetHybrisaSidewalkCornerNorthWest
    InnerSouthEast: RMCPlanetHybrisaSidewalkCornerSouthEast
    InnerSouthWest: RMCPlanetHybrisaSidewalkCornerSouthWest
  lineNorthSouth: RMCDecalHybrisaZM2
  lineEastWest: RMCDecalHybrisaZM1
  light: RMCStreetlight
  brokenLight: RMCStreetlightBroken
  brokenLightChance: 0.3
  lightSpacing: 6
  junctionLight: RMCTrafficLight
  scatter:
  - prototypes: [ RMCPropVehicleMonoSupronRed ]
    where: Kerb
    axis: EastWest
    per100: 6
  - prototypes: [ RMCPropVehicleMonoSupronBlueDestroyed ]
    where: Kerb
    per100: 4
  - prototypes: [ RMCPropVehicleTruck, RMCPropVehicleVanDamaged ]
    where: Kerb
    axis: EastWest
    align: true
    per100: 4
  - prototypes: [ WFRoadPropArmyTruck, WFRoadPropHumveeDestroyed ]
    where: Kerb
    align: true
    per100: 5
  - prototypes: [ RMCBarricadeHybrisaPlasticRoadBarrier ]
    align: true
    per100: 4
  - prototypes: [ WFStreetsTrashBin, RMCPropFireHydrantRed ]
    where: Shoulder
    per100: 8
  - prototypes: [ AU14DumpsterBlue ]
    where: Shoulder
    align: true
    per100: 4
  decals:
  - decals: [ RMCDecalRoadTiremark5 ]
    per100: 10
  - decals: [ RMCDecalHybrisaTrash1 ]
    where: Shoulder
    per100: 10

- type: wfPlanetPoiTable
  id: WFPoiDressingTest
  minCount: 3
  maxCount: 3
  spacing: 44
  edgeMargin: 24
  centreClear: 24
  footprint: 12
  roads:
    tile: RMCPlanetHybrisaAsphalt
    width: 5
    shoulderTile: RMCPlanetHybrisaSidewalk
    shoulderWidth: 1
    dressing: WFRoadDressingTest
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
    minCount: 2
    maxCount: 2
    connection: Roadside
    sockets:
    - offset: 0,6
      direction: West
";

    [Test]
    public async Task RoadsAreFurnishedAndStayOpen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var maps = server.System<SharedMapSystem>();
        var lookup = server.System<EntityLookupSystem>();
        var decals = server.System<DecalSystem>();

        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, Radius);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WFPoiDressingTest");
            var surface = proto.Index<WFPlanetSurfacePrototype>(PlanetFixture.SurfaceProto);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Asclepiu", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
        });

        var ground = layers[0];
        try
        {
            await PlanetBoundsTest.WaitPreloaded(pair, ground);

            await server.WaitPost(() => Check(server, ground, "WFRoadDressingTest", World, "RMCDecalRoadTiremark5"));
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The furnished roads of a ground: kerbs, lights, scattered pieces, an open middle and the marks on the tarmac.</summary>
    private static void Check(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid ground, string dressingId, Box2 world, string? mark)
    {
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var maps = server.System<SharedMapSystem>();
        var lookup = server.System<EntityLookupSystem>();
        var decals = server.System<DecalSystem>();

        var record = entMan.GetComponent<WFPlanetPoiComponent>(ground);
        var grid = entMan.GetComponent<MapGridComponent>(ground);
        var dressing = proto.Index<WFRoadDressingPrototype>(dressingId);

        Assert.That(record.GenerationError, Is.Null);
        Assert.That(record.Centreline, Is.Not.Empty, "The world got no roads to furnish.");
        Assert.That(record.Furniture, Is.Not.Empty, "Nothing was put on the roads.");

        bool InSite(Vector2i tile) => record.Sites.Any(site =>
            tile.X >= site.Bounds.Left && tile.X < site.Bounds.Right && tile.Y >= site.Bounds.Bottom && tile.Y < site.Bounds.Top);

        var lights = 0;
        var parked = 0;
        var blocked = new HashSet<Vector2i>();
        var kerbside = dressing.Scatter.Where(rule => rule.Where == WFRoadSpot.Kerb).SelectMany(rule => rule.Prototypes)
            .Select(id => id.Id).ToHashSet();

        Assert.Multiple(() =>
        {
            // Kerbs: every shoulder tile outside a site is the tile its place by the road calls for.
            foreach (var tile in record.ShoulderTiles)
            {
                if (InSite(tile) || WFRoadDressingPlanner.KerbOf(record.RoadTiles, tile) is not { } kerb)
                    continue;

                Assert.That(tileDefs[maps.GetTileRef(ground, grid, tile).Tile.TypeId].ID, Is.EqualTo(dressing.Kerbs[kerb].Id),
                    $"The shoulder at {tile} shows the wrong kerb for {kerb}.");
            }

            foreach (var uid in record.Furniture)
            {
                Assert.That(entMan.EntityExists(uid), Is.True, "A piece was lost while the terrain loaded.");
                if (!entMan.EntityExists(uid))
                    continue;

                var xform = entMan.GetComponent<TransformComponent>(uid);
                var id = entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID;
                var tile = xform.LocalPosition.Floored();

                Assert.That(xform.ParentUid, Is.EqualTo(ground), $"{id} is not on the ground.");
                Assert.That(InSite(tile), Is.False, $"{id} at {tile} stands inside a site.");

                if (Lights.Contains(id))
                {
                    lights++;
                    Assert.That(record.ShoulderTiles.Contains(tile), Is.True, $"A street light at {tile} is not on the shoulder.");
                }

                if (!entMan.TryGetComponent(uid, out FixturesComponent? fixtures) || fixtures.FixtureCount == 0
                    || !entMan.TryGetComponent(uid, out PhysicsComponent? body) || !body.CanCollide)
                    continue;

                // What it covers must be road or shoulder, and never the middle of the road.
                var box = lookup.GetAABBNoContainer(uid, xform.LocalPosition, xform.LocalRotation);
                var footprint = WFRoadDressingPlanner.Footprint(box.Translated(-new Vector2(tile.X + 0.5f, tile.Y + 0.5f)), tile).ToList();
                if (kerbside.Contains(id))
                {
                    parked++;
                    Assert.That(footprint.Any(covered => new[] { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right }
                        .Any(step => record.ShoulderTiles.Contains(covered + step))), Is.True, $"{id} at {tile} is not against the kerb.");
                }

                foreach (var covered in footprint)
                {
                    blocked.Add(covered);
                    Assert.That(record.RoadTiles.Contains(covered) || record.ShoulderTiles.Contains(covered), Is.True,
                        $"{id} at {tile} reaches off the road at {covered}.");
                    Assert.That(record.Centreline.Contains(covered), Is.False, $"{id} at {tile} blocks the middle of the road at {covered}.");
                }
            }

            Assert.That(lights, Is.GreaterThan(3), "The roads have almost no street lights.");
            Assert.That(record.Furniture.Count, Is.GreaterThan(lights), "Only lights were placed; nothing was scattered.");
            Assert.That(parked, Is.GreaterThan(0), "No vehicle was parked at the kerb.");

            // The road is open from end to end: its middle is one piece and nothing stands on it.
            var reached = new HashSet<Vector2i>();
            var frontier = new Queue<Vector2i>();
            frontier.Enqueue(record.Centreline.First());
            while (frontier.TryDequeue(out var tile))
            {
                if (!reached.Add(tile))
                    continue;

                foreach (var step in new[] { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right })
                {
                    if (record.RoadTiles.Contains(tile + step) && !blocked.Contains(tile + step))
                        frontier.Enqueue(tile + step);
                }
            }

            Assert.That(record.Centreline.All(reached.Contains), Is.True, "Furniture cut the road in two.");

            // The line down the middle, and the scattered marks.
            var marks = decals.GetDecalsIntersecting(ground, world).Select(decal => decal.Decal.Id).ToList();
            Assert.That(marks.Count(mark => mark is "RMCDecalHybrisaZM1" or "RMCDecalHybrisaZM2"), Is.GreaterThan(10), "The roads have no middle line.");
            if (mark != null)
                Assert.That(marks, Does.Contain(mark));
        });

        Dump(record, entMan, tileDefs, maps, decals, ground, world, dressingId);
    }

    /// <summary>The worlds that ship with dressed roads get them at their real size, with their own cities on the network.</summary>
    [TestCase("WFSurfaceAsclepiu", "WFPoisAsclepiu", 384)]
    [TestCase("WFSurfaceMerak", "WFPoisMerak", 320)]
    public async Task ShippedWorldsHaveFurnishedRoads(string surfaceId, string tableId, int radius)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        var holds = new List<Task>();
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, radius);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, tableId);
            var surface = proto.Index<WFPlanetSurfacePrototype>(surfaceId);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, surfaceId, null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);

            // The sites and roads are laid as a hold on the preload; the terrain itself is not needed here.
            var starting = new WFPlanetPreloadStartingEvent(layers[0], Vector2.Zero, radius, holds);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref starting);
        });

        try
        {
            for (var attempt = 0; attempt < 900 && holds.Any(hold => !hold.IsCompleted); attempt++)
                await pair.RunTicksSync(10);

            Assert.That(holds, Is.Not.Empty);
            Assert.That(holds.All(hold => hold.IsCompletedSuccessfully), Is.True, "The sites and roads were not laid.");

            await server.WaitPost(() =>
            {
                var dressing = proto.Index<WFPlanetPoiTablePrototype>(tableId).Roads!.Dressing!.Value;
                var world = new Box2(-radius - 8, -radius - 8, radius + 8, radius + 8);
                Check(server, layers[0], dressing, world, null);

                // A tunnel or two: roofed road, still open to drive, walled with rock either side.
                var record = entMan.GetComponent<WFPlanetPoiComponent>(layers[0]);
                var grid = entMan.GetComponent<MapGridComponent>(layers[0]);
                var maps = server.System<SharedMapSystem>();
                var roofs = server.System<Content.Shared.Light.EntitySystems.SharedRoofSystem>();
                var roof = entMan.GetComponent<Content.Shared.Light.Components.RoofComponent>(layers[0]);
                var wall = proto.Index<WFPlanetPoiTablePrototype>(tableId).Roads!.Tunnels!.Wall.Id;
                Assert.That(record.Tunnels, Is.Not.Empty, "The roads have no tunnels.");
                foreach (var tunnel in record.Tunnels)
                {
                    Assert.That(tunnel.All(tile => roofs.IsRooved((layers[0], grid, roof), tile)), Is.True, "A tunnel is open to the sky.");
                    Assert.That(tunnel.All(tile => record.RoadTiles.Contains(tile) || record.ShoulderTiles.Contains(tile)), Is.True,
                        "A tunnel strays off the road.");
                    Assert.That(tunnel.Any(tile => maps.GetAnchoredEntities((layers[0], grid), tile)
                        .Any(entity => entMan.GetComponent<MetaDataComponent>(entity).EntityPrototype?.ID == wall)), Is.False,
                        "Rock was left across the road.");
                    var walled = tunnel.Count(tile => new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) }
                        .Any(step => maps.GetAnchoredEntities((layers[0], grid), tile + step)
                            .Any(entity => entMan.GetComponent<MetaDataComponent>(entity).EntityPrototype?.ID == wall)));
                    Assert.That(walled, Is.GreaterThan(tunnel.Count / 8), "A tunnel has no rock either side.");
                }
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The dressings the worlds ship with only turn pieces that can turn, and name tiles for every kerb.</summary>
    [Test]
    public async Task ShippedDressingsAreSound()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var tables = proto.EnumeratePrototypes<WFPlanetPoiTablePrototype>().Where(table => table.Roads?.Dressing != null).ToList();
            Assert.That(tables, Has.Count.GreaterThanOrEqualTo(2), "Asclepiu and Merak should both dress their roads.");

            Assert.Multiple(() =>
            {
                foreach (var table in tables)
                {
                    var dressing = proto.Index(table.Roads!.Dressing!.Value);
                    Assert.That(dressing.Kerbs.Keys, Is.EquivalentTo(System.Enum.GetValues<WFRoadKerb>()), $"{dressing.ID} leaves some kerbs bare.");
                    Assert.That(dressing.Light, Is.Not.Null, $"{dressing.ID} has no street light.");

                    foreach (var rule in dressing.Scatter.Where(rule => rule.Align))
                    foreach (var id in rule.Prototypes)
                    {
                        var turns = !proto.Index(id).TryGetComponent<TransformComponent>(out var xform, factory) || !xform.NoLocalRotation;
                        Assert.That(turns, Is.True, $"{dressing.ID} aligns {id}, which cannot turn.");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Writes the furnished roads to a file beside the path WF_ROAD_DUMP names, for drawing a picture of them outside the game.</summary>
    private static void Dump(WFPlanetPoiComponent record, IEntityManager entMan, ITileDefinitionManager tileDefs, SharedMapSystem maps,
        DecalSystem decals, EntityUid ground, Box2 world, string name)
    {
        if (System.Environment.GetEnvironmentVariable("WF_ROAD_DUMP") is not { Length: > 0 } path)
            return;

        var grid = entMan.GetComponent<MapGridComponent>(ground);
        var tiles = record.RoadTiles.Concat(record.ShoulderTiles)
            .Select(tile => new object[] { tile.X, tile.Y, tileDefs[maps.GetTileRef(ground, grid, tile).Tile.TypeId].ID }).ToList();
        var furniture = record.Furniture.Where(entMan.EntityExists).Select(uid =>
        {
            var xform = entMan.GetComponent<TransformComponent>(uid);
            return new object[] { entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID, xform.LocalPosition.X, xform.LocalPosition.Y, xform.LocalRotation.Degrees };
        }).ToList();
        var marks = decals.GetDecalsIntersecting(ground, world)
            .Select(decal => new object[] { decal.Decal.Id, decal.Decal.Coordinates.X, decal.Decal.Coordinates.Y }).ToList();
        var sites = record.Sites.Select(site => new[] { site.Bounds.Left, site.Bounds.Bottom, site.Bounds.Right, site.Bounds.Top }).ToList();

        File.WriteAllText($"{path}.{name}.json", JsonSerializer.Serialize(new { tiles, furniture, decals = marks, sites }));
    }
}
