#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets.Bounds;
using Content.Server._WF.Planets.Hop;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// A ship put on a bounded world, by a hop or by being put back from the edge, lies wholly inside its circle: the
/// hull's origin is often a corner, so the reach is measured from the middle of its bounds and not from the origin.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetBoundsSystem))]
public sealed class PlanetArrivalTest
{
    /// <summary>The slack the clamp leaves a hull's corners, past rounding.</summary>
    private const float Epsilon = 0.05f;

    /// <summary>Hulls of many sizes, put at, past and well past the edge at several angles, end wholly inside.</summary>
    [TestCase(64, 8, 8)]
    [TestCase(64, 40, 12)]
    [TestCase(64, 60, 24)]
    [TestCase(100, 90, 30)]
    [TestCase(40, 20, 10)]
    public async Task AClampedHullLiesWhollyInsideTheCircle(int radius, int width, int height)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var bounds = server.System<WFPlanetBoundsSystem>();
        var xforms = server.System<SharedTransformSystem>();

        var layers = await Build(pair, radius);
        var ground = layers[0];
        var hull = await BuildHull(pair, ground, width, height);
        var failures = new List<string>();
        bool originCheckHolds = false;

        await server.WaitAssertion(() =>
        {
            // The origin well inside, the far end of a long hull past the edge: only a check on the hull's middle sees it.
            xforms.SetWorldPosition(hull, new Vector2(radius - 6f, 0f));
            xforms.SetWorldRotation(hull, Angle.Zero);
            originCheckHolds = width > 12 && !bounds.IsHullInside(hull);

            foreach (var angle in new[] { 0.0, 33.0, 90.0, 215.0 })
            {
                foreach (var distance in new[] { radius - 8f, radius * 1f, radius + 25f, radius * 3f })
                {
                    foreach (var bearing in new[] { 0.0, 120.0, 250.0 })
                    {
                        var dir = new Vector2(MathF.Cos((float) Angle.FromDegrees(bearing)), MathF.Sin((float) Angle.FromDegrees(bearing)));
                        xforms.SetWorldRotation(hull, Angle.FromDegrees(angle));
                        xforms.SetWorldPosition(hull, dir * distance);
                        bounds.ClampHull(hull);

                        var worst = Farthest(entMan, xforms, hull);

                        if (worst > radius + Epsilon)
                            failures.Add($"{width}x{height} at {angle} deg, {distance:0} out at {bearing}: corner {worst:0.00} from the centre of a {radius} circle");

                        if (!bounds.IsHullInside(hull))
                            failures.Add($"{width}x{height} at {angle} deg, {distance:0} out at {bearing}: still not inside after the clamp");
                    }
                }
            }
        });

        Assert.Multiple(() =>
        {
            if (width > 12)
                Assert.That(originCheckHolds, Is.True, "Precondition: a long hull with its origin inside and its far end out read as inside.");

            Assert.That(failures, Is.Empty, string.Join("; ", failures));
        });

        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A hop onto the ground layer aimed on, past and far beyond the edge comes out inside.</summary>
    [TestCase(0f)]
    [TestCase(20f)]
    [TestCase(200f)]
    public async Task AJumpAimedAtOrPastTheEdgeComesOutInside(float pastTheEdge)
    {
        const int radius = 64;
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var bounds = server.System<WFPlanetBoundsSystem>();
        var hops = server.System<WFPlanetHopSystem>();

        var layers = await Build(pair, radius);
        var ground = layers[0];
        // Departing a planet layer is refused, so the ship starts on a map of its own, as one coming from the sector does.
        var start = await pair.CreateTestMap();
        var hull = await BuildHull(pair, start.MapUid, 40, 14);
        var accepted = false;
        string? refusal = null;

        await server.WaitPost(() =>
        {
            var shuttle = entMan.GetComponent<ShuttleComponent>(hull);
            accepted = hops.TryStart((hull, shuttle), ground, new Vector2(radius + pastTheEdge, 0f), null, true, out refusal);
        });

        Assert.That(accepted, Is.True, $"The hop was refused: {refusal}");

        for (var tick = 0; tick < 3000 && await IsJumping(pair, hull); tick += 10)
        {
            await pair.RunTicksSync(10);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFPlanetHopComponent>(hull), Is.False, "The hop never finished.");
            Assert.That(entMan.HasComponent<FTLComponent>(hull), Is.False, "The hop gave the hull an FTLComponent.");
            Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(ground));
            Assert.That(bounds.IsHullInside(hull), Is.True,
                $"The hull came out with its middle at {bounds.HullCentre(hull)}, outside the {radius} circle.");
            Assert.That(Farthest(entMan, server.System<SharedTransformSystem>(), hull), Is.LessThanOrEqualTo(radius + Epsilon),
                "A corner of the hull is outside the circle.");
        });

        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    private static async Task<bool> IsJumping(TestPair pair, EntityUid hull)
    {
        var jumping = false;
        await pair.Server.WaitPost(() => jumping = pair.Server.EntMan.HasComponent<WFPlanetHopComponent>(hull));
        return jumping;
    }

    /// <summary>The farthest any corner of a hull's bounds is from the centre of the world.</summary>
    private static float Farthest(IEntityManager entMan, SharedTransformSystem xforms, EntityUid hull)
    {
        var box = entMan.GetComponent<MapGridComponent>(hull).LocalAABB;
        var matrix = xforms.GetWorldMatrix(hull);
        var worst = 0f;

        foreach (var corner in new[] { box.BottomLeft, box.BottomRight, box.TopLeft, box.TopRight })
        {
            worst = MathF.Max(worst, Vector2.Transform(corner, matrix).Length());
        }

        return worst;
    }

    /// <summary>A bounded stack with a circle of the given radius at the origin, nothing preloaded.</summary>
    private static async Task<List<EntityUid>> Build(TestPair pair, int radius)
    {
        await PlanetFixture.EnableFeature(pair);
        await pair.Server.WaitPost(() =>
        {
            pair.Server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            pair.Server.CfgMan.SetCVar(PlanetCVars.Radius, radius);
        });

        return await PlanetFixture.BuildStandalone(pair);
    }

    /// <summary>A flyable floor of the given size on a layer, its origin at a corner as ship maps leave it.</summary>
    private static async Task<EntityUid> BuildHull(TestPair pair, EntityUid layer, int width, int height)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapId = await MapIdOf(pair, layer);
        var grid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
            var created = maps.CreateGridEntity(mapId);
            var floor = new Tile(tileDefs[PlanetFixture.FloorTile].TileId);
            var tiles = new List<(Vector2i, Tile)>();

            for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
            {
                tiles.Add((new Vector2i(x, y), floor));
            }

            maps.SetTiles(created.Owner, created.Comp, tiles);
            entMan.EnsureComponent<ShuttleComponent>(created.Owner);
            server.System<ShuttleSystem>().Enable(created.Owner, force: true);
            entMan.RunMapInit(created.Owner, entMan.GetComponent<MetaDataComponent>(created.Owner));
            grid = created.Owner;
        });

        await server.WaitRunTicks(1);
        return grid;
    }
}
