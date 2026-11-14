#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Caverns;
using Content.Server._WF.LivingWorld;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._DV.Planet;
using Content.Shared._WF.Caverns;
using Content.Shared._WF.LivingWorld;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.EntityTable;
using Content.Shared.FixedPoint;
using Content.Shared.Kitchen;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>
/// The cave life of the Underkarst, the Cinder Vaults and the Sandstone Galleries: each zone's plants and wildlife
/// grow on that zone's ground, the plants react and give their produce, and every animal lives in its cavern's air.
/// </summary>
[TestFixture]
public sealed class CavernLifeKarstCinderSandstoneTest
{
    /// <summary>Edge of the square sampled round each gate.</summary>
    private const int SampleSize = 512;

    /// <summary>One world's zone: its floor, the other zones' floors, and the plants and fauna markers that belong on it.</summary>
    private sealed record Zone(string Name, string Floor, string[] Foreign, string[] Harvest, string[] Hazard, string Marker);

    private static readonly Dictionary<string, Zone[]> Zones = new()
    {
        {
            "WFSurfaceAsclepiu", new[]
            {
                new Zone("dripstone halls", "WFCavernFloorFlowstone", new[] { "WFCavernFloorWetRock", "WFCavernFloorCaveSoil" },
                    new[] { "WFCaveLichenPatch" }, new[] { "WFCaveGraspingRoot" }, "WFCavernFaunaAsclepiuHalls"),
                new Zone("flooded grottos", "WFCavernFloorWetRock", new[] { "WFCavernFloorFlowstone", "WFCavernFloorCaveSoil" },
                    new[] { "WFCavePaleReeds" }, new[] { "WFCavePitcherPod" }, "WFCavernFaunaAsclepiuGrottos"),
                new Zone("mushroom hollows", "WFCavernFloorCaveSoil", new[] { "WFCavernFloorFlowstone", "WFCavernFloorWetRock" },
                    new[] { "WFCaveCapPatch" }, new[] { "WFCaveSporePuff" }, "WFCavernFaunaAsclepiuHollows"),
            }
        },
        {
            "WFSurfaceFervidus", new[]
            {
                new Zone("obsidian galleries", "WFCavernFloorObsidian", new[] { "WFCavernFloorSulphur" },
                    new[] { "WFCaveHeatShelf" }, new[] { "WFCaveFirePod" }, "WFCavernFaunaFervidusGalleries"),
                new Zone("sulphur vents", "WFCavernFloorSulphur", new[] { "WFCavernFloorObsidian" },
                    new[] { "WFCaveSulphurBloom" }, new[] { "WFCaveVentPuff" }, "WFCavernFaunaFervidusVents"),
            }
        },
        {
            "WFSurfaceMerak", new[]
            {
                new Zone("pillared halls", "WFCavernFloorSandstone", new[] { "WFCavernFloorFossilBed", "WFCavernFloorRuinStone" },
                    new[] { "WFCaveGalleryAloe" }, new[] { "WFCaveSandSnare" }, "WFCavernFaunaMerakHalls"),
                new Zone("fossil beds", "WFCavernFloorFossilBed", new[] { "WFCavernFloorRuinStone" },
                    new[] { "WFCaveMarrowCap" }, new[] { "WFCaveBrittleGeode" }, "WFCavernFaunaMerakFossils"),
                new Zone("ruin quarter", "WFCavernFloorRuinStone", new[] { "WFCavernFloorFossilBed" },
                    new[] { "WFCaveGravebloom" }, new[] { "WFCaveDustPod" }, "WFCavernFaunaMerakRuins"),
            }
        },
    };

    /// <summary>The cave animals each world's tables roll.</summary>
    private static readonly Dictionary<string, string[]> Animals = new()
    {
        { "WFSurfaceAsclepiu", new[] { "WFMobCavePaleBat", "WFMobCaveFrog", "WFMobCaveCrab", "WFMobCaveSpider" } },
        { "WFSurfaceFervidus", new[] { "WFMobCinderSpider", "WFMobEmberViper", "WFMobSlagTick", "WFMobSulphurCarp" } },
        { "WFSurfaceMerak", new[] { "WFMobPallidSandviper", "WFMobDustBat", "WFMobGalleryCrab", "WFMobCryptTick" } },
    };

