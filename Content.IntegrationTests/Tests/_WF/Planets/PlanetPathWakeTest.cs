#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Atmos;
using Content.Shared.Gravity;
using Content.Shared.NPC;
using Content.Shared.Physics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// With the planet navmesh built only near someone, a path asked for to ground nobody has been near has that ground
/// built and finds its way, instead of finding none.
/// </summary>
[TestFixture]
[TestOf(typeof(PathfindingSystem))]
public sealed class PlanetPathWakeTest
{
    /// <summary>How far east the far end of the strip is, in tiles: well past the navmesh range round anyone.</summary>
    private const int Length = 150;

    /// <summary>Where the wall across the strip stands; its gap is at y = 0.</summary>
    private const int WallX = 75;

    /// <summary>A planet-like map grid: a floor strip a tile either side of y = 0, walled across with a gap.</summary>
    private static async Task<EntityUid> Strip(Pair.TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        EntityUid map = default;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.PathfindingLazy, true);
            server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.NPCPauseWhenNoPlayersInRange, false);
            var maps = server.System<SharedMapSystem>();
            map = maps.CreateMap(out _);
            var grid = entMan.EnsureComponent<MapGridComponent>(map);
            var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -4; x <= Length + 4; x++)
            for (var y = -1; y <= 1; y++)
                tiles.Add((new Vector2i(x, y), plating));
            maps.SetTiles(map, grid, tiles);
            for (var y = -1; y <= 1; y++)
            {
                if (y != 0)
                    entMan.SpawnEntity("WallSolid", new EntityCoordinates(map, WallX + 0.5f, y + 0.5f));
            }

            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map, false, air);
            var gravity = entMan.EnsureComponent<GravityComponent>(map);
            gravity.Enabled = true;
            gravity.Inherent = true;
            entMan.Dirty(map, gravity);
            entMan.EnsureComponent<WFPlanetLayerComponent>(map);
        });

        // Nobody is near, so nothing of the strip is built.
        await server.WaitRunTicks(30);
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<GridPathfindingComponent>(map);
            Assert.That(comp.DirtyChunks, Does.Contain(new Vector2i(Length / 8, 0)), "The far end was built with nobody near.");
            Assert.That(comp.DirtyChunks, Does.Contain(Vector2i.Zero), "The near end was built with nobody near.");
        });
        return map;
    }

    /// <summary>A path asked for from one end of unbuilt ground to the other, round a wall, is found.</summary>
    [Test]
    public async Task PathAcrossUnbuiltGroundIsFound()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await Strip(pair);
        var pathfinding = server.System<PathfindingSystem>();

        Task<PathResultEvent>? path = null;
        await server.WaitPost(() => path = pathfinding.GetPath(
            new EntityCoordinates(map, 0.5f, 0.5f),
            new EntityCoordinates(map, Length - 0.5f, 0.5f),
            0.5f,
            (int) CollisionGroup.MobLayer,
            (int) CollisionGroup.MobMask,
            CancellationToken.None));

        for (var i = 0; i < 300 && !path!.IsCompleted; i++)
        {
            await server.WaitRunTicks(1);
            await Task.Delay(2);
        }

        Assert.That(path!.IsCompleted, Is.True, "The path request was never answered.");
        var result = await path;
        var state = string.Empty;
        await server.WaitPost(() =>
        {
            var comp = server.EntMan.GetComponent<GridPathfindingComponent>(map);
            state = $"dirty {comp.DirtyChunks.Count}, built {comp.Chunks.Count}, start poly {pathfinding.GetPoly(new EntityCoordinates(map, 0.5f, 0.5f)) != null}, "
                + $"end poly {pathfinding.GetPoly(new EntityCoordinates(map, Length - 0.5f, 0.5f)) != null}";
        });
        Assert.That(result.Result, Is.EqualTo(PathResult.Path), $"No way was found across ground nobody had been near: {state}.");
        await server.WaitAssertion(() =>
        {
            var crossed = result.Path.Exists(poly => MathF.Abs(poly.Box.Center.X - (WallX + 0.5f)) < 0.5f && poly.Box.Center.Y > 0f && poly.Box.Center.Y < 1f);
            Assert.That(crossed, Is.True, "The path did not go through the gap in the wall.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(map));
        await pair.CleanReturnAsync();
    }

    /// <summary>An awake NPC sent to the far, never-visited end of the strip walks there, round the wall.</summary>
    [Test]
    public async Task NpcWalksToGroundNobodyVisited()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Strip(pair);
        var target = new EntityCoordinates(map, Length - 0.5f, 0.5f);
        EntityUid npc = default;
        await server.WaitPost(() =>
        {
            npc = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map, 0.5f, 0.5f));
            entMan.EnsureComponent<ActiveNPCComponent>(npc);
            server.System<NPCSteeringSystem>().Register(npc, target);
        });

        var xforms = server.System<SharedTransformSystem>();
        var arrived = false;
        var status = SteeringStatus.Moving;
        var trace = string.Empty;
        for (var i = 0; i < 240 && !arrived; i++)
        {
            await server.WaitRunTicks(15);
            await Task.Delay(2);
            await server.WaitPost(() =>
            {
                arrived = Vector2.Distance(xforms.GetWorldPosition(npc), new Vector2(Length - 0.5f, 0.5f)) < 2f;
                if (entMan.TryGetComponent<NPCSteeringComponent>(npc, out var steering))
                {
                    status = steering.Status;
                    trace = $"failed paths {steering.FailedPathCount}, path {steering.CurrentPath.Count}, pathfinding {steering.Pathfind}";
                }
            });
            if (status == SteeringStatus.NoPath)
                break;
        }

        var where = Vector2.Zero;
        await server.WaitPost(() => where = xforms.GetWorldPosition(npc));
        Assert.That(status, Is.Not.EqualTo(SteeringStatus.NoPath), $"The NPC found no path and gave up at {where}: {trace}.");
        Assert.That(arrived, Is.True, $"The NPC never reached the far end; it got to {where}.");

        await server.WaitPost(() => entMan.DeleteEntity(map));
        await pair.CleanReturnAsync();
    }
}
