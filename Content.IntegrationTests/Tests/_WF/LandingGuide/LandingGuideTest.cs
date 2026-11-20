#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.LandingGuide;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Caverns;
using Content.Server._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.LandingGuide;
using Content.Shared._WF.Planets;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.LandingGuide;

/// <summary>
/// The landing guide reads the ground under a hull flying a planet's air: clear ground, a wall, another grid and
/// missing ground each read as they would for a set-down, and nothing is read in orbit, on the ground or in space.
/// </summary>
[TestFixture]
[TestOf(typeof(WFLandingSurveySystem))]
public sealed class LandingGuideTest
{
    /// <summary>Where the hull hovers: far from the origin, where no terrain has loaded yet.</summary>
    private static readonly Vector2i Site = new(4000, 4000);

    private const string Wall = "WallSolid";

    /// <summary>The test hull's side, in tiles (see PlanetFixture.BuildHull).</summary>
    private const int Side = 15;

    [Test]
    public async Task TheSurveyMarksClearBlockedGridAndMissingGround()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var hull = await BuildHull(pair, await CavernFixture.MapIdOf(pair, layers[^1]));
        await MapInitHull(pair, hull);

        var survey = server.System<WFLandingSurveySystem>();
        var zLevels = server.System<CEZLevelsSystem>();
        var maps = server.System<SharedMapSystem>();
        var transform = server.System<SharedTransformSystem>();

        var footprint = new HashSet<Vector2i>();
        var counts = new Dictionary<WFLandingTile, int>();
        var beside = WFLandingTile.Clear;
        var wallTile = WFLandingTile.Clear;
        var gridTile = WFLandingTile.Clear;
        var gapTile = WFLandingTile.Clear;
        var clearedTile = WFLandingTile.Blocked;
        bool besideRefused = true, wallRefused = false, clearedRefused = true;

        await server.WaitAssertion(() =>
        {
            Hover(pair, hull, layers[1], Site, Angle.Zero);
            var groundGrid = entMan.GetComponent<MapGridComponent>(ground);
            var hullGrid = entMan.GetComponent<MapGridComponent>(hull);

            // Ground round the hull, but none under its last column.
            Lay(pair, ground, Site + new Vector2i(-2, -2), Site + new Vector2i(Side - 2, Side + 1));

            var besideWall = entMan.SpawnEntity(Wall, Centre(ground, Site + new Vector2i(-1, 7)));
            beside = survey.Classify((ground, groundGrid), Site + new Vector2i(-1, 7));
            besideRefused = zLevels.WfObstacleUnder(hull, ground);
            entMan.DeleteEntity(besideWall);

            var wall = entMan.SpawnEntity(Wall, Centre(ground, Site + new Vector2i(4, 4)));
            wallRefused = zLevels.WfObstacleUnder(hull, ground);

            // Another hull parked on the ground under one tile.
            var parked = maps.CreateGridEntity(entMan.GetComponent<MapComponent>(ground).MapId);
            transform.SetLocalPosition(parked.Owner, Site + new Vector2i(10, 10));
            maps.SetTile(parked.Owner, parked.Comp, Vector2i.Zero, FloorTileOf(pair));

            survey.GetFootprint((hull, hullGrid), (ground, groundGrid), footprint);
            foreach (var index in footprint)
            {
                var status = survey.Classify((ground, groundGrid), index);
                counts[status] = counts.GetValueOrDefault(status) + 1;
            }

            wallTile = survey.Classify((ground, groundGrid), Site + new Vector2i(4, 4));
            gridTile = survey.Classify((ground, groundGrid), Site + new Vector2i(10, 10));
            gapTile = survey.Classify((ground, groundGrid), Site + new Vector2i(Side - 1, 3));

            entMan.DeleteEntity(wall);
            clearedTile = survey.Classify((ground, groundGrid), Site + new Vector2i(4, 4));
            clearedRefused = zLevels.WfObstacleUnder(hull, ground);
        });

