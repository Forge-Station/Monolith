#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Caverns;
using Content.Server._WF.Caverns;
using Content.Server._WF.WildSlimes;
using Content.Server._WF.Xenobiology.Effects;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NPC.HTN;
using Content.Shared._WF.WildSlimes;
using Content.Shared._WF.Xenobiology;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared._WF.Xenobiology.Systems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.WildSlimes;

/// <summary>Carcinoma's wild slimes: their breeds, their patches and grottos, and how they behave while wild.</summary>
[TestFixture]
public sealed class WildSlimesTest
{
    private const string Habitat = "WFWildSlimesCarcinoma";
    private const string Carcinoma = "WFCarcinomaMutation";
    private const string Metastatic = "WFMetastaticMutation";
    private static readonly string[] WildBreeds = { Carcinoma, Metastatic };
    /// <summary>Most patches a world may have; they are meant to be a rare find.</summary>
    private const int MaxPatches = 12;

    private static readonly string[] Extracts = { "WFCarcinomaSlimeExtract", "WFMetastaticSlimeExtract" };

    private static SlimeComponent SlimeOf(BreedPrototype breed)
    {
        return (SlimeComponent) breed.Components["Slime"].Component;
    }

    /// <summary>
    /// No ordinary breed mutates into a wild one, the metastatic slime comes only from the carcinoma slime and goes
    /// back to it, and every reagent and entity the wild extracts react to or make exists.
    /// </summary>
    [Test]
    public async Task WildBreedsComeOnlyFromTheirWorld()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var breed in protos.EnumeratePrototypes<BreedPrototype>())
                {
                    var mutations = SlimeOf(breed).PotentialMutations.Select(m => m.Id).ToHashSet();
                    if (WildBreeds.Contains(breed.ID))
                        continue;

                    Assert.That(mutations.Intersect(WildBreeds), Is.Empty, $"{breed.ID} mutates into a wild breed.");
                }

                Assert.That(SlimeOf(protos.Index<BreedPrototype>(Carcinoma)).PotentialMutations.Select(m => m.Id),
                    Is.EquivalentTo(new[] { Metastatic }));
                Assert.That(SlimeOf(protos.Index<BreedPrototype>(Metastatic)).PotentialMutations.Select(m => m.Id),
                    Is.EquivalentTo(new[] { Carcinoma }));
                Assert.That(protos.Index<WFWildSlimeHabitatPrototype>(Habitat).Breed.Id, Is.EqualTo(Carcinoma));

                foreach (var extractId in Extracts)
                {
                    Assert.That(protos.Index<EntityPrototype>(extractId).TryGetComponent<ReactiveComponent>(out var reactive, factory), Is.True);
                    Assert.That(reactive!.Reactions, Is.Not.Null.And.Count.EqualTo(3), $"{extractId} reacts to plasma, water and blood.");
                    foreach (var reaction in reactive.Reactions!)
                    {
                        foreach (var reagent in reaction.Reagents ?? new HashSet<string>())
                        {
                            Assert.That(protos.HasIndex<ReagentPrototype>(reagent), Is.True, $"{extractId} reacts to missing {reagent}.");
                        }

                        CheckEffects(protos, extractId, reaction.Effects);
                    }
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    private static void CheckEffects(IPrototypeManager protos, string extractId, IEnumerable<EntityEffect> effects)
    {
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case XenobioReaction reaction:
                    CheckEffects(protos, extractId, reaction.Effects);
                    break;
                case XenobioSpawnEntity spawn:
                    Assert.That(protos.HasIndex<EntityPrototype>(spawn.Entity), Is.True, $"{extractId} spawns missing {spawn.Entity}.");
                    break;
                case XenobioAdjustSolution solution:
                    Assert.That(protos.HasIndex<ReagentPrototype>(solution.Reagent), Is.True, $"{extractId} makes missing {solution.Reagent}.");
                    break;
            }
        }
    }

    /// <summary>
    /// A formed Carcinoma has slime patches of see-through, solid, breathable jelly, each over a grotto of jelly floor
    /// in the Gut at the same tiles with a shaft down to it, and its patch and grotto fill with wild slimes.
    /// </summary>
    [Test]
    public async Task CarcinomaPatchesSitOverGrottos()
    {
        await using var pair = await PoolManager.GetServerClient();
        await CavernFixture.EnableCaverns(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var biomes = server.System<SharedBiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var wildSlimes = server.System<WFWildSlimeSystem>();
        var habitat = protos.Index<WFWildSlimeHabitatPrototype>(Habitat);

        var world = await CavernFixture.BuildWorld(pair, "WFSurfaceCarcinoma");
        WFWildSlimePatch patch = default!;

        await server.WaitAssertion(() =>
        {
            Assert.That(world.Cavern.Valid, Is.True, "Carcinoma built without its cavern.");
            Assert.That(entMan.TryGetComponent<WFWildSlimeGroundComponent>(world.Ground, out var ground), Is.True, "Carcinoma has no slime patches.");
            TestContext.Out.WriteLine($"{ground!.Patches.Count} patches: "
                + string.Join(", ", ground.Patches.Select(p => $"{p.Tiles.Count} tiles at {p.Centre} (grotto {p.Grotto.Count})")));
            Assert.That(ground.Patches, Is.Not.Empty, "Carcinoma has no slime patches.");
            Assert.That(ground.Patches, Has.Count.LessThanOrEqualTo(MaxPatches), "Slime patches should be a rare find.");
            Assert.That(ground.Cavern, Is.EqualTo(world.Cavern));

            var surfaceBiome = entMan.GetComponent<BiomeComponent>(world.Ground);
            var cavernBiome = entMan.GetComponent<BiomeComponent>(world.Cavern);
            using (Assert.EnterMultipleScope())
            {
                foreach (var each in ground.Patches)
                {
                    Assert.That(each.Grotto, Is.Not.Empty, $"The patch at {each.Centre} has no grotto.");
                    foreach (var tile in each.Tiles)
                    {
                        biomes.TryGetBiomeTile(tile, surfaceBiome.Layers, surfaceBiome.Seed, (Entity<MapGridComponent>?) null, out var top);
                        var topDef = (ContentTileDefinition) tileDefs[top!.Value.TypeId];
                        Assert.That(topDef.ID, Is.EqualTo(habitat.PatchTile.Id));
                        Assert.That(topDef.Transparent, Is.True, "The patch hides what is under it.");
                        Assert.That(topDef.MapAtmosphere, Is.False, "The patch is space.");

                        biomes.TryGetBiomeTile(tile, cavernBiome.Layers, cavernBiome.Seed, (Entity<MapGridComponent>?) null, out var below);
                        Assert.That(below.HasValue, Is.True, $"No cavern floor under the patch at {tile}.");
                        var belowDef = (ContentTileDefinition) tileDefs[below!.Value.TypeId];
                        Assert.That(belowDef.ID, Is.EqualTo(habitat.GrottoTile!.Value.Id), $"The cavern under the patch at {tile} is {belowDef.ID}.");
                    }
                }
            }

            patch = ground.Patches.OrderByDescending(p => p.Tiles.Count).First();
        });

        var from = patch.Tiles.Aggregate((a, b) => new Vector2i(System.Math.Min(a.X, b.X), System.Math.Min(a.Y, b.Y))) - new Vector2i(4, 4);
        var to = patch.Tiles.Aggregate((a, b) => new Vector2i(System.Math.Max(a.X, b.X), System.Math.Max(a.Y, b.Y))) + new Vector2i(4, 4);
        await CavernFixture.LoadChunks(pair, world.Ground, from, to);
        await CavernFixture.LoadChunks(pair, world.Cavern, from, to);
        await server.WaitRunTicks(5);

        var spawned = 0;
        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
            var cavernGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);
            var cavernGround = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
            var atmos = server.System<AtmosphereSystem>();

            Assert.That(cavernGround.Shades.ContainsKey(patch.Centre), Is.True, "The patch has no shaft down to its grotto.");
            var edge = patch.Tiles.First(t => t != patch.Centre && !cavernGround.Shades.ContainsKey(t));
            Assert.That(maps.TryGetTileRef(world.Ground, grid, edge, out var top) && !top.Tile.IsEmpty, Is.True, $"The patch tile {edge} is empty.");
            Assert.That(tileDefs[top.Tile.TypeId].ID, Is.EqualTo(habitat.PatchTile.Id));
            Assert.That(maps.TryGetTileRef(world.Cavern, cavernGrid, edge, out var below) && !below.Tile.IsEmpty, Is.True, $"No grotto floor under {edge}.");
            Assert.That(tileDefs[below.Tile.TypeId].ID, Is.EqualTo(habitat.GrottoTile!.Value.Id));

            var air = atmos.GetTileMixture(world.Ground, world.Ground, edge);
            Assert.That(air, Is.Not.Null, "The patch has no air.");
            Assert.That(air!.TotalMoles, Is.GreaterThan(50f), "The patch has no air.");

            var ground = entMan.GetComponent<WFWildSlimeGroundComponent>(world.Ground);
            spawned = wildSlimes.Populate((world.Ground, ground), habitat, patch, timing.CurTime);
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var onPatch = 0;
            var inGrotto = 0;
            var query = entMan.EntityQueryEnumerator<WFWildSlimeComponent, SlimeComponent, TransformComponent>();
            while (query.MoveNext(out _, out var wild, out var slime, out var xform))
            {
                // The pooled server may hold slimes from other tests; only this world's count.
                if (xform.MapUid != world.Ground && xform.MapUid != world.Cavern)
                    continue;

                Assert.That(slime.Breed.Id, Is.EqualTo(Carcinoma));
                if (xform.MapUid == world.Ground)
                    onPatch++;
                else if (xform.MapUid == world.Cavern)
                    inGrotto++;

                Assert.That(Vector2.Distance(xform.Coordinates.Position, patch.Middle), Is.LessThan(patch.Radius + 6f));
            }

            TestContext.Out.WriteLine($"Populated {spawned}: {onPatch} on the patch, {inGrotto} in the grotto.");
            Assert.That(onPatch, Is.GreaterThanOrEqualTo(1), "No wild slimes on the patch.");
            Assert.That(inGrotto, Is.GreaterThanOrEqualTo(1), "No wild slimes in the grotto.");
        });

        await CavernFixture.Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A wild slime takes nobody for an enemy and latches onto nobody beside it, and neither starves, grows nor splits;
    /// once tamed it is an ordinary slime that hunts and splits, and one put in a container is released too.
    /// </summary>
    [Test]
    public async Task WildSlimesStayDocileUntilCaught()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        var xeno = server.System<XenobiologySystem>();
        var wildSlimes = server.System<WFWildSlimeSystem>();
        var factions = server.System<NpcFactionSystem>();
        var hunger = server.System<HungerSystem>();
        var mobs = server.System<MobStateSystem>();
        var habitat = protos.Index<WFWildSlimeHabitatPrototype>(Habitat);

        EntityUid human = default, adult = default, baby = default, caught = default;
        await server.WaitPost(() =>
        {
            var floor = new Tile(tileDefs["Plating"].TileId);
            for (var x = -4; x <= 4; x++)
            for (var y = -4; y <= 4; y++)
            {
                maps.SetTile(map.Grid, new Vector2i(x, y), floor);
            }

            var coords = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
            human = entMan.SpawnEntity("MobHuman", coords);
            adult = xeno.SpawnSlimeAt(new EntityCoordinates(map.Grid, 1.5f, 0.5f), habitat.AdultPrototype, habitat.Breed)!.Value;
            baby = xeno.SpawnSlimeAt(new EntityCoordinates(map.Grid, -0.5f, 0.5f), habitat.BabyPrototype, habitat.Breed)!.Value;
            caught = xeno.SpawnSlimeAt(new EntityCoordinates(map.Grid, 0.5f, 1.5f), habitat.BabyPrototype, habitat.Breed)!.Value;
            foreach (var slime in new[] { adult, baby, caught })
            {
                wildSlimes.MakeWild(slime, habitat, map.MapUid, new Vector2(0.5f, 0.5f), 4f);
            }

            // A starving baby, and an adult fed to just short of splitting: both come back to a wild slime's hunger.
            var slimeComp = entMan.GetComponent<SlimeComponent>(adult);
            hunger.SetHunger(baby, 5f);
            hunger.SetHunger(adult, slimeComp.MitosisHunger - slimeComp.JitterDifference - 1f);
        });

        await pair.RunSeconds(10);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var slime in new[] { adult, baby })
                {
                    Assert.That(entMan.EntityExists(slime), Is.True, "A wild slime split or vanished.");
                    Assert.That(entMan.HasComponent<WFWildSlimeComponent>(slime), Is.True);
                    Assert.That(mobs.IsAlive(slime), Is.True, "A wild slime died.");
                    Assert.That(factions.GetNearbyHostiles(slime, 5f), Is.Empty, "A wild slime takes someone for an enemy.");
                    Assert.That(entMan.GetComponent<SlimeComponent>(slime).LatchedTarget, Is.Null, "A wild slime latched on.");
                    var value = hunger.GetHunger(entMan.GetComponent<HungerComponent>(slime));
                    Assert.That(value, Is.InRange(WFWildSlimeSystem.MinWildHunger, WFWildSlimeSystem.MaxWildHunger), "A wild slime's hunger ran away.");
                }

                Assert.That(entMan.GetComponent<MobGrowthComponent>(baby).IsFirstStage, Is.True, "A wild baby grew.");
                Assert.That(entMan.HasComponent<Content.Shared.Damage.Components.DamagedByContactComponent>(human), Is.False);
            }

            // Tame the adult and catch the baby in a container.
            entMan.GetComponent<SlimeComponent>(adult).Tamer = human;
            var holder = entMan.SpawnEntity(null, new EntityCoordinates(map.Grid, 3.5f, 3.5f));
            var containers = entMan.System<SharedContainerSystem>();
            var box = containers.EnsureContainer<Container>(holder, "wild-slime-test");
            Assert.That(containers.Insert(caught, box), Is.True);
        });

        await pair.RunSeconds(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFWildSlimeComponent>(adult), Is.False, "A tamed slime stayed wild.");
            Assert.That(entMan.HasComponent<WFWildSlimeComponent>(caught), Is.False, "A caught slime stayed wild.");
            Assert.That(factions.IsMember(adult, "SimpleHostile"), Is.True);
            Assert.That(entMan.GetComponent<HTNComponent>(adult).RootTask.Task, Is.EqualTo(habitat.TameRootTask), "A tamed slime still runs the wild HTN.");

            // The test map is vacuum, far outside Carcinoma's warmth: a fed slime only splits once its climate allows it.
            var slime = entMan.GetComponent<SlimeComponent>(adult);
            hunger.SetHunger(adult, slime.MitosisHunger + 10f);
        });

        await pair.RunSeconds(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(adult) && !entMan.IsQueuedForDeletion(adult), Is.True, "A slime split outside its climate.");
            Assert.That(server.System<WFSlimeClimateSystem>().InClimate((adult, entMan.GetComponent<WFSlimeClimateComponent>(adult))), Is.False);
            entMan.GetComponent<WFSlimeClimateComponent>(adult).MinTemperature = 0f;
            var slime = entMan.GetComponent<SlimeComponent>(adult);
            hunger.SetHunger(adult, slime.MitosisHunger + 10f);
        });

        await pair.RunSeconds(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(!entMan.EntityExists(adult) || entMan.IsQueuedForDeletion(adult), Is.True, "A tamed, well fed slime in its climate didn't split.");
        });

        await pair.CleanReturnAsync();
    }
}