    /// <summary>Each zone's floor covers real ground, and its plants and markers grow on its floor or the common rim, never on another zone's.</summary>
    [Test]
    public async Task ZonesGrowTheirOwnLife()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var sampler = server.System<WFCavernSampler>();
        await EnableCaverns(pair);

        foreach (var (surfaceId, zones) in Zones)
        {
            var world = await BuildWorld(pair, surfaceId);

            try
            {
                var spec = CavernOf(pair, surfaceId).Mouths;
                await server.WaitAssertion(() =>
                {
                    var biome = server.EntMan.GetComponent<BiomeComponent>(world.Cavern);
                    var planet = server.EntMan.GetComponent<WFCavernGroundComponent>(world.Ground).Centre;
                    var centre = new Vector2i((int) MathF.Floor(planet.X), (int) MathF.Floor(planet.Y)) + spec.GateOffset;
                    var sample = sampler.Sample(biome, centre, SampleSize);
                    var tileCount = new Dictionary<string, int>();
                    var onTile = new Dictionary<string, Dictionary<string, int>>();

                    for (var i = 0; i < sample.Count; i++)
                    {
                        var tile = sample.Tiles[i];
                        if (tile == null)
                            continue;

                        tileCount[tile] = tileCount.GetValueOrDefault(tile) + 1;
                        if (sample.Entities[i] is not { } entity)
                            continue;

                        if (!onTile.TryGetValue(entity, out var tiles))
                            onTile[entity] = tiles = new Dictionary<string, int>();
                        tiles[tile] = tiles.GetValueOrDefault(tile) + 1;
                    }

                    var open = Enumerable.Range(0, sample.Count).Count(sample.IsOpen);

                    using (Assert.EnterMultipleScope())
                    {
                        foreach (var zone in zones)
                        {
                            var floor = tileCount.GetValueOrDefault(zone.Floor);
                            var share = (double) floor / sample.Count;
                            TestContext.Out.WriteLine($"{surfaceId} {zone.Name}: {zone.Floor} {100 * share:F1}% of the sample.");
                            Assert.That(share, Is.InRange(0.01, 0.35), $"{surfaceId}: {zone.Floor} covers {100 * share:F1}% of the cavern.");

                            var harvestCount = 0;
                            var hazardCount = 0;
                            foreach (var id in zone.Harvest.Concat(zone.Hazard).Append(zone.Marker))
                            {
                                var tiles = onTile.GetValueOrDefault(id) ?? new Dictionary<string, int>();
                                var total = tiles.Values.Sum();
                                var own = tiles.GetValueOrDefault(zone.Floor);
                                TestContext.Out.WriteLine($"  {id}: {total}, {own} on {zone.Floor}, {string.Join(", ", tiles.Select(kv => $"{kv.Key} {kv.Value}"))}");

                                Assert.That(total, Is.GreaterThanOrEqualTo(8), $"{surfaceId}: only {total} {id} in {SampleSize}² round the gate.");
                                foreach (var foreign in zone.Foreign)
                                    Assert.That(tiles.GetValueOrDefault(foreign), Is.Zero, $"{id} stands on {foreign}, another zone's floor.");
                                Assert.That(own, Is.GreaterThan(0), $"{id} never stands on its own zone's floor {zone.Floor}.");

                                if (zone.Harvest.Contains(id))
                                    harvestCount += total;
                                else if (zone.Hazard.Contains(id))
                                    hazardCount += total;
                            }

                            Assert.That(hazardCount, Is.LessThan(harvestCount / 2), $"{surfaceId}: {zone.Name} have {hazardCount} hazards to {harvestCount} harvestables.");
                            Assert.That(hazardCount / (double) open, Is.LessThan(0.002), $"{surfaceId}: hazards cover {100.0 * hazardCount / open:F2}% of the open ground.");
                        }
                    }
                });
            }
            finally
            {
                await Teardown(pair, world);
            }
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Every plant, produce and animal spawns, every table rolls only mobs, and each world's table set covers its animals.</summary>
    [Test]
    public async Task EveryNewPrototypeSpawns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var rolled = new HashSet<string>();
        var others = new List<string>();

        await server.WaitAssertion(() =>
        {
            var protos = server.ProtoMan;
            var tables = server.System<EntityTableSystem>();

            foreach (var zones in Zones.Values)
            foreach (var zone in zones)
            {
                Assert.That(protos.HasIndex<EntityPrototype>(zone.Marker), Is.True, $"{zone.Marker} has no marker.");
                for (var i = 0; i < 300; i++)
                    foreach (var spawn in tables.GetSpawns(protos.Index<EntityTablePrototype>(zone.Marker).Table))
                        rolled.Add(spawn.Id);
                others.AddRange(zone.Harvest);
                others.AddRange(zone.Hazard);
            }

            foreach (var id in protos.EnumeratePrototypes<EntityPrototype>().Select(e => e.ID))
            {
                if (id.StartsWith("WFCave") && !id.StartsWith("WFCavern") && !protos.Index<EntityPrototype>(id).Abstract)
                    others.Add(id);
            }

            foreach (var (surface, animals) in Animals)
            foreach (var animal in animals)
                Assert.That(rolled, Does.Contain(animal), $"{surface}: no zone table rolls a {animal}.");
        });

        await server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f);
            foreach (var id in rolled.Concat(others).Distinct().Where(id => !id.StartsWith("WFCavernFauna")))
            {
                var uid = entMan.SpawnEntity(id, coords);
                if (rolled.Contains(id))
                    Assert.That(entMan.HasComponent<MobStateComponent>(uid), Is.True, $"{id} is rolled as an animal but is not a mob.");
            }
        });

        await pair.RunSeconds(2);
        await pair.CleanReturnAsync();
    }

    /// <summary>A plating floor with the plant at the centre and a human at each distance along +x.</summary>
    private static async Task<(EntityUid Plant, EntityUid[] Mobs)> Rig(TestPair pair, string plant, params float[] distances)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        EntityUid plantUid = default;
        var mobs = new EntityUid[distances.Length];

        await server.WaitPost(() =>
        {
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -8; x <= 8; x++)
            for (var y = -8; y <= 8; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
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

    /// <summary>The roots take hold of whoever is close, and not of someone five tiles off.</summary>
    [TestCase("WFCaveGraspingRoot")]
    [TestCase("WFCaveSandSnare")]
    public async Task RootsGrab(string plant)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (_, mobs) = await Rig(pair, plant, 0.9f, 5f);
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.HasComponent<WFFloraGrabbedComponent>(mobs[0]), Is.True, "The roots should take hold.");
            Assert.That(server.EntMan.HasComponent<WFFloraGrabbedComponent>(mobs[1]), Is.False, "Someone five tiles off is left alone.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>The acid pitcher spits at someone in range.</summary>
    [Test]
    public async Task PitcherSpitsAcid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (_, mobs) = await Rig(pair, "WFCavePitcherPod", 2.5f, 8f);
        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(pair, mobs[0], "Caustic"), Is.GreaterThan(FixedPoint2.Zero), "The spit should hit the mob in range.");
            Assert.That(Damage(pair, mobs[1], "Caustic"), Is.EqualTo(FixedPoint2.Zero), "Someone out of range is left alone.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>The puffs, vent caps and dust pods breathe out a cloud when someone is close, and stay shut when nobody is.</summary>
    [TestCase("WFCaveSporePuff")]
    [TestCase("WFCaveVentPuff")]
    [TestCase("WFCaveDustPod")]
    public async Task CloudsPuffOnlyForSomeoneClose(string plant)
    {
        await using var near = await PoolManager.GetServerClient();
        await Rig(near, plant, 1f);
        await near.RunSeconds(1.5f);
        await near.Server.WaitAssertion(() =>
            Assert.That(near.Server.EntMan.EntityQuery<SmokeComponent>().Any(), Is.True, "The plant should release a cloud."));
        await near.CleanReturnAsync();

        await using var far = await PoolManager.GetServerClient();
        await Rig(far, plant, 6f);
        await far.RunSeconds(1.5f);
        await far.Server.WaitAssertion(() =>
            Assert.That(far.Server.EntMan.EntityQuery<SmokeComponent>().Any(), Is.False, "Nobody near, so no cloud."));
        await far.CleanReturnAsync();
    }

    /// <summary>The fire pod swells and bursts in a flash of heat, then waits spent; the geode shatters and is gone.</summary>
    [TestCase("WFCaveFirePod", "Heat", false)]
    [TestCase("WFCaveBrittleGeode", "Slash", true)]
    public async Task PodsBurst(string plant, string damage, bool consumed)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (pod, mobs) = await Rig(pair, plant, 1f, 6f);
        await pair.RunSeconds(0.8f);

        // The bare test map chips a little heat off everyone, so the burst is read as a rise.
        var before = FixedPoint2.Zero;
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFReactiveFloraComponent>(pod).State, Is.EqualTo(WFReactiveFloraState.Armed), "It should swell first.");
            before = Damage(pair, mobs[0], damage);
            Assert.That(before, Is.LessThan(FixedPoint2.New(2)), "No real damage before the burst.");
        });
        await pair.RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(pair, mobs[0], damage), Is.GreaterThan(before + FixedPoint2.New(5)), "The burst hurts the mob beside it.");
            Assert.That(Damage(pair, mobs[1], damage), Is.LessThan(FixedPoint2.New(2)), "Out of the burst radius.");
            if (consumed)
                Assert.That(entMan.Deleted(pod), Is.True, "The burst consumes the plant.");
            else
                Assert.That(entMan.GetComponent<WFReactiveFloraComponent>(pod).State, Is.EqualTo(WFReactiveFloraState.Spent), "The pod is spent, to regrow.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Each harvestable gives its produce once and then nothing until it regrows.</summary>
    [TestCase("WFCaveLichenPatch", "WFCaveDropletLichen")]
    [TestCase("WFCavePaleReeds", "WFPlantFibre")]
    [TestCase("WFCaveCapPatch", "WFCaveCapMushroom")]
    [TestCase("WFCaveHeatShelf", "WFCinderCapMushroom")]
    [TestCase("WFCaveSulphurBloom", "WFCaveSulphurCrust")]
    [TestCase("WFCaveGalleryAloe", "WFGalleryAloe")]
    [TestCase("WFCaveMarrowCap", "WFMarrowCapMushroom")]
    [TestCase("WFCaveGravebloom", "WFGravebloomPetals")]
    public async Task PlantsGiveTheirProduce(string plant, string produce)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid uid = default;
        await server.WaitPost(() => uid = entMan.SpawnEntity(plant, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f)));

        int Count() => entMan.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == produce);
        await server.WaitAssertion(() =>
        {
            var harvest = server.System<WFRegrowingHarvestSystem>();
            var comp = entMan.GetComponent<WFRegrowingHarvestComponent>(uid);
            Assert.That(comp.Harvested, Is.False, "It starts ripe.");
            harvest.Harvest((uid, comp), null);
            Assert.That(Count(), Is.GreaterThan(0), "Picking should give the produce.");
            var first = Count();
            harvest.Harvest((uid, comp), null);
            Assert.That(Count(), Is.EqualTo(first), "A bare plant gives nothing.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>The mushrooms have their own soups, and the herbs, petals and crust carry what they are for.</summary>
    [Test]
    public async Task ProduceCooksAndHeals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var protos = server.ProtoMan;
            foreach (var (recipeId, soup, mushroom) in new[]
                     {
                         ("RecipeWFCaveCapSoup", "WFFoodSoupCaveCap", "WFCaveCapMushroom"),
                         ("RecipeWFCinderCapSoup", "WFFoodSoupCinderCap", "WFCinderCapMushroom"),
                         ("RecipeWFMarrowCapSoup", "WFFoodSoupMarrowCap", "WFMarrowCapMushroom"),
                     })
            {
                var recipe = protos.Index<FoodRecipePrototype>(recipeId);
                Assert.That(recipe.Result, Is.EqualTo(soup));
                Assert.That(recipe.IngredientsSolids.GetValueOrDefault(mushroom), Is.EqualTo(FixedPoint2.New(2)), $"{recipeId} wants two {mushroom}.");
                Assert.That(protos.Index<FoodRecipePrototype>("RecipeWFWildMushroomSoup").IngredientsSolids.ContainsKey(mushroom), Is.False,
                    "The generic wild mushroom soup must not take a cave mushroom's place.");
            }

            var solutions = server.System<Content.Shared.Chemistry.EntitySystems.SharedSolutionContainerSystem>();
            foreach (var (id, solution, reagent) in new[]
                     {
                         ("WFCaveDropletLichen", "food", "Bicaridine"), ("WFGalleryAloe", "food", "Bicaridine"),
                         ("WFGravebloomPetals", "food", "Dexalin"), ("WFCaveSulphurCrust", "crust", "Sulfur"),
                         ("WFCinderCapMushroom", "food", "Nutriment"),
                     })
            {
                var uid = entMan.SpawnEntity(id, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
                Assert.That(solutions.TryGetSolution(uid, solution, out _, out var found), Is.True, $"{id} has no {solution} solution.");
                Assert.That(found!.GetTotalPrototypeQuantity(reagent), Is.GreaterThan(FixedPoint2.Zero), $"{id} holds no {reagent}.");
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Each cave animal lives in its own cavern's air for twenty seconds: Fervidus's hot, oxygen-free vault included.</summary>
    [TestCase("WFSurfaceAsclepiu")]
    [TestCase("WFSurfaceFervidus")]
    [TestCase("WFSurfaceMerak")]
    public async Task AnimalsLiveInTheirCavernAir(string surfaceId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        var mobs = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            var protos = server.ProtoMan;
            var cavern = protos.Index<WFCavernPrototype>("WFCavern" + surfaceId["WFSurface".Length..]);
            var level = protos.Index<PlanetPrototype>(cavern.Level);
            var air = level.Atmosphere.Clone();
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            var x = 0;

            foreach (var animal in Animals[surfaceId])
            {
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x + dx, dy), new Tile(plating));
                mobs.Add(entMan.SpawnEntity(animal, new EntityCoordinates(map.Grid.Owner, x + 0.5f, 0.5f)));
                x += 40;
            }
        });

        await pair.RunSeconds(20);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var mob in mobs)
                {
                    var state = entMan.GetComponent<MobStateComponent>(mob).CurrentState;
                    var damage = entMan.GetComponent<DamageableComponent>(mob).TotalDamage;
                    Assert.That(state, Is.EqualTo(MobState.Alive), $"{entMan.ToPrettyString(mob)} is {state} after 20 s in {surfaceId}'s cavern air.");
                    Assert.That(damage, Is.LessThan(FixedPoint2.New(5)), $"{entMan.ToPrettyString(mob)} took {damage} damage from {surfaceId}'s cavern air.");
                }
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>The three worlds name a cavern soundscape of their own, and its files exist.</summary>
    [Test]
    public async Task CavernsHaveTheirOwnAmbience()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            foreach (var surface in Zones.Keys)
            {
                var cavern = server.ProtoMan.Index<WFCavernPrototype>("WFCavern" + surface["WFSurface".Length..]);
                Assert.That(cavern.Ambience, Is.Not.Null, $"{cavern.ID} plays the surface's muffled ambience instead of its own.");
                var profile = server.ProtoMan.Index(cavern.Ambience!.Value);
                Assert.That(profile.Loops, Is.Not.Empty, $"{profile.ID} has no bed.");
                Assert.That(profile.OneShots, Is.Not.Empty, $"{profile.ID} has no accents.");
            }
        });
        await pair.CleanReturnAsync();
    }
}
