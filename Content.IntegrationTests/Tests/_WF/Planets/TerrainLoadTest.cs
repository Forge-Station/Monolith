#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Caverns;
using Content.Server.Parallax;
using Content.Shared._WF.CCVar;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// A planet layer loads the ground round a new arrival at once and the far part of its load area over the passes
/// after, except under a hull, which loads at once wherever it is.
/// </summary>
[TestFixture]
[TestOf(typeof(BiomeSystem))]
public sealed class TerrainLoadTest
{
    private const string Surface = "WFSurfaceMerak";

    /// <summary>Where the viewer arrives.</summary>
    private static readonly Vector2i Home = new(200, 40);

    /// <summary>Two chunks out: inside the part that loads at once.</summary>
    private static readonly Vector2i Near = Home + new Vector2i(16, -16);

    /// <summary>Fresh ground, far from the first.</summary>
    private static readonly Vector2i Elsewhere = Home + new Vector2i(400, 0);

    /// <summary>Fresh ground again, for a viewer in the cavern under it.</summary>
    private static readonly Vector2i Below = Home + new Vector2i(800, 0);

    /// <summary>Three chunks out and under a hull.</summary>
    private static readonly Vector2i UnderHull = Home + new Vector2i(28, 20);

    /// <summary>Three chunks out with nothing over it, in a block that is not the first to load.</summary>
    private static readonly Vector2i Far = Home + new Vector2i(28, -20);

    [Test]
    public async Task TheFarGroundLoadsOverTheNextPassesButNotUnderAHull()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();

        await EnableCaverns(pair);
        await DisableClaims(pair);

        // Too little for anything: each layer then loads exactly the one block a pass it is always allowed.
        await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.TerrainLoadBudget, 0.0001f));
        var world = await BuildWorld(pair, Surface);

        try
        {
            var hull = await PlanetFixture.BuildDebris(pair, await MapIdOf(pair, world.Ground), 3, new Vector2(UnderHull.X, UnderHull.Y));
            await server.WaitPost(() =>
            {
                server.System<SharedPhysicsSystem>().SetBodyType(hull, BodyType.Static);
                server.System<SharedTransformSystem>().SetLocalPosition(hull, new Vector2(UnderHull.X, UnderHull.Y));
            });

            var viewer = await PlanetFixture.AttachViewer(pair, world.Ground, TileCentre(Home));

            // To the first pass that loads for the viewer.
            var arrived = false;

            for (var tick = 0; tick < 30 && !arrived; tick++)
            {
                await pair.RunTicksSync(1);
                await server.WaitPost(() =>
                    arrived = biomes.WfIsChunkLoaded((world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground)), Home));
            }

            await server.WaitAssertion(() =>
            {
                var biome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));

                Assert.Multiple(() =>
                {
                    Assert.That(arrived, Is.True, "Precondition: the ground under the viewer never loaded.");
                    Assert.That(biomes.WfIsChunkLoaded(biome, Near), Is.True, "Ground two chunks from the viewer was put off.");
                    Assert.That(biomes.WfIsChunkLoaded(biome, UnderHull), Is.True, "Ground under a hull was put off.");
                    Assert.That(biomes.WfIsChunkLoaded(biome, Far), Is.False, "Far ground loaded in the pass it came into range.");
                    // The six by six chunks round the viewer, twelve more round the hull, and one block of four.
                    Assert.That(Loaded(biomes, biome, Home), Is.EqualTo(52),
                        "The first pass did not load just the ring, the ground round the hull and one whole block.");
                });
            });

            await pair.RunTicksSync(pair.SecondsToTicks(4f));

            await server.WaitAssertion(() =>
            {
                var biome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));

                Assert.Multiple(() =>
                {
                    Assert.That(biomes.WfIsChunkLoaded(biome, Far), Is.True, "Far ground never loaded.");
                    Assert.That(Loaded(biomes, biome, Home), Is.EqualTo(81), "Part of the load area never loaded.");
                });
            });

            // With time to spare, the far ground loads in the same pass as the near.
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(PlanetCVars.TerrainLoadBudget, 10000f);
                server.System<SharedTransformSystem>().SetCoordinates(viewer, new EntityCoordinates(world.Ground, TileCentre(Elsewhere)));
            });

            arrived = false;

            for (var tick = 0; tick < 30 && !arrived; tick++)
            {
                await pair.RunTicksSync(1);
                await server.WaitPost(() =>
                    arrived = biomes.WfIsChunkLoaded((world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground)), Elsewhere));
            }

            await server.WaitAssertion(() =>
            {
                var biome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));
                Assert.That(Loaded(biomes, biome, Elsewhere), Is.EqualTo(81), "A pass with time to spare put ground off.");
            });

            // A viewer in the cavern sees the ground above through an eye, which hurries less of it than a body does.
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(PlanetCVars.TerrainLoadBudget, 0.0001f);
                server.System<SharedTransformSystem>().SetCoordinates(viewer, new EntityCoordinates(world.Cavern, TileCentre(Below)));
            });

            arrived = false;

            for (var tick = 0; tick < 90 && !arrived; tick++)
            {
                await pair.RunTicksSync(1);
                await server.WaitPost(() =>
                    arrived = biomes.WfIsChunkLoaded((world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground)), Below));
            }

            await server.WaitAssertion(() =>
            {
                var biome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));

                Assert.Multiple(() =>
                {
                    Assert.That(arrived, Is.True, "Precondition: the ground over a cavern viewer never loaded.");

                    // The four by four chunks round the eye and one block of four.
                    Assert.That(Loaded(biomes, biome, Below), Is.EqualTo(20),
                        "An eye's first pass did not load just the chunks round it and one whole block.");
                });
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>How many chunks of a viewer's load area, four chunks out each way, are loaded.</summary>
    private static int Loaded(BiomeSystem biomes, (EntityUid, BiomeComponent) biome, Vector2i centre)
    {
        return Enumerable.Range(-4, 9)
            .SelectMany(x => Enumerable.Range(-4, 9).Select(y => centre + new Vector2i(x, y) * ChunkSize))
            .Count(chunk => biomes.WfIsChunkLoaded(biome, chunk));
    }
}