        Assert.Multiple(() =>
        {
            Assert.That(footprint, Has.Count.EqualTo(Side * Side), "A hull square on the ground grid covers one ground tile per hull tile.");
            Assert.That(footprint.Contains(Site) && footprint.Contains(Site + new Vector2i(Side - 1, Side - 1)), Is.True,
                "The footprint is not under the hull.");
            Assert.That(beside, Is.EqualTo(WFLandingTile.Blocked), "A wall beside the hull did not read as blocked on its own tile.");
            Assert.That(footprint.Contains(Site + new Vector2i(-1, 7)), Is.False, "The tile beside the hull is in its footprint.");
            Assert.That(besideRefused, Is.False, "Precondition: a wall beside the hull refused the set-down.");
            Assert.That(wallTile, Is.EqualTo(WFLandingTile.Blocked), "A wall under the hull did not read as blocked.");
            Assert.That(wallRefused, Is.True, "Precondition: a wall under the hull did not refuse the set-down.");
            Assert.That(gridTile, Is.EqualTo(WFLandingTile.Grid), "A grid parked under the hull did not read as one.");
            Assert.That(gapTile, Is.EqualTo(WFLandingTile.NoGround), "A tile with no ground read as ground.");
            Assert.That(counts.GetValueOrDefault(WFLandingTile.Blocked), Is.EqualTo(1));
            Assert.That(counts.GetValueOrDefault(WFLandingTile.Grid), Is.EqualTo(1));
            Assert.That(counts.GetValueOrDefault(WFLandingTile.NoGround), Is.EqualTo(Side), "The ungrounded column is not all unknown.");
            Assert.That(counts.GetValueOrDefault(WFLandingTile.Clear), Is.EqualTo(Side * Side - Side - 2));
            Assert.That(clearedTile, Is.EqualTo(WFLandingTile.Clear), "The tile stayed blocked after its wall went.");
            Assert.That(clearedRefused, Is.False, "The set-down was still refused after the wall went.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>At an angle, a wall on a ground tile refuses the set-down exactly when that tile is in the footprint.</summary>
    [TestCase(30)]
    [TestCase(45)]
    public async Task TheFootprintAgreesWithTheSetDownRefusal(int degrees)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var hull = await BuildHull(pair, await CavernFixture.MapIdOf(pair, layers[^1]));
        await MapInitHull(pair, hull);

        var survey = server.System<WFLandingSurveySystem>();
        var zLevels = server.System<CEZLevelsSystem>();
        var footprint = new HashSet<Vector2i>();
        var mismatches = new List<string>();
        var checkedTiles = 0;

        await server.WaitAssertion(() =>
        {
            Hover(pair, hull, layers[1], Site, Angle.FromDegrees(degrees));
            Lay(pair, ground, Site - new Vector2i(Side + 4, Side + 4), Site + new Vector2i(Side * 2 + 4, Side * 2 + 4));

            var groundGrid = entMan.GetComponent<MapGridComponent>(ground);
            survey.GetFootprint((hull, entMan.GetComponent<MapGridComponent>(hull)), (ground, groundGrid), footprint);

            // The hull turns about its corner at the site, so it spans either side of it and upwards.
            var reach = (int) MathF.Ceiling(Side * 1.5f) + 2;
            for (var x = -reach; x <= reach; x++)
            for (var y = -3; y <= reach; y++)
            {
                var index = Site + new Vector2i(x, y);
                var wall = entMan.SpawnEntity(Wall, Centre(ground, index));
                var refused = zLevels.WfObstacleUnder(hull, ground);
                if (refused != footprint.Contains(index))
                    mismatches.Add($"{x},{y}: refused {refused}, in footprint {footprint.Contains(index)}");
                if (footprint.Contains(index) && survey.Classify((ground, groundGrid), index) != WFLandingTile.Blocked)
                    mismatches.Add($"{x},{y}: a wall under the hull did not read as blocked");
                entMan.DeleteEntity(wall);
                checkedTiles++;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(footprint.Count, Is.GreaterThan(Side * Side), "A turned hull should cross more ground tiles than it has.");
            Assert.That(checkedTiles, Is.GreaterThan(footprint.Count));
            Assert.That(mismatches, Is.Empty, string.Join("; ", mismatches.Take(20)));
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheGuideIsOnlyOnInThePlanetsAir()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var orbit = layers[^1];
        var hull = await BuildHull(pair, await CavernFixture.MapIdOf(pair, orbit));
        await MapInitHull(pair, hull);
        var space = await pair.CreateTestMap();

        var survey = server.System<WFLandingSurveySystem>();
        var zLevels = server.System<CEZLevelsSystem>();
        bool inOrbit = true, inAir = false, inTransit = false, onGround = true, inSpace = true;
        EntityUid airGround = default, transitGround = default;

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFOrbitLayerComponent>(orbit), Is.True, "Precondition: the top layer is not orbit.");
            Assert.That(entMan.HasComponent<WFPlanetLayerComponent>(layers[1]) && !entMan.HasComponent<WFOrbitLayerComponent>(layers[1])
                        && !survey.IsGroundLayer(layers[1]), Is.True, "Precondition: the second layer is not air.");

            inOrbit = survey.TryGetLandingGround(hull, out _);

            Hover(pair, hull, layers[1], Site, Angle.Zero);
            inAir = survey.TryGetLandingGround(hull, out airGround);

            var grid = entMan.GetComponent<MapGridComponent>(hull);
            Assert.That(zLevels.TryEnterTransit(new Entity<MapGridComponent>(hull, grid)), Is.True, "Precondition: the hull would not enter transit.");
            Assert.That(entMan.HasComponent<CEZTransitMapComponent>(entMan.GetComponent<TransformComponent>(hull).MapUid), Is.True,
                "Precondition: the hull is not in transit.");
            inTransit = survey.TryGetLandingGround(hull, out transitGround);

            Hover(pair, hull, ground, Site, Angle.Zero);
            onGround = survey.TryGetLandingGround(hull, out _);

            Hover(pair, hull, space.MapUid, Vector2i.Zero, Angle.Zero);
            inSpace = survey.TryGetLandingGround(hull, out _);
        });

        Assert.Multiple(() =>
        {
            Assert.That(inOrbit, Is.False, "The guide is on in orbit.");
            Assert.That(inAir, Is.True, "The guide is off on an air layer.");
            Assert.That(airGround, Is.EqualTo(ground), "The guide reads the wrong layer from the air.");
            Assert.That(inTransit, Is.True, "The guide is off between layers.");
            Assert.That(transitGround, Is.EqualTo(ground), "The guide reads the wrong layer between layers.");
            Assert.That(onGround, Is.False, "The guide is on on the ground.");
            Assert.That(inSpace, Is.False, "The guide is on in space.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A pilot's client reads the ground under its hull from what its z-level eye streams in, and comes to the same
    /// reading as the server; letting go of the helm clears it.
    /// </summary>
    [Test]
    public async Task ThePilotsClientReadsTheSameGroundAsTheServer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var hull = await BuildHull(pair, await CavernFixture.MapIdOf(pair, layers[^1]));
        await MapInitHull(pair, hull);

        await server.WaitPost(() =>
        {
            Hover(pair, hull, layers[1], Site, Angle.Zero);
            Lay(pair, ground, Site + new Vector2i(-4, -4), Site + new Vector2i(Side + 3, Side + 3));
            entMan.SpawnEntity(Wall, Centre(ground, Site + new Vector2i(4, 4)));
            entMan.SpawnEntity(Wall, Centre(ground, Site + new Vector2i(9, 2)));
        });

        var pilot = await AttachViewer(pair, hull, new Vector2(2.5f, 3.5f));
        await server.WaitPost(() =>
        {
            var helm = entMan.EnsureComponent<PilotComponent>(pilot);
            helm.Console = FindShuttleConsole(entMan, hull);
            entMan.Dirty(pilot, helm);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(4f));

        var survey = server.System<WFLandingSurveySystem>();
        var serverTiles = 0;
        var serverBlocked = 0;
        await server.WaitAssertion(() =>
        {
            var footprint = new HashSet<Vector2i>();
            var groundGrid = entMan.GetComponent<MapGridComponent>(ground);
            survey.GetFootprint((hull, entMan.GetComponent<MapGridComponent>(hull)), (ground, groundGrid), footprint);
            serverTiles = footprint.Count;
            serverBlocked = footprint.Count(index => survey.Classify((ground, groundGrid), index) is WFLandingTile.Blocked or WFLandingTile.Grid);
        });

        var netHull = entMan.GetNetEntity(hull);
        var netGround = entMan.GetNetEntity(ground);
        EntityUid? clientHull = null, clientGround = null, expectedHull = null, expectedGround = null;
        var clientTiles = 0;
        var clientBlocked = 0;
        await client.WaitAssertion(() =>
        {
            var guide = client.System<WFLandingGuideSystem>();
            expectedHull = client.EntMan.GetEntity(netHull);
            expectedGround = client.EntMan.GetEntity(netGround);
            clientHull = guide.Hull;
            clientGround = guide.Ground;
            clientTiles = guide.Tiles.Count;
            clientBlocked = guide.Blocked;
        });

        Assert.Multiple(() =>
        {
            Assert.That(clientHull, Is.EqualTo(expectedHull), "The pilot's client is not guiding its hull.");
            Assert.That(clientGround, Is.EqualTo(expectedGround), "The pilot's client reads the wrong ground.");
            Assert.That(serverBlocked, Is.GreaterThanOrEqualTo(2), "Precondition: the walls under the hull are not blocking.");
            Assert.That(clientTiles, Is.EqualTo(serverTiles), "The client's footprint differs from the server's.");
            Assert.That(clientBlocked, Is.EqualTo(serverBlocked), "The client sees different blockers from the server.");
        });

        await server.WaitPost(() => entMan.RemoveComponent<PilotComponent>(pilot));
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            Assert.That(client.System<WFLandingGuideSystem>().Hull, Is.Null,
                "The guide stayed up after the pilot let go of the helm.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Puts a hull on a map at a tile corner, at an angle, held still so it neither falls nor drifts.</summary>
    private static void Hover(TestPair pair, EntityUid hull, EntityUid map, Vector2i at, Angle rotation)
    {
        var transform = pair.Server.System<SharedTransformSystem>();
        pair.Server.System<SharedPhysicsSystem>().SetBodyType(hull, BodyType.Static);
        transform.SetCoordinates(hull, new EntityCoordinates(map, at));
        transform.SetWorldRotation(hull, rotation);
    }

    /// <summary>Floors a rectangle of the ground at once; call on the server thread.</summary>
    private static void Lay(TestPair pair, EntityUid ground, Vector2i from, Vector2i to)
    {
        var grid = pair.Server.EntMan.GetComponent<MapGridComponent>(ground);
        var floor = FloorTileOf(pair);
        var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
        for (var x = from.X; x <= to.X; x++)
        for (var y = from.Y; y <= to.Y; y++)
        {
            tiles.Add((new Vector2i(x, y), floor));
        }

        pair.Server.System<SharedMapSystem>().SetTiles(ground, grid, tiles);
    }

    private static Tile FloorTileOf(TestPair pair)
    {
        return new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()[FloorTile].TileId);
    }

    private static EntityCoordinates Centre(EntityUid ground, Vector2i index)
    {
        return new EntityCoordinates(ground, new Vector2(index.X + 0.5f, index.Y + 0.5f));
    }
}
