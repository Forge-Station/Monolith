#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Caverns;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Caverns;
using Content.Server.Parallax;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Caverns;
using Content.Shared.Damage;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>Every world gets a gate at build: a pinned hole with shades, a solid lip, a rock-free pad and a climb point.</summary>
[TestFixture]
[TestOf(typeof(WFCavernMouthSystem))]
public sealed class CavernMouthTest
{
    /// <summary>Section 4.8: what each world's shafts report about the air below.</summary>
    private static readonly Dictionary<string, WFCavernAir> ExpectedAir = new()
    {
        { "WFSurfaceAsclepiu", WFCavernAir.Breathable },
        { "WFSurfaceFervidus", WFCavernAir.Scalding },
        { "WFSurfaceMerak", WFCavernAir.Breathable },
        { "WFSurfaceAerumna", WFCavernAir.Toxic },
        { "WFSurfaceThrascias", WFCavernAir.Freezing },
        { "WFSurfaceCarcinoma", WFCavernAir.Foul },
    };

    /// <summary>Section 3.5: each world's landing tile fall multiplier.</summary>
    private static readonly Dictionary<string, float> ExpectedLanding = new()
    {
        { "WFSurfaceAsclepiu", 0f },
        { "WFSurfaceFervidus", 0.75f },
        { "WFSurfaceMerak", 0.5f },
        { "WFSurfaceAerumna", 1.5f },
        { "WFSurfaceThrascias", 0.25f },
        { "WFSurfaceCarcinoma", 0.4f },
    };

    /// <summary>Section 3.6: each world's climb-up time in seconds.</summary>
    private static readonly Dictionary<string, float> ExpectedClimb = new()
    {
        { "WFSurfaceAsclepiu", 4f },
        { "WFSurfaceFervidus", 4f },
        { "WFSurfaceMerak", 4.6f },
        { "WFSurfaceAerumna", 10f },
        { "WFSurfaceThrascias", 5f },
        { "WFSurfaceCarcinoma", 4f },
    };

    /// <summary>Each world, built and torn down in turn, has a fully fitted gate as soon as it is built.</summary>
    [Test]
    public async Task GateExists()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await EnableCaverns(pair);

        foreach (var surfaceId in Surfaces)
        {
            var world = await BuildWorld(pair, surfaceId);

            try
            {
                var cavern = CavernOf(pair, surfaceId);
                var gate = await Gate(pair, world);

                await AssertGateFitted(pair, world, cavern, gate, surfaceId);

                // A cavern viewer's load brings rock around the pad, never onto it.
                var spec = cavern.Mouths;
                await LoadChunks(pair, world.Cavern, gate.Min - new Vector2i(spec.PadRadius, spec.PadRadius),
                    gate.Max + new Vector2i(spec.PadRadius, spec.PadRadius));

                await server.WaitAssertion(() => AssertPadClear(pair, world, cavern, gate, surfaceId));
            }
            finally
            {
                await Teardown(pair, world);
            }
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Unloading and reloading both maps' chunks leaves the gate's tiles and entities exactly as they were.</summary>
    [Test]
    public async Task GateSurvivesUnloadReload()
    {
        const string surfaceId = "WFSurfaceFervidus";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, surfaceId);

        try
        {
            var cavern = CavernOf(pair, surfaceId);
            var gate = await Gate(pair, world);
            var spec = cavern.Mouths;
            var from = gate.Min - new Vector2i(spec.PadRadius + 1, spec.PadRadius + 1);
            var to = gate.Max + new Vector2i(spec.PadRadius + 1, spec.PadRadius + 1);

            await LoadChunks(pair, world.Ground, from, to);
            await LoadChunks(pair, world.Cavern, from, to);

            Snapshot? before = null;
            await server.WaitPost(() => before = Snap(pair, world, gate, spec));

            Assert.That(before!.Rim, Is.Not.Empty, "Precondition: the Fervidus gate has no rim decor to keep track of.");

            await UnloadChunks(pair, world.Ground, from, to);
            await UnloadChunks(pair, world.Cavern, from, to);

            await server.WaitAssertion(() =>
            {
                var groundBiome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));
                Assert.That(ChunkOrigins(from, to).Any(chunk => biomes.WfIsChunkLoaded(groundBiome, chunk)), Is.False,
                    "Precondition: a ground chunk under the gate is still loaded.");
                Snap(pair, world, gate, spec).AssertSame(before, "after the unload");
            });

            await LoadChunks(pair, world.Ground, from, to);
            await LoadChunks(pair, world.Cavern, from, to);

            await server.WaitAssertion(() =>
            {
                Snap(pair, world, gate, spec).AssertSame(before, "after the reload");
                AssertPadClear(pair, world, cavern, gate, surfaceId);
            });

