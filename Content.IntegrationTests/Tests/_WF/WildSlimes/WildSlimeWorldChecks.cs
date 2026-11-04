#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Caverns;
using Content.Server._WF.Caverns;
using Content.Server._WF.WildSlimes;
using Content.Server._WF.Xenobiology.Effects;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.WildSlimes;
using Content.Shared._WF.Xenobiology;
using Content.Shared._WF.Xenobiology.Bounties;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared._WF.Xenobiology.Systems;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.EntityEffects;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WF.WildSlimes;

/// <summary>The checks every world's wild slimes share: breeds and extracts, patches over grottos, survival, climate.</summary>
public static class WildSlimeWorldChecks
{
    /// <summary>Most patches a world may have; they are meant to be a rare find.</summary>
    public const int MaxPatches = 12;

    private static SlimeComponent SlimeOf(BreedPrototype breed)
    {
        return (SlimeComponent) breed.Components["Slime"].Component;
    }

    /// <summary>
    /// No other breed mutates into the wild breed or its offshoot, the two only mutate into each other at about 10% and
    /// 30%, both have a climate, and every reagent and entity their extracts react to or make exists, with a bounty for
    /// each extract.
    /// </summary>
    public static async Task CheckBreeds(TestPair pair, string habitatId, string wild, string offshoot)
    {
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var pairBreeds = new[] { wild, offshoot };

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var breed in protos.EnumeratePrototypes<BreedPrototype>())
                {
                    if (pairBreeds.Contains(breed.ID))
                        continue;

                    var mutations = SlimeOf(breed).PotentialMutations.Select(m => m.Id);
                    Assert.That(mutations.Intersect(pairBreeds), Is.Empty, $"{breed.ID} mutates into a {habitatId} breed.");
                }

                var wildBreed = protos.Index<BreedPrototype>(wild);
                var offshootBreed = protos.Index<BreedPrototype>(offshoot);
                Assert.That(SlimeOf(wildBreed).PotentialMutations.Select(m => m.Id), Is.EquivalentTo(new[] { offshoot }));
                Assert.That(SlimeOf(offshootBreed).PotentialMutations.Select(m => m.Id), Is.EquivalentTo(new[] { wild }));
                Assert.That(SlimeOf(wildBreed).MutationChance, Is.EqualTo(0.1f).Within(0.05f));
                Assert.That(SlimeOf(offshootBreed).MutationChance, Is.EqualTo(0.3f).Within(0.1f));
                Assert.That(protos.Index<WFWildSlimeHabitatPrototype>(habitatId).Breed.Id, Is.EqualTo(wild));

