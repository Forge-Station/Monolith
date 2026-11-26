#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Caverns;
using Content.Server.Parallax;
using Content.Shared._WF.CCVar;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// A body arriving on unloaded cavern ground loads the block it stands in and the blocks a few steps round it at once,
/// not the thirty-six chunks round it, and the rest of the area over the passes after.
/// </summary>
[TestFixture]
[TestOf(typeof(BiomeSystem))]
public sealed class TerrainArrivalLoadTest
{
    private const string Surface = "WFSurfaceMerak";

    /// <summary>Fresh cavern ground for the old setting, then for the new one, with a block corner under neither.</summary>
    private static readonly Vector2i OldSpot = new(200, 40);
    private static readonly Vector2i NewSpot = new(1000, 40);

    private static readonly Vector2i WarmSpot = new(2400, 40);

    /// <summary>A body half a tile in from a block's edge, where the block beside it matters most.</summary>
    private static readonly Vector2i EdgeSpot = new(1600, 40);

    private sealed record Arrival(List<int> Chunks, List<double> Ms, int Settled);

    [Test]
    public async Task AnArrivalOnUnloadedCavernGroundSpreadsItsLoad()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var biomes = server.System<BiomeSystem>();

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var viewer = await PlanetFixture.AttachViewer(pair, world.Ground, TileCentre(OldSpot));

            // Cavern chunks cost the most, and the real budget is what a server runs with.
            await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.TerrainLoadBudget, PlanetCVars.TerrainLoadBudget.DefaultValue));

            // The first cavern load pays for warming up, which would be charged to whichever setting goes first.
            await Arrive(pair, world, viewer, WarmSpot, 2, 0f);

            var old = await Arrive(pair, world, viewer, OldSpot, 2, 0f);
            var spread = await Arrive(pair, world, viewer, NewSpot, PlanetCVars.TerrainLoadNowRing.DefaultValue, PlanetCVars.TerrainLoadNowPad.DefaultValue);
            var edge = await Arrive(pair, world, viewer, EdgeSpot, PlanetCVars.TerrainLoadNowRing.DefaultValue, PlanetCVars.TerrainLoadNowPad.DefaultValue);

            TestContext.Out.WriteLine($"old    first pass {old.Chunks[0]} chunks {old.Ms[0]:F1} ms, worst pass {old.Ms.Max():F1} ms, passes {string.Join(",", old.Chunks)}");
            TestContext.Out.WriteLine($"spread first pass {spread.Chunks[0]} chunks {spread.Ms[0]:F1} ms, worst pass {spread.Ms.Max():F1} ms, passes {string.Join(",", spread.Chunks)}");
            TestContext.Out.WriteLine($"edge   first pass {edge.Chunks[0]} chunks {edge.Ms[0]:F1} ms, worst pass {edge.Ms.Max():F1} ms, passes {string.Join(",", edge.Chunks)}");

            Assert.Multiple(() =>
            {
                Assert.That(old.Chunks[0], Is.GreaterThanOrEqualTo(36), "Precondition: the old setting loads the six by six chunks round the body at once.");
                Assert.That(spread.Chunks[0], Is.LessThanOrEqualTo(8), "The first pass loaded more than the body's block and the next one.");
                Assert.That(edge.Chunks[0], Is.LessThanOrEqualTo(16), "A body on a block edge loaded more than the blocks beside it.");
                Assert.That(spread.Chunks.Max(), Is.LessThan(old.Chunks[0]), "A later pass loaded as much as the old first one.");
                Assert.That(spread.Settled, Is.EqualTo(81), "Part of the load area never loaded.");
                Assert.That(edge.Settled, Is.EqualTo(81), "Part of the load area never loaded.");
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Moves the viewer onto fresh cavern ground and runs the loading pass by pass, noting the chunks each pass loaded
    /// and what it cost; the chunks of the area loaded after the last pass come back too.
    /// </summary>
    private static async Task<Arrival> Arrive(TestPair pair, World world, EntityUid viewer, Vector2i spot, int ring, float pad)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var chunks = new List<int>();
        var times = new List<double>();

        int Loaded()
        {
            var biome = (world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern));
            return Enumerable.Range(-4, 9)
                .SelectMany(x => Enumerable.Range(-4, 9).Select(y => spot + new Vector2i(x, y) * ChunkSize))
                .Count(chunk => biomes.WfIsChunkLoaded(biome, chunk));
        }

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.TerrainLoadNowRing, ring);
            server.CfgMan.SetCVar(PlanetCVars.TerrainLoadNowPad, pad);
            server.System<SharedTransformSystem>().SetCoordinates(viewer, new EntityCoordinates(world.Cavern, TileCentre(spot)));
        });

        var before = 0;
        await server.WaitPost(() => before = Loaded());
        Assert.That(before, Is.EqualTo(0), "Precondition: the arrival ground was already loaded.");

        for (var pass = 0; pass < 40; pass++)
        {
            // A tick on, so the pass starts afresh; the engine's own timer is short of its next pass.
            await server.WaitRunTicks(1);

            var last = before;
            var ms = 0.0;
            await server.WaitPost(() =>
            {
                var watch = Stopwatch.StartNew();
                biomes.Update(0.2f);
                ms = watch.Elapsed.TotalMilliseconds;
                before = Loaded();
            });

            chunks.Add(before - last);
            times.Add(ms);

            if (before >= 81)
                break;
        }

        return new Arrival(chunks, times, before);
    }
}
