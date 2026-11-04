#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.NpcVehicleCrew;
using Content.Shared._WF.NpcVehicleCrew;
using Content.Shared.CCVar;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.NpcVehicleCrew;

/// <summary>
/// An NPC driver keeps a real vehicle on the road: round a bend in a road-wide corridor to the far end, without circling,
/// round a wall blocking the way, and through a string of patrol points without stopping at each.
/// </summary>
[TestFixture]
[TestOf(typeof(WFVehicleCrewSystem))]
public sealed class WFVehicleCrewDrivingTest
{
    private const string Crew = "WFVehicleCrewCivilianTruck";
    private const string RoadCrew = "WFVehicleCrewTestRoadTruck";

    [TestPrototypes]
    private const string Prototypes = @"
- type: wfVehicleCrew
  id: WFVehicleCrewTestRoadTruck
  vehicle: AU14VehicleCivTruck
  driver: WFVehicleDriver
  profile: WFVehicleCrewProfileCivilian
  faction: WFCrew
  behaviour: Patrol
  engagement: WhenAttacked
  skill: Green
  patrolPauseMin: 0
  patrolPauseMax: 0
";

    /// <summary>A road-wide corridor: east along y = 0, then north up x = 47.</summary>
    private static bool Road(Vector2i tile) =>
        tile.X is >= -4 and <= 50 && tile.Y is >= -3 and <= 3 || tile.X is >= 44 and <= 50 && tile.Y is >= -3 and <= 44;