            await AssertGateFitted(pair, world, cavern, gate, surfaceId);
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Across many seeds, every world's holes stay in their size range, joined edge to edge without spurs, pinches or
    /// islands, with the climb tile past the hole on the climb side and rim spots clear of it. They also vary: few plain
    /// rectangles (a rift aside) or fallbacks, most of the size range used and few repeats.
    /// </summary>
    // The shape is a pure function, but its specs are the worlds' prototypes, which only a server loads.
    [Test]
    public async Task ShapesStayInRange()
    {
        const int seeds = 400;
        const float maxRectangles = 0.05f;
        const float maxFallbacks = 0.05f;

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            foreach (var surfaceId in Surfaces)
            {
                var spec = CavernOf(pair, surfaceId).Mouths;
                var distinct = new HashSet<string>();
                var sizes = new HashSet<int>();
                var rectangles = 0;
                var fallbacks = 0;

                using (Assert.EnterMultipleScope())
                {
                    for (var seed = 0; seed < seeds; seed++)
                    {
                        var shape = WFCavernMouthShape.Generate(spec, seed * 7919 + 13);
                        var again = WFCavernMouthShape.Generate(spec, seed * 7919 + 13);
                        var name = $"{surfaceId} seed {seed}";

                        Assert.That(shape.Hole, Is.EquivalentTo(again.Hole), $"{name}: the same seed grew another hole.");
                        Assert.That(shape.Rim, Is.EqualTo(again.Rim), $"{name}: the same seed picked other rim spots.");
                        distinct.Add(string.Join(';', shape.Hole.OrderBy(t => t.X).ThenBy(t => t.Y)));
                        sizes.Add(shape.Hole.Count);

                        if (shape.Hole.Count == (shape.Max.X - shape.Min.X + 1) * (shape.Max.Y - shape.Min.Y + 1))
                            rectangles++;
                        if (shape.Fallback)
                            fallbacks++;

                        AssertShape(spec, shape, name);
                    }

                    if (spec.Style != WFCavernMouthStyle.Rift)
                    {
                        Assert.That(rectangles, Is.AtMost(seeds * maxRectangles),
                            $"{surfaceId}: {rectangles} of {seeds} holes are plain rectangles.");
                    }

                    Assert.That(fallbacks, Is.AtMost(seeds * maxFallbacks),
                        $"{surfaceId}: {fallbacks} of {seeds} seeds missed every attempt and fell back.");
                    Assert.That(sizes.Count, Is.AtLeast((spec.MaxTiles - spec.MinTiles + 1) / 2),
                        $"{surfaceId}: the holes take only {sizes.Count} sizes of {spec.MinTiles}-{spec.MaxTiles}.");
                    Assert.That(distinct.Count, Is.AtLeast(seeds / 5),
                        $"{surfaceId}: the seeds grew only {distinct.Count} different holes.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every world's pit RSI holds every state the client's shade visuals can ask for, and nothing else.</summary>
    [Test]
    public async Task PitStatesExist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var protoMan = client.ResolveDependency<IPrototypeManager>();
        var factory = client.ResolveDependency<IComponentFactory>();
        var states = WFCavernShadeVisualsSystem.AllStates().ToHashSet();

        await client.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var surfaceId in Surfaces)
                {
                    var shade = protoMan.Index<EntityPrototype>(CavernOf(pair, surfaceId).Mouths.Shade.Id);
                    Assert.That(shade.TryGetComponent<SpriteComponent>(out var sprite, factory), Is.True,
                        $"{shade.ID} has no sprite.");

                    Assert.That(sprite?.BaseRSI, Is.Not.Null, $"{shade.ID} has no RSI.");
                    if (sprite?.BaseRSI is not { } rsi)
                        continue;

                    var missing = states.Where(state => !rsi.TryGetState(state, out _)).ToList();
                    var stray = rsi.Select(state => state.StateId.Name ?? string.Empty).Where(name => !states.Contains(name)).ToList();

                    Assert.That(missing, Is.Empty, $"{rsi.Path} lacks {missing.Count} states, such as {missing.FirstOrDefault()}.");
                    Assert.That(stray, Is.Empty, $"{rsi.Path} has {stray.Count} states the client never asks for, such as {stray.FirstOrDefault()}.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A cell whose site lands on pinned tiles, such as another mouth's pad, is Empty for good rather than Deferred.</summary>
    [Test]
    public async Task PinnedSiteIsEmpty()
    {
        const string surfaceId = "WFSurfaceMerak";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var mouths = server.System<WFCavernMouthSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, surfaceId);

        try
        {
            var spec = CavernOf(pair, surfaceId).Mouths;
            var gate = await Gate(pair, world);

            await server.WaitAssertion(() =>
            {
                var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));
                var levelBiome = (world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern));
                var first = WFCavernMouthSystem.CellOf(spec, gate.Origin);
                var tried = 0;

                // Cells three over from the gate's, far from anything loaded; the first with a site decides.
                foreach (var cell in new[] { new Vector2i(3, 0), new Vector2i(-3, 0), new Vector2i(0, 3), new Vector2i(0, -3) })
                {
                    var target = first + cell;
                    var tiles = new List<Vector2i>(spec.CellSize * spec.CellSize);
                    for (var x = 0; x < spec.CellSize; x++)
                    for (var y = 0; y < spec.CellSize; y++)
                    {
                        tiles.Add(target * spec.CellSize + new Vector2i(x, y));
                    }

                    biomes.WfPinTiles(levelBiome, tiles);
                    var claim = mouths.TryClaimCell(ground, target);
                    tried++;

                    if (ground.Item2.Cells[target].Site == null)
                        continue;

                    Assert.That(claim, Is.EqualTo(WFCavernClaim.Empty), $"{surfaceId}: a site on a pinned pad came out {claim}.");
                    Assert.That(mouths.TryClaimCell(ground, target), Is.EqualTo(WFCavernClaim.Empty),
                        $"{surfaceId}: the pinned cell did not stay Empty.");
                    return;
                }

                Assert.Fail($"Precondition: none of the {tried} cells tried around the {surfaceId} gate has a site.");
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>An Asclepiu mouth cut beside a pool keeps the pool's water on its pinned pad, where the biome no longer grows it.</summary>
    [Test]
    public async Task PadKeepsPools()
    {
        const string surfaceId = "WFSurfaceAsclepiu";
        const int tries = 12;

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var mouths = server.System<WFCavernMouthSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, surfaceId);

        try
        {
            var spec = CavernOf(pair, surfaceId).Mouths;

            await server.WaitAssertion(() =>
            {
                var water = spec.LandingEntity!.Value.Id;
                var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));
                var levelBiome = entMan.GetComponent<BiomeComponent>(world.Cavern);
                Entity<MapGridComponent>? noGrid = null;
                var origins = new List<Vector2i>();

                // Pool edges well clear of the gate and of each other, in chunks nobody has loaded.
                for (var x = 160; x < 480 && origins.Count < tries; x += 2)
                for (var y = -160; y < 160 && origins.Count < tries; y += 2)
                {
                    var index = new Vector2i(x, y);
                    if (origins.Any(o => (o - index).Length < 40)
                        || !biomes.TryGetTile(index, levelBiome.Layers, levelBiome.Seed, noGrid, out var tile)
                        || !biomes.TryGetEntity(index, levelBiome.Layers, tile.Value, levelBiome.Seed, noGrid, out var entity)
                        || entity != water)
                        continue;

                    origins.Add(index + new Vector2i(3, 0));
                }

                foreach (var origin in origins)
                {
                    if (!mouths.TryOpenMouth(ground, origin, out _))
                        continue;

                    var mouth = ground.Item2.Mouths.Single(m => m.Origin == origin);
                    if (AssertPadPoolsKept(pair, world, mouth, spec, $"{surfaceId} mouth at {origin}") > 0)
                        return;
                }

                Assert.Fail($"Precondition: none of the {origins.Count} mouths cut beside {surfaceId}'s pools has pool on its pad.");
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A site under a loaded ground chunk waits as Deferred, then is stamped where it was found once the chunk unloads.</summary>
    [Test]
    public async Task ClaimDeferredWhileChunkLoaded()
    {
        const string surfaceId = "WFSurfaceMerak";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var mouths = server.System<WFCavernMouthSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, surfaceId);

        try
        {
            var spec = CavernOf(pair, surfaceId).Mouths;
            var gate = await Gate(pair, world);
            Vector2i? cell = null;
            WFCavernSite? site = null;

            await server.WaitPost(() =>
            {
                var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));
                var biome = entMan.GetComponent<BiomeComponent>(world.Ground);
                var first = WFCavernMouthSystem.CellOf(spec, gate.Origin);

                // Clear of the gate search's cells. The footprint must grow nothing: an unload pins the tile of any entity
                // that went, and fauna markers delete themselves on spawn, which would make the cell Empty instead.
                for (var dx = -3; dx <= 3 && site == null; dx++)
                for (var dy = -3; dy <= 3 && site == null; dy++)
                {
                    if (Math.Abs(dx) < 2 && Math.Abs(dy) < 2)
                        continue;

                    var candidate = first + new Vector2i(dx, dy);
                    if (mouths.EvaluateCell(ground, candidate) is not { } found
                        || found.Shape.Hole.Concat(found.Shape.Ring).Any(offset => GrowsEntity(biomes, biome, found.Origin + offset)))
                        continue;

                    cell = candidate;
                    site = found;
                }
            });

            Assert.That(site, Is.Not.Null, "Precondition: no cell near the gate has a site whose footprint grows nothing.");
            var origin = site!.Value.Origin;

            await LoadChunks(pair, world.Ground, origin, origin);
            await server.WaitAssertion(() =>
            {
                var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(mouths.TryClaimCell(ground, cell!.Value), Is.EqualTo(WFCavernClaim.Deferred),
                        "A site under a loaded ground chunk was not deferred.");
                    Assert.That(ground.Item2.Mouths.Any(mouth => mouth.Origin == origin), Is.False, "The deferred site was stamped.");
                    Assert.That(ground.Item2.Shades.ContainsKey(origin), Is.False, "The deferred site has a shade.");
                }
            });

            await UnloadChunks(pair, world.Ground, origin, origin);
            await server.WaitAssertion(() =>
            {
                var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));

                Assert.That(mouths.TryClaimCell(ground, cell!.Value), Is.EqualTo(WFCavernClaim.Claimed),
                    "The site was not stamped once its chunk unloaded.");

                var mouth = ground.Item2.Mouths.Last();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(mouth.Origin, Is.EqualTo(origin), "The claim stamped another site than the one it found.");
                    Assert.That(mouth.Kind, Is.EqualTo(WFCavernMouthKind.Cell), "The claim stamped a mouth of another kind.");
                }
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A site on ground that has unloaded waits while a mob still stands on it. Once it is stamped, what was left
    /// lying over the hole falls through, though no tile changed to say the ground had gone.
    /// </summary>
    [Test]
    public async Task ClaimOnUnloadedGroundWaitsForMobsAndDropsWhatLiesThere()
    {
        const string surfaceId = "WFSurfaceMerak";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var mouths = server.System<WFCavernMouthSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, surfaceId);

        try
        {
            var spec = CavernOf(pair, surfaceId).Mouths;
            var gate = await Gate(pair, world);
            Vector2i? cell = null;
            WFCavernSite? site = null;

            await server.WaitPost(() =>
            {
                var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));
                var biome = entMan.GetComponent<BiomeComponent>(world.Ground);
                var first = WFCavernMouthSystem.CellOf(spec, gate.Origin);

                for (var dx = -3; dx <= 3 && site == null; dx++)
                for (var dy = -3; dy <= 3 && site == null; dy++)
                {
                    if (Math.Abs(dx) < 2 && Math.Abs(dy) < 2)
                        continue;

                    var candidate = first + new Vector2i(dx, dy);
                    if (mouths.EvaluateCell(ground, candidate) is not { } found
                        || found.Shape.Hole.Concat(found.Shape.Ring).Any(offset => GrowsEntity(biomes, biome, found.Origin + offset)))
                        continue;

                    cell = candidate;
                    site = found;
                }
            });

            Assert.That(site, Is.Not.Null, "Precondition: no cell near the gate has a site whose footprint grows nothing.");
            var origin = site!.Value.Origin;
            var from = origin + site.Value.Shape.Min - new Vector2i(2, 2);
            var to = origin + site.Value.Shape.Max + new Vector2i(2, 2);
            var item = EntityUid.Invalid;
            var mob = EntityUid.Invalid;

            await LoadChunks(pair, world.Ground, from, to);
            await server.WaitPost(() =>
            {
                item = entMan.SpawnEntity("Crowbar", new EntityCoordinates(world.Ground, TileCentre(origin)));
                mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(world.Ground, TileCentre(origin + site.Value.Shape.Ring.First())));
            });
            await server.WaitRunTicks(pair.SecondsToTicks(3f));
            await UnloadChunks(pair, world.Ground, from, to);
            await server.WaitRunTicks(pair.SecondsToTicks(1f));

            await server.WaitAssertion(() =>
            {
                var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(item).MapUid, Is.EqualTo(world.Ground),
                        "Precondition: the item fell through ground that had only unloaded.");
                    Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Ground),
                        "Precondition: the mob fell through ground that had only unloaded.");
                    Assert.That(mouths.TryClaimCell(ground, cell!.Value), Is.EqualTo(WFCavernClaim.Deferred),
                        "A site with a mob standing on it was not deferred.");
                    Assert.That(ground.Item2.Mouths.Any(mouth => mouth.Origin == origin), Is.False, "A mouth was stamped under a mob.");
                }
            });

            await server.WaitPost(() => entMan.DeleteEntity(mob));
            await server.WaitRunTicks(2);

            await server.WaitAssertion(() =>
            {
                var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));

                Assert.That(mouths.TryClaimCell(ground, cell!.Value), Is.EqualTo(WFCavernClaim.Claimed),
                    "The site was not stamped once the mob had gone.");
            });

            await server.WaitRunTicks(pair.SecondsToTicks(3f));

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(item).MapUid, Is.EqualTo(world.Cavern),
                    "An item left over a hole cut in unloaded ground did not fall through it.");
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A mouth cut through a rock outcrop takes the rock with it: rock the loader rolled on loaded ground, and rock a
    /// chunk's unload kept because someone had chipped it. Neither counts as built.
    /// </summary>
    [Test]
    public async Task OpenCutsThroughOutcropRockLoadedOrKept()
    {
        const string surfaceId = "WFSurfaceAsclepiu";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var mouths = server.System<WFCavernMouthSystem>();
        var maps = server.System<SharedMapSystem>();
        var from = new Vector2i(160, 0);
        var to = new Vector2i(160 + 95, 95);

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, surfaceId);

        try
        {
            await LoadChunks(pair, world.Ground, from, to);
            await server.WaitRunTicks(10);

            var rocks = new List<(EntityUid Uid, Vector2i Tile)>();

            await server.WaitAssertion(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var biome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));
                var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));

                // Well inside the patch, so that a whole mouth fits on it.
                for (var x = from.X + 16; x <= to.X - 16; x++)
                for (var y = from.Y + 16; y <= to.Y - 16; y++)
                {
                    var index = new Vector2i(x, y);

                    foreach (var uid in maps.GetAnchoredEntities(world.Ground, grid, index))
                    {
                        if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID.StartsWith("WallRock") == true
                            && biomes.WfIsBiomeSpawned(biome, uid, index))
                            rocks.Add((uid, index));
                    }
                }

                Assert.That(rocks.Count, Is.GreaterThan(40), "Precondition: too little outcrop rock here that the biome knows as its own.");
                Assert.That(OpenThroughRock(mouths, maps, entMan, ground, grid, rocks.Take(12).Select(rock => rock.Tile)), Is.True,
                    "No mouth could be cut through loaded outcrop rock.");

                // Chipped, so the unload keeps them.
                var chip = new DamageSpecifier();
                chip.DamageDict.Add("Blunt", 1);

                foreach (var (uid, _) in rocks.TakeLast(12))
                {
                    server.System<DamageableSystem>().TryChangeDamage(uid, chip, true);
                }
            });

            await server.WaitRunTicks(5);
            await UnloadChunks(pair, world.Ground, from, to);

            await server.WaitAssertion(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));
                var kept = rocks.TakeLast(12).Where(rock => !entMan.Deleted(rock.Uid)).Select(rock => rock.Tile).ToList();

                Assert.That(kept, Is.Not.Empty, "Precondition: the unload kept none of the chipped rock.");
                Assert.That(OpenThroughRock(mouths, maps, entMan, ground, grid, kept), Is.True,
                    "No mouth could be cut through rock an unload had kept.");
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Cuts an admin mouth at the first of some rock tiles that takes one, and checks its hole is left empty. Fails if
    /// any is refused as built.
    /// </summary>
    private static bool OpenThroughRock(WFCavernMouthSystem mouths, SharedMapSystem maps, IEntityManager entMan,
        (EntityUid, WFCavernGroundComponent) ground, MapGridComponent grid, IEnumerable<Vector2i> tiles)
    {
        foreach (var origin in tiles)
        {
            var shape = mouths.AdminShape(ground, origin);
            Assert.That(shape, Is.Not.Null, "Precondition: the cavern is gone.");

            if (!mouths.TryOpenMouth(ground, origin, out var refusal))
            {
                Assert.That(refusal, Is.Not.EqualTo("built"), $"A mouth at {origin} was refused over rock the biome grew.");
                continue;
            }

            foreach (var offset in shape!.Hole)
            {
                Assert.That(maps.GetAnchoredEntities(ground.Item1, grid, origin + offset).Select(uid => entMan.ToPrettyString(uid).ToString()),
                    Is.Empty, $"The mouth at {origin} was cut with something still standing in its hole.");
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// A cell that is all sea (Asclepiu's riverbed, Carcinoma's blood-sea floor) gets no mouth, and every mouth claimed
    /// around each world's gate has its hole and lip on the world's ground tiles, clear of anything to avoid.
    /// </summary>
    [Test]
    public async Task NoMouthOffTheAllowlist()
    {
        var seas = new Dictionary<string, string>
        {
            { "WFSurfaceAsclepiu", "FloorRiverbed" },
            { "WFSurfaceCarcinoma", "WFBloodOceanSeabed" },
        };

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var mouths = server.System<WFCavernMouthSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        Entity<MapGridComponent>? noGrid = null;

        await EnableCaverns(pair);

        foreach (var surfaceId in Surfaces)
        {
            var world = await BuildWorld(pair, surfaceId);

            try
            {
                var spec = CavernOf(pair, surfaceId).Mouths;
                var gate = await Gate(pair, world);

                await server.WaitAssertion(() =>
                {
                    var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));
                    var biome = entMan.GetComponent<BiomeComponent>(world.Ground);
                    var first = WFCavernMouthSystem.CellOf(spec, gate.Origin);

                    using (Assert.EnterMultipleScope())
                    {
                        if (seas.TryGetValue(surfaceId, out var sea))
                        {
                            var seaCell = FindSeaCell(biomes, tileDefs, biome, spec, first, sea);
                            Assert.That(seaCell, Is.Not.Null, $"Precondition: no {surfaceId} cell near the gate is all {sea}.");

                            if (seaCell is { } found)
                            {
                                Assert.That(mouths.EvaluateCell(ground, found), Is.Null, $"{surfaceId}: sea cell {found} has a site.");
                                Assert.That(mouths.TryClaimCell(ground, found), Is.EqualTo(WFCavernClaim.Empty),
                                    $"{surfaceId}: sea cell {found} is not Empty.");
                            }
                        }

                        for (var dx = -1; dx <= 1; dx++)
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            mouths.TryClaimCell(ground, first + new Vector2i(dx, dy));
                        }

                        var allowed = spec.GroundTiles.Select(tile => tile.Id).ToHashSet();
                        var avoid = spec.Avoid.Select(entity => entity.Id).ToHashSet();

                        foreach (var mouth in ground.Item2.Mouths)
                        {
                            foreach (var index in mouth.Footprint)
                            {
                                Assert.That(biomes.TryGetTile(index, biome.Layers, biome.Seed, noGrid, out var tile), Is.True,
                                    $"{surfaceId}: mouth tile {index} has no natural ground.");
                                if (tile == null)
                                    continue;

                                var id = tileDefs[tile.Value.TypeId].ID;
                                Assert.That(allowed, Does.Contain(id), $"{surfaceId}: a {mouth.Kind} mouth cuts {id} at {index}.");
                                Assert.That(biomes.TryGetEntity(index, biome.Layers, tile.Value, biome.Seed, noGrid, out var entity)
                                            && avoid.Contains(entity), Is.False,
                                    $"{surfaceId}: a {mouth.Kind} mouth cuts through {entity} at {index}.");
                            }
                        }

                        TestContext.Out.WriteLine($"{surfaceId}: {ground.Item2.Mouths.Count} mouths around the gate.");
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

    /// <summary>
    /// A viewer flying east over Merak on the first air layer claims the cells ahead through its eye on the ground: at
    /// the end every cell within reach is Claimed or Empty, and every cell mouth stands intact in the terrain loaded
    /// since. Logs what the claims cost.
    /// </summary>
    // From the air the viewer loads ground but no cavern, which keeps the test light.
    [Test]
    public async Task ClaimAheadOfViewer()
    {
        const string surfaceId = "WFSurfaceMerak";
        // Mid cell (0, 0), so only that column can be left Deferred by the arrival, and it is out of reach at the end.
        const int start = 48;
        const int step = 8;
        const int steps = 19;
        const float settle = 2.5f;

        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var mouths = server.System<WFCavernMouthSystem>();
        var transform = server.System<SharedTransformSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, surfaceId);

        try
        {
            var spec = CavernOf(pair, surfaceId).Mouths;

            // One claim far away first, so compiling the claim code isn't counted in its cost.
            await server.WaitPost(() =>
            {
                var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));
                for (var x = -8; x <= -6; x++)
                {
                    mouths.TryClaimCell(ground, new Vector2i(x, -8));
                }
            });

            var viewer = await PlanetFixture.AttachViewer(pair, world.Layers[1], new Vector2(start + 0.5f, 0.5f));
            await server.WaitPost(() =>
            {
                entMan.RemoveComponent<CEZPhysicsComponent>(viewer);
                mouths.ClaimStats.Reset();
            });

            for (var i = 1; i <= steps; i++)
            {
                var x = start + i * step + 0.5f;
                await server.WaitPost(() => transform.SetCoordinates(viewer, new EntityCoordinates(world.Layers[1], new Vector2(x, 0.5f))));
                await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            }

            await pair.RunTicksSync(pair.SecondsToTicks(settle));

            var end = start + steps * step;
            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var groundBiome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));
                var levelBiome = (world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern));
                var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                var min = WFCavernMouthSystem.CellOf(spec, new Vector2i(end - WFCavernMouthSystem.ClaimReach, -WFCavernMouthSystem.ClaimReach));
                var max = WFCavernMouthSystem.CellOf(spec, new Vector2i(end + WFCavernMouthSystem.ClaimReach, WFCavernMouthSystem.ClaimReach));
                var rim = spec.Rim.Select(entity => entity.Id).ToHashSet();
                var cellMouths = ground.Mouths.Where(mouth => mouth.Kind == WFCavernMouthKind.Cell).ToList();
                var stats = mouths.ClaimStats;
                var seconds = steps * 0.5f + settle;
                var flown = cellMouths.Where(mouth => mouth.Origin.Y >= -7 * spec.CellSize).ToList();
                var entities = flown.Sum(mouth => mouth.Size + mouth.Shape.Rim.Count + 1);

                TestContext.Out.WriteLine(
                    $"Claims over {seconds:F1} s: {stats.Stamped} mouths ({stats.Stamped / seconds:F2}/s), {stats.Candidates} candidates, " +
                    $"{stats.BusyTicks} busy ticks, {stats.TotalMs:F1} ms in all, {stats.TotalMs / Math.Max(1, stats.BusyTicks):F2} ms a busy tick, " +
                    $"{stats.MaxTickMs:F2} ms at most, {stats.StampMs / Math.Max(1, stats.Stamped):F2} ms a stamp; " +
                    $"{entities} entities over {flown.Count} cell mouths.");

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(viewer).MapUid, Is.EqualTo(world.Layers[1]),
                        "Precondition: the viewer left the air layer.");
                    Assert.That(biomes.WfIsChunkLoaded(groundBiome, new Vector2i(end, 0)), Is.True,
                        "Precondition: the ground under the viewer never loaded.");
                    Assert.That(flown, Is.Not.Empty, "Precondition: the flight claimed no cell mouth.");

                    for (var x = min.X; x <= max.X; x++)
                    for (var y = min.Y; y <= max.Y; y++)
                    {
                        var cell = new Vector2i(x, y);
                        var state = ground.Cells.TryGetValue(cell, out var found) ? found.State : WFCavernClaim.Unclaimed;
                        Assert.That(state, Is.EqualTo(WFCavernClaim.Claimed).Or.EqualTo(WFCavernClaim.Empty),
                            $"Cell {cell} within reach of the viewer is {state}.");
                    }

                    foreach (var mouth in flown)
                    {
                        foreach (var index in mouth.Hole)
                        {
                            Assert.That(maps.TryGetTileRef(world.Ground, groundGrid, index, out var tile) && !tile.Tile.IsEmpty, Is.False,
                                $"Hole tile {index} of the mouth at {mouth.Origin} is filled.");
                            Assert.That(biomes.WfIsPinned(groundBiome, index), Is.True, $"Hole tile {index} is not pinned.");
                            Assert.That(tileDefs[maps.GetTileRef(world.Cavern, levelGrid, index).Tile.TypeId].ID, Is.EqualTo(spec.LandingTile.Id),
                                $"The cavern under hole tile {index} is not the landing tile.");
                            Assert.That(biomes.WfIsPinned(levelBiome, index), Is.True, $"The landing under hole tile {index} is not pinned.");
                        }

                        foreach (var index in mouth.Ring)
                        {
                            Assert.That(maps.TryGetTileRef(world.Ground, groundGrid, index, out var tile) && !tile.Tile.IsEmpty, Is.True,
                                $"Lip tile {index} of the mouth at {mouth.Origin} is empty.");
                            Assert.That(biomes.WfIsPinned(groundBiome, index), Is.True, $"Lip tile {index} is not pinned.");
                        }

                        foreach (var index in mouth.Footprint)
                        {
                            foreach (var anchored in maps.GetAnchoredEntities(world.Ground, groundGrid, index))
                            {
                                Assert.That(rim, Does.Contain(entMan.GetComponent<MetaDataComponent>(anchored).EntityPrototype?.ID ?? string.Empty),
                                    $"{entMan.ToPrettyString(anchored)} stands in the footprint of the mouth at {mouth.Origin}.");
                            }
                        }
                    }
                }
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A ghost that loads no terrain claims nothing; an admin ghost claims the cells around it, but never stamps a
    /// mouth where its own chunks are about to load, even with nothing loaded yet.
    /// </summary>
    // The ground's loader is off, so only the claim's own guard can keep the site under the ghost Deferred.
    [TestCase("MobObserver", false)]
    [TestCase("AdminObserver", true)]
    public async Task GhostsClaimOnlyIfTheyLoadTerrain(string ghost, bool claims)
    {
        const string surfaceId = "WFSurfaceMerak";

        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var mouths = server.System<WFCavernMouthSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, surfaceId);

        try
        {
            var spec = CavernOf(pair, surfaceId).Mouths;
            Vector2i? cell = null;
            WFCavernSite? site = null;
            var before = new Dictionary<Vector2i, WFCavernClaim>();

            await server.WaitPost(() =>
            {
                biomes.SetEnabled((world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground)), false);

                var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));
                var first = WFCavernMouthSystem.CellOf(spec, new Vector2i(240, 0));

                for (var dx = 0; dx <= 2 && site == null; dx++)
                {
                    if (mouths.EvaluateCell(ground, first + new Vector2i(dx, 0)) is not { } found)
                        continue;

                    cell = first + new Vector2i(dx, 0);
                    site = found;
                }

                foreach (var (index, state) in ground.Item2.Cells)
                {
                    before[index] = state.State;
                }
            });

            Assert.That(site, Is.Not.Null, "Precondition: no cell east of the gate has a site.");

            var viewer = await PlanetFixture.AttachViewer(pair, world.Ground, TileCentre(site!.Value.Origin), ghost);
            await pair.RunTicksSync(pair.SecondsToTicks(1.5f));

            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var cellMouths = ground.Mouths.Where(mouth => mouth.Kind == WFCavernMouthKind.Cell).ToList();
                var reach = biomes.WfLoadRange + ChunkSize;
                var loading = Box2.CenteredAround(TileCentre(site.Value.Origin), new Vector2(reach * 2));

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(viewer).MapUid, Is.EqualTo(world.Ground),
                        "Precondition: the ghost left the ground.");

                    if (!claims)
                    {
                        Assert.That(cellMouths, Is.Empty, $"A {ghost} claimed cell mouths.");
                        Assert.That(ground.Cells.ToDictionary(entry => entry.Key, entry => entry.Value.State), Is.EquivalentTo(before),
                            $"A {ghost} changed cell claims.");
                        return;
                    }

                    Assert.That(cellMouths, Is.Not.Empty, $"An {ghost} claimed no cell mouth around it.");
                    Assert.That(ground.Cells[cell!.Value].State, Is.EqualTo(WFCavernClaim.Deferred),
                        $"The site under an arriving {ghost} was not deferred.");

                    foreach (var mouth in cellMouths)
                    {
                        var pad = new Box2(mouth.Min - new Vector2i(spec.PadRadius, spec.PadRadius),
                            mouth.Max + new Vector2i(spec.PadRadius + 1, spec.PadRadius + 1));
                        Assert.That(pad.Intersects(loading), Is.False,
                            $"The mouth at {mouth.Origin} was stamped where the {ghost}'s chunks are about to load.");
                    }
                }
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Whether the ground's biome grows an entity on a tile.</summary>
    private static bool GrowsEntity(BiomeSystem biomes, BiomeComponent biome, Vector2i index)
    {
        Entity<MapGridComponent>? noGrid = null;
        return biomes.TryGetTile(index, biome.Layers, biome.Seed, noGrid, out var tile)
               && biomes.TryGetEntity(index, biome.Layers, tile.Value, biome.Seed, noGrid, out _);
    }

    /// <summary>The nearest cell to a start cell whose natural ground, sampled every few tiles, is all one sea tile; searched outward ring by ring.</summary>
    private static Vector2i? FindSeaCell(BiomeSystem biomes, ITileDefinitionManager tileDefs, BiomeComponent biome, WFCavernMouthSpec spec,
        Vector2i from, string sea)
    {
        const int rings = 40;
        const int sampleStep = 6;
        Entity<MapGridComponent>? noGrid = null;

        bool IsSea(Vector2i index)
        {
            return biomes.TryGetTile(index, biome.Layers, biome.Seed, noGrid, out var tile) && tileDefs[tile.Value.TypeId].ID == sea;
        }

        for (var ring = 0; ring <= rings; ring++)
        {
            for (var dx = -ring; dx <= ring; dx++)
            for (var dy = -ring; dy <= ring; dy++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                    continue;

                var cell = from + new Vector2i(dx, dy);
                var origin = cell * spec.CellSize;
                if (!IsSea(origin + new Vector2i(spec.CellSize / 2, spec.CellSize / 2)))
                    continue;

                var all = true;
                for (var x = 0; x < spec.CellSize && all; x += sampleStep)
                for (var y = 0; y < spec.CellSize && all; y += sampleStep)
                {
                    all = IsSea(origin + new Vector2i(x, y));
                }

                if (all)
                    return cell;
            }
        }

        return null;
    }

    /// <summary>Fails on a hole out of range, split, pinched at a corner, spurred or enclosing ground, or on a bad lip, climb tile, rim spot or rim count.</summary>
    private static void AssertShape(WFCavernMouthSpec spec, WFCavernMouthShape shape, string name)
    {
        var hole = shape.Hole;

        Assert.That(hole.Count, Is.InRange(spec.MinTiles, spec.MaxTiles), $"{name}: {hole.Count} tiles is outside the size range.");
        Assert.That(hole, Does.Contain(Vector2i.Zero), $"{name}: the anchor is not a hole tile.");
        Assert.That(WFCavernMouthShape.Flood(hole, Vector2i.Zero, new HashSet<Vector2i>()), Has.Count.EqualTo(hole.Count),
            $"{name}: the hole is not joined edge to edge.");

        var ends = hole.Count(tile => WFCavernMouthShape.Neighbours(hole, tile) < 2);
        Assert.That(ends, Is.LessThanOrEqualTo(spec.Style == WFCavernMouthStyle.Rift ? 2 : 0),
            $"{name}: {ends} hole tiles hang on by one edge.");

        foreach (var tile in hole)
        {
            foreach (var diagonal in new[] { new Vector2i(1, 1), new Vector2i(-1, 1) })
            {
                Assert.That(hole.Contains(tile + diagonal) && !hole.Contains(tile + new Vector2i(diagonal.X, 0))
                            && !hole.Contains(tile + new Vector2i(0, 1)), Is.False,
                    $"{name}: {tile} and {tile + diagonal} touch only at a corner.");
            }
        }

        // Ground the hole encloses can't be reached from outside its bounds.
        var min = shape.Min - Vector2i.One;
        var max = shape.Max + Vector2i.One;
        var ground = new HashSet<Vector2i>();
        for (var x = min.X; x <= max.X; x++)
        for (var y = min.Y; y <= max.Y; y++)
        {
            if (!hole.Contains(new Vector2i(x, y)))
                ground.Add(new Vector2i(x, y));
        }

        Assert.That(WFCavernMouthShape.Flood(ground, min, new HashSet<Vector2i>()), Has.Count.EqualTo(ground.Count),
            $"{name}: the hole encloses ground.");

        var ring = new HashSet<Vector2i>();
        foreach (var tile in hole)
        {
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            {
                if (!hole.Contains(tile + new Vector2i(x, y)))
                    ring.Add(tile + new Vector2i(x, y));
            }
        }

        var step = spec.ClimbSide.ToIntVec();
        var climbReach = shape.Climb.X * step.X + shape.Climb.Y * step.Y;
        Assert.That(shape.Ring, Is.EquivalentTo(ring), $"{name}: the lip is not every tile touching the hole.");
        Assert.That(ring, Does.Contain(shape.Climb), $"{name}: the climb tile is off the lip.");
        Assert.That(hole, Does.Contain(shape.Climb - step), $"{name}: the climb tile is not beside the hole on the climb side.");
        Assert.That(hole.All(tile => tile.X * step.X + tile.Y * step.Y < climbReach), Is.True,
            $"{name}: the climb tile is not past the whole hole on the climb side.");

        var scaled = Math.Max(1, (int) MathF.Round(spec.RimCount * hole.Count / (float) spec.MaxTiles));
        Assert.That(shape.Rim, Has.Count.InRange(spec.RimCount > 0 ? 1 : 0, scaled),
            $"{name}: {shape.Rim.Count} rim spots for a hole of {hole.Count}.");

        foreach (var spot in shape.Rim)
        {
            Assert.That(ring, Does.Contain(spot), $"{name}: rim spot {spot} is off the lip.");
            Assert.That(Math.Max(Math.Abs(spot.X - shape.Climb.X), Math.Abs(spot.Y - shape.Climb.Y)), Is.GreaterThan(1),
                $"{name}: rim spot {spot} is on or beside the climb tile.");
        }
    }

    /// <summary>The gate's hole, lip, pad, shades and climb point are as section 3.3 stamps them.</summary>
    private static async Task AssertGateFitted(TestPair pair, World world, WFCavernPrototype cavern, WFCavernMouth gate, string surfaceId)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var spec = cavern.Mouths;

        await server.WaitAssertion(() =>
        {
            var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
            var groundBiome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));
            var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
            var levelBiome = (world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern));
            var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(gate.Kind, Is.EqualTo(WFCavernMouthKind.Gate), $"{surfaceId}: the gate has the wrong kind.");
                Assert.That(gate.Size, Is.InRange(spec.MinTiles, spec.MaxTiles), $"{surfaceId}: the gate hole is outside its size range.");
                Assert.That(ground.Mouths.Count(mouth => mouth.Kind == WFCavernMouthKind.Gate), Is.EqualTo(1),
                    $"{surfaceId}: the ground should have exactly one gate.");
                Assert.That(gate.Ring, Does.Contain(gate.ClimbTile), $"{surfaceId}: the climb tile is not on the lip.");
                Assert.That(gate.Hole, Does.Contain(gate.ClimbTile - spec.ClimbSide.ToIntVec()),
                    $"{surfaceId}: the climb tile is not beside the hole on the climb side.");

                foreach (var index in gate.Hole)
                {
                    Assert.That(biomes.WfIsPinned(groundBiome, index), Is.True, $"{surfaceId}: hole tile {index} is not pinned.");
                    Assert.That(TileAt(maps, world.Ground, groundGrid, index).IsEmpty, Is.True,
                        $"{surfaceId}: hole tile {index} is not empty.");
                    Assert.That(ground.Shades.TryGetValue(index, out var shade), Is.True, $"{surfaceId}: hole tile {index} has no shade.");

                    var xform = entMan.GetComponent<TransformComponent>(shade);
                    Assert.That(xform.MapUid, Is.EqualTo(world.Ground), $"{surfaceId}: the shade over {index} is not on the ground.");
                    Assert.That(xform.Anchored, Is.False, $"{surfaceId}: the shade over {index} is anchored.");
                    Assert.That(maps.TileIndicesFor(world.Ground, groundGrid, xform.Coordinates), Is.EqualTo(index),
                        $"{surfaceId}: the shade over {index} sits on another tile.");

                    var shaft = entMan.GetComponent<WFCavernShaftComponent>(shade);
                    Assert.That(shaft.Cavern?.Id, Is.EqualTo(cavern.ID), $"{surfaceId}: the shade names the wrong cavern.");
                    Assert.That(shaft.Air, Is.EqualTo(ExpectedAir[surfaceId]), $"{surfaceId}: the shade reports the wrong air.");
                    Assert.That(shaft.LandingMultiplier, Is.EqualTo(ExpectedLanding[surfaceId]).Within(0.001f),
                        $"{surfaceId}: the shade reports the wrong landing.");

                    var landing = TileAt(maps, world.Cavern, levelGrid, index);
                    Assert.That(tileDefs[landing.TypeId].ID, Is.EqualTo(spec.LandingTile.Id),
                        $"{surfaceId}: cavern tile {index} under the hole is not the landing tile.");

                    if (spec.LandingEntity is { } landingEntity)
                    {
                        Assert.That(maps.GetAnchoredEntities(world.Cavern, levelGrid, index)
                                .Count(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == landingEntity.Id), Is.EqualTo(1),
                            $"{surfaceId}: cavern tile {index} under the hole does not hold exactly one {landingEntity}.");
                    }
                }

                foreach (var index in gate.Ring)
                {
                    Assert.That(biomes.WfIsPinned(groundBiome, index), Is.True, $"{surfaceId}: lip tile {index} is not pinned.");
                    Assert.That(TileAt(maps, world.Ground, groundGrid, index).IsEmpty, Is.False,
                        $"{surfaceId}: lip tile {index} is empty.");
                }

                foreach (var index in gate.Pad(spec.PadRadius))
                {
                    Assert.That(biomes.WfIsPinned(levelBiome, index), Is.True, $"{surfaceId}: pad tile {index} is not pinned.");
                    Assert.That(TileAt(maps, world.Cavern, levelGrid, index).IsEmpty, Is.False,
                        $"{surfaceId}: pad tile {index} is empty.");
                }

                AssertPadPoolsKept(pair, world, gate, spec, surfaceId);

                var rimIds = spec.Rim.Select(rim => rim.Id).ToHashSet();
                foreach (var offset in gate.Shape.Rim)
                {
                    var index = gate.Origin + offset;
                    Assert.That(gate.Ring, Does.Contain(index), $"{surfaceId}: rim spot {index} is off the lip.");

                    if (rimIds.Count == 0)
                        continue;

                    Assert.That(maps.GetAnchoredEntities(world.Ground, groundGrid, index)
                            .Any(uid => rimIds.Contains(entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? string.Empty)),
                        Is.True, $"{surfaceId}: rim spot {index} has no rim decor.");
                }

                Assert.That(maps.GetAnchoredEntities(world.Ground, groundGrid, gate.ClimbTile), Is.Empty,
                    $"{surfaceId}: something stands on the climb tile.");

                Assert.That(ground.ClimbPoints.TryGetValue(gate.ClimbTile, out var climb), Is.True,
                    $"{surfaceId}: the gate has no climb point.");

                var climbXform = entMan.GetComponent<TransformComponent>(climb);
                Assert.That(climbXform.MapUid, Is.EqualTo(world.Cavern), $"{surfaceId}: the climb point is not in the cavern.");
                Assert.That(climbXform.Anchored, Is.True, $"{surfaceId}: the climb point is not anchored.");
                Assert.That(maps.TileIndicesFor(world.Cavern, levelGrid, climbXform.Coordinates), Is.EqualTo(gate.ClimbTile),
                    $"{surfaceId}: the climb point is not under the climb tile.");
                Assert.That(entMan.GetComponent<WFCavernClimbComponent>(climb).Delay, Is.EqualTo(ExpectedClimb[surfaceId]).Within(0.01f),
                    $"{surfaceId}: the climb point has the wrong climb time.");
                Assert.That(entMan.GetComponent<MetaDataComponent>(climb).EntityPrototype?.ID, Is.EqualTo(spec.ClimbPoint.Id),
                    $"{surfaceId}: the climb point is the wrong entity.");
            }
        });
    }

    /// <summary>Fails if anything but the climb point is anchored on the pad.</summary>
    private static void AssertPadClear(TestPair pair, World world, WFCavernPrototype cavern, WFCavernMouth gate, string surfaceId)
    {
        var entMan = pair.Server.EntMan;
        var maps = pair.Server.System<SharedMapSystem>();
        var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);
        var levelBiome = (world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern));
        var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
        var climb = ground.ClimbPoints[gate.ClimbTile];

        Assert.That(pair.Server.System<BiomeSystem>().WfIsChunkLoaded(levelBiome, gate.Origin), Is.True,
            $"Precondition: {surfaceId}'s cavern chunk under the gate never loaded.");

        using (Assert.EnterMultipleScope())
        {
            foreach (var index in gate.Pad(cavern.Mouths.PadRadius))
            {
                foreach (var anchored in maps.GetAnchoredEntities(world.Cavern, levelGrid, index))
                {
                    if (anchored == climb || IsLandingEntity(entMan, anchored, cavern.Mouths))
                        continue;

                    Assert.Fail($"{surfaceId}: {entMan.ToPrettyString(anchored)} stands on pad tile {index}.");
                }
            }
        }
    }

    /// <summary>Fails unless every pad tile off the hole where the cavern grows the landing entity holds exactly one; returns how many there are.</summary>
    private static int AssertPadPoolsKept(TestPair pair, World world, WFCavernMouth mouth, WFCavernMouthSpec spec, string name)
    {
        if (spec.LandingEntity is not { } landingEntity)
            return 0;

        var entMan = pair.Server.EntMan;
        var biomes = pair.Server.System<BiomeSystem>();
        var maps = pair.Server.System<SharedMapSystem>();
        var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);
        var levelBiome = entMan.GetComponent<BiomeComponent>(world.Cavern);
        Entity<MapGridComponent>? noGrid = null;
        var pools = 0;

        foreach (var index in mouth.Pad(spec.PadRadius))
        {
            if (mouth.Contains(index)
                || !biomes.TryGetTile(index, levelBiome.Layers, levelBiome.Seed, noGrid, out var natural)
                || !biomes.TryGetEntity(index, levelBiome.Layers, natural.Value, levelBiome.Seed, noGrid, out var entity)
                || entity != landingEntity.Id)
                continue;

            pools++;
            Assert.That(maps.GetAnchoredEntities(world.Cavern, levelGrid, index).Count(uid => IsLandingEntity(entMan, uid, spec)),
                Is.EqualTo(1), $"{name}: pool tile {index} on the pad does not hold exactly one {landingEntity}.");
        }

        return pools;
    }

    /// <summary>A map's tile at an index, empty where there is none.</summary>
    private static Tile TileAt(SharedMapSystem maps, EntityUid map, MapGridComponent grid, Vector2i index)
    {
        return maps.TryGetTileRef(map, grid, index, out var tile) ? tile.Tile : Tile.Empty;
    }

    /// <summary>Takes down the gate's tiles on both maps and the entities stamped with it.</summary>
    private static Snapshot Snap(TestPair pair, World world, WFCavernMouth gate, WFCavernMouthSpec spec)
    {
        var entMan = pair.Server.EntMan;
        var maps = pair.Server.System<SharedMapSystem>();
        var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
        var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);
        var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
        var snapshot = new Snapshot();

        foreach (var index in gate.Footprint)
        {
            snapshot.Ground[index] = TileAt(maps, world.Ground, groundGrid, index);

            foreach (var anchored in maps.GetAnchoredEntities(world.Ground, groundGrid, index))
            {
                snapshot.Rim.Add(anchored);
            }
        }

        foreach (var index in gate.Pad(spec.PadRadius))
        {
            snapshot.Pad[index] = TileAt(maps, world.Cavern, levelGrid, index);
        }

        foreach (var uid in ground.Shades.Values.Concat(ground.ClimbPoints.Values).Concat(snapshot.Rim))
        {
            snapshot.Entities[uid] = entMan.EntityExists(uid)
                ? entMan.GetComponent<TransformComponent>(uid).Coordinates
                : EntityCoordinates.Invalid;
        }

        return snapshot;
    }

    /// <summary>The gate's tiles and stamped entities at one moment.</summary>
    private sealed class Snapshot
    {
        public readonly Dictionary<Vector2i, Tile> Ground = new();
        public readonly Dictionary<Vector2i, Tile> Pad = new();
        public readonly List<EntityUid> Rim = new();
        public readonly Dictionary<EntityUid, EntityCoordinates> Entities = new();

        /// <summary>Fails on any tile or entity that differs from an earlier snapshot.</summary>
        public void AssertSame(Snapshot? before, string when)
        {
            Assert.That(before, Is.Not.Null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Ground, Is.EquivalentTo(before!.Ground), $"The gate's ground tiles changed {when}.");
                Assert.That(Pad, Is.EquivalentTo(before.Pad), $"The gate's pad tiles changed {when}.");
                Assert.That(Rim, Is.EquivalentTo(before.Rim), $"The gate's rim decor changed {when}.");
                Assert.That(Entities, Is.EquivalentTo(before.Entities), $"The gate's shades, climb point or rim moved {when}.");
            }
        }
    }
}
