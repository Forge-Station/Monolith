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

namespace Content.IntegrationTests.Tests._WF.WildSlimes;

/// <summary>The checks every world's wild slime shares: its breeds, its patches and grottos, its survival and its climate.</summary>
public static class SlimeWorldTestKit
{
    /// <summary>Most patches a world may have; they are meant to be a rare find.</summary>
    public const int MaxPatches = 12;

    /// <summary>One world's wild slime as the tests see it.</summary>
    public sealed record Spec(string Surface, string Habitat, string Wild, string Offshoot, string WildExtract, string OffshootExtract);

    private static SlimeComponent SlimeOf(BreedPrototype breed)
    {
        return (SlimeComponent) breed.Components["Slime"].Component;
    }

    /// <summary>
    /// No other breed mutates into the wild one, wild and offshoot only mutate into each other, both have a climate and
    /// a bounty, and every reagent and entity their extracts react to or make exists.
    /// </summary>
    public static async Task CheckBreeds(Spec spec)
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
                    if (breed.ID == spec.Offshoot)
                        continue;

                    Assert.That(SlimeOf(breed).PotentialMutations.Select(m => m.Id), Does.Not.Contain(spec.Wild),
                        $"{breed.ID} mutates into the wild {spec.Wild}.");
                }

                var wild = protos.Index<BreedPrototype>(spec.Wild);
                var offshoot = protos.Index<BreedPrototype>(spec.Offshoot);
                Assert.That(SlimeOf(wild).PotentialMutations.Select(m => m.Id), Is.EquivalentTo(new[] { spec.Offshoot }));
                Assert.That(SlimeOf(offshoot).PotentialMutations.Select(m => m.Id), Is.EquivalentTo(new[] { spec.Wild }));
                Assert.That(SlimeOf(wild).MutationChance, Is.EqualTo(0.1f).Within(0.01f));
                Assert.That(SlimeOf(offshoot).MutationChance, Is.EqualTo(0.3f).Within(0.01f));
                Assert.That(wild.ProducedExtract.Id, Is.EqualTo(spec.WildExtract));
                Assert.That(offshoot.ProducedExtract.Id, Is.EqualTo(spec.OffshootExtract));
                Assert.That(wild.Components.ContainsKey("WFSlimeClimate"), Is.True, $"{spec.Wild} has no climate.");
                Assert.That(offshoot.Components.ContainsKey("WFSlimeClimate"), Is.True, $"{spec.Offshoot} has no climate.");
                Assert.That(protos.Index<WFWildSlimeHabitatPrototype>(spec.Habitat).Breed.Id, Is.EqualTo(spec.Wild));
                Assert.That(protos.Index<WFWildSlimeHabitatPrototype>(spec.Habitat).Surface.Id, Is.EqualTo(spec.Surface));

                foreach (var extractId in new[] { spec.WildExtract, spec.OffshootExtract })
                {
                    var extract = protos.Index<EntityPrototype>(extractId);
                    Assert.That(extract.TryGetComponent<ReactiveComponent>(out var reactive, factory), Is.True);
                    Assert.That(reactive!.Reactions, Is.Not.Null.And.Count.EqualTo(3), $"{extractId} reacts to plasma, water and blood.");
                    foreach (var reaction in reactive.Reactions!)
                    {
                        foreach (var reagent in reaction.Reagents ?? new HashSet<string>())
                        {
                            Assert.That(protos.HasIndex<ReagentPrototype>(reagent), Is.True, $"{extractId} reacts to missing {reagent}.");
                        }

                        CheckEffects(protos, extractId, reaction.Effects);
                    }

                    Assert.That(extract.TryGetComponent<TagComponent>(out var tags, factory), Is.True, $"{extractId} has no tags.");
                    var extractTags = tags!.Tags;
                    var bounty = protos.EnumeratePrototypes<XenobiologyBountyPrototype>()
                        .Any(b => b.Entries.Any(e => e.Whitelist.Tags != null && e.Whitelist.Tags.Any(t => extractTags.Contains(t))));
                    Assert.That(bounty, Is.True, $"No bounty asks for {extractId}.");
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
                case XenobioRelayNearby relay:
                    CheckEffects(protos, extractId, new[] { relay.Relayed });
                    break;
                case XenobioSpawnEntity spawn:
                    Assert.That(protos.HasIndex<EntityPrototype>(spawn.Entity), Is.True, $"{extractId} spawns missing {spawn.Entity}.");
                    break;
                case XenobioAdjustSolution solution:
                    Assert.That(protos.HasIndex<ReagentPrototype>(solution.Reagent), Is.True, $"{extractId} makes missing {solution.Reagent}.");
                    break;
                case XenobioSmoke smoke:
                    Assert.That(protos.HasIndex<EntityPrototype>(smoke.SmokePrototype), Is.True, $"{extractId} makes missing smoke {smoke.SmokePrototype}.");
                    foreach (var reagent in smoke.Solution.Contents)
                    {
                        Assert.That(protos.HasIndex<ReagentPrototype>(reagent.Reagent.Prototype), Is.True, $"{extractId} smokes missing {reagent.Reagent.Prototype}.");
                    }

                    break;
            }
        }
    }

    /// <summary>A formed world with its largest patch and grotto loaded and stocked.</summary>
    public sealed record FormedWorld(CavernFixture.World World, WFWildSlimePatch Patch);

    /// <summary>
    /// Builds the world and checks it has slime patches of see-through, solid, breathable floor, each over a grotto of its
    /// cavern floor at the same tiles with a shaft down to it, and that its largest patch and grotto fill with wild slimes.
    /// The caller tears the world down.
    /// </summary>
    public static async Task<FormedWorld> CheckPatches(TestPair pair, Spec spec)
    {
        await CavernFixture.EnableCaverns(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var biomes = server.System<SharedBiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var wildSlimes = server.System<WFWildSlimeSystem>();
        var habitat = protos.Index<WFWildSlimeHabitatPrototype>(spec.Habitat);

        var world = await CavernFixture.BuildWorld(pair, spec.Surface);
        WFWildSlimePatch patch = default!;

        await server.WaitAssertion(() =>
        {
            Assert.That(world.Cavern.Valid, Is.True, $"{spec.Surface} built without its cavern.");
            Assert.That(entMan.TryGetComponent<WFWildSlimeGroundComponent>(world.Ground, out var ground), Is.True, $"{spec.Surface} has no slime patches.");
            TestContext.Out.WriteLine($"{ground!.Patches.Count} patches: "
                + string.Join(", ", ground.Patches.Select(p => $"{p.Tiles.Count} tiles at {p.Centre} (grotto {p.Grotto.Count})")));
            Assert.That(ground.Patches, Has.Count.GreaterThanOrEqualTo(3), $"{spec.Surface} has too few slime patches.");
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
            while (query.MoveNext(out _, out _, out var slime, out var xform))
            {
                // The pooled server may hold slimes from other tests; only this world's count.
                if (xform.MapUid != world.Ground && xform.MapUid != world.Cavern)
                    continue;

                Assert.That(slime.Breed.Id, Is.EqualTo(spec.Wild));
                if (xform.MapUid == world.Ground)
                    onPatch++;
                else if (xform.MapUid == world.Cavern)
                    inGrotto++;
            }

            TestContext.Out.WriteLine($"Populated {spawned}: {onPatch} on the patch, {inGrotto} in the grotto.");
            Assert.That(onPatch, Is.GreaterThanOrEqualTo(1), "No wild slimes on the patch.");
            Assert.That(inGrotto, Is.GreaterThanOrEqualTo(1), "No wild slimes in the grotto.");
        });

        return new FormedWorld(world, patch);
    }

    /// <summary>
    /// Leaves a wild slime of each breed on the patch and in the grotto of a formed world for a while; none takes damage.
    /// Returns the wild ones, which stay until the world is torn down.
    /// </summary>
    public static async Task<List<EntityUid>> CheckSurvival(TestPair pair, Spec spec, FormedWorld formed, float seconds = 40f)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var xeno = server.System<XenobiologySystem>();
        var wildSlimes = server.System<WFWildSlimeSystem>();
        var mobs = server.System<MobStateSystem>();
        var habitat = protos.Index<WFWildSlimeHabitatPrototype>(spec.Habitat);
        var world = formed.World;
        var patch = formed.Patch;
        var slimes = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            var turf = server.System<TurfSystem>();
            var maps = server.System<SharedMapSystem>();
            var tiles = patch.Tiles.ToHashSet();

            // Free floor, away from the shaft.
            bool Free(EntityUid map, Vector2i tile)
            {
                var offset = tile - patch.Centre;
                return System.Math.Max(System.Math.Abs(offset.X), System.Math.Abs(offset.Y)) > 1
                       && maps.TryGetTileRef(map, entMan.GetComponent<MapGridComponent>(map), tile, out var tileRef)
                       && !tileRef.Tile.IsEmpty
                       && !turf.IsTileBlocked(tileRef, CollisionGroup.MobMask);
            }

            var spots = new List<(EntityUid Map, Vector2i Tile, EntityUid Other)>();
            var surface = patch.Tiles.Where(t => Free(world.Ground, t)).Take(4).ToList();
            var grotto = patch.Grotto.Where(t => tiles.Contains(t) && Free(world.Cavern, t)).Take(4).ToList();
            foreach (var tile in surface)
            {
                spots.Add((world.Ground, tile, world.Cavern));
            }

            foreach (var tile in grotto)
            {
                spots.Add((world.Cavern, tile, world.Ground));
            }

            for (var i = 0; i < spots.Count; i++)
            {
                var (map, tile, other) = spots[i];
                var breed = i % 2 == 0 ? spec.Wild : spec.Offshoot;
                var proto = i % 4 < 2 ? habitat.BabyPrototype : habitat.AdultPrototype;
                var coords = new EntityCoordinates(map, new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
                var slime = xeno.SpawnSlimeAt(coords, proto, breed)!.Value;
                wildSlimes.MakeWild(slime, habitat, map, patch.Middle, patch.Radius + WFWildSlimeSystem.LeashMargin, other);
                slimes.Add(slime);
            }
        });

        await pair.RunSeconds(seconds);

        await server.WaitAssertion(() =>
        {
            Assert.That(slimes, Is.Not.Empty);
            using (Assert.EnterMultipleScope())
            {
                foreach (var slime in slimes)
                {
                    Assert.That(entMan.EntityExists(slime), Is.True, "A wild slime vanished.");
                    if (!entMan.EntityExists(slime))
                        continue;

                    var name = entMan.ToPrettyString(slime);
                    Assert.That(mobs.IsAlive(slime), Is.True, $"{name} died on its own world.");
                    var damage = entMan.GetComponent<DamageableComponent>(slime);
                    Assert.That(damage.TotalDamage.Float(), Is.Zero,
                        $"{name} took damage on its own world: {string.Join(", ", damage.Damage.DamageDict.Where(d => d.Value > 0).Select(d => $"{d.Key} {d.Value}"))}");
                }
            }
        });

        return slimes;
    }

    /// <summary>A tamed adult won't split out of its climate (vacuum), and splits once the air is its world's.</summary>
    public static async Task CheckClimate(Spec spec, float temperature, float[] moles)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        var xeno = server.System<XenobiologySystem>();
        var hunger = server.System<HungerSystem>();
        var climate = server.System<WFSlimeClimateSystem>();
        var habitat = protos.Index<WFWildSlimeHabitatPrototype>(spec.Habitat);

        var adults = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            var floor = new Tile(tileDefs["Plating"].TileId);
            for (var x = -4; x <= 4; x++)
            for (var y = -4; y <= 4; y++)
            {
                maps.SetTile(map.Grid, new Vector2i(x, y), floor);
            }

            var human = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 3.5f, 3.5f));
            foreach (var (breed, x) in new[] { (spec.Wild, -1.5f), (spec.Offshoot, 1.5f) })
            {
                var adult = xeno.SpawnSlimeAt(new EntityCoordinates(map.Grid, x, 0.5f), habitat.AdultPrototype, breed)!.Value;
                entMan.GetComponent<SlimeComponent>(adult).Tamer = human;
                adults.Add(adult);
                hunger.SetHunger(adult, entMan.GetComponent<SlimeComponent>(adult).MitosisHunger + 10f);
            }
        });

        await pair.RunSeconds(3);

        await server.WaitAssertion(() =>
        {
            foreach (var adult in adults)
            {
                Assert.That(entMan.EntityExists(adult) && !entMan.IsQueuedForDeletion(adult), Is.True, "A slime split outside its climate.");
                Assert.That(climate.InClimate((adult, entMan.GetComponent<WFSlimeClimateComponent>(adult))), Is.False);
                hunger.SetHunger(adult, entMan.GetComponent<SlimeComponent>(adult).MitosisHunger + 10f);
            }

            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = temperature };
            for (var gas = 0; gas < moles.Length; gas++)
            {
                air.AdjustMoles(gas, moles[gas]);
            }

            // Without a grid atmosphere the floor takes the map's air.
            entMan.RemoveComponent<Content.Server.Atmos.Components.GridAtmosphereComponent>(map.Grid);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            foreach (var adult in adults)
            {
                Assert.That(climate.InClimate((adult, entMan.GetComponent<WFSlimeClimateComponent>(adult))), Is.True,
                    $"{entMan.ToPrettyString(adult)} isn't in its climate in its world's air.");
            }
        });

        await pair.RunSeconds(3);

        await server.WaitAssertion(() =>
        {
            foreach (var adult in adults)
            {
                Assert.That(!entMan.EntityExists(adult) || entMan.IsQueuedForDeletion(adult), Is.True, "A tamed, well fed slime in its climate didn't split.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