    private static async Task<(EntityUid Vehicle, MapCoordinates Goal)> Rig(TestPair pair, Func<Vector2i, bool> floor, Vector2 goal,
        string crew = Crew, int maxX = 54, int maxY = 48)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid vehicle = default;
        MapCoordinates target = default;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false);
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            // Floor first: a grid left with no tiles at all is deleted.
            var laid = new List<(Vector2i, Tile)>();
            var cleared = new List<(Vector2i, Tile)>();
            for (var x = -8; x <= maxX; x++)
            for (var y = -8; y <= maxY; y++)
            {
                var tile = new Vector2i(x, y);
                if (floor(tile))
                    laid.Add((tile, new Tile(plating)));
                else
                    cleared.Add((tile, Tile.Empty));
            }
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, laid);
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, cleared);

            Assert.That(server.System<WFVehicleCrewSystem>().TrySpawn(crew, new EntityCoordinates(map.Grid.Owner, 2.5f, 0.5f),
                out vehicle, WFVehicleBehaviour.Hold), Is.True);
            target = server.System<SharedTransformSystem>().ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, goal));
            server.System<WFVehicleCrewSystem>().SetHome(vehicle, target);
        });
        return (vehicle, target);
    }

    /// <summary>Drives until the vehicle is at the goal or the time is up; returns how far it turned in all.</summary>
    private static async Task<float> DriveTo(TestPair pair, EntityUid vehicle, MapCoordinates goal, float seconds)
    {
        var server = pair.Server;
        var xforms = server.System<SharedTransformSystem>();
        var turned = 0f;
        Angle? last = null;
        for (var waited = 0f; waited < seconds; waited += 0.25f)
        {
            var arrived = false;
            await server.WaitPost(() =>
            {
                var rotation = xforms.GetWorldRotation(vehicle);
                if (last is { } previous)
                    turned += (float) Math.Abs(Angle.ShortestDistance(previous, rotation).Degrees);
                last = rotation;
                arrived = Vector2.Distance(xforms.GetWorldPosition(vehicle), goal.Position) < 2.5f;
            });
            if (arrived)
                break;
            await pair.RunSeconds(0.25f);
        }
        return turned;
    }

    [Test]
    public async Task DrivesRoundABendWithoutCircling()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var (vehicle, goal) = await Rig(pair, Road, new Vector2(47.5f, 40.5f));
        var turned = await DriveTo(pair, vehicle, goal, 60f);

        await server.WaitAssertion(() =>
        {
            var distance = Vector2.Distance(server.System<SharedTransformSystem>().GetWorldPosition(vehicle), goal.Position);
            Assert.That(distance, Is.LessThan(2.5f), $"The vehicle didn't get round the bend: {distance:0.0} tiles short.");
            // One bend is a quarter turn; a little weaving on top is fine, laps of the corridor aren't.
            Assert.That(turned, Is.LessThan(300f), $"The vehicle turned {turned:0} degrees on a road with one bend.");
        });
        await pair.Server.WaitPost(() => server.CfgMan.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, CCVars.NPCPauseWhenNoPlayersInRange.DefaultValue));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DrivesRoundAWallInTheWay()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        // Open ground with a wall across the middle of the way.
        var (vehicle, goal) = await Rig(pair, tile => tile.X is >= -6 and <= 50 && tile.Y is >= -20 and <= 20, new Vector2(44.5f, 0.5f));
        await server.WaitPost(() =>
        {
            var grid = server.EntMan.GetComponent<TransformComponent>(vehicle).GridUid!.Value;
            for (var y = -6; y <= 6; y++)
                server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 22.5f, y + 0.5f));
        });
        await DriveTo(pair, vehicle, goal, 60f);

        await server.WaitAssertion(() =>
        {
            var distance = Vector2.Distance(server.System<SharedTransformSystem>().GetWorldPosition(vehicle), goal.Position);
            Assert.That(distance, Is.LessThan(2.5f), $"The vehicle didn't get round the wall: {distance:0.0} tiles short.");
        });
        await pair.Server.WaitPost(() => server.CfgMan.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, CCVars.NPCPauseWhenNoPlayersInRange.DefaultValue));
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Sends a truck patrolling through the points with no pause at each and samples its speed every quarter second until
    /// it is near the last one.
    /// </summary>
    private static async Task<(List<float> Speeds, bool Arrived)> Patrol(TestPair pair, Func<Vector2i, bool> floor,
        List<Vector2> points, int maxX, int maxY)
    {
        var server = pair.Server;
        var xforms = server.System<SharedTransformSystem>();
        var (vehicle, _) = await Rig(pair, floor, points[^1], RoadCrew, maxX, maxY);
        var last = default(MapCoordinates);
        await server.WaitPost(() =>
        {
            var grid = server.EntMan.GetComponent<TransformComponent>(vehicle).GridUid!.Value;
            var waypoints = new List<MapCoordinates>();
            foreach (var point in points)
                waypoints.Add(xforms.ToMapCoordinates(new EntityCoordinates(grid, point)));
            last = waypoints[^1];
            server.System<WFVehicleCrewSystem>().SetWaypoints(vehicle, waypoints);
        });

        var speeds = new List<float>();
        var arrived = false;
        var before = Vector2.Zero;
        await server.WaitPost(() => before = xforms.GetWorldPosition(vehicle));
        for (var waited = 0f; waited < 60f && !arrived; waited += 0.25f)
        {
            await pair.RunSeconds(0.25f);
            await server.WaitPost(() =>
            {
                var now = xforms.GetWorldPosition(vehicle);
                speeds.Add(Vector2.Distance(now, before) / 0.25f);
                before = now;
                arrived = Vector2.Distance(now, last.Position) < 3f;
            });
        }

        TestContext.Out.WriteLine("speeds: " + string.Join(" ", speeds.ConvertAll(speed => speed.ToString("0.0"))));
        return (speeds, arrived);
    }

    /// <summary>Asserts a run of speed samples never dips below a floor once the truck has got going.</summary>
    private static void AssertRollsOn(List<float> speeds, bool arrived, float floor, int allowedStops)
    {
        Assert.That(arrived, Is.True, "The truck never reached the last point.");
        const int warmUp = 8;
        for (var i = warmUp; i < speeds.Count; i++)
            Assert.That(speeds[i], Is.GreaterThanOrEqualTo(floor), $"Speed fell to {speeds[i]:0.0} at {i * 0.25f:0.00} s.");

        var stops = 0;
        var stopped = false;
        for (var i = 1; i < speeds.Count; i++)
        {
            var now = speeds[i] < 0.5f;
            if (now && !stopped)
                stops++;
            stopped = now;
        }

        Assert.That(stops, Is.LessThanOrEqualTo(allowedStops), $"The truck stopped {stops} times.");
    }

    [Test]
    public async Task PatrolRollsThroughStraightWaypointsWithoutStopping()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var points = new List<Vector2>();
        for (var i = 1; i <= 5; i++)
            points.Add(new Vector2(2.5f + 14f * i, 0.5f));

        var (speeds, arrived) = await Patrol(pair, tile => tile.X is >= -6 and <= 80 && tile.Y is >= -8 and <= 8, points, 84, 12);
        AssertRollsOn(speeds, arrived, 2f, 1);
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, CCVars.NPCPauseWhenNoPlayersInRange.DefaultValue));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PatrolRollsThroughABendWithoutStopping()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var points = new List<Vector2>
        {
            new(16.5f, 0.5f),
            new(30.5f, 0.5f),
            new(44.5f, 0.5f),
            new(44.5f, 14.5f),
            new(44.5f, 28.5f),
        };

        var (speeds, arrived) = await Patrol(pair, tile => tile.X is >= -6 and <= 52 && tile.Y is >= -8 and <= 36, points, 54, 40);
        AssertRollsOn(speeds, arrived, 0.5f, 1);
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, CCVars.NPCPauseWhenNoPlayersInRange.DefaultValue));
        await pair.CleanReturnAsync();
    }
}
