using System.Numerics;
using System.Collections.Generic;
using Content.Server._Forge.KIAS;
using Content.Server.Power.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasRoomLifecycleTests
{
    [Test]
    public async Task OpenGridFallbackExcludesDistantPeopleAndWallDevices()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var topology = em.System<KiasRoomTopologySystem>();
        EntityUid scanner = default, near = default, far = default, wallDevice = default;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x <= 8; x++)
            for (var y = 0; y <= 20; y++) maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            scanner = em.SpawnEntity("KiasRoomScanner", new EntityCoordinates(map.Grid, .5f, 10.5f));
            em.RemoveComponent<ApcPowerReceiverComponent>(scanner);
            near = em.SpawnEntity("BorgChassisGeneric", new EntityCoordinates(map.Grid, 6.5f, 10.5f));
            far = em.SpawnEntity("BorgChassisGeneric", new EntityCoordinates(map.Grid, 8.5f, 9.5f));
            em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 8.5f, 10.5f));
            wallDevice = em.SpawnEntity("KiasRoomScanner", new EntityCoordinates(map.Grid, 8.5f, 10.5f));
            em.System<SharedTransformSystem>().SetLocalRotation(wallDevice, Angle.FromDegrees(270));
            topology.Invalidate(map.Grid);
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(topology.TryGetScannerRoom(scanner, out _, out var status), Is.True);
            Assert.That(status, Is.EqualTo(KiasRoomStatus.OpenToSpace));
            Assert.That(topology.ContainsTargetForScanner(scanner, near), Is.True);
            Assert.That(topology.ContainsTargetForScanner(scanner, far), Is.False);
            Assert.That(topology.ContainsTargetForScanner(scanner, wallDevice), Is.False);
            TestContext.Out.WriteLine("Open grid: reachable person at 6 tiles covered; person and wall device beyond 7 tiles excluded.");
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(0, 2, 4)]
    [TestCase(90, 0, 2)]
    [TestCase(180, 2, 0)]
    [TestCase(270, 4, 2)]
    public async Task G01_G09_G12_WallOrientationAndBoundedBreach(int degrees, int sx, int sy)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var topology = em.System<KiasRoomTopologySystem>();
        EntityUid scanner = default, wall = default;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 5; y++)
            {
                maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                if (x != 0 && x != 4 && y != 0 && y != 4) continue;
                var uid = em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, x + .5f, y + .5f));
                if (x == 2 && y == 4) wall = uid;
            }
            scanner = em.SpawnEntity("KiasRoomScanner", new EntityCoordinates(map.Grid, sx + .5f, sy + .5f));
            em.RemoveComponent<ApcPowerReceiverComponent>(scanner);
            em.System<SharedTransformSystem>().SetLocalRotation(scanner, Angle.FromDegrees(degrees));
            topology.Invalidate(map.Grid);
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(topology.TryGetScannerRoom(scanner, out var room, out var status), Is.True, status.ToString());
            Assert.That(room.Status, Is.EqualTo(KiasRoomStatus.Ok));
            Assert.That(room.Tiles.Count, Is.EqualTo(9));
            em.System<SharedTransformSystem>().SetLocalRotation(scanner, Angle.FromDegrees(degrees + 180));
            Assert.That(topology.TryGetScannerRoom(scanner, out _, out status), Is.False);
            Assert.That(status, Is.EqualTo(KiasRoomStatus.RebuildPending));
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(topology.TryGetScannerRoom(scanner, out _, out var status), Is.False);
            Assert.That(status, Is.EqualTo(KiasRoomStatus.NoInteriorSeed));
            em.System<SharedTransformSystem>().SetLocalRotation(scanner, Angle.FromDegrees(degrees));
            em.DeleteEntity(wall);
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(topology.TryGetScannerRoom(scanner, out var room, out _), Is.True);
            Assert.That(room.Status, Is.EqualTo(KiasRoomStatus.OpenToSpace));
            Assert.That(room.Tiles.Count, Is.EqualTo(10));
            Assert.That(topology.Grids[map.Grid].Geometry.Interior.ContainsKey(new Vector2i(2, 5)), Is.False);
            TestContext.Out.WriteLine($"G01/G09/G12 rotation={degrees}; expected=9/10; actual={room.Tiles.Count}; status={room.Status}; revision={topology.Grids[map.Grid].Revision}");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task S08_G13_FortyGridSameTickWaveCommitsWithoutStaleCoverage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        var topology = em.System<KiasRoomTopologySystem>();
        var grids = new List<EntityUid>();
        var scanners = new List<EntityUid>();
        await pair.Server.WaitAssertion(() =>
        {
            for (var n = 0; n < 40; n++)
            {
                var grid = maps.CreateGridEntity(map.MapId);
                grids.Add(grid);
                em.System<SharedTransformSystem>().SetLocalPosition(grid, new Vector2(n * 20, 0));
                for (var x = 0; x < 4; x++)
                for (var y = 0; y < 3; y++) maps.SetTile(grid, grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                var scanner = em.SpawnEntity("KiasRoomScanner", new EntityCoordinates(grid, .5f, 2.5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(scanner);
                scanners.Add(scanner);
                topology.Invalidate(grid);
            }
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            for (var n = 0; n < 40; n++)
            {
                Assert.That(topology.TryGetScannerRoom(scanners[n], out var room, out _), Is.True);
                Assert.That(room.Tiles.Count, Is.EqualTo(12));
                Assert.That(topology.ContainsTarget(grids[n], room, scanners[(n + 1) % 40]), Is.False);
                em.SpawnEntity("WallSolid", new EntityCoordinates(grids[n], 2.5f, 1.5f));
                Assert.That(topology.TryGetScannerRoom(scanners[n], out _, out var status), Is.False);
                Assert.That(status, Is.EqualTo(KiasRoomStatus.RebuildPending));
            }
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            for (var n = 0; n < 40; n++)
            {
                Assert.That(topology.TryGetScannerRoom(scanners[n], out var room, out _), Is.True);
                Assert.That(room.Tiles.Count, Is.EqualTo(11));
                Assert.That(topology.Grids[grids[n]].MaxCommitTicks, Is.LessThanOrEqualTo(30));
            }
            TestContext.Out.WriteLine("S08: planned=40 attempted=40 committed=40; bounded latency<=30 ticks; stale positives=0");
        });
        await pair.CleanReturnAsync();
    }
}
