#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.PlanetPois;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared._WF.Traders;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.Parallax;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Doors.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Physics;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>Checks shipped authored content and complete road networks at production world sizes.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetPoiSystem))]
public sealed class PlanetPoiContentTest
{
    private static readonly string[] Tables =
    {
        "WFPoisAsclepiu", "WFPoisMerak", "WFPoisFervidus", "WFPoisThrascias", "WFPoisAerumna", "WFPoisCarcinoma",
    };

    [Test]
    public async Task ShippedMapsLoadWithWalkablePortsRoofsAndServices()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitPost(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var maps = server.System<SharedMapSystem>();
            var loader = server.System<MapLoaderSystem>();
            var roofs = server.System<SharedRoofSystem>();
            var entries = Tables.SelectMany(id => prototypes.Index<WFPlanetPoiTablePrototype>(id).Entries)
                .Where(entry => entry.MapPath != null).GroupBy(entry => entry.MapPath).Select(group => group.First()).ToList();
            Assert.That(entries, Has.Count.GreaterThanOrEqualTo(6), "Every authored map must be registered in a live table.");
            foreach (var entry in entries)
            {
                var map = maps.CreateMap(out var mapId);
                try
                {
                    Assert.That(loader.TryLoadGrid(mapId, new ResPath(entry.MapPath!), out var loaded), Is.True, entry.MapPath);
                    var grid = loaded!.Value;
                    Assert.That(entry.Footprint, Is.Not.Null, entry.MapPath);
                    if (entry.Below is { } below)
                    {
                        // The level below shares the site's frame and footprint.
                        Assert.That(loader.TryLoadGrid(mapId, new ResPath(below), out var lower), Is.True, below);
                        var lowerTiles = maps.GetAllTilesEnumerator(lower!.Value.Owner, lower.Value.Comp);
                        while (lowerTiles.MoveNext(out var tile))
                            Assert.That(Contains(entry.Footprint!.Value, tile.Value.GridIndices), Is.True, $"{below} has a tile outside its site's footprint.");
                    }
                    var bounds = entry.Footprint!.Value;
                    var tiles = new HashSet<Vector2i>();
                    var enumerator = maps.GetAllTilesEnumerator(grid.Owner, grid.Comp);
                    while (enumerator.MoveNext(out var tile))
                    {
                        Assert.That(Contains(bounds, tile.Value.GridIndices), Is.True, $"{entry.MapPath} has a tile outside its footprint.");
                        tiles.Add(tile.Value.GridIndices);
                    }
                    Assert.That(tiles, Is.Not.Empty, entry.MapPath);
                    Assert.That(entities.TryGetComponent<RoofComponent>(grid.Owner, out var roof), Is.True, entry.MapPath);
                    // A site off the roads, such as a shrine, is open ground with no building to roof or door.
                    var building = entry.Connection != WFPlanetPoiConnection.Isolated;
                    if (building)
                        Assert.That(tiles.Any(tile => roofs.IsRooved((grid.Owner, grid.Comp, roof!), tile)), Is.True, $"{entry.MapPath} has no covered buildings.");
                    var roots = Children(entities, grid.Owner);
                    if (building)
                        Assert.That(roots.Any(entity => entities.HasComponent<DoorComponent>(entity)), Is.True, $"{entry.MapPath} has no authored doors.");
                    foreach (var root in roots)
                    {
                        var metadata = entities.GetComponent<MetaDataComponent>(root);
                        Assert.That(metadata.EntityPrototype, Is.Not.Null, $"{entry.MapPath} contains an unrecognized entity prototype.");
                        Assert.That(metadata.EntityLifeStage, Is.EqualTo(EntityLifeStage.MapInitialized), entry.MapPath);
                    }
                    var path = entry.MapPath!.ToLowerInvariant();
                    var abandoned = path.Contains("derelict") || path.Contains("abandoned")
                        || path.Contains("fervidus") || path.Contains("thrascias") || path.Contains("aerumna") || path.Contains("carcinoma");
                    var traders = roots.Count(entity => entities.HasComponent<TraderComponent>(entity));
                    // A service stop has someone serving; an abandoned site has nobody. Houses and vaults may go either way.
                    if (abandoned)
                        Assert.That(traders, Is.Zero, $"{entry.MapPath} is abandoned but has a trader.");
                    else if (path.Contains("station") || path.Contains("service"))
                        Assert.That(traders, Is.GreaterThanOrEqualTo(1), $"{entry.MapPath} has nobody serving.");
                    var clearance = Tables.Select(id => prototypes.Index<WFPlanetPoiTablePrototype>(id))
                        .Where(table => table.Entries.Any(candidate => candidate.MapPath == entry.MapPath))
                        .Max(table => table.Roads is { } roads ? roads.Width / 2 + roads.ShoulderWidth : 0);
                    foreach (var socket in entry.Sockets)
                    {
                        var direction = WFPlanetRoadPlanner.Direction(socket.Direction);
                        Assert.That(Contains(bounds, socket.Offset) && (entry.Trail || !Contains(bounds, socket.Offset + direction)), Is.True, entry.MapPath);
                        if (entry.Trail)
                        {
                            // A footpath starts on open ground with a door behind it.
                            Assert.That(maps.GetAnchoredEntities(grid, socket.Offset).Any(entity => entities.TryGetComponent<PhysicsComponent>(entity, out var body)
                                && body.CanCollide && body.Hard && (body.CollisionLayer & (int)CollisionGroup.MobMask) != 0), Is.False, $"{entry.MapPath} blocks its footpath.");
                            Assert.That(maps.GetAnchoredEntities(grid, socket.Offset - direction).Any(entities.HasComponent<DoorComponent>), Is.True,
                                $"{entry.MapPath}'s footpath doesn't start at a door.");
                            continue;
                        }
                        for (var side = -clearance; side <= clearance; side++)
                        {
                            var mouth = socket.Offset + new Vector2i(-direction.Y, direction.X) * side;
                            Assert.That(Passable(entities, maps, grid, mouth), Is.True, $"{entry.MapPath} blocks road width at {mouth}.");
                            Assert.That(roofs.IsRooved((grid.Owner, grid.Comp, roof!), mouth), Is.False, $"{entry.MapPath} roofs a road port.");
                        }
                    }
                    if (entry.Connection == WFPlanetPoiConnection.Through)
                    {
                        Assert.That(entry.Sockets.Select(socket => socket.Direction).Distinct().Count(), Is.GreaterThanOrEqualTo(2), entry.MapPath);
                        var accessible = Flood(entry.Sockets[0].Offset, tile => tiles.Contains(tile) && Passable(entities, maps, grid, tile));
                        foreach (var socket in entry.Sockets)
                            Assert.That(accessible, Does.Contain(socket.Offset), $"{entry.MapPath} does not connect its road ports internally.");
                    }
                }
                finally { entities.DeleteEntity(map); }
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("WFSurfaceAsclepiu", "WFPoisAsclepiu", 384, 1977)]
    [TestCase("WFSurfaceAsclepiu", "WFPoisAsclepiu", 384, 8231)]
    [TestCase("WFSurfaceAsclepiu", "WFPoisAsclepiu", 384, 44119)]
    [TestCase("WFSurfaceMerak", "WFPoisMerak", 320, 1977)]
    [TestCase("WFSurfaceMerak", "WFPoisMerak", 320, 8231)]
    [TestCase("WFSurfaceMerak", "WFPoisMerak", 320, 44119)]
    public async Task RoadWorldsConnectAtProductionRadius(string surfaceId, string tableId, int radius, int seed)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, ServerSeed = seed });
        var server = pair.Server;
        var entities = server.EntMan;
        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        var holds = new List<Task>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var table = prototypes.Index<WFPlanetPoiTablePrototype>(tableId);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, radius);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, tableId);
            server.CfgMan.SetCVar(PlanetCVars.FixedCities, true);
            var surface = prototypes.Index<WFPlanetSurfacePrototype>(surfaceId);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, surfaceId, null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var preload = new WFPlanetPreloadStartingEvent(layers[0], Vector2.Zero, radius, holds);
            entities.EventBus.RaiseEvent(EventSource.Local, ref preload);
        });
        try
        {
            for (var attempt = 0; attempt < 900 && holds.Any(hold => !hold.IsCompleted); attempt++)
                await pair.RunTicksSync(10);
            await server.WaitPost(() =>
            {
                Assert.That(holds, Is.Not.Empty);
                var ground = layers[0];
                var record = entities.GetComponent<WFPlanetPoiComponent>(ground);
                Assert.That(record.GenerationError, Is.Null, $"{tableId}, seed {seed}");
                Assert.That(holds.All(hold => hold.IsCompletedSuccessfully), Is.True);
                Assert.That(record.NetworkReady, Is.True);
                // A fixed city comes on top of the table's own sites.
                var tableSites = record.Sites.Count(site => table.Entries.Any(entry => entry.MapPath == site.MapPath && entry.Dungeon == site.Dungeon));
                Assert.That(tableSites, Is.InRange(table.MinCount, table.MaxCount));
                // Asclepiu's city is Crocus, a fixed city placed ahead of the table.
                if (surfaceId == "WFSurfaceAsclepiu")
                    Assert.That(record.Sites.Any(site => site.Bounds.Width >= 280 && site.Bounds.Height >= 140), Is.True, "The city was not guaranteed.");
                foreach (var entry in table.Entries)
                {
                    var count = record.Sites.Count(site => site.MapPath == entry.MapPath && site.Dungeon == entry.Dungeon);
                    Assert.That(count, Is.GreaterThanOrEqualTo(entry.MinCount), entry.MapPath);
                    if (entry.MaxCount is { } maximum) Assert.That(count, Is.LessThanOrEqualTo(maximum), entry.MapPath);
                }
                Assert.That(record.RoadTiles, Is.Not.Empty);
                var reached = Flood(record.RoadTiles.First(), record.RoadTiles.Contains);
                var stranded = record.RoadTiles.Where(tile => !reached.Contains(tile)).OrderBy(t => t.X).ThenBy(t => t.Y).ToList();
                var tunnelTiles = record.Tunnels.SelectMany(tunnel => tunnel).ToHashSet();
                string Describe(Vector2i tile) => $"{tile}" + (record.Centreline.Contains(tile) ? " centreline" : "")
                    + (tunnelTiles.Contains(tile) ? " tunnel" : "")
                    + (record.Sites.FirstOrDefault(site => site.Bounds.Contains(tile)) is { } inSite ? $" in {inSite.Name}" : "");
                Assert.That(reached.Count, Is.EqualTo(record.RoadTiles.Count),
                    $"The exterior road graph is disconnected: {stranded.Count} stranded road tiles: {string.Join(", ", stranded.Take(20).Select(Describe))}");
                foreach (var site in record.Sites.Where(site => site.Connection != WFPlanetPoiConnection.Isolated))
                foreach (var socket in site.Sockets)
                    Assert.That(reached, Does.Contain(socket.Position), site.MapPath);

                // Nothing stands in water: no road or footpath, no camp or wreck in its ring, no house; a bigger site at
                // most on a pond's edge.
                var biome = new Entity<BiomeComponent>(ground, entities.GetComponent<BiomeComponent>(ground));
                var biomes = server.System<BiomeSystem>();
                Assert.That(record.RoadTiles.Concat(record.ShoulderTiles).Concat(record.TrailTiles).Where(tile => biomes.WfIsWater(biome, tile)),
                    Is.Empty, "A road or footpath runs through water.");
                foreach (var site in record.Sites.Where(site => site.Bounds.Width * site.Bounds.Height <= 96 * 96))
                {
                    var middle = new Vector2((site.Bounds.Left + site.Bounds.Right) / 2f, (site.Bounds.Bottom + site.Bounds.Top) / 2f);
                    var round = site.MapPath == null && site.Dungeon == null;
                    var tiles = 0;
                    var wet = 0;
                    for (var x = site.Bounds.Left; x < site.Bounds.Right; x++)
                    for (var y = site.Bounds.Bottom; y < site.Bounds.Top; y++)
                    {
                        if (round && Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), middle) > (site.Bounds.Width - 2) / 2f)
                            continue;
                        tiles++;
                        if (biomes.WfIsWater(biome, new Vector2i(x, y)))
                            wet++;
                    }
                    var small = round || site.Bounds.Width * site.Bounds.Height <= 48 * 48;
                    Assert.That(wet, Is.LessThanOrEqualTo(small ? 0 : tiles * 0.03f), $"{site.Name} at {site.Origin} stands in water.");
                }
            });
        }
        finally { await PlanetFixture.Teardown(pair, layers); }
        await pair.CleanReturnAsync();
    }

    private static bool Contains(Box2i bounds, Vector2i tile) => tile.X >= bounds.Left && tile.X < bounds.Right && tile.Y >= bounds.Bottom && tile.Y < bounds.Top;

    /// <summary>Snapshots grid roots after map initialization.</summary>
    private static List<EntityUid> Children(IEntityManager entities, EntityUid parent)
    {
        var children = entities.GetComponent<TransformComponent>(parent).ChildEnumerator;
        var result = new List<EntityUid>();
        while (children.MoveNext(out var child)) result.Add(child);
        return result;
    }

    /// <summary>Checks floor and anchored obstacles independently of the production resolver.</summary>
    private static bool Passable(IEntityManager entities, SharedMapSystem maps, Entity<MapGridComponent> grid, Vector2i tile)
    {
        if (maps.GetTileRef(grid.Owner, grid.Comp, tile).Tile.IsEmpty) return false;
        foreach (var entity in maps.GetAnchoredEntities(grid, tile))
        {
            if (entities.HasComponent<DoorComponent>(entity))
            {
                if (entities.TryGetComponent<DoorBoltComponent>(entity, out var bolts) && bolts.BoltsDown) return false;
                continue;
            }
            if (entities.TryGetComponent<PhysicsComponent>(entity, out var physics) && physics.CanCollide
                && (physics.CollisionLayer & (int)CollisionGroup.MobMask) != 0) return false;
        }
        return true;
    }

    /// <summary>Finds physically connected cardinal tiles.</summary>
    private static HashSet<Vector2i> Flood(Vector2i start, System.Func<Vector2i, bool> allowed)
    {
        var reached = new HashSet<Vector2i>();
        var frontier = new Queue<Vector2i>();
        frontier.Enqueue(start);
        while (frontier.TryDequeue(out var current))
        {
            if (!allowed(current) || !reached.Add(current)) continue;
            foreach (var direction in new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
                if (!reached.Contains(current + direction)) frontier.Enqueue(current + direction);
        }
        return reached;
    }
}
