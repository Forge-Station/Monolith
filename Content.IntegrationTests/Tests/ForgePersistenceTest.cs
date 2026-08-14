using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Content.Server._Forge.Persistence;
using Content.Server.StationRecords;
using Content.Shared._Forge.Persistence;
using Content.Shared.Station.Components;
using Content.Shared.VendingMachines;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

// Forge-Change-full: weekly world persistence
namespace Content.IntegrationTests.Tests;

[TestFixture]
[NonParallelizable]
public sealed class ForgePersistenceTest
{
    [Test]
    public async Task HangarSaveDoesNotIncludeExternalStation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var resources = server.ResolveDependency<IResourceManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entities.System<SharedMapSystem>();
        var persistence = entities.System<GridPersistenceService>();
        var path = persistence.GetHangarSavePath(Guid.NewGuid());

        await server.WaitAssertion(() =>
        {
            mapSystem.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);
            var station = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            entities.AddComponent<StationRecordsComponent>(station);
            entities.AddComponent(grid, new StationMemberComponent { Station = station });

            Assert.That(persistence.SaveGrid(grid, path, coldStartAtmosphere: true), Is.True);

            using var stream = resources.UserData.Open(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var reader = new StreamReader(stream);
            Assert.That(reader.ReadToEnd(), Does.Not.Contain(nameof(StationRecordsComponent)));

            entities.DeleteEntity(station);
            mapSystem.DeleteMap(mapId);
            resources.UserData.Delete(path);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SavesAndLoadsThreePersistentMaps()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var resources = server.ResolveDependency<IResourceManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entities.System<SharedMapSystem>();
        var persistence = entities.System<PersistentWorldSystem>();
        var files = entities.System<GridPersistenceService>();

        var createdMaps = new List<MapId>();
        await server.WaitAssertion(() =>
        {
            for (var i = 0; i < 3; i++)
            {
                var mapUid = mapSystem.CreateMap(out var mapId);
                createdMaps.Add(mapId);
                entities.AddComponent(mapUid, new PersistentMapComponent { Id = $"integration-{i}" });
                var grid = mapManager.CreateGridEntity(mapId);
                entities.AddComponent(grid, new WorldPersistableComponent());
                if (i == 0)
                {
                    entities.AddComponent(grid, new VendingMachineComponent
                    {
                        Inventory =
                        {
                            ["CigPackRed"] = new VendingMachineInventoryEntry(
                                InventoryType.Regular,
                                "CigPackRed",
                                3),
                        },
                    });
                }
            }

            Assert.That(persistence.SaveWorld(), Is.True);
            Assert.That(persistence.GetManifestEntries(), Has.Count.EqualTo(3));

            foreach (var mapId in createdMaps)
                mapSystem.DeleteMap(mapId);

            // Startup RestartRound used to persist an empty world and wipe the
            // manifest, leaving YAML files unused on the next launch.
            Assert.That(persistence.SaveWorld(), Is.True);
            Assert.That(persistence.GetManifestEntries(), Has.Count.EqualTo(3));
            Assert.That(persistence.GetCycleInfo(), Is.Not.Null);
        });

        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            for (var i = 0; i < 3; i++)
            {
                var path = files.GetMapSavePath($"integration-{i}");
                Assert.That(files.LoadMap(path, out var map, out _), Is.True);
                Assert.That(map, Is.Not.Null);
                Assert.That(entities.HasComponent<PersistentMapComponent>(map!.Value.Owner), Is.True);
            }

            resources.UserData.Delete(files.MapsPath);
            resources.UserData.CreateDir(files.MapsPath);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MapLoaderReadsGzipWorldSave()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var resources = server.ResolveDependency<IResourceManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entities.System<SharedMapSystem>();
        var files = entities.System<GridPersistenceService>();
        var loader = entities.System<MapLoaderSystem>();

        await server.WaitAssertion(() =>
        {
            var mapUid = mapSystem.CreateMap(out var mapId);
            entities.AddComponent(mapUid, new PersistentMapComponent { Id = "gzip-loader" });
            var grid = mapManager.CreateGridEntity(mapId);
            entities.AddComponent(grid, new WorldPersistableComponent());

            var path = files.GetMapSavePath("gzip-loader");
            Assert.That(files.SaveMap(mapUid, path), Is.True);
            mapSystem.DeleteMap(mapId);

            // GameTicker loads the primary map via MapLoader.TryLoadMap(path),
            // not GridPersistenceService.LoadMap, so gzip must work at this layer.
            Assert.That(loader.TryLoadMap(path, out var loaded, out _), Is.True);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(entities.HasComponent<PersistentMapComponent>(loaded!.Value.Owner), Is.True);

            mapSystem.DeleteMap(entities.GetComponent<TransformComponent>(loaded.Value.Owner).MapID);
            resources.UserData.Delete(files.MapsPath);
            resources.UserData.CreateDir(files.MapsPath);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MapSaveOmitsUnmarkedGrids()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var resources = server.ResolveDependency<IResourceManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entities.System<SharedMapSystem>();
        var persistence = entities.System<PersistentWorldSystem>();
        var files = entities.System<GridPersistenceService>();
        var meta = entities.System<MetaDataSystem>();

        await server.WaitAssertion(() =>
        {
            var mapUid = mapSystem.CreateMap(out var mapId);
            entities.AddComponent(mapUid, new PersistentMapComponent { Id = "filter-map" });

            var keep = mapManager.CreateGridEntity(mapId);
            entities.AddComponent(keep, new WorldPersistableComponent());
            entities.AddComponent(keep, new VendingMachineComponent
            {
                Inventory =
                {
                    ["CigPackRed"] = new VendingMachineInventoryEntry(
                        InventoryType.Regular,
                        "CigPackRed",
                        3),
                },
            });

            var junk = mapManager.CreateGridEntity(mapId);
            meta.SetEntityName(junk, "AbandonedJunkShuttle");

            Assert.That(persistence.SaveWorld(), Is.True);

            var path = files.GetMapSavePath("filter-map");
            using var stream = resources.UserData.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            var yaml = reader.ReadToEnd();

            Assert.That(yaml, Does.Contain("VendingMachine"));
            Assert.That(yaml, Does.Not.Contain("AbandonedJunkShuttle"));

            mapSystem.DeleteMap(mapId);
            resources.UserData.Delete(files.MapsPath);
            resources.UserData.CreateDir(files.MapsPath);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BssNetworkMatchesBranchingDiagram()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.That(prototypes.TryIndex<Content.Shared._Forge.Bss.BssNetworkPrototype>("ForgeBssNetwork", out var network), Is.True);
            Assert.That(network!.Sectors, Has.Count.EqualTo(14));
            Assert.That(network.Sectors.Count(sector => sector.Primary), Is.EqualTo(1));
            Assert.That(network.Links, Has.Count.EqualTo(19));
            Assert.That(network.RegionName, Is.EqualTo("Фронтир"));
            Assert.That(network.Groups.Select(group => group.Id), Is.EquivalentTo(new[] { "green", "yellow", "red", "black" }));
            Assert.That(network.Sectors.Count(system => system.Group == "green"), Is.EqualTo(3));
            Assert.That(network.Sectors.Count(system => system.Group == "yellow"), Is.EqualTo(4));
            Assert.That(network.Sectors.Count(system => system.Group == "red"), Is.EqualTo(4));
            Assert.That(network.Sectors.Count(system => system.Group == "black" && system.Access == Content.Shared._Forge.Bss.BssSystemAccess.Hidden), Is.EqualTo(3));

            var groups = network.Sectors.ToDictionary(sector => sector.Id, sector => sector.Group);
            var adjacency = new Dictionary<string, HashSet<string>>();
            foreach (var sector in network.Sectors)
                adjacency[sector.Id] = [];

            foreach (var link in network.Links)
            {
                Assert.That(link.Bidirectional, Is.True);
                adjacency[link.From.Sector].Add(link.To.Sector);
                adjacency[link.To.Sector].Add(link.From.Sector);
            }

            Assert.That(adjacency["central"], Is.EquivalentTo(new[] { "poiWest", "vkr" }));
            Assert.That(adjacency["siv"], Is.EquivalentTo(new[] { "poiNorth", "nexus" }));
            Assert.That(adjacency["tsf"], Is.EquivalentTo(new[] { "poiEast", "poiSouth", "wildEast" }));
            Assert.That(adjacency["vkr"], Is.EquivalentTo(new[] { "central", "poiWest", "nexus" }));
            Assert.That(adjacency["poiWest"], Is.EquivalentTo(new[] { "central", "vkr", "nexus", "wildWest" }));
            Assert.That(adjacency["poiNorth"], Is.EquivalentTo(new[] { "siv", "nexus" }));
            Assert.That(adjacency["poiEast"], Is.EquivalentTo(new[] { "tsf", "nexus", "wildEast" }));
            Assert.That(adjacency["poiSouth"], Is.EquivalentTo(new[] { "tsf", "nexus", "wildWest", "wildEast" }));
            Assert.That(adjacency["nexus"], Is.EquivalentTo(new[] { "siv", "poiWest", "poiNorth", "poiEast", "poiSouth", "vkr", "wildWest", "wildEast" }));
            Assert.That(adjacency["wildWest"], Is.EquivalentTo(new[] { "poiWest", "poiSouth", "nexus" }));
            Assert.That(adjacency["wildEast"], Is.EquivalentTo(new[] { "tsf", "poiEast", "poiSouth", "nexus" }));
            Assert.That(adjacency["voidWest"], Is.Empty);
            Assert.That(adjacency["voidNorth"], Is.Empty);
            Assert.That(adjacency["voidSouth"], Is.Empty);

            var greens = network.Sectors.Where(sector => sector.Group == "green").Select(sector => sector.Id).ToList();
            foreach (var green in greens)
            {
                Assert.That(adjacency[green].All(neighbor => groups[neighbor] != "green"), Is.True,
                    $"Green system {green} must not connect directly to another green system.");
            }

            for (var i = 0; i < greens.Count; i++)
            for (var j = i + 1; j < greens.Count; j++)
            {
                Assert.That(PathGoesThroughRed(adjacency, groups, greens[i], greens[j]), Is.True,
                    $"Path from {greens[i]} to {greens[j]} must pass through a red system.");
            }
        });

        await pair.CleanReturnAsync();
    }

    private static bool PathGoesThroughRed(
        Dictionary<string, HashSet<string>> adjacency,
        Dictionary<string, string> groups,
        string from,
        string to)
    {
        var queue = new Queue<(string Id, bool SeenRed)>();
        var visited = new HashSet<(string Id, bool SeenRed)>();
        queue.Enqueue((from, false));
        visited.Add((from, false));

        while (queue.Count > 0)
        {
            var (id, seenRed) = queue.Dequeue();
            if (id == to && seenRed)
                return true;

            foreach (var next in adjacency[id])
            {
                var nextSeenRed = seenRed || groups[next] == "red";
                if (!visited.Add((next, nextSeenRed)))
                    continue;

                queue.Enqueue((next, nextSeenRed));
            }
        }

        return false;
    }
}
