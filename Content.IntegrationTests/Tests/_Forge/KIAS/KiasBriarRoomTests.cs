using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using Content.Server._Forge.KIAS;
using Content.Shared._Forge.KIAS;
using Content.Server.Power.EntitySystems;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;
using System.Linq;
using System.Numerics;
using Content.Server._Forge.KIAS.Controllers;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasBriarRoomTests
{
    [Test]
    public async Task SavedBriarRoomGeometryExportsAllScannerPlacements()
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        EntityUid grid = default;
        MapId mapId = default;
        await pair.Server.WaitAssertion(() =>
        {
            maps.CreateMap(out mapId, runMapInit: false);
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(mapId,
                new ResPath("/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml"), out var loaded), Is.True);
            grid = loaded!.Value.Owner;
            maps.InitializeMap(mapId);
            var batteries = em.AllEntityQueryEnumerator<BatteryComponent, TransformComponent>();
            while (batteries.MoveNext(out var uid, out var battery, out var transform))
                if (transform.GridUid == grid) em.System<BatterySystem>().SetCharge(uid, battery.MaxCharge);
            em.System<KiasRoomTopologySystem>().Invalidate(grid);
        });
        await pair.RunTicksSync(1800);
        await pair.Server.WaitAssertion(() =>
        {
            var topology = em.System<KiasRoomTopologySystem>();
            Assert.That(topology.Grids.ContainsKey(grid), Is.True);
            Assert.That(topology.Grids[grid].Pending, Is.False);
            var snapshot = JsonSerializer.Serialize(topology.DebugSnapshot(grid), new JsonSerializerOptions { WriteIndented = true });
            TestContext.Out.WriteLine(snapshot);
            if (Environment.GetEnvironmentVariable("KIAS_LAB_OUTPUT") is { Length: > 0 } output)
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "briar-room-map.json"), snapshot);
            }
            using var json = JsonDocument.Parse(snapshot);
            Assert.That(json.RootElement.GetProperty("scanners").GetArrayLength(), Is.EqualTo(21));
            foreach (var scanner in json.RootElement.GetProperty("scanners").EnumerateArray())
            {
                if (scanner.GetProperty("x").GetInt32() is not (-11 or -5) || scanner.GetProperty("y").GetInt32() != 2) continue;
                Assert.That(scanner.GetProperty("rotation").GetDouble(), Is.Zero);
                Assert.That(scanner.GetProperty("status").GetString(), Is.EqualTo("Ok"));
                Assert.That(scanner.GetProperty("tileCount").GetInt32(), Is.GreaterThan(0));
                Assert.That(scanner.GetProperty("seed")[0].GetInt32(), Is.EqualTo(scanner.GetProperty("x").GetInt32()));
                Assert.That(scanner.GetProperty("seed")[1].GetInt32(), Is.EqualTo(1));
            }
            foreach (var rotation in new[] { 0d, 90d, 180d, -90d })
                Assert.That(json.RootElement.GetProperty("scanners").EnumerateArray().Any(scanner =>
                    scanner.GetProperty("rotation").GetDouble() == rotation && scanner.GetProperty("status").GetString() == "Ok"), Is.True);
            var externalScanners = em.AllEntityQueryEnumerator<KiasRoomScannerComponent, TransformComponent>();
            var exteriorCount = 0;
            while (externalScanners.MoveNext(out var uid, out _, out var transform))
            {
                if (transform.GridUid != grid) continue;
                var tile = maps.TileIndicesFor(grid, em.GetComponent<MapGridComponent>(grid), transform.Coordinates);
                if (tile.Y != 35 || tile.X is not (-1 or 3)) continue;
                Assert.That(topology.TryGetScannerRoom(uid, out var exterior, out var mode), Is.True);
                Assert.That(mode, Is.EqualTo(KiasRoomStatus.ExteriorSector));
                var outward = KiasRoomTopologySystem.Inward(transform.LocalRotation);
                var cells = topology.ScannerCells(uid).ToArray();
                Assert.That(cells, Is.Not.Empty);
                Assert.That(cells.Any(cell => !topology.Grids[grid].Geometry.Floors.ContainsKey(cell)), Is.True);
                var targetCell = cells.First(cell => !topology.Grids[grid].Geometry.Floors.ContainsKey(cell));
                var foreign = maps.CreateGridEntity(mapId);
                maps.SetTile(foreign, Vector2i.Zero, maps.GetTileRef(grid, em.GetComponent<MapGridComponent>(grid), new Vector2i(-5, 0)).Tile);
                var transforms = em.System<SharedTransformSystem>();
                transforms.SetWorldPosition(foreign, new Vector2(targetCell.X, targetCell.Y));
                var creature = em.SpawnEntity("MobCarp", new EntityCoordinates(foreign, .5f, .5f));
                transforms.SetParent(creature, foreign);
                Assert.That(topology.Contains(uid, creature, KiasScannerModules.Threat), Is.True);
                Assert.That(topology.Contains(uid, creature, KiasScannerModules.Spectral), Is.True);
                Assert.That(topology.Contains(uid, creature, KiasScannerModules.Connector), Is.False);
                Assert.That(topology.ContainsTargetForScanner(uid, creature), Is.False);
                em.System<KiasCrewSystem>().RefreshCounts(grid);
                Assert.That(em.System<KiasControllerIoSystem>().TryOutput(uid, "RoomScanner", "FaunaThreat", out _, out _), Is.True);
                transforms.SetWorldPosition(foreign, new Vector2(tile.X + outward.X * 40, tile.Y + outward.Y * 40));
                Assert.That(topology.Contains(uid, creature, KiasScannerModules.Threat), Is.False);
                em.DeleteEntity(foreign);
                exteriorCount++;
            }
            Assert.That(exteriorCount, Is.EqualTo(2));
            var bodies = new List<object>();
            var query = em.AllEntityQueryEnumerator<KiasDeviceComponent, PhysicsComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var body, out var transform))
            {
                if (transform.GridUid != grid) continue;
                var bodyType = body.BodyType;
                bodies.Add(new { uid = uid.ToString(), prototype = em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID,
                    bodyType = bodyType.ToString(), body.Awake, body.CanCollide, body.ContactCount,
                    fixtures = em.TryGetComponent<FixturesComponent>(uid, out var fixtures) ? fixtures.FixtureCount : 0 });
            }
            var physics = JsonSerializer.Serialize(bodies, new JsonSerializerOptions { WriteIndented = true });
            TestContext.Out.WriteLine(physics);
            if (Environment.GetEnvironmentVariable("KIAS_LAB_OUTPUT") is { Length: > 0 } physicsOutput)
                File.WriteAllText(Path.Combine(physicsOutput, "briar-physics-inventory.json"), physics);
        });
        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task AdvancedInteriorScannerDoesNotSwitchToExteriorAfterNativeFloorBreach()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        var rooms = em.System<KiasRoomTopologySystem>();
        EntityUid grid = default, scanner = default;
        Tile original = default;
        await pair.Server.WaitAssertion(() =>
        {
            maps.CreateMap(out var map, runMapInit: false);
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map,
                new ResPath("/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml"), out var loaded), Is.True);
            grid = loaded!.Value.Owner;
            maps.InitializeMap(map);
            var batteries = em.AllEntityQueryEnumerator<BatteryComponent, TransformComponent>();
            while (batteries.MoveNext(out var uid, out var battery, out var transform))
                if (transform.GridUid == grid) em.System<BatterySystem>().SetCharge(uid, battery.MaxCharge);
        });
        await pair.RunTicksSync(600);
        await pair.Server.WaitAssertion(() =>
        {
            var scanners = em.AllEntityQueryEnumerator<KiasRoomScannerComponent, TransformComponent>();
            while (scanners.MoveNext(out var uid, out var component, out var transform))
                if (transform.GridUid == grid && transform.LocalPosition == new Vector2(-10.5f, 2.5f))
                { scanner = uid; component.Advanced = true; }
            Assert.That(scanner.Valid, Is.True);
            Assert.That(rooms.TryGetScannerRoom(scanner, out _, out var status), Is.True);
            Assert.That(status, Is.EqualTo(KiasRoomStatus.Ok));
            var seed = new Vector2i(-11, 1);
            original = maps.GetTileRef(grid, em.GetComponent<MapGridComponent>(grid), seed).Tile;
            maps.SetTile(grid, em.GetComponent<MapGridComponent>(grid), seed, Tile.Empty);
        });
        await pair.RunTicksSync(180);
        await pair.Server.WaitAssertion(() =>
        {
            rooms.TryGetScannerRoom(scanner, out _, out var status);
            Assert.That(status, Is.Not.EqualTo(KiasRoomStatus.ExteriorSector));
            Assert.That(status, Is.Not.EqualTo(KiasRoomStatus.RebuildPending));
            maps.SetTile(grid, em.GetComponent<MapGridComponent>(grid), new Vector2i(-11, 1), original);
        });
        await pair.RunTicksSync(180);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(rooms.TryGetScannerRoom(scanner, out _, out var status), Is.True);
            Assert.That(status, Is.EqualTo(KiasRoomStatus.Ok));
        });
        await pair.CleanReturnAsync();
    }

}
