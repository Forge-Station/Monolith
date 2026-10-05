#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Caverns;
using Content.Server._WF.Planets;
using Content.Server.Parallax;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>Ground that unloads under an animal still holds it up; wildlife that falls through a hole into the cavern stays.</summary>
[TestFixture]
[TestOf(typeof(WFCavernSystem))]
public sealed class CavernWildlifeTest
{
    /// <summary>Stands in for an animal; any unanchored mob with z-physics falls the same way.</summary>
    private const string MobProto = "MobHuman";

    /// <summary>The tile the wildlife stands on, well away from the planet centre.</summary>
    private static readonly Vector2i Spot = new(204, 204);

    /// <summary>The patch of ground loaded around it: the spot's chunk and its neighbours.</summary>
    private static readonly Vector2i From = Spot - new Vector2i(ChunkSize, ChunkSize);
    private static readonly Vector2i To = Spot + new Vector2i(ChunkSize, ChunkSize);

    /// <summary>An awake mob whose ground chunk unloads stays standing on it, wildlife or not: unloaded ground is still ground.</summary>
    [Test]
    public async Task MobsOverUnloadedGroundStayOnIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");
        await LoadChunks(pair, world.Ground, From, To);

        var animal = await SpawnMob(pair, world, Spot, wildlife: true);
        var control = await SpawnMob(pair, world, Spot + new Vector2i(1, 0), wildlife: false);
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(animal).MapUid, Is.EqualTo(world.Ground),
                    "Precondition: the animal is not standing on the ground.");
                Assert.That(entMan.GetComponent<TransformComponent>(control).MapUid, Is.EqualTo(world.Ground),
                    "Precondition: the control mob is not standing on the ground.");
            }
        });

        await UnloadChunks(pair, world.Ground, From, To);
        await Wake(pair, animal, control);
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(world.Ground);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(server.System<SharedMapSystem>().GetTileRef(world.Ground, grid, Spot).Tile.IsEmpty, Is.True,
                    "Precondition: the ground under the animal did not unload.");

                foreach (var mob in new[] { animal, control })
                {
                    Assert.That(entMan.Deleted(mob) || entMan.IsQueuedForDeletion(mob), Is.False,
                        "A mob over unloaded ground was deleted.");
                    Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Ground),
                        "A mob over unloaded ground fell through it.");
                    Assert.That(entMan.GetComponent<CEZPhysicsComponent>(mob).LocalPosition, Is.EqualTo(0f).Within(0.01f),
                        "A mob over unloaded ground is not standing on it.");
                }
            }
        });

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>An animal that falls through a pinned hole into the cavern is kept, even with its chunk unloaded.</summary>
    [Test]
    public async Task WildlifeThroughPinnedHoleIsKept()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");
        await LoadChunks(pair, world.Ground, From, To);

        var animal = await SpawnMob(pair, world, Spot, wildlife: true);
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        // Pinned, so the unload keeps its tile and the animal on it; the hole is opened by hand after.
        await server.WaitPost(() =>
            biomes.WfPinTiles((world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground)), new[] { Spot }));
        await UnloadChunks(pair, world.Ground, From, To);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<TransformComponent>(animal).MapUid, Is.EqualTo(world.Ground),
                "Precondition: the animal on its pinned tile fell when the chunk unloaded."));

        await server.WaitPost(() =>
            maps.SetTile(world.Ground, entMan.GetComponent<MapGridComponent>(world.Ground), Spot, Tile.Empty));
        await Wake(pair, animal);
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(animal) || entMan.IsQueuedForDeletion(animal), Is.False,
                "Wildlife that fell through a pinned hole was deleted.");
            Assert.That(entMan.GetComponent<TransformComponent>(animal).MapUid, Is.EqualTo(world.Cavern),
                "Precondition: the animal did not fall through the hole into the cavern.");
        });

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>An animal that falls through a hole opened in a loaded chunk is kept, and lands on the hole's landing.</summary>
    // The hole queue pins the hole and lays its landing within a tick, so either check keeps the animal.
    [Test]
    public async Task WildlifeThroughLoadedHoleIsKept()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");
        await LoadChunks(pair, world.Ground, From, To);

        var animal = await SpawnMob(pair, world, Spot, wildlife: true);
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<TransformComponent>(animal).MapUid, Is.EqualTo(world.Ground),
                "Precondition: the animal is not standing on the ground."));

        await server.WaitPost(() =>
            maps.SetTile(world.Ground, entMan.GetComponent<MapGridComponent>(world.Ground), Spot, Tile.Empty));
        await Wake(pair, animal);
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var biome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(biomes.WfIsChunkLoaded(biome, Spot), Is.True,
                    "Precondition: the hole's chunk unloaded.");
                Assert.That(biomes.WfIsPinned(biome, Spot), Is.True, "The hole queue left the hole unpinned.");
                Assert.That(entMan.GetComponent<WFCavernGroundComponent>(world.Ground).Shades.ContainsKey(Spot), Is.True,
                    "The hole queue gave the hole no shade.");
                Assert.That(entMan.Deleted(animal) || entMan.IsQueuedForDeletion(animal), Is.False,
                    "Wildlife that fell through a hole in a loaded chunk was deleted.");
            }

            Assert.That(entMan.GetComponent<TransformComponent>(animal).MapUid, Is.EqualTo(world.Cavern),
                "Precondition: the animal did not fall through the hole into the cavern.");
            Assert.That(entMan.GetComponent<CEZPhysicsComponent>(animal).LocalPosition, Is.GreaterThan(-0.1f),
                "The animal sank into the cavern floor instead of landing under the hole.");
        });

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>Spawns a mob on a ground tile's centre, marked as the ground's wildlife if asked.</summary>
    private static async Task<EntityUid> SpawnMob(TestPair pair, World world, Vector2i tile, bool wildlife)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mob = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            mob = entMan.SpawnEntity(MobProto, new EntityCoordinates(world.Ground, new Vector2(tile.X + 0.5f, tile.Y + 0.5f)));

            if (!wildlife)
                return;

            // Seen just now, as the fauna spawner leaves it, so it isn't retired for want of anyone near.
            var comp = entMan.EnsureComponent<WFPlanetWildlifeComponent>(mob);
            comp.Ground = world.Ground;
            comp.LastNearby = server.Timing.CurTime;
        });

        await server.WaitRunTicks(1);
        return mob;
    }

    /// <summary>Wakes z-physics bodies, as a wandering animal would be; a sleeping one never notices its ground go.</summary>
    private static async Task Wake(TestPair pair, params EntityUid[] mobs)
    {
        var levels = pair.Server.System<CEZLevelsSystem>();

        await pair.Server.WaitPost(() =>
        {
            foreach (var mob in mobs)
            {
                levels.WakeBody(mob);
            }
        });
    }
}