                var bounties = protos.EnumeratePrototypes<XenobiologyBountyPrototype>().ToList();
                foreach (var breed in new[] { wildBreed, offshootBreed })
                {
                    Assert.That(breed.Components.ContainsKey("WFSlimeClimate"), Is.True, $"{breed.ID} has no climate.");

                    var extractId = breed.ProducedExtract.Id;
                    Assert.That(protos.TryIndex<EntityPrototype>(extractId, out var extract), Is.True, $"{breed.ID} makes missing {extractId}.");
                    Assert.That(extract!.TryGetComponent<ReactiveComponent>(out var reactive, factory), Is.True);
                    Assert.That(reactive!.Reactions, Is.Not.Null.And.Count.EqualTo(3), $"{extractId} reacts to plasma, water and blood.");
                    foreach (var reaction in reactive.Reactions!)
                    {
                        foreach (var reagent in reaction.Reagents ?? new HashSet<string>())
                        {
                            Assert.That(protos.HasIndex<ReagentPrototype>(reagent), Is.True, $"{extractId} reacts to missing {reagent}.");
                        }

                        CheckEffects(protos, extractId, reaction.Effects);
                    }

                    Assert.That(extract.TryGetComponent<TagComponent>(out var tags, factory), Is.True, $"{extractId} has no tag.");
                    var extractTags = tags!.Tags;
                    var bounty = bounties.FirstOrDefault(b => b.Entries.Any(e => e.Whitelist.Tags?.Any(t => extractTags.Contains(t)) == true));
                    Assert.That(bounty, Is.Not.Null, $"No bounty takes {extractId}.");
                    Assert.That(bounty!.PointsAwarded, Is.EqualTo(breed == wildBreed ? 15000f : 40000f));
                }
            }
        });
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
    /// A formed world has a handful of slime patches of see-through, solid, breathable ground, each over a grotto at the
    /// same tiles with a shaft down to it; its patch and grotto fill with wild slimes, and wild slimes left on the patch
    /// and in the grotto for <paramref name="seconds"/> take no damage from the world.
    /// </summary>
    public static async Task CheckWorld(TestPair pair, string surfaceId, string habitatId, int minPatches, float seconds)
    {
        var server = pair.Server;
        await CavernFixture.EnableCaverns(pair);
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var biomes = server.System<SharedBiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var wildSlimes = server.System<WFWildSlimeSystem>();
        var habitat = protos.Index<WFWildSlimeHabitatPrototype>(habitatId);

        var world = await CavernFixture.BuildWorld(pair, surfaceId);
        WFWildSlimePatch patch = default!;

        await server.WaitAssertion(() =>
        {
            Assert.That(world.Cavern.Valid, Is.True, $"{surfaceId} built without its cavern.");
            Assert.That(entMan.TryGetComponent<WFWildSlimeGroundComponent>(world.Ground, out var ground), Is.True, $"{surfaceId} has no slime patches.");
            TestContext.Out.WriteLine($"{ground!.Patches.Count} patches: "
                + string.Join(", ", ground.Patches.Select(p => $"{p.Tiles.Count} tiles at {p.Centre} (grotto {p.Grotto.Count})")));
            Assert.That(ground.Patches, Has.Count.InRange(minPatches, MaxPatches), "A world should have a handful of slime patches.");
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
                        Assert.That(topDef.Weather, Is.True, "The patch takes no weather.");

                        biomes.TryGetBiomeTile(tile, cavernBiome.Layers, cavernBiome.Seed, (Entity<MapGridComponent>?) null, out var below);
                        Assert.That(below.HasValue, Is.True, $"No cavern floor under the patch at {tile}.");
                        var belowDef = (ContentTileDefinition) tileDefs[below!.Value.TypeId];
                        Assert.That(belowDef.ID, Is.EqualTo(habitat.GrottoTile!.Value.Id), $"The cavern under the patch at {tile} is {belowDef.ID}.");
                    }
                }
            }

            patch = ground.Patches.OrderByDescending(p => p.Tiles.Count).First();
        });

        var from = patch.Tiles.Aggregate((a, b) => new Vector2i(System.Math.Min(a.X, b.X), System.Math.Min(a.Y, b.Y))) - new Vector2i(10, 10);
        var to = patch.Tiles.Aggregate((a, b) => new Vector2i(System.Math.Max(a.X, b.X), System.Math.Max(a.Y, b.Y))) + new Vector2i(10, 10);
        await CavernFixture.LoadChunks(pair, world.Ground, from, to);
        await CavernFixture.LoadChunks(pair, world.Cavern, from, to);
        await server.WaitRunTicks(5);

        var populated = new List<EntityUid>();
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
            wildSlimes.Populate((world.Ground, ground), habitat, patch, timing.CurTime);
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var onPatch = 0;
            var inGrotto = 0;
            var query = entMan.EntityQueryEnumerator<WFWildSlimeComponent, SlimeComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var slime, out var xform))
            {
                // The pooled server may hold slimes from other tests; only this world's count.
                if (xform.MapUid != world.Ground && xform.MapUid != world.Cavern)
                    continue;

                Assert.That(slime.Breed.Id, Is.EqualTo(habitat.Breed.Id));
                if (xform.MapUid == world.Ground)
                    onPatch++;
                else if (xform.MapUid == world.Cavern)
                    inGrotto++;
            }

            TestContext.Out.WriteLine($"Populated {onPatch} on the patch, {inGrotto} in the grotto.");
            Assert.That(onPatch, Is.GreaterThanOrEqualTo(1), "No wild slimes on the patch.");
            Assert.That(inGrotto, Is.GreaterThanOrEqualTo(1), "No wild slimes in the grotto.");
        });

        // Slimes of our own, which the patch never retires. Each gets a mind, so no player being near doesn't let its
        // body skip its upkeep (WFDormancySystem) and hide what the air does to it.
        var survivors = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var xeno = server.System<XenobiologySystem>();
            var minds = server.System<SharedMindSystem>();
            var cavernGround = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
            var leash = patch.Radius + WFWildSlimeSystem.LeashMargin;
            var turf = server.System<TurfSystem>();
            bool Free(EntityUid map, Vector2i tile) => maps.TryGetTileRef(map, entMan.GetComponent<MapGridComponent>(map), tile, out var tileRef)
                && !tileRef.Tile.IsEmpty && !turf.IsTileBlocked(tileRef, CollisionGroup.MobMask);
            var spots = patch.Tiles.Where(t => !cavernGround.Shades.ContainsKey(t) && Free(world.Ground, t)).Take(3).Select(t => (world.Ground, t, (EntityUid?) world.Cavern))
                .Concat(patch.Grotto.Where(t => patch.Tiles.Contains(t) && t != patch.Centre && Free(world.Cavern, t)).Take(2).Select(t => (world.Cavern, t, (EntityUid?) world.Ground)))
                .ToList();
            Assert.That(spots, Has.Count.EqualTo(5), "Too few free tiles on the patch and in its grotto.");
            foreach (var (map, tile, other) in spots)
            {
                var coords = new EntityCoordinates(map, new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
                var slime = xeno.SpawnSlimeAt(coords, habitat.AdultPrototype, habitat.Breed)!.Value.Owner;
                wildSlimes.MakeWild(slime, habitat, map, patch.Middle, leash, other);
                minds.TransferTo(minds.CreateMind(null), slime);
                survivors.Add(slime);
            }
        });

        await pair.RunSeconds(seconds);

        await server.WaitAssertion(() =>
        {
            var mobs = server.System<MobStateSystem>();
            using (Assert.EnterMultipleScope())
            {
                foreach (var slime in survivors)
                {
                    Assert.That(entMan.EntityExists(slime), Is.True, "A wild slime vanished.");
                    if (!entMan.EntityExists(slime))
                        continue;

                    var damage = entMan.GetComponent<DamageableComponent>(slime);
                    var where = entMan.GetComponent<TransformComponent>(slime).MapUid == world.Cavern ? "grotto" : "patch";
                    TestContext.Out.WriteLine($"A slime in the {where}: {damage.TotalDamage} damage ({string.Join(", ", damage.Damage.DamageDict.Where(d => d.Value > 0).Select(d => $"{d.Key} {d.Value}"))}).");
                    Assert.That(mobs.IsAlive(slime), Is.True, $"A wild slime in the {where} died.");
                    // A slime that wanders down the shaft lands with a little blunt damage; that isn't the world's air.
                    var hurt = damage.Damage.DamageDict.Where(d => d.Key != "Blunt").Sum(d => d.Value.Float());
                    Assert.That(hurt, Is.Zero, $"A wild slime in the {where} was hurt by its own world.");
                }
            }
        });

        await CavernFixture.Teardown(pair, world);
    }

    /// <summary>Spawns an adult of the habitat's breed on a fresh test map holding breathable air at <paramref name="temperature"/>.</summary>
    public static async Task<(TestMapData Map, EntityUid Slime)> SpawnInAir(TestPair pair, string habitatId, float temperature)
    {
        var server = pair.Server;
        var habitat = server.ResolveDependency<IPrototypeManager>().Index<WFWildSlimeHabitatPrototype>(habitatId);
        var map = await pair.CreateTestMap();
        EntityUid slime = default;
        await server.WaitPost(() =>
        {
            SetAir(server.System<AtmosphereSystem>(), map, temperature);
            slime = server.System<XenobiologySystem>().SpawnSlimeAt(map.GridCoords, habitat.AdultPrototype, habitat.Breed)!.Value.Owner;
        });

        await server.WaitRunTicks(2);
        return (map, slime);
    }

    private static void SetAir(AtmosphereSystem atmos, TestMapData map, float temperature)
    {
        var air = new GasMixture(Atmospherics.CellVolume) { Temperature = temperature };
        air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
        air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
        atmos.SetMapAtmosphere(map.MapUid, false, air);
    }

    /// <summary>
    /// A well fed adult doesn't split out of its climate and does in it: the outside one in air at
    /// <paramref name="outside"/> kelvin, the inside one at <paramref name="inside"/> kelvin standing on
    /// <paramref name="underfoot"/> if given.
    /// </summary>
    public static async Task CheckClimate(TestPair pair, string habitatId, float outside, float inside, string? underfoot = null)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var xeno = server.System<XenobiologySystem>();
        var hunger = server.System<HungerSystem>();
        var climate = server.System<WFSlimeClimateSystem>();
        var habitat = protos.Index<WFWildSlimeHabitatPrototype>(habitatId);
        var outMap = await pair.CreateTestMap();
        var inMap = await pair.CreateTestMap();

        EntityUid outSlime = default, inSlime = default;
        await server.WaitPost(() =>
        {
            var atmos = server.System<AtmosphereSystem>();
            SetAir(atmos, outMap, outside);
            SetAir(atmos, inMap, inside);

            outSlime = xeno.SpawnSlimeAt(outMap.GridCoords, habitat.AdultPrototype, habitat.Breed)!.Value.Owner;
            inSlime = xeno.SpawnSlimeAt(inMap.GridCoords, habitat.AdultPrototype, habitat.Breed)!.Value.Owner;
            if (underfoot != null)
                entMan.SpawnEntity(underfoot, inMap.GridCoords);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(climate.InClimate((outSlime, entMan.GetComponent<WFSlimeClimateComponent>(outSlime))), Is.False, "The slime out of its climate is in it.");
            Assert.That(climate.InClimate((inSlime, entMan.GetComponent<WFSlimeClimateComponent>(inSlime))), Is.True, "The slime in its climate is out of it.");
            foreach (var slime in new[] { outSlime, inSlime })
            {
                var comp = entMan.GetComponent<SlimeComponent>(slime);
                hunger.SetHunger(slime, comp.MitosisHunger + 10f);
            }
        });

        await pair.RunSeconds(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(outSlime) && !entMan.IsQueuedForDeletion(outSlime), Is.True, "A slime split outside its climate.");
            Assert.That(!entMan.EntityExists(inSlime) || entMan.IsQueuedForDeletion(inSlime), Is.True, "A well fed slime in its climate didn't split.");
        });
    }
}
