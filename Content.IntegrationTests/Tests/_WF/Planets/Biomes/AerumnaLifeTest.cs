#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.LivingWorld;
using Robust.Server.GameObjects;
using Content.Server._WF.Planets;
using Content.Server.Body.Components;
using Content.Shared._WF.LivingWorld;
using Content.Shared._WF.Planets;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.EntityTable;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets.Biomes;

/// <summary>Aerumna's animals and plants: each lies on its own region's ground at a sane density, and does its job.</summary>
[TestFixture]
public sealed class AerumnaLifeTest
{
    private const string Surface = "WFSurfaceAerumna";
    private const string Forest = "WFFloorAerumnaShroud";
    private const string Spires = "WFFloorAerumnaCrystalBed";

    private static readonly HashSet<string> Flats = new() { "FloorChromite", "WFFloorAerumnaClay", "WFFloorAerumnaClayRock", "WFFloorAerumnaDusk" };

    private static readonly HashSet<string> ForestPlants = new()
    {
        "WFAerumnaGlowshroom", "WFAerumnaSapPlant", "WFAerumnaSporePod", "WFAerumnaMindFogMushroom",
    };

    private static readonly HashSet<string> Boulders = new()
    {
        "CoyoteFloraChompBoulder", "CoyoteFloraChompLargeBoulder", "CoyoteFloraChompTallBoulder",
        "DarkPackFloraRockDarkpack", "DarkPackFloraRockDarkpackBig",
        "GoonStone2", "GoonStone3", "GoonStone4", "GoonStone5", "GoonStone6", "GoonStone7", "GoonStone8",
    };

    /// <summary>Samples the surface: forest plants and spawners only where they belong, at a density a person can walk through.</summary>
    [Test]
    public async Task LifeLiesOnItsOwnRegionAtSaneDensity()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        var layers = new List<EntityUid>();
        var report = "";
        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var surface = proto.Index<WFPlanetSurfacePrototype>(Surface);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Aerumna", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var biome = entities.GetComponent<BiomeComponent>(layers[0]);
            var generator = server.System<SharedBiomeSystem>();
            var radius = surface.Radius;
            Entity<MapGridComponent>? noGrid = null;

            var sampled = 0;
            var forestTiles = 0;
            var counts = new Dictionary<string, int>();
            var misplaced = new HashSet<string>();
            var crabNearBoulder = 0;

            string? FeatureAt(Vector2i pos)
            {
                if (!generator.TryGetBiomeTile(pos, biome.Layers, biome.Seed, noGrid, out var tile))
                    return null;
                generator.TryGetEntity(pos, biome.Layers, tile.Value, biome.Seed, noGrid, out var feature);
                return feature;
            }

            var crabSites = new List<Vector2i>();
            for (var x = -radius; x < radius; x++)
            for (var y = -radius; y < radius; y++)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                var pos = new Vector2i(x, y);
                Assert.That(generator.TryGetBiomeTile(pos, biome.Layers, biome.Seed, noGrid, out var tile), Is.True);
                var ground = ((ContentTileDefinition) tiles[tile!.Value.TypeId]).ID;
                sampled++;
                if (ground == Forest)
                    forestTiles++;
                generator.TryGetEntity(pos, biome.Layers, tile.Value, biome.Seed, noGrid, out var feature);
                if (feature == null)
                    continue;
                counts[feature] = counts.GetValueOrDefault(feature) + 1;

