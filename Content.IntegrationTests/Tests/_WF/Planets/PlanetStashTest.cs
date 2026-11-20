#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server.Parallax;
using Content.Shared._WF.CCVar;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// A preloaded ground keeps its tiles but puts away the trees and rocks nobody is near: they come back the same when
/// someone does, a touched one never goes, and nobody standing where one would come back is walled in.
/// </summary>
[TestFixture]
[TestOf(typeof(BiomeSystem))]
public sealed class PlanetStashTest
{
    private const int Radius = 64;

    private static readonly Vector2 Here = new(36.5f, 0.5f);
    private static readonly Vector2 Away = new(-36.5f, 0.5f);

    [Test]
    public async Task StashedTerrainComesBackAsSomeoneComesNear()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var xforms = server.System<SharedTransformSystem>();

        await PlanetFixture.EnableFeature(pair);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, Radius);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.TerrainUnloadIdle, 1f);
        });

        var layers = await PlanetFixture.BuildStandalone(pair);
        var ground = layers[0];

        try
        {
            await PlanetBoundsTest.WaitPreloaded(pair, ground);
            var chunk = Chunk(Here);

            // Formed with nobody near: every chunk's entities are put away, and its tiles stay.
            await server.WaitPost(() =>
            {
                var biome = entMan.GetComponent<BiomeComponent>(ground);
                Assert.Multiple(() =>
                {
                    Assert.That(biomes.WfStashedChunks(ground), Is.GreaterThan(100), "The formed world stashed nothing.");
                    Assert.That(biome.LoadedEntities.Values.Sum(e => e.Count), Is.Zero, "Biome entities stayed out with nobody near.");
                    Assert.That(biomes.WfIsStashed(ground, chunk), Is.True);
                    Assert.That(biome.LoadedChunks, Does.Contain(chunk), "A stashed chunk lost its ground.");
                    Assert.That(maps.GetTileRef(ground, entMan.GetComponent<MapGridComponent>(ground), (Vector2i) Here).Tile.IsEmpty, Is.False);
                });
            });

            // Someone comes: the ground round them is laid out again.
            var viewer = await PlanetFixture.AttachViewer(pair, ground, Here);
            await pair.RunTicksSync(10);
            var first = await Snapshot(pair, ground, chunk);
            await server.WaitPost(() =>
            {
                Assert.That(biomes.WfIsStashed(ground, chunk), Is.False, "The chunk under the viewer stayed stashed.");
                Assert.That(biomes.WfIsStashed(ground, Chunk(Away)), Is.True, "A chunk nobody is near came back.");
            });

            var near = await Near(pair, ground, Here, 24);
            Assert.That(near, Is.Not.Empty, "The ground round the viewer came back bare.");

            // One is touched, and someone stands where a solid one stood.
            EntityUid touched = default;
            EntityUid occupant = default;
            Vector2i? occupied = null;
            var solid = new List<Vector2i>();
            await server.WaitPost(() =>
            {
                var biome = entMan.GetComponent<BiomeComponent>(ground);
                var all = biome.LoadedEntities.Values.SelectMany(e => e).ToList();
                touched = all.First().Key;
                entMan.DirtyEntity(touched);
                solid.AddRange(all.Skip(1).Select(p => p.Value).Where(tile => tile != all[0].Value && Solid(tile)));
            });

            // They leave: it is put away again, all but the touched one.
            await server.WaitPost(() => xforms.SetCoordinates(viewer, new EntityCoordinates(ground, Away)));
            await pair.RunTicksSync(pair.SecondsToTicks(4f));
            await server.WaitPost(() =>
            {
                Assert.That(biomes.WfIsStashed(ground, chunk), Is.True, "The chunk left behind was not stashed again.");
                Assert.That(entMan.EntityExists(touched), Is.True, "A touched biome entity was put away.");
            });
            Assert.That(await Near(pair, ground, Here, 24), Has.Count.LessThan(near.Count), "Nothing round the old spot was put away.");

            // Someone stands where a solid one stood and was put away.
            await server.WaitPost(() =>
            {
                occupied = solid.Where(tile => !Solid(tile)).Select(tile => (Vector2i?) tile).FirstOrDefault();
                if (occupied is { } tile)
                    occupant = entMan.SpawnEntity(PlanetFixture.ViewerProto, new EntityCoordinates(ground, (Vector2) tile + new Vector2(0.5f)));
            });
            Assert.That(occupied, Is.Not.Null, "No solid biome entity near the viewer was put away to stand in place of.");

            // Back again: the same comes back, round whoever stands in the way. The occupant is set back on its tile
            // first, as the trees round it can shove it off while it waits.
            await server.WaitPost(() =>
            {
                xforms.SetCoordinates(occupant, new EntityCoordinates(ground, (Vector2) occupied!.Value + new Vector2(0.5f)));
                xforms.SetCoordinates(viewer, new EntityCoordinates(ground, Here));
            });
            await pair.RunTicksSync(10);
            var second = await Snapshot(pair, ground, chunk);
            Assert.That(second.Except(first), Is.Empty, "Something new came back.");
            Assert.That(first.Count - second.Count, Is.LessThanOrEqualTo(2), "Less came back than went.");

            await server.WaitPost(() =>
            {
                var standing = maps.LocalToTile(ground, entMan.GetComponent<MapGridComponent>(ground), entMan.GetComponent<TransformComponent>(occupant).Coordinates);
                Assert.That(Solid(standing), Is.False, "Someone standing on a stashed tile was walled in.");
                Assert.That(entMan.EntityExists(occupant), Is.True);
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        // Whether something hard is anchored on a ground tile.
        bool Solid(Vector2i tile)
        {
            return maps.GetAnchoredEntities(ground, entMan.GetComponent<MapGridComponent>(ground), tile)
                .Any(e => entMan.TryGetComponent<PhysicsComponent>(e, out var body) && body.Hard);
        }

        await pair.CleanReturnAsync();
    }

    private static Vector2i Chunk(Vector2 pos)
    {
        // SharedBiomeSystem.ChunkSize, which is protected.
        const int chunkSize = 8;
        return SharedMapSystem.GetChunkIndices(pos, chunkSize) * chunkSize;
    }

    /// <summary>What stands on each tile of a chunk, by prototype; the touched and the pinned aside.</summary>
    private static async Task<HashSet<(string, Vector2i)>> Snapshot(TestPair pair, EntityUid ground, Vector2i chunk)
    {
        var result = new HashSet<(string, Vector2i)>();
        await pair.Server.WaitPost(() =>
        {
            var biome = pair.Server.EntMan.GetComponent<BiomeComponent>(ground);
            var all = biome.LoadedEntities;
            if (!all.TryGetValue(chunk, out var loaded))
                return;
            foreach (var (uid, tile) in loaded)
                result.Add((pair.Server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? "", tile));
        });
        return result;
    }

    /// <summary>Every entity anchored on the ground within a square round a spot.</summary>
    private static async Task<List<EntityUid>> Near(TestPair pair, EntityUid ground, Vector2 centre, int half)
    {
        var result = new List<EntityUid>();
        await pair.Server.WaitPost(() =>
        {
            var maps = pair.Server.System<SharedMapSystem>();
            var grid = pair.Server.EntMan.GetComponent<MapGridComponent>(ground);
            for (var x = -half; x <= half; x++)
            for (var y = -half; y <= half; y++)
                result.AddRange(maps.GetAnchoredEntities(ground, grid, (Vector2i) centre + new Vector2i(x, y)));
        });
        return result;
    }
}
