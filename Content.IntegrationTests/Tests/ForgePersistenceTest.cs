using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.Server._Forge.Persistence;
using Content.Server.StationRecords;
using Content.Shared._Forge.Persistence;
using Content.Shared.Station.Components;
using Content.Shared.VendingMachines;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

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
    public async Task BssNetworkMatchesBranchingDiagram()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.That(prototypes.TryIndex<Content.Shared._Forge.Bss.BssNetworkPrototype>("ForgeBssNetwork", out var network), Is.True);
            Assert.That(network!.Sectors, Has.Count.EqualTo(9));
            Assert.That(network.Sectors.Count(sector => sector.Primary), Is.EqualTo(1));
            Assert.That(network.Links, Has.Count.EqualTo(16));

            var adjacency = new Dictionary<string, HashSet<string>>();
            foreach (var sector in network.Sectors)
                adjacency[sector.Id] = [];

            foreach (var link in network.Links)
            {
                Assert.That(link.Bidirectional, Is.True);
                adjacency[link.From.Sector].Add(link.To.Sector);
                adjacency[link.To.Sector].Add(link.From.Sector);
            }

            Assert.That(adjacency["central"], Is.EquivalentTo(new[] { "vkr", "poiWest", "poiEast", "siv", "tsf" }));
            Assert.That(adjacency["siv"], Is.EquivalentTo(new[] { "central", "tsf", "poiWest", "poiSouth" }));
            Assert.That(adjacency["tsf"], Is.EquivalentTo(new[] { "central", "siv", "poiEast", "poiSouth" }));
            Assert.That(adjacency["vkr"], Is.EquivalentTo(new[] { "poiWest", "poiEast", "central" }));
            Assert.That(adjacency["poiWest"], Is.EquivalentTo(new[] { "vkr", "central", "siv", "wildWest" }));
            Assert.That(adjacency["poiEast"], Is.EquivalentTo(new[] { "vkr", "central", "tsf", "wildEast" }));
            Assert.That(adjacency["poiSouth"], Is.EquivalentTo(new[] { "siv", "tsf", "wildWest", "wildEast" }));
            Assert.That(adjacency["wildWest"], Is.EquivalentTo(new[] { "poiWest", "poiSouth" }));
            Assert.That(adjacency["wildEast"], Is.EquivalentTo(new[] { "poiEast", "poiSouth" }));
        });

        await pair.CleanReturnAsync();
    }
}