                if (ForestPlants.Contains(feature) && ground != Forest)
                    misplaced.Add($"{feature} on {ground}");
                if (feature == "WFFaunaAerumnaForest" && ground != Forest)
                    misplaced.Add($"{feature} on {ground}");
                if (feature == "WFFaunaAerumnaSpires" && ground != Spires)
                    misplaced.Add($"{feature} on {ground}");
                if (feature is "WFFaunaAerumna" or "WFFaunaAerumnaBoulders" && !Flats.Contains(ground))
                    misplaced.Add($"{feature} on {ground}");
                if (feature == "WFAerumnaLuceVine" && ground == "WFFloorAerumnaHive")
                    misplaced.Add($"{feature} on {ground}");
                if (feature == "WFFaunaAerumnaBoulders")
                    crabSites.Add(pos);
            }

            // A gravity crab belongs among the boulders: most of its spawners have one within a few tiles.
            foreach (var site in crabSites)
            {
                var found = false;
                for (var dx = -6; dx <= 6 && !found; dx++)
                for (var dy = -6; dy <= 6 && !found; dy++)
                    found = FeatureAt(site + new Vector2i(dx, dy)) is { } f && Boulders.Contains(f);
                if (found)
                    crabNearBoulder++;
            }

            var stats = new System.Text.StringBuilder();
            int N(string id) => counts.GetValueOrDefault(id);
            string Pct(int n, int of) => of == 0 ? "n/a" : $"{100.0 * n / of:F3}%";
            stats.AppendLine($"Aerumna life: {sampled} tiles, forest {forestTiles}");
            foreach (var id in ForestPlants)
                stats.AppendLine($"  {id}: {N(id)} ({Pct(N(id), forestTiles)} of forest)");
            stats.AppendLine($"  WFAerumnaLuceVine: {N("WFAerumnaLuceVine")} ({Pct(N("WFAerumnaLuceVine"), sampled)} of surface)");
            foreach (var id in new[] { "WFFaunaAerumna", "WFFaunaAerumnaForest", "WFFaunaAerumnaBoulders", "WFFaunaAerumnaSpires", "WFFaunaAerumnaHive" })
                stats.AppendLine($"  {id}: {N(id)} ({Pct(N(id), sampled)} of surface)");
            stats.AppendLine($"  crab sites with a boulder within 6 tiles: {crabNearBoulder} of {crabSites.Count}");

            report = stats.ToString();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(misplaced, Is.Empty, "Something outside its own region.");

                foreach (var id in ForestPlants)
                    Assert.That(N(id), Is.GreaterThan(0), $"{id} never appears.");
                Assert.That(N("WFAerumnaLuceVine"), Is.GreaterThan(0), "No living luce-vine.");
                foreach (var id in new[] { "WFFaunaAerumna", "WFFaunaAerumnaForest", "WFFaunaAerumnaBoulders", "WFFaunaAerumnaSpires" })
                    Assert.That(N(id), Is.GreaterThan(0), $"No {id} spawners.");

                // Harvestables can be common; the ones that react are an event, not a wall.
                Assert.That((double) N("WFAerumnaGlowshroom") / forestTiles, Is.InRange(0.003, 0.03), "Glowshroom density.");
                Assert.That((double) N("WFAerumnaSapPlant") / forestTiles, Is.InRange(0.003, 0.03), "Sap plant density.");
                Assert.That((double) N("WFAerumnaMindFogMushroom") / forestTiles, Is.InRange(0.001, 0.03), "Mind-fog density.");
                Assert.That((double) N("WFAerumnaSporePod") / forestTiles, Is.InRange(0.0005, 0.005), "Spore pods must stay an event.");
                Assert.That((double) N("WFAerumnaLuceVine") / sampled, Is.LessThan(0.002), "Luce-vine must stay rare.");

                // The planet total stays near what it was: no region is thick with animals.
                var animals = N("WFFaunaAerumna") + N("WFFaunaAerumnaForest") + N("WFFaunaAerumnaBoulders") + N("WFFaunaAerumnaSpires");
                Assert.That((double) animals / sampled, Is.LessThan(0.02), "Too many animal spawners.");

                Assert.That(crabSites.Count, Is.GreaterThan(0));
                Assert.That((double) crabNearBoulder / crabSites.Count, Is.GreaterThan(0.8), "Gravity crabs belong in the boulder fields.");
            }
        });
        TestContext.Out.WriteLine(report);
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>No fauna table outside the hive spawns a xeno, and every creature in them spawns as a living mob.</summary>
    [Test]
    public async Task EveryAnimalSpawnsAndXenosStayInTheHive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var proto = server.ProtoMan;
            var tables = server.System<EntityTableSystem>();
            foreach (var id in new[] { "WFFaunaAerumna", "WFFaunaAerumnaForest", "WFFaunaAerumnaBoulders", "WFFaunaAerumnaSpires" })
            {
                var table = proto.Index<EntityTablePrototype>(id);
                var spawnable = new HashSet<string>();
                for (var i = 0; i < 400; i++)
                    foreach (var spawn in tables.GetSpawns(table.Table))
                        spawnable.Add(spawn);
                Assert.That(spawnable, Is.Not.Empty, id);
                foreach (var animal in spawnable)
                {
                    Assert.That(animal, Does.Not.StartWith("MobXeno"), $"{id} spawns a xeno outside the hive.");
                    var uid = entMan.SpawnEntity(animal, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
                    Assert.That(entMan.HasComponent<MobStateComponent>(uid), Is.True, $"{animal} is not a mob.");
                }
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>The gravity crab is slower and hits harder than the iron crab it grew from.</summary>
    [Test]
    public async Task GravityCrabIsSlowAndHeavy()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f);
            var crab = entMan.SpawnEntity("WFMobGravityCrab", coords);
            var iron = entMan.SpawnEntity("MobIronCrabNF", coords);
            var crabSpeed = entMan.GetComponent<MovementSpeedModifierComponent>(crab);
            var ironSpeed = entMan.GetComponent<MovementSpeedModifierComponent>(iron);
            Assert.That(crabSpeed.BaseSprintSpeed, Is.LessThan(ironSpeed.BaseSprintSpeed));
            var blow = entMan.GetComponent<Content.Shared.Weapons.Melee.MeleeWeaponComponent>(crab).Damage.GetTotal();
            var ironBlow = entMan.GetComponent<Content.Shared.Weapons.Melee.MeleeWeaponComponent>(iron).Damage.GetTotal();
            Assert.That(blow, Is.GreaterThan(ironBlow));
            var thresholds = entMan.GetComponent<MobThresholdsComponent>(crab);
            Assert.That(thresholds.Thresholds.Last().Key, Is.GreaterThan(entMan.GetComponent<MobThresholdsComponent>(iron).Thresholds.Last().Key));
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Every picked plant yields its produce; the glowcap and sap pod are lights, the sap is a harmless drink, the mind-fog cap carries space drugs.</summary>
    [Test]
    public async Task PickedPlantsYieldTheirProduce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var harvest = server.System<WFRegrowingHarvestSystem>();
        var solutions = server.System<SharedSolutionContainerSystem>();
        await server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f);
            int Count(string id) => entMan.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == id);

            foreach (var (plant, produce) in new[]
            {
                ("WFAerumnaGlowshroom", "WFAerumnaGlowcap"),
                ("WFAerumnaSapPlant", "WFAerumnaSapPod"),
                ("WFAerumnaMindFogMushroom", "WFAerumnaMindFogCap"),
            })
            {
                var uid = entMan.SpawnEntity(plant, coords);
                var ent = (uid, entMan.GetComponent<WFRegrowingHarvestComponent>(uid));
                Assert.That(Count(produce), Is.EqualTo(0));
                harvest.Harvest(ent, null);
                Assert.That(Count(produce), Is.GreaterThan(0), $"{plant} yields nothing.");
                Assert.That(ent.Item2.Harvested, Is.True);
            }

            foreach (var light in new[] { "WFAerumnaGlowcap", "WFAerumnaSapPod" })
            {
                var item = entMan.SpawnEntity(light, coords);
                Assert.That(entMan.HasComponent<Content.Shared.Item.ItemComponent>(item), Is.True, $"{light} can't be carried.");
                var pointLight = entMan.GetComponent<PointLightComponent>(item);
                Assert.That(pointLight.Enabled, Is.True, $"{light} gives no light.");
            }

            FixedPoint2 Reagent(string produce, string reagent)
            {
                var item = entMan.SpawnEntity(produce, coords);
                Assert.That(solutions.TryGetSolution(item, "food", out _, out var food), Is.True);
                return food!.GetTotalPrototypeQuantity(reagent);
            }
            Assert.That(Reagent("WFAerumnaSapPod", "WFGlowSap"), Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(Reagent("WFAerumnaMindFogCap", "SpaceDrugs"), Is.GreaterThan(FixedPoint2.Zero));
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A plating floor with the plant at the centre and a human at each distance along +x.</summary>
    private static async Task<(EntityUid Plant, EntityUid[] Mobs)> Rig(TestPair pair, string plant, params float[] distances)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid plantUid = default;
        var mobs = new EntityUid[distances.Length];
        await server.WaitPost(() =>
        {
            plantUid = entMan.SpawnEntity(plant, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            for (var i = 0; i < distances.Length; i++)
                mobs[i] = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f + distances[i], 0.5f));
        });
        return (plantUid, mobs);
    }

    private static FixedPoint2 Damage(TestPair pair, EntityUid uid, string type)
    {
        return pair.Server.EntMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.GetValueOrDefault(type);
    }

    /// <summary>The luce-vine lashes a human, spares native wildlife, and tends a xeno.</summary>
    [Test]
    public async Task LuceVineLashesHumansSparesNativesAndTendsXenos()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (_, mobs) = await Rig(pair, "WFAerumnaLuceVine", 0.9f, 1f, 1.1f, 5f);
        var (human, native, xeno, far) = (mobs[0], mobs[1], mobs[2], mobs[3]);
        await server.WaitPost(() =>
        {
            var factions = server.System<NpcFactionSystem>();
            factions.AddFaction(native, "SimpleHostile");
            factions.AddFaction(xeno, "Xeno");
            var slash = server.ProtoMan.Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Slash");
            server.System<DamageableSystem>().TryChangeDamage(xeno, new DamageSpecifier(slash, 20), true);
        });
        await pair.RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Damage(pair, human, "Slash"), Is.GreaterThan(FixedPoint2.Zero), "The vine should lash a human.");
                Assert.That(Damage(pair, native, "Slash"), Is.EqualTo(FixedPoint2.Zero), "Native wildlife should be spared.");
                Assert.That(Damage(pair, xeno, "Slash"), Is.LessThan(FixedPoint2.New(20)), "A xeno should be tended.");
                Assert.That(Damage(pair, far, "Slash"), Is.EqualTo(FixedPoint2.Zero), "Out of range.");
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>The spore pod puffs a blinding cloud for someone beside it.</summary>
    [Test]
    public async Task SporePodBurstsForSomeoneBesideIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        await Rig(pair, "WFAerumnaSporePod", 0.9f);
        await pair.RunSeconds(1.5f);
        await pair.Server.WaitAssertion(() =>
            Assert.That(pair.Server.EntMan.EntityQuery<SmokeComponent>().Any(), Is.True, "The pod should burst."));
        await pair.CleanReturnAsync();
    }

    /// <summary>The spore pod keeps quiet when no one is near.</summary>
    [Test]
    public async Task SporePodStaysShutWithNoOneNear()
    {
        await using var pair = await PoolManager.GetServerClient();
        await Rig(pair, "WFAerumnaSporePod", 6f);
        await pair.RunSeconds(1.5f);
        await pair.Server.WaitAssertion(() =>
            Assert.That(pair.Server.EntMan.EntityQuery<SmokeComponent>().Any(), Is.False, "Nothing in range, no cloud."));
        await pair.CleanReturnAsync();
    }
}
