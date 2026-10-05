#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Caverns;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Parallax;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._DV.Planet;
using Content.Shared._WF.Caverns;
using Content.Shared._WF.Planets;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>
/// The hole queue: any ground tile emptied on loaded terrain becomes a way down with a pinned landing, a shade and a
/// climb point, while unloads open nothing; the shovel shaft reaches ground no tool digs, and neither acid nor anything
/// else opens the cavern floor.
/// </summary>
[TestFixture]
[TestOf(typeof(WFCavernMouthSystem))]
public sealed class CavernHoleTest
{
    /// <summary>Merak ground a shovel empties in one dig.</summary>
    private static readonly string[] MerakSand = { "FloorAsteroidSandPlanet", "FloorAsteroidSandUnvariantizedPlanet" };

    /// <summary>Where the tests look for ground: east of the planet centre, clear of the gate.</summary>
    private static readonly Vector2i SearchFrom = new(150, -120);
    private static readonly Vector2i SearchTo = new(420, 120);

    private static readonly Entity<MapGridComponent>? NoGrid = null;

    /// <summary>
    /// Unloading loaded ground empties its tiles but opens no hole: no shade, no climb point, no pins on either map. A
    /// hole dug in the same tick, east of the unload so it sorts after all its tiles, is fitted out on the next update.
    /// </summary>
    [Test]
    public async Task UnloadDoesNotOpenHoles()
    {
        var from = new Vector2i(192, 192);
        var to = new Vector2i(215, 215);

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceMerak");

        try
        {
            Vector2i? found = null;
            await server.WaitPost(() =>
            {
                var groundBiome = entMan.GetComponent<BiomeComponent>(world.Ground);
                for (var x = to.X + 2 * ChunkSize; x < to.X + 6 * ChunkSize && found == null; x++)
                {
                    if (MerakSand.Any(id => IsClearGround(biomes, tileDefs, groundBiome, new Vector2i(x, from.Y), id)))
                        found = new Vector2i(x, from.Y);
                }
            });

            Assert.That(found, Is.Not.Null, "Precondition: no clear sand east of the unload.");
            var hole = found!.Value;

            await LoadChunks(pair, world.Ground, from, to);
            await LoadChunks(pair, world.Ground, hole, hole);

            var shades = 0;
            var climbs = 0;
            await server.WaitPost(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                shades = ground.Shades.Count;
                climbs = ground.ClimbPoints.Keys.Count(index => Chebyshev(index, hole) > WFCavernMouthSystem.ClimbReach);
            });

            await server.WaitPost(() =>
            {
                var biome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground), entMan.GetComponent<MapGridComponent>(world.Ground));
                foreach (var origin in ChunkOrigins(from, to))
                {
                    biomes.WfUnloadChunk(biome, origin);
                }

                maps.SetTile(world.Ground, biome.Item3, hole, Tile.Empty);
            });
            await server.WaitRunTicks(1);

            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var groundBiome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));
                var levelBiome = (world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern));
                var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                var emptied = 0;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(ground.Shades.ContainsKey(hole), Is.True, "The hole dug with the unload waited behind the unload's tiles.");
                    Assert.That(ground.Shades, Has.Count.EqualTo(shades + 1), "The unload gave shades to emptied ground.");
                    Assert.That(ground.ClimbPoints.Keys.Count(index => Chebyshev(index, hole) > WFCavernMouthSystem.ClimbReach), Is.EqualTo(climbs),
                        "The unload added climb points.");
                    Assert.That(ground.Opened, Is.Empty, "The hole queue still holds the unload's tiles.");

                    for (var x = from.X; x <= to.X; x++)
                    for (var y = from.Y; y <= to.Y; y++)
                    {
                        var index = new Vector2i(x, y);
                        if (IsEmpty(maps, world.Ground, groundGrid, index))
                        {
                            emptied++;
                            Assert.That(biomes.WfIsPinned(groundBiome, index), Is.False, $"The unload pinned emptied ground {index}.");
                        }

                        Assert.That(biomes.WfIsPinned(levelBiome, index), Is.False, $"The unload pinned cavern tile {index}.");
                        Assert.That(IsEmpty(maps, world.Cavern, levelGrid, index), Is.True, $"The unload filled cavern tile {index}.");
                    }

                    Assert.That(emptied, Is.GreaterThan(400), "Precondition: the unload emptied almost nothing.");
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
    /// Merak sand dug out from under a human over solid rock: within two ticks the hole is pinned with a shade, a
    /// cleared, pinned landing lies below and a climb point stands near it. The human lands unhurt enough on the
    /// landing and climbs back out; a shovel used on nearby sand digs it itself, not as a shaft.
    /// </summary>
    [Test]
    public async Task DugHoleGetsLandingShadeAndClimb()
    {
        const string surfaceId = "WFSurfaceMerak";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, surfaceId);

        try
        {
            var cavern = CavernOf(pair, surfaceId);
            var spot = await FindSpot(pair, world, MerakSand, 2, rockBelow: true);
            await LoadChunks(pair, world.Ground, spot - new Vector2i(ChunkSize, ChunkSize), spot + new Vector2i(ChunkSize, ChunkSize));
            await LoadChunks(pair, world.Cavern, spot - new Vector2i(ChunkSize, ChunkSize), spot + new Vector2i(ChunkSize, ChunkSize));

            await server.WaitAssertion(() =>
                Assert.That(maps.GetAnchoredEntities(world.Cavern, entMan.GetComponent<MapGridComponent>(world.Cavern), spot), Is.Not.Empty,
                    "Precondition: no rock stands in the cavern under the spot."));

            var mob = await SpawnAwake(pair, world.Ground, spot, "MobHuman");
            var climbs = await ClimbCount(pair, world);

            await Dig(pair, world, spot, "Shovel");
            await server.WaitRunTicks(2);

            var climb = EntityUid.Invalid;
            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var groundBiome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));
                var levelBiome = (world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern));
                var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(IsEmpty(maps, world.Ground, groundGrid, spot), Is.True, "Precondition: the dig left the sand.");
                    Assert.That(biomes.WfIsPinned(groundBiome, spot), Is.True, "The dug hole is not pinned.");
                    Assert.That(ground.Shades.TryGetValue(spot, out var shade), Is.True, "The dug hole has no shade.");

                    var shaft = entMan.GetComponent<WFCavernShaftComponent>(shade);
                    Assert.That(shaft.Cavern?.Id, Is.EqualTo(cavern.ID), "The shade names the wrong cavern.");
                    Assert.That(shaft.Air, Is.EqualTo(WFCavernAir.Breathable), "The shade reports the wrong air.");
                    Assert.That(shaft.LandingMultiplier, Is.EqualTo(0.5f).Within(0.001f), "The shade reports the wrong landing.");

                    Assert.That(tileDefs[maps.GetTileRef(world.Cavern, levelGrid, spot).Tile.TypeId].ID, Is.EqualTo(cavern.Mouths.LandingTile.Id),
                        "The cavern under the hole is not the landing tile.");

                    for (var x = -1; x <= 1; x++)
                    for (var y = -1; y <= 1; y++)
                    {
                        var index = spot + new Vector2i(x, y);
                        Assert.That(IsEmpty(maps, world.Cavern, levelGrid, index), Is.False, $"Landing tile {index} is empty.");
                        Assert.That(biomes.WfIsPinned(levelBiome, index), Is.True, $"Landing tile {index} is not pinned.");
                        Assert.That(maps.GetAnchoredEntities(world.Cavern, levelGrid, index)
                                .Where(uid => !entMan.HasComponent<WFCavernClimbComponent>(uid)), Is.Empty,
                            $"Something still stands on landing tile {index}.");
                    }

                    Assert.That(ground.ClimbPoints, Has.Count.EqualTo(climbs + 1), "The hole did not get exactly one climb point.");

                    var near = ground.ClimbPoints.Where(entry => Chebyshev(entry.Key, spot) <= WFCavernMouthSystem.ClimbReach).ToList();
                    Assert.That(near, Has.Count.EqualTo(1), "No climb point lies near the hole.");
                    if (near.Count == 0)
                        return;

                    var (index2, uid2) = near[0];
                    climb = uid2;
                    Assert.That(entMan.GetComponent<TransformComponent>(uid2).Anchored, Is.True, "The climb point is not anchored.");
                    Assert.That(IsEmpty(maps, world.Cavern, levelGrid, index2), Is.False, "The climb point stands on no floor.");
                    Assert.That(biomes.WfIsPinned(levelBiome, index2), Is.True, "The climb point's floor is not pinned.");
                    Assert.That(IsEmpty(maps, world.Ground, groundGrid, index2), Is.False, "The climb point lies under no ground.");
                    Assert.That(ground.Shades.ContainsKey(index2), Is.False, "The climb point lies under a hole.");
                }
            });

            Assert.That(await WaitForLanding(pair, world, mob, 120), Is.True, "The human never landed in the cavern.");

            await server.WaitAssertion(() =>
            {
                var physics = entMan.GetComponent<CEZPhysicsComponent>(mob);
                var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                var index = maps.TileIndicesFor(world.Cavern, levelGrid, entMan.GetComponent<TransformComponent>(mob).Coordinates);
                var damage = entMan.GetComponent<DamageableComponent>(mob).Damage.DamageDict.Where(entry => entry.Value > 0);
                TestContext.Out.WriteLine($"The fall dealt {string.Join(", ", damage.Select(entry => $"{entry.Value} {entry.Key}"))}.");

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<MobStateComponent>(mob).CurrentState, Is.EqualTo(MobState.Alive), "The fall hurt the human badly.");
                    Assert.That(physics.LocalPosition, Is.GreaterThan(-0.1f), "The human sank into the cavern floor.");
                    Assert.That(IsEmpty(maps, world.Cavern, levelGrid, index), Is.False, "The human came to rest over no floor.");
                }

                Assert.That(server.System<WFCavernClimbSystem>().ClimbUp(mob, climb), Is.EqualTo(WFCavernClimbResult.Climbed),
                    "The human could not climb out.");
                Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Ground), "The climb left the human below.");
            });

            // The shovel on sand it digs anyway: its own dig, not a shaft, and the queue fits the hole out as any other.
            var sand = spot + new Vector2i(0, 2);
            var digger = await SpawnAwake(pair, world.Ground, sand + new Vector2i(1, 0), "MobHuman");
            var doAfter = await UseShovelOn(pair, world, digger, sand);

            Assert.That(doAfter, Is.InstanceOf<TileToolDoAfterEvent>(), "The shovel started a shaft on sand it digs itself.");

            await server.WaitRunTicks(pair.SecondsToTicks(3f));
            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);

                Assert.That(ground.Shades.ContainsKey(sand), Is.True, "The shovelled sand became no hole.");
                Assert.That(ground.ClimbPoints, Has.Count.EqualTo(climbs + 1), "A hole beside a climb point got another.");
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Two holes dug 4 tiles apart over solid rock on Merak land in separate pockets, so the second gets its own climb
    /// point although the first's is within reach as the crow flies; each landing walks to one. A climb point or shade
    /// deleted by hand leaves the registry.
    /// </summary>
    [Test]
    public async Task SealedLandingGetsItsOwnClimb()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceMerak");

        try
        {
            // Clear sand over both holes and their neighbours, over rock that walls both landings in.
            Vector2i? found = null;
            await server.WaitPost(() =>
            {
                var ground = entMan.GetComponent<BiomeComponent>(world.Ground);
                var level = entMan.GetComponent<BiomeComponent>(world.Cavern);

                bool IsRock(Vector2i index) =>
                    biomes.TryGetTile(index, level.Layers, level.Seed, NoGrid, out var tile)
                    && biomes.TryGetEntity(index, level.Layers, tile.Value, level.Seed, NoGrid, out var rock)
                    && rock.StartsWith("WallRock");

                for (var x = SearchFrom.X; x < SearchTo.X && found == null; x += 2)
                for (var y = SearchFrom.Y; y < SearchTo.Y && found == null; y += 2)
                {
                    var at = new Vector2i(x, y);
                    if (Box(at - new Vector2i(1, 1), at + new Vector2i(5, 1))
                            .All(index => MerakSand.Any(id => IsClearGround(biomes, tileDefs, ground, index, id)))
                        && Box(at - new Vector2i(2, 2), at + new Vector2i(6, 2)).All(IsRock))
                        found = at;
                }
            });

            Assert.That(found, Is.Not.Null, "Precondition: no clear sand over solid rock east of the gate.");
            var first = found!.Value;
            var second = first + new Vector2i(4, 0);
            await LoadChunks(pair, world.Ground, first - new Vector2i(2, 2), second + new Vector2i(2, 2));
            await LoadChunks(pair, world.Cavern, first - new Vector2i(2, 2), second + new Vector2i(2, 2));

            var climbs = await ClimbCount(pair, world);
            await Dig(pair, world, first, "Shovel");
            await server.WaitRunTicks(2);
            await Dig(pair, world, second, "Shovel");
            await server.WaitRunTicks(2);

            var shaded = false;
            var climbsAfter = 0;
            int? firstSteps = null;
            int? secondSteps = null;
            await server.WaitPost(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                shaded = ground.Shades.ContainsKey(first) && ground.Shades.ContainsKey(second);
                climbsAfter = ground.ClimbPoints.Count;
                firstSteps = StepsToClimb(entMan, maps, world, first, 64);
                secondSteps = StepsToClimb(entMan, maps, world, second, 64);
            });

            // Asserted here rather than on the server thread, so a failure is not hidden by the teardown.
            using (Assert.EnterMultipleScope())
            {
                Assert.That(shaded, Is.True, "Precondition: a dug hole has no shade.");
                Assert.That(climbsAfter, Is.EqualTo(climbs + 2), "The two sealed landings did not get a climb point each.");
                Assert.That(firstSteps ?? int.MaxValue, Is.LessThanOrEqualTo(WFCavernMouthSystem.ClimbReach), "The first landing walks to no climb point.");
                Assert.That(secondSteps ?? int.MaxValue, Is.LessThanOrEqualTo(WFCavernMouthSystem.ClimbReach), "The second landing walks to no climb point.");
            }

            // Registry upkeep: an admin deleting the second hole's climb point and shade.
            await server.WaitPost(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var climb = ground.ClimbPoints.First(entry => Chebyshev(entry.Key, second) <= 1).Value;
                entMan.DeleteEntity(climb);
                entMan.DeleteEntity(ground.Shades[second]);
            });
            await server.WaitRunTicks(1);

            var shadeKept = true;
            await server.WaitPost(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                climbsAfter = ground.ClimbPoints.Count;
                shadeKept = ground.Shades.ContainsKey(second);
            });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(climbsAfter, Is.EqualTo(climbs + 1), "A deleted climb point stayed in the registry.");
                Assert.That(shadeKept, Is.False, "A deleted shade stayed in the registry.");
            }
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Lattice laid over a hole takes its shade and cutting it away brings a new one, with no second climb point; the
    /// record of what lay under lattice the hole replaced is gone, so the cut reopens the hole rather than plugging it.
    /// </summary>
    [Test]
    public async Task CoveredHoleLosesShade()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceMerak");

        try
        {
            var spot = await FindSpot(pair, world, MerakSand, 1);
            await LoadChunks(pair, world.Ground, spot, spot);

            // Lattice laid on the sand, as FloorTileSystem does it, then taken by an RCD, which leaves the hole.
            await server.WaitPost(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
                entMan.EnsureComponent<WFPlanetBuiltTilesComponent>(world.Ground).Underlay[spot] = maps.GetTileRef(world.Ground, grid, spot).Tile;
                maps.SetTile(world.Ground, grid, spot, new Tile(tileDefs["Lattice"].TileId));
            });
            await server.WaitRunTicks(1);
            await SetTile(pair, world.Ground, spot, Tile.Empty);
            await server.WaitRunTicks(2);

            var climbs = 0;
            var first = EntityUid.Invalid;
            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                climbs = ground.ClimbPoints.Count;

                Assert.That(ground.Shades.TryGetValue(spot, out first), Is.True, "Precondition: the hole got no shade.");
                Assert.That(entMan.GetComponent<WFPlanetBuiltTilesComponent>(world.Ground).Underlay.ContainsKey(spot), Is.False,
                    "The hole kept the record of the sand once under lattice.");
            });

            await server.WaitPost(() =>
                maps.SetTile(world.Ground, entMan.GetComponent<MapGridComponent>(world.Ground), spot, new Tile(tileDefs["Lattice"].TileId)));
            await server.WaitRunTicks(2);

            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(ground.Shades.ContainsKey(spot), Is.False, "Lattice over the hole left its shade.");
                    Assert.That(entMan.Deleted(first), Is.True, "The covered hole's shade was not deleted.");
                }
            });

            await Dig(pair, world, spot, "Wirecutter");
            await server.WaitRunTicks(2);

            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(IsEmpty(maps, world.Ground, entMan.GetComponent<MapGridComponent>(world.Ground), spot), Is.True,
                        "Cutting the lattice plugged the hole with the ground once under it.");
                    Assert.That(ground.Shades.ContainsKey(spot), Is.True, "The reopened hole has no shade.");
                    Assert.That(ground.ClimbPoints, Has.Count.EqualTo(climbs), "The reopened hole got another climb point.");
                }
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A blast on Aerumna's chromite opens holes, and every one of them is pinned with a shade, a pinned floor below and a climb point near it.</summary>
    [Test]
    public async Task ExplosionHolesAllGetLandings()
    {
        const int reach = 8;

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAerumna");

        try
        {
            var spot = await FindSpot(pair, world, new[] { "FloorChromite" }, 4);
            await LoadChunks(pair, world.Ground, spot - new Vector2i(2 * ChunkSize, 2 * ChunkSize), spot + new Vector2i(2 * ChunkSize, 2 * ChunkSize));

            var solid = new HashSet<Vector2i>();
            await server.WaitPost(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
                foreach (var index in Square(spot, reach))
                {
                    if (!IsEmpty(maps, world.Ground, grid, index))
                        solid.Add(index);
                }

                var mapId = entMan.GetComponent<MapComponent>(world.Ground).MapId;
                server.System<ExplosionSystem>().QueueExplosion(new MapCoordinates(TileCentre(spot), mapId), "Default", 150f, 10f, 30f, null);
            });

            await server.WaitRunTicks(pair.SecondsToTicks(3f));

            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var groundBiome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));
                var levelBiome = (world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern));
                var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                var opened = solid.Where(index => IsEmpty(maps, world.Ground, groundGrid, index)).ToList();

                TestContext.Out.WriteLine($"The blast opened {opened.Count} tiles.");
                Assert.That(opened, Is.Not.Empty, "Precondition: the blast opened no chromite.");

                using (Assert.EnterMultipleScope())
                {
                    foreach (var index in opened)
                    {
                        Assert.That(biomes.WfIsPinned(groundBiome, index), Is.True, $"Blast hole {index} is not pinned.");
                        Assert.That(ground.Shades.ContainsKey(index), Is.True, $"Blast hole {index} has no shade.");
                        Assert.That(IsEmpty(maps, world.Cavern, levelGrid, index), Is.False, $"Blast hole {index} has no floor below.");
                        Assert.That(biomes.WfIsPinned(levelBiome, index), Is.True, $"The floor under blast hole {index} is not pinned.");
                        Assert.That(StepsToClimb(entMan, maps, world, index, 64) ?? int.MaxValue, Is.LessThanOrEqualTo(WFCavernMouthSystem.ClimbReach),
                            $"The landing under blast hole {index} walks to no climb point.");
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

    /// <summary>Carcinoma's flesh pried to plating, axed to lattice and cut away opens a hole with a shade and a climb point.</summary>
    [Test]
    public async Task PriedFleshOpensHole()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceCarcinoma");

        try
        {
            var spot = await FindSpot(pair, world, new[] { "WFFloorFlesh" }, 1);
            await LoadChunks(pair, world.Ground, spot, spot);

            foreach (var (tool, left) in new[] { ("Crowbar", "Plating"), ("FireAxe", "Lattice"), ("Wirecutter", string.Empty) })
            {
                await Dig(pair, world, spot, tool);
                await server.WaitAssertion(() =>
                {
                    var tile = maps.GetTileRef(world.Ground, entMan.GetComponent<MapGridComponent>(world.Ground), spot).Tile;
                    var id = tile.IsEmpty ? string.Empty : tileDefs[tile.TypeId].ID;
                    Assert.That(id, Is.EqualTo(left), $"Precondition: the {tool} left {id} instead of {left}.");
                });
            }

            await server.WaitRunTicks(2);
            await AssertHoleFitted(pair, world, spot, "WFSurfaceCarcinoma");
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Thrascias snow dug twice is bedrock a shovel can only shaft; a blast opens it, and it becomes a hole.</summary>
    [Test]
    public async Task ThrasciasSnowDugThenBlown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var digs = server.System<WFCavernDigSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceThrascias");

        try
        {
            var spot = await FindSpot(pair, world, new[] { "FloorSnow" }, 1);
            await LoadChunks(pair, world.Ground, spot - new Vector2i(ChunkSize, ChunkSize), spot + new Vector2i(ChunkSize, ChunkSize));

            await Dig(pair, world, spot, "Shovel");
            await Dig(pair, world, spot, "Shovel");

            await server.WaitAssertion(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
                Entity<WFCavernGroundComponent, MapGridComponent> ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground), grid);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(tileDefs[maps.GetTileRef(world.Ground, grid, spot).Tile.TypeId].ID, Is.EqualTo("FloorBedrock"),
                        "Precondition: two digs did not leave bedrock.");
                    Assert.That(digs.CanDigShaft(ground, spot, out _), Is.True, "A shovel can't shaft the bedrock left by digging snow.");
                    Assert.That(digs.CanDigShaft(ground, spot + new Vector2i(1, 0), out _), Is.False,
                        "A shovel would shaft snow it digs itself.");
                }

                var mapId = entMan.GetComponent<MapComponent>(world.Ground).MapId;
                server.System<ExplosionSystem>().QueueExplosion(new MapCoordinates(TileCentre(spot), mapId), "Default", 30f, 30f, 30f, null);
            });

            await server.WaitRunTicks(pair.SecondsToTicks(3f));
            await AssertHoleFitted(pair, world, spot, "WFSurfaceThrascias");
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>An admin mouth cut into loaded Aerumna ground, a rift reaching past a hole's climb reach, adds only its own climb point.</summary>
    [Test]
    public async Task MouthStampAddsNoClimb()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var mouths = server.System<WFCavernMouthSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAerumna");

        try
        {
            Vector2i? origin = null;
            WFCavernMouthShape? shape = null;

            await server.WaitPost(() =>
            {
                var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));
                var groundBiome = entMan.GetComponent<BiomeComponent>(world.Ground);

                for (var x = SearchFrom.X; x < SearchTo.X && origin == null; x += 7)
                for (var y = SearchFrom.Y; y < SearchTo.Y && origin == null; y += 7)
                {
                    var candidate = new Vector2i(x, y);
                    var grown = mouths.AdminShape(ground, candidate)!;

                    // A rift long enough that a hole at its far end is past the climb reach, on clear chromite.
                    if (grown.Hole.Max(tile => Chebyshev(tile, grown.Climb)) <= WFCavernMouthSystem.ClimbReach
                        || !grown.Hole.Concat(grown.Ring).All(offset => IsClearGround(biomes, tileDefs, groundBiome, candidate + offset, "FloorChromite")))
                        continue;

                    origin = candidate;
                    shape = grown;
                }
            });

            Assert.That(origin, Is.Not.Null, "Precondition: no long rift fits on clear chromite.");
            var at = origin!.Value;
            await LoadChunks(pair, world.Ground, at + shape!.Min - new Vector2i(2, 2), at + shape.Max + new Vector2i(2, 2));

            var climbs = 0;
            var shades = 0;
            var size = 0;
            string? refused = null;
            await server.WaitPost(() =>
            {
                var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));
                climbs = ground.Item2.ClimbPoints.Count;
                shades = ground.Item2.Shades.Count;

                if (mouths.TryOpenMouth(ground, at, out refused))
                    size = ground.Item2.Mouths.Last().Size;
            });

            Assert.That(refused, Is.Null, $"Precondition: the mouth was refused: {refused}.");

            await server.WaitRunTicks(3);

            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(ground.ClimbPoints, Has.Count.EqualTo(climbs + 1), "The loaded mouth got climb points besides its own.");
                    Assert.That(ground.Shades, Has.Count.EqualTo(shades + size), "The loaded mouth has a shade count other than its size.");
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
    /// A shovel used on Fervidus basalt, which no tool digs, starts a slow shaft that opens it into a fitted hole; a
    /// mouth's lip and a built floor can't be shafted.
    /// </summary>
    [Test]
    public async Task ShovelShaftOnBasalt()
    {
        const string surfaceId = "WFSurfaceFervidus";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var digs = server.System<WFCavernDigSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, surfaceId);

        try
        {
            var cavern = CavernOf(pair, surfaceId);
            var gate = await Gate(pair, world);
            var spot = await FindSpot(pair, world, new[] { "FloorBasalt" }, 2);
            var built = spot + new Vector2i(0, 2);
            await LoadChunks(pair, world.Ground, spot - new Vector2i(ChunkSize, ChunkSize), spot + new Vector2i(ChunkSize, ChunkSize));
            await SetTile(pair, world.Ground, built, new Tile(tileDefs["FloorSteel"].TileId));

            await server.WaitAssertion(() =>
            {
                Entity<WFCavernGroundComponent, MapGridComponent> ground =
                    (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground), entMan.GetComponent<MapGridComponent>(world.Ground));

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(digs.CanDigShaft(ground, spot, out _), Is.True, "A shovel can't shaft clear basalt.");
                    Assert.That(digs.CanDigShaft(ground, gate.Ring.First(), out _), Is.False, "A shovel can shaft a mouth's lip.");
                    Assert.That(digs.CanDigShaft(ground, built, out _), Is.False, "A shovel can shaft a built floor.");
                }
            });

            var digger = await SpawnAwake(pair, world.Ground, spot + new Vector2i(1, 0), "MobHuman");
            var doAfter = await UseShovelOn(pair, world, digger, spot);

            Assert.That(doAfter, Is.InstanceOf<WFCavernShaftDigDoAfterEvent>(), "The shovel started no shaft on basalt.");
            Assert.That(((WFCavernShaftDigDoAfterEvent) doAfter!).Tile, Is.EqualTo(spot), "The shaft is dug somewhere else.");

            await server.WaitAssertion(() =>
            {
                var delay = entMan.GetComponent<DoAfterComponent>(digger).DoAfters.Values.Single().Args.Delay;
                Assert.That(delay.TotalSeconds, Is.EqualTo(cavern.Mouths.ShaftSeconds).Within(0.5),
                    "A standard shovel's shaft does not take the world's shaft time.");
            });

            await server.WaitRunTicks(pair.SecondsToTicks(cavern.Mouths.ShaftSeconds / 2));
            await server.WaitAssertion(() =>
                Assert.That(IsEmpty(maps, world.Ground, entMan.GetComponent<MapGridComponent>(world.Ground), spot), Is.False,
                    "The shaft opened long before its time."));

            await server.WaitRunTicks(pair.SecondsToTicks(cavern.Mouths.ShaftSeconds / 2 + 1));
            await AssertHoleFitted(pair, world, spot, surfaceId);
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Xeno acid spilled on Aerumna's chromite leaves it whole, while the same acid pries up a steel floor laid beside it.</summary>
    [Test]
    public async Task AcidDoesNotOpenChromite()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAerumna");

        try
        {
            var spot = await FindSpot(pair, world, new[] { "FloorChromite" }, 1);
            var floor = spot + new Vector2i(0, 1);
            await LoadChunks(pair, world.Ground, spot - Vector2i.One, spot + Vector2i.One);
            await SetTile(pair, world.Ground, floor, new Tile(tileDefs["FloorSteel"].TileId));

            await Spill(pair, world.Ground, spot);
            await Spill(pair, world.Ground, floor);
            await server.WaitRunTicks(2);

            await server.WaitAssertion(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(tileDefs[maps.GetTileRef(world.Ground, grid, floor).Tile.TypeId].ID, Is.EqualTo("Plating"),
                        "Precondition: the acid pried nothing, not even a steel floor.");
                    Assert.That(IsEmpty(maps, world.Ground, grid, spot), Is.False, "The acid opened the chromite.");
                    Assert.That(ground.Shades.ContainsKey(spot), Is.False, "The chromite under the acid became a hole.");
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
    /// Every cavern floor has no base turf, so acid can't open it, and a cavern tile emptied some other way, lattice
    /// laid on the floor and then taken by an RCD, is filled again with the floor under the lattice.
    /// </summary>
    [Test]
    public async Task CavernFloorCannotBeOpened()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var proto = server.ResolveDependency<IPrototypeManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var biomes = server.System<BiomeSystem>();

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var cavern in proto.EnumeratePrototypes<WFCavernPrototype>())
                {
                    var tiles = new HashSet<string>();
                    CavernPrototypeTest.Walk(proto, proto.Index<PlanetPrototype>(cavern.Level).Biome, new List<string>(), tiles, new HashSet<string>());
                    tiles.Add(cavern.Mouths.LandingTile);

                    foreach (var id in tiles)
                    {
                        Assert.That(((ContentTileDefinition) tileDefs[id]).BaseTurf, Is.Empty, $"{cavern.ID}: {id} breaks down to another tile.");
                    }
                }
            }
        });

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAerumna");

        try
        {
            Vector2i? found = null;
            await server.WaitPost(() =>
            {
                var level = entMan.GetComponent<BiomeComponent>(world.Cavern);
                for (var x = SearchFrom.X; x < SearchTo.X && found == null; x += 3)
                {
                    var index = new Vector2i(x, 0);
                    if (biomes.TryGetTile(index, level.Layers, level.Seed, NoGrid, out var tile)
                        && tileDefs[tile.Value.TypeId].ID == "WFCavernFloorChromite"
                        && !biomes.TryGetEntity(index, level.Layers, tile.Value, level.Seed, NoGrid, out _))
                        found = index;
                }
            });

            Assert.That(found, Is.Not.Null, "Precondition: no open chromite floor in the cavern.");
            var spot = found!.Value;
            await LoadChunks(pair, world.Cavern, spot, spot);

            var before = Tile.Empty;
            await server.WaitPost(() => before = maps.GetTileRef(world.Cavern, entMan.GetComponent<MapGridComponent>(world.Cavern), spot).Tile);
            await Spill(pair, world.Cavern, spot);
            await server.WaitRunTicks(1);

            await server.WaitAssertion(() =>
                Assert.That(maps.GetTileRef(world.Cavern, entMan.GetComponent<MapGridComponent>(world.Cavern), spot).Tile.TypeId,
                    Is.EqualTo(before.TypeId), "Acid opened the cavern floor."));

            // Lattice laid on the floor, as FloorTileSystem does on a planet layer, then taken by an RCD.
            await server.WaitPost(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                entMan.EnsureComponent<WFPlanetBuiltTilesComponent>(world.Cavern).Underlay[spot] = before;
                maps.SetTile(world.Cavern, grid, spot, new Tile(tileDefs["Lattice"].TileId));
                maps.SetTile(world.Cavern, grid, spot, Tile.Empty);
            });
            await server.WaitRunTicks(2);

            await server.WaitAssertion(() =>
            {
                var tile = maps.GetTileRef(world.Cavern, entMan.GetComponent<MapGridComponent>(world.Cavern), spot).Tile;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(tile.IsEmpty, Is.False, "The cavern floor stayed open under the lattice the RCD took.");
                    Assert.That(tile.TypeId, Is.EqualTo(before.TypeId), "The cavern floor came back as another tile.");
                    Assert.That(entMan.GetComponent<WFPlanetBuiltTilesComponent>(world.Cavern).Underlay.ContainsKey(spot), Is.False,
                        "The record of the floor under the lattice outlived it.");
                }
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A ground tile that is its natural tile, one of the given ids, with no natural entity; checked on the server thread.</summary>
    /// <summary>A hole opened over floor someone laid in the cavern leaves that floor: under the hole and around it.</summary>
    [Test]
    public async Task AHoleLeavesFloorBuiltBelowIt()
    {
        const string Floor = "Plating";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, "WFSurfaceMerak");

        try
        {
            var spot = await FindSpot(pair, world, MerakSand, 2);
            await LoadChunks(pair, world.Ground, spot - new Vector2i(8, 8), spot + new Vector2i(8, 8));
            await LoadChunks(pair, world.Cavern, spot - new Vector2i(8, 8), spot + new Vector2i(8, 8));

            // Laid on a loaded chunk, as a player lays it: nothing pins it.
            var floor = new Tile(tileDefs[Floor].TileId);
            foreach (var index in Square(spot, 1))
            {
                await SetTile(pair, world.Cavern, index, floor);
            }

            await SetTile(pair, world.Ground, spot, Tile.Empty);
            await server.WaitRunTicks(3);

            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(ground.Shades.ContainsKey(spot), Is.True, "Precondition: the hole was not fitted out.");

                    foreach (var index in Square(spot, 1))
                    {
                        var tile = maps.GetTileRef(world.Cavern, levelGrid, index).Tile;
                        Assert.That(tile.IsEmpty ? string.Empty : tileDefs[tile.TypeId].ID, Is.EqualTo(Floor),
                            $"The hole at {spot} relaid the floor built at {index} in the cavern.");
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

    private static bool IsClearGround(BiomeSystem biomes, ITileDefinitionManager tileDefs, BiomeComponent biome, Vector2i index, string tileId)
    {
        return biomes.TryGetTile(index, biome.Layers, biome.Seed, NoGrid, out var tile)
               && tileDefs[tile.Value.TypeId].ID == tileId
               && !biomes.TryGetEntity(index, biome.Layers, tile.Value, biome.Seed, NoGrid, out _);
    }

    /// <summary>
    /// A ground tile east of the gate whose natural tile, like every other within <paramref name="clear"/> tiles, is one
    /// of <paramref name="tiles"/> with no natural entity, optionally with rock in the cavern below it.
    /// </summary>
    private static async Task<Vector2i> FindSpot(TestPair pair, World world, IReadOnlyCollection<string> tiles, int clear, bool rockBelow = false)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        Vector2i? found = null;

        await server.WaitPost(() =>
        {
            var ground = entMan.GetComponent<BiomeComponent>(world.Ground);
            var level = entMan.GetComponent<BiomeComponent>(world.Cavern);

            for (var x = SearchFrom.X; x < SearchTo.X && found == null; x += 2)
            for (var y = SearchFrom.Y; y < SearchTo.Y && found == null; y += 2)
            {
                var index = new Vector2i(x, y);

                if (!biomes.TryGetTile(index, ground.Layers, ground.Seed, NoGrid, out var tile) || !tiles.Contains(tileDefs[tile.Value.TypeId].ID))
                    continue;

                if (rockBelow
                    && (!biomes.TryGetTile(index, level.Layers, level.Seed, NoGrid, out var below)
                        || !biomes.TryGetEntity(index, level.Layers, below.Value, level.Seed, NoGrid, out _)))
                    continue;

                var clearHere = true;
                foreach (var near in Square(index, clear))
                {
                    if (biomes.TryGetTile(near, ground.Layers, ground.Seed, NoGrid, out var nearTile)
                        && tiles.Contains(tileDefs[nearTile.Value.TypeId].ID)
                        && !biomes.TryGetEntity(near, ground.Layers, nearTile.Value, ground.Seed, NoGrid, out _))
                        continue;

                    clearHere = false;
                    break;
                }

                if (clearHere)
                    found = index;
            }
        });

        Assert.That(found, Is.Not.Null, $"Precondition: no clear {string.Join(" or ", tiles)} ground east of the gate.");
        return found!.Value;
    }

    /// <summary>Takes a ground tile apart once with a fresh tool of this prototype, as its DoAfter would on completion.</summary>
    private static async Task Dig(TestPair pair, World world, Vector2i index, string toolProto)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        var done = false;

        await server.WaitPost(() =>
        {
            var tool = entMan.SpawnEntity(toolProto, new EntityCoordinates(world.Ground, TileCentre(index) + new Vector2(0, 8)));
            var tileRef = maps.GetTileRef(world.Ground, entMan.GetComponent<MapGridComponent>(world.Ground), index);

            done = server.System<SharedToolSystem>().TryDeconstructWithToolQualities(tileRef, entMan.GetComponent<ToolComponent>(tool).Qualities);
            entMan.DeleteEntity(tool);
        });

        Assert.That(done, Is.True, $"Precondition: the {toolProto} could not take apart {index}.");
        await server.WaitRunTicks(1);
    }

    /// <summary>Gives a mob a shovel and has it use the shovel on a ground tile; returns the tile DoAfter's event, or null.</summary>
    private static async Task<DoAfterEvent?> UseShovelOn(TestPair pair, World world, EntityUid user, Vector2i index)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        DoAfterEvent? started = null;
        var held = false;

        await server.WaitPost(() =>
        {
            var shovel = entMan.SpawnEntity("Shovel", entMan.GetComponent<TransformComponent>(user).Coordinates);
            held = server.System<SharedHandsSystem>().TryPickup(user, shovel);
            if (held)
                server.System<SharedInteractionSystem>().UserInteraction(user, new EntityCoordinates(world.Ground, TileCentre(index)), null);
        });

        Assert.That(held, Is.True, "Precondition: the mob can't hold the shovel.");

        await server.WaitRunTicks(1);
        await server.WaitPost(() =>
        {
            if (!entMan.TryGetComponent(user, out DoAfterComponent? doAfters))
                return;

            foreach (var doAfter in doAfters.DoAfters.Values)
            {
                started = doAfter.Args.Event is SharedToolSystem.ToolDoAfterEvent tool ? tool.WrappedEvent : doAfter.Args.Event;
            }
        });

        return started;
    }

    /// <summary>Spawns a mob on a tile's centre, lets it settle and wakes its z-physics, so the ground going drops it.</summary>
    private static async Task<EntityUid> SpawnAwake(TestPair pair, EntityUid map, Vector2i index, string proto)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mob = EntityUid.Invalid;

        await server.WaitPost(() => mob = entMan.SpawnEntity(proto, new EntityCoordinates(map, TileCentre(index))));
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitPost(() => server.System<CEZLevelsSystem>().WakeBody(mob));
        return mob;
    }

    /// <summary>Spills a little fluorosulfuric acid, the acid xenos bleed, on a tile.</summary>
    private static async Task Spill(TestPair pair, EntityUid map, Vector2i index)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await server.WaitPost(() =>
        {
            var tileRef = maps.GetTileRef(map, entMan.GetComponent<MapGridComponent>(map), index);
            server.System<PuddleSystem>().TrySpillAt(tileRef, new Solution("FluorosulfuricAcid", FixedPoint2.New(10)), out _);
        });
    }

    /// <summary>Sets one tile of a map.</summary>
    private static async Task SetTile(TestPair pair, EntityUid map, Vector2i index, Tile tile)
    {
        var server = pair.Server;

        await server.WaitPost(() =>
            server.System<SharedMapSystem>().SetTile(map, server.EntMan.GetComponent<MapGridComponent>(map), index, tile));
        await server.WaitRunTicks(1);
    }

    /// <summary>How many climb points the ground has.</summary>
    private static async Task<int> ClimbCount(TestPair pair, World world)
    {
        var count = 0;
        await pair.Server.WaitPost(() => count = pair.Server.EntMan.GetComponent<WFCavernGroundComponent>(world.Ground).ClimbPoints.Count);
        return count;
    }

    /// <summary>Fails unless a ground tile is an empty, pinned hole with a shade, the world's landing tile pinned below it and a climb point near it.</summary>
    private static async Task AssertHoleFitted(TestPair pair, World world, Vector2i index, string surfaceId)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var landing = CavernOf(pair, surfaceId).Mouths.LandingTile.Id;

        await server.WaitAssertion(() =>
        {
            var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
            var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
            var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);
            var below = maps.GetTileRef(world.Cavern, levelGrid, index).Tile;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(IsEmpty(maps, world.Ground, groundGrid, index), Is.True, $"{surfaceId}: {index} did not open.");
                Assert.That(biomes.WfIsPinned((world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground)), index), Is.True,
                    $"{surfaceId}: hole {index} is not pinned.");
                Assert.That(ground.Shades.ContainsKey(index), Is.True, $"{surfaceId}: hole {index} has no shade.");
                Assert.That(below.IsEmpty ? string.Empty : tileDefs[below.TypeId].ID, Is.EqualTo(landing),
                    $"{surfaceId}: the cavern under hole {index} is not the landing tile.");
                Assert.That(biomes.WfIsPinned((world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern)), index), Is.True,
                    $"{surfaceId}: the landing under hole {index} is not pinned.");
                Assert.That(StepsToClimb(entMan, maps, world, index, 64) ?? int.MaxValue, Is.LessThanOrEqualTo(WFCavernMouthSystem.ClimbReach),
                    $"{surfaceId}: the landing under hole {index} walks to no climb point.");
            }
        });
    }

    /// <summary>Ticks until the entity stands still on the cavern floor, or the budget runs out.</summary>
    private static async Task<bool> WaitForLanding(TestPair pair, World world, EntityUid uid, int ticks)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var landed = false;

        for (var tick = 0; tick < ticks && !landed; tick++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() =>
            {
                var physics = entMan.GetComponent<CEZPhysicsComponent>(uid);
                landed = entMan.GetComponent<TransformComponent>(uid).MapUid == world.Cavern
                         && physics.Velocity == 0f
                         && physics.LocalPosition < 0.05f;
            });
        }

        await server.WaitRunTicks(5);
        return landed;
    }

    /// <summary>
    /// Steps across cavern floor with nothing hard anchored on it from the tile under a hole to the nearest climb point,
    /// or null if none is within the limit.
    /// </summary>
    private static int? StepsToClimb(IEntityManager entMan, SharedMapSystem maps, World world, Vector2i from, int limit)
    {
        var climbs = entMan.GetComponent<WFCavernGroundComponent>(world.Ground).ClimbPoints;
        var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);
        var steps = new Dictionary<Vector2i, int> { [from] = 0 };
        var frontier = new Queue<Vector2i>();
        frontier.Enqueue(from);

        while (frontier.TryDequeue(out var index))
        {
            if (climbs.ContainsKey(index))
                return steps[index];

            if (steps[index] >= limit)
                continue;

            foreach (var side in new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
            {
                var next = index + side;
                if (steps.ContainsKey(next)
                    || IsEmpty(maps, world.Cavern, grid, next)
                    || maps.GetAnchoredEntities(world.Cavern, grid, next)
                        .Any(uid => entMan.TryGetComponent(uid, out PhysicsComponent? body) && body.CanCollide && body.Hard))
                    continue;

                steps[next] = steps[index] + 1;
                frontier.Enqueue(next);
            }
        }

        return null;
    }

    /// <summary>Every tile in a rectangle, corners included.</summary>
    private static IEnumerable<Vector2i> Box(Vector2i from, Vector2i to)
    {
        for (var x = from.X; x <= to.X; x++)
        for (var y = from.Y; y <= to.Y; y++)
        {
            yield return new Vector2i(x, y);
        }
    }

    /// <summary>Every tile within a reach of a centre, in either axis.</summary>
    private static IEnumerable<Vector2i> Square(Vector2i centre, int reach)
    {
        for (var x = -reach; x <= reach; x++)
        for (var y = -reach; y <= reach; y++)
        {
            yield return centre + new Vector2i(x, y);
        }
    }

    /// <summary>The larger of the two axis distances between tiles.</summary>
    private static int Chebyshev(Vector2i a, Vector2i b)
    {
        return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    /// <summary>Whether a map has no tile at an index.</summary>
    private static bool IsEmpty(SharedMapSystem maps, EntityUid map, MapGridComponent grid, Vector2i index)
    {
        return !maps.TryGetTileRef(map, grid, index, out var tile) || tile.Tile.IsEmpty;
    }
}
