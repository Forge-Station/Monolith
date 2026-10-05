using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared._WF.Planets;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using Content.Shared.Maps;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class PlanetPopulationTest
{
    [Test]
    public async Task IdleWorldsStayUnloadedAndAmbientPopulationIsBounded()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var em = server.EntMan;
        var allLayers = new List<EntityUid>();
        var grounds = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var systemId = "SystemKyphrus";
            var system = proto.Index<StarSystemPrototype>(systemId);
            Assert.That(system.Planets.Exists(p => p.Planet.Id == "WFPlanetCarcinoma"), Is.True);
            var stopwatch = Stopwatch.StartNew();
            foreach (var name in new[] { "Asclepiu", "Fervidus", "Merak", "Aerumna", "Thrascias", "Carcinoma" })
            {
                var surface = proto.Index<WFPlanetSurfacePrototype>("WFSurface" + name);
                Assert.That(surface.Sanctioned, Is.EqualTo(name != "Carcinoma"));
                var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, name, null);
                Assert.That(network, Is.Not.Null);
                var layers = em.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers;
                allLayers.AddRange(layers);
                grounds.Add(layers[0]);
                var biome = em.GetComponent<BiomeComponent>(layers[0]);
                Assert.That(biome.LoadedChunks, Is.Empty, "An unvisited planet generated terrain eagerly.");
                Assert.That(biome.LoadedEntities, Is.Empty, "An unvisited planet spawned scenery or wildlife eagerly.");
            }
            TestContext.Out.WriteLine($"Six empty planet networks: {allLayers.Count} maps, {stopwatch.ElapsedMilliseconds} ms construction, zero loaded terrain chunks.");
            Assert.That(allLayers, Has.Count.EqualTo(30));

            List<EntityUid> Animals(EntityUid? ground = null)
            {
                var result = new List<EntityUid>();
                var query = em.EntityQueryEnumerator<MobStateComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var xform))
                {
                    if (xform.MapUid is { } map && grounds.Contains(map) && (ground == null || map == ground))
                        result.Add(uid);
                }
                return result;
            }

            var visitors = new List<EntityCoordinates>();
            var maps = server.System<SharedMapSystem>();
            var tileId = server.ResolveDependency<ITileDefinitionManager>()["FloorFlesh"].TileId;
            var ecology = server.System<WFPlanetFaunaSystem>();
            // Terrain discovery registers sites without filling the entire cap at the arrival location.
            foreach (var ground in grounds)
            {
                var grid = em.GetComponent<MapGridComponent>(ground);
                for (var i = 0; i < WFPlanetFaunaSystem.MaxPerPlanet + 5; i++)
                {
                    var pos = new Vector2(i * 128, 0);
                    maps.SetTile(ground, grid, new Vector2i(i * 128, 0), new Tile(tileId));
                    em.SpawnEntity("WFFaunaAsclepiu", new EntityCoordinates(ground, pos));
                    visitors.Add(new EntityCoordinates(ground, pos + new Vector2(30, 0)));
                }
            }
            Assert.That(Animals(), Is.Empty, "Discovery alone (including orbital views) must not spawn fauna.");
            for (var i = 0; i < 100; i++)
                ecology.RefreshPopulation(visitors);
            var animals = Animals();
            Assert.That(animals, Has.Count.EqualTo(WFPlanetFaunaSystem.MaxTotal));
            foreach (var ground in grounds)
                Assert.That(Animals(ground).Count, Is.LessThanOrEqualTo(WFPlanetFaunaSystem.MaxPerPlanet));

            // Capturing an animal must not free a slot. Parent changes also permanently protect it from retirement.
            var origin = em.GetComponent<WFPlanetWildlifeComponent>(animals[0]).Ground;
            var destination = origin == grounds[0] ? grounds[1] : grounds[0];
            server.System<SharedTransformSystem>().SetCoordinates(animals[0],
                new EntityCoordinates(destination, new Vector2(200, 0)));
            ecology.RefreshPopulation(visitors);
            Assert.That(Animals(), Has.Count.EqualTo(WFPlanetFaunaSystem.MaxTotal));
            Assert.That(em.GetComponent<WFPlanetWildlifeComponent>(animals[0]).Protected, Is.True);

            // Deleted animals release budget; blocked attempts do not leak slots.
            em.DeleteEntity(animals[0]);
            for (var i = 0; i < 10; i++)
                ecology.RefreshPopulation(visitors);
            Assert.That(Animals(), Has.Count.EqualTo(WFPlanetFaunaSystem.MaxTotal));

            var body = em.SpawnEntity(null, new EntityCoordinates(grounds[^1], Vector2.Zero));
            var carcinomaId = "WFSurfaceCarcinoma";
            var registered = server.System<WFPlanetRegistrySystem>().ApplySurface(body,
                proto.Index<WFPlanetSurfacePrototype>(carcinomaId));
            Assert.That(registered.Comp.Sanctioned, Is.False, "The unsanctioned-orbit confirm reads this flag.");
        });
        await Teardown(pair, allLayers);
        await pair.CleanReturnAsync();
    }
}