using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Shared._WF.Planets;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Server.GameStates;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class PlanetRadarTest
{
    [Test]
    public async Task ClientReceivesRecipeAndCanSampleWithoutGroundTiles()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var netOrbit = default(NetEntity);
        Tile? expected = null;
        var point = new Vector2(40000, -40000);
        await pair.Server.WaitPost(() =>
        {
            netOrbit = pair.Server.EntMan.GetNetEntity(layers[^1]);
            pair.Server.System<PvsOverrideSystem>().AddGlobalOverride(layers[^1]);
            expected = pair.Server.System<WFPlanetRadarSystem>().Sample(
                pair.Server.EntMan.GetComponent<WFOrbitLayerComponent>(layers[^1]), point);
        });
        await pair.RunSeconds(2);
        await pair.Client.WaitAssertion(() =>
        {
            var uid = pair.Client.EntMan.GetEntity(netOrbit);
            var orbit = pair.Client.EntMan.GetComponent<WFOrbitLayerComponent>(uid);
            var recipe = new WFOrbitLayerComponent { RadarLayers = orbit.RadarLayers, RadarSeed = orbit.RadarSeed };
            Assert.That(recipe.RadarLayers, Is.Not.Empty);
            Assert.That(pair.Client.System<WFPlanetRadarSystem>().Sample(recipe, point), Is.EqualTo(expected));
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnloadedTerrainMatchesRecipeWithoutLoadingChunks()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var orbit = em.GetComponent<WFOrbitLayerComponent>(layers[^1]);
            var biome = em.GetComponent<BiomeComponent>(layers[0]);
            var generator = pair.Server.System<SharedBiomeSystem>();
            var radar = pair.Server.System<WFPlanetRadarSystem>();
            Assert.That(orbit.RadarSeed, Is.EqualTo(biome.Seed));
            Assert.That(orbit.RadarLayers, Is.Not.Empty);
            var loaded = biome.LoadedChunks.Count;
            var entities = biome.LoadedEntities.Count;
            var decals = biome.LoadedDecals.Count;
            // Exercise exactly the recipe-only case: ground is outside the client's PVS.
            var recipe = new WFOrbitLayerComponent { RadarSeed = orbit.RadarSeed, RadarLayers = orbit.RadarLayers };
            for (var x = 40000; x < 40100; x += 10)
            for (var y = -40000; y < -39900; y += 10)
            {
                Assert.That(generator.TryGetBiomeTile(new Vector2i(x, y), biome.Layers, biome.Seed,
                    (Entity<MapGridComponent>?) null, out var expected), Is.True);
                Assert.That(radar.Sample(recipe, new Vector2(x, y)), Is.EqualTo(expected));
                radar.SampleFeature(recipe, new Vector2(x, y)); // Scenery sampling must not generate entities either.
            }
            Assert.That(biome.LoadedChunks.Count, Is.EqualTo(loaded));
            Assert.That(biome.LoadedEntities.Count, Is.EqualTo(entities));
            Assert.That(biome.LoadedDecals.Count, Is.EqualTo(decals));
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LoadedTilesAndRadarScarsOverrideRecipe()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        await LayTiles(pair, layers[0], new Vector2i(-2, -2), new Vector2i(2, 2));
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var orbit = em.GetComponent<WFOrbitLayerComponent>(layers[^1]);
            var radar = pair.Server.System<WFPlanetRadarSystem>();
            var grid = em.GetComponent<MapGridComponent>(layers[0]);
            var tile = pair.Server.System<SharedMapSystem>().GetTileRef(layers[0], grid, Vector2i.Zero).Tile;
            Assert.That(radar.Sample(orbit, Vector2.Zero), Is.EqualTo(tile));
            orbit.RadarScars.Add(new Vector3(0, 0, 3));
            em.Dirty(layers[^1], orbit);
            Assert.That(orbit.RadarScars, Has.Count.EqualTo(1));
            Assert.That(radar.Sample(orbit, Vector2.Zero)!.Value.IsEmpty, Is.True);
            Assert.That(radar.Sample(orbit, new Vector2(20, 20))!.Value.IsEmpty, Is.False);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
