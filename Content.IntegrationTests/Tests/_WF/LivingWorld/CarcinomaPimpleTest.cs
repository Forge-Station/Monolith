#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Server._WF.Planets;
using Content.Shared._WF.LivingWorld;
using Content.Shared._WF.Planets;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.LivingWorld;

/// <summary>Carcinoma's pimple bursts on outsiders, spares the flesh, and grows sparsely on the surface.</summary>
[TestFixture]
public sealed class CarcinomaPimpleTest
{
    private const string Pimple = "WFCarcinomaPimple";

    [Test]
    public async Task BurstsOnAnOutsiderButNotOnTheFlesh()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        EntityUid outsiderPimple = default, fleshPimple = default, farPimple = default, outsider = default, flesh = default;
        await server.WaitPost(() =>
        {
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -10; x <= 30; x++)
            for (var y = -5; y <= 5; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            var grid = map.Grid.Owner;
            outsiderPimple = entMan.SpawnEntity(Pimple, new EntityCoordinates(grid, 0.5f, 0.5f));
            outsider = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 1.5f, 0.5f));
            fleshPimple = entMan.SpawnEntity(Pimple, new EntityCoordinates(grid, 15.5f, 0.5f));
            flesh = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 16.5f, 0.5f));
            server.System<NpcFactionSystem>().AddFaction(flesh, "Chimera");
            farPimple = entMan.SpawnEntity(Pimple, new EntityCoordinates(grid, -4.5f, 0.5f));
        });
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFReactiveFloraComponent>(outsiderPimple).State, Is.EqualTo(WFReactiveFloraState.Armed),
                "It swells when an outsider comes close.");
        });
        await pair.RunSeconds(2);

        // The bare test map chips a little blunt and heat off everyone, so read the burst's slash.
        FixedPoint2 Slash(EntityUid mob) => entMan.GetComponent<DamageableComponent>(mob).Damage.DamageDict.GetValueOrDefault("Slash");
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.Deleted(outsiderPimple), Is.True, "The pimple is consumed by its burst.");
                Assert.That(Slash(outsider), Is.GreaterThan(FixedPoint2.Zero), "The burst slashes the outsider.");
                Assert.That(entMan.EntityQuery<PuddleComponent>().Any(), Is.True, "It leaves a puddle of blood.");
                Assert.That(entMan.EntityQuery<SmokeComponent>().Any(), Is.True, "It leaves a spore cloud.");
                Assert.That(entMan.Deleted(fleshPimple), Is.False, "Flesh mobs don't set it off.");
                Assert.That(entMan.GetComponent<WFReactiveFloraComponent>(fleshPimple).State, Is.EqualTo(WFReactiveFloraState.Idle));
                Assert.That(Slash(flesh), Is.EqualTo(FixedPoint2.Zero));
                Assert.That(entMan.Deleted(farPimple), Is.False, "Six metres away is out of range.");
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GrowsSparselyOnTheSurface()
    {
        await using var pair = await PoolManager.GetServerClient();
        await PlanetFixture.EnableFeature(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        var layers = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var surface = server.ResolveDependency<IPrototypeManager>().Index<WFPlanetSurfacePrototype>("WFSurfaceCarcinoma");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Carcinoma", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var biome = entities.GetComponent<BiomeComponent>(layers[0]);
            var generator = server.System<SharedBiomeSystem>();

            var radius = surface.Radius;
            var sampled = 0;
            var pimples = new List<Vector2i>();
            for (var x = -radius; x < radius; x++)
            for (var y = -radius; y < radius; y++)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                var pos = new Vector2i(x, y);
                if (!generator.TryGetBiomeTile(pos, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var tile) || tile == null)
                    continue;
                sampled++;
                generator.TryGetEntity(pos, biome.Layers, tile.Value, biome.Seed, (Entity<MapGridComponent>?) null, out var feature);
                if (feature == Pimple)
                    pimples.Add(pos);
            }

            var share = 100.0 * pimples.Count / sampled;
            TestContext.Progress.WriteLine($"Carcinoma pimples: {pimples.Count} of {sampled} tiles ({share:F3}%)");
            Assert.That(pimples.Count, Is.GreaterThan(20), "Pimples should grow on the surface.");
            Assert.That(share, Is.LessThan(0.6), "Pimples should stay sparse.");
        });
        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
