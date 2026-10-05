using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Server.Parallax;
using Content.Server.Body.Components;
using Content.Server.Temperature.Components;
using Content.Shared._WF.Planets;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class PlanetEcologyTest
{
    [TestCase("Asclepiu")]
    [TestCase("Fervidus")]
    [TestCase("Merak")]
    [TestCase("Aerumna")]
    [TestCase("Thrascias")]
    [TestCase("Carcinoma")]
    public async Task OpenTerrainHasSparseMobileWildlife(string name)
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var em = server.EntMan;
        var layers = new List<EntityUid>();
        var wildlifeTile = (Vector2i?) null;
        var wildlifeChunk = Vector2i.Zero;
        var mobs = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var surface = server.ResolveDependency<IPrototypeManager>()
                .Index<WFPlanetSurfacePrototype>("WFSurface" + name);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, name, null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(em.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var biome = em.GetComponent<BiomeComponent>(layers[0]);
            var generator = server.System<SharedBiomeSystem>();
            var count = 0;
            var open = 0;
            var rocks = 0;
            var wildlife = 0;
            var rivers = 0;
            var fleshFlora = 0;
            var pustuleTrees = 0;
            var ordinaryTrees = 0;
            var tendons = 0;
            var sacks = 0;
            var bloodOcean = 0;
            // Widely separated regions, not just the convenient landing location.
            foreach (var offset in new[] { Vector2i.Zero, new Vector2i(4096, -4096), new Vector2i(-4096, 4096) })
            for (var x = -128; x < 128; x += 4)
            for (var y = -128; y < 128; y += 4)
            {
                var pos = offset + new Vector2i(x, y);
                generator.TryGetBiomeTile(pos, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var tile);
                Assert.That(tile.HasValue && !tile.Value.IsEmpty, Is.True, "Natural terrain has a hole.");
                generator.TryGetEntity(pos, biome.Layers, tile!.Value, biome.Seed,
                    (Entity<MapGridComponent>?) null, out var feature);
                if (feature is "FloorLavaEntity" or "FloorLiquidPlasmaEntity" or "MonoFloorWaterEntity" or "WFBloodRiver")
                    rivers++;
                if (feature is "WFFleshTree" or "WFFleshPustuleTree" or "WFFleshPolyp")
                    fleshFlora++;
                if (feature == "WFFleshPustuleTree") pustuleTrees++;
                if (feature == "WFFleshTree") ordinaryTrees++;
                if (feature == "WFCarcinomaTendons") tendons++;
                if (feature == "WFCarcinomaAssimilationSack") sacks++;
                if (feature == "WFBloodOcean") bloodOcean++;
                count++;
                if (feature == null)
                    open++;
                else if (feature.Contains("PlanetmapOre") || feature == "WallMeat")
                    rocks++;
                else if (feature.Contains("Fauna"))
                {
                    wildlife++;
                    wildlifeTile ??= pos;
                }
            }
            TestContext.Out.WriteLine($"{name}: open {open}/{count}, ore walls {rocks}/{count}, fauna {wildlife}/{count}");
            Assert.That((double) open / count, Is.GreaterThan(0.60), "Most of the surface should be open.");
            Assert.That((double) rocks / count, Is.LessThan(0.15), "Ore walls should be isolated outcrops.");
            if (name == "Carcinoma")
                Assert.That(rocks, Is.GreaterThan(0), "Biothreat terrain must contain flesh walls.");
            TestContext.Out.WriteLine($"River samples: {rivers}, flesh flora: {fleshFlora}");
            Assert.That(rivers, Is.GreaterThan(0), "Themed river channels vanished from this planet's generated terrain.");
            if (name == "Carcinoma")
                Assert.That(fleshFlora, Is.GreaterThan(0), "Carcinoma must have static flesh groves.");
            if (name == "Carcinoma")
            {
                Assert.That(pustuleTrees, Is.GreaterThan(0), "Harvestable trees must be reachable through the terrain layers.");
                TestContext.Out.WriteLine($"Ordinary trees: {ordinaryTrees}, pustule trees: {pustuleTrees}");
                Assert.That((double) pustuleTrees / (ordinaryTrees + pustuleTrees), Is.LessThan(0.15),
                    "Harvestable trees should be a rare minority of the grove.");
                Assert.That(tendons, Is.GreaterThan(0), "Missing tendon forest regions.");
                Assert.That(sacks, Is.GreaterThan(0), "Missing assimilation sacks.");
                Assert.That(bloodOcean, Is.GreaterThan(0), "Missing blood ocean regions.");
                TestContext.Out.WriteLine($"Tendons {tendons}, sacks {sacks}, blood ocean {bloodOcean}");
            }
            Assert.That(wildlife, Is.GreaterThan(0), "No ambient wildlife was reachable through the terrain layers.");
            Assert.That((double) wildlife / count, Is.LessThan(0.02), "Wildlife must not flood loaded regions.");

            wildlifeChunk = SharedMapSystem.GetChunkIndices(wildlifeTile!.Value, 8) * 8;
            server.System<BiomeSystem>().WfLoadChunk(
                (layers[0], em.GetComponent<BiomeComponent>(layers[0]), em.GetComponent<MapGridComponent>(layers[0])),
                wildlifeChunk);
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            server.System<WFPlanetFaunaSystem>().RefreshPopulation(new[] {
                new EntityCoordinates(layers[0], (Vector2)wildlifeTile!.Value + new Vector2(30, 0)) });
            var query = em.EntityQueryEnumerator<MobStateComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var mob, out var xform))
            {
                if (xform.MapUid != layers[0])
                    continue;
                mobs.Add(uid);
                Assert.That(xform.Anchored, Is.False, "Biome wildlife must be able to roam.");
                Assert.That(mob.CurrentState, Is.EqualTo(MobState.Alive));
                if (name is "Fervidus" or "Aerumna" or "Thrascias")
                    Assert.That(em.HasComponent<RespiratorComponent>(uid), Is.False,
                        "Low-oxygen planets need wildlife that does not suffocate in its native atmosphere.");
                if (em.TryGetComponent<TemperatureComponent>(uid, out var temperature))
                {
                    var ambient = name switch
                    {
                        "Fervidus" => 373.15f,
                        "Merak" => 318.15f,
                        "Aerumna" => 285.15f,
                        "Thrascias" => 180f,
                        "Carcinoma" => 310.15f,
                        _ => 293.15f,
                    };
                    Assert.That(ambient, Is.InRange(temperature.ColdDamageThreshold, temperature.HeatDamageThreshold),
                        "Wildlife cannot tolerate its native temperature.");
                }
            }
            Assert.That(mobs, Is.Not.Empty, "The sampled fauna spawner did not create a mob.");
            var biomes = server.System<BiomeSystem>();
            Entity<BiomeComponent, MapGridComponent> ground =
                (layers[0], em.GetComponent<BiomeComponent>(layers[0]), em.GetComponent<MapGridComponent>(layers[0]));
            biomes.WfUnloadChunk(ground, wildlifeChunk);
            biomes.WfLoadChunk(ground, wildlifeChunk);
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var count = 0;
            var query = em.EntityQueryEnumerator<MobStateComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapUid != layers[0])
                    continue;
                count++;
                Assert.That(mobs, Does.Contain(uid), "Reloading terrain duplicated wildlife.");
            }
            Assert.That(count, Is.EqualTo(mobs.Count));
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
