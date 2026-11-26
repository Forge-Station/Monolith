using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.StateRecovery;
using Content.IntegrationTests.Pair;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.StateRecovery;

/// <summary>
/// Healthy physics never triggers a request; repeated failed physics steps request a full game state, which gives a
/// desynced grid its missing chunk fixture back.
/// </summary>
[TestFixture]
[TestOf(typeof(WFStateRecoverySystem))]
public sealed class StateRecoveryTest
{
    [Test]
    public async Task FailedStepsRestoreDesyncedGrid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var cEntMan = client.EntMan;

        var map = await pair.CreateTestMap();
        var mapSys = sEntMan.System<SharedMapSystem>();
        EntityUid grid = default;
        await server.WaitPost(() =>
        {
            var created = mapSys.CreateGridEntity(map.MapId);
            grid = created.Owner;

            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 5; y++)
                {
                    tiles.Add((new Vector2i(x + 20, y), map.Tile.Tile));
                }
            }

            mapSys.SetTiles(grid, created.Comp, tiles);
        });

        await RunFor(pair, TimeSpan.FromSeconds(3));
        var recovery = cEntMan.System<WFStateRecoverySystem>();
        Assert.That(recovery.Requests, Is.Zero, "healthy physics requested a full state");

        // Same broken state as live: the client's grid lost a chunk fixture the chunk still names.
        var netGrid = sEntMan.GetNetEntity(grid);
        var removed = string.Empty;
        await client.WaitPost(() =>
        {
            var fixtures = cEntMan.GetComponent<FixturesComponent>(cEntMan.GetEntity(netGrid)).Fixtures;
            removed = fixtures.Keys.First(id => id.StartsWith("grid_chunk"));
            fixtures.Remove(removed);
        });

        // The test client rethrows tick exceptions, so stand in for the failed steps with unmatched before-solve events.
        await client.WaitPost(() =>
        {
            for (var i = 0; i < 15; i++)
            {
                var ev = new PhysicsUpdateBeforeSolveEvent(false, 0f);
                cEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            }
        });

        // The request logs a warning on purpose.
        var failureLevel = pair.ClientLogHandler.FailureLevel;
        pair.ClientLogHandler.FailureLevel = LogLevel.Error;
        try
        {
            await RunFor(pair, TimeSpan.FromSeconds(3));
        }
        finally
        {
            pair.ClientLogHandler.FailureLevel = failureLevel;
        }

        var restored = false;
        await client.WaitPost(() =>
        {
            restored = cEntMan.GetComponent<FixturesComponent>(cEntMan.GetEntity(netGrid)).Fixtures.ContainsKey(removed);
        });

        Assert.Multiple(() =>
        {
            Assert.That(recovery.Requests, Is.EqualTo(1));
            Assert.That(restored, Is.True, $"{removed} is still missing on the client");
        });

        await server.WaitPost(() => sEntMan.DeleteEntity(grid));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FailedStepsPurgeDeletedLookupEntry()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var cEntMan = client.EntMan;

        var map = await pair.CreateTestMap();
        var netMap = pair.Server.EntMan.GetNetEntity(map.MapUid);
        await RunFor(pair, TimeSpan.FromSeconds(3));
        var recovery = cEntMan.System<WFStateRecoverySystem>();
        var requests = recovery.Requests;

        // Same broken state as live: a map's sundries tree holds an entity that no longer exists.
        DynamicTree<EntityUid> tree = default!;
        var stale = EntityUid.Invalid;
        await client.WaitPost(() =>
        {
            tree = cEntMan.GetComponent<BroadphaseComponent>(cEntMan.GetEntity(netMap)).SundriesTree;
            stale = cEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            tree.Add(stale, new Box2(5000f, 5000f, 5001f, 5001f));
            cEntMan.DeleteEntity(stale);

            for (var i = 0; i < 15; i++)
            {
                var ev = new PhysicsUpdateBeforeSolveEvent(false, 0f);
                cEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            }
        });

        // The purge logs a warning on purpose.
        var failureLevel = pair.ClientLogHandler.FailureLevel;
        pair.ClientLogHandler.FailureLevel = LogLevel.Error;
        try
        {
            await RunFor(pair, TimeSpan.FromSeconds(3));
        }
        finally
        {
            pair.ClientLogHandler.FailureLevel = failureLevel;
        }

        Assert.Multiple(() =>
        {
            Assert.That(tree.Contains(stale), Is.False, "the deleted entity is still in the tree");
            Assert.That(recovery.Requests, Is.EqualTo(requests), "the purge should have made a full state unnecessary");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PurgesCannotPostponeFullStateForever()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var cEntMan = client.EntMan;

        var map = await pair.CreateTestMap();
        var netMap = pair.Server.EntMan.GetNetEntity(map.MapUid);
        await RunFor(pair, TimeSpan.FromSeconds(3));
        var recovery = cEntMan.System<WFStateRecoverySystem>();
        var requests = recovery.Requests;

        // Steps keep failing and every window finds a new deleted entry to purge.
        var failureLevel = pair.ClientLogHandler.FailureLevel;
        pair.ClientLogHandler.FailureLevel = LogLevel.Error;
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(7))
            {
                await client.WaitPost(() =>
                {
                    var tree = cEntMan.GetComponent<BroadphaseComponent>(cEntMan.GetEntity(netMap)).SundriesTree;
                    var stale = cEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
                    tree.Add(stale, new Box2(5000f, 5000f, 5001f, 5001f));
                    cEntMan.DeleteEntity(stale);

                    for (var i = 0; i < 5; i++)
                    {
                        var ev = new PhysicsUpdateBeforeSolveEvent(false, 0f);
                        cEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
                    }
                });

                await RunFor(pair, TimeSpan.FromSeconds(0.25));
            }

            // The last window's warning has to land before warnings count as failures again.
            await RunFor(pair, TimeSpan.FromSeconds(3));
        }
        finally
        {
            pair.ClientLogHandler.FailureLevel = failureLevel;
        }

        Assert.That(recovery.Requests, Is.GreaterThan(requests), "purges kept postponing the full state");
        await pair.CleanReturnAsync();
    }

    // The detector windows on real time, which pooled ticks outrun.
    private static async Task RunFor(TestPair pair, TimeSpan time)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < time)
        {
            await pair.RunTicksSync(1);
            await Task.Delay(20);
        }
    }
}
