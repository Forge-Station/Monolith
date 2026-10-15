using System.Numerics;
using System.Linq;
using System.Text.Json;
using Content.Server._Forge.KIAS;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared.Atmos;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Power;
using Content.Shared.Mind;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasRoomTopologyTests
{
    [TestCase(0, 1)]
    [TestCase(0, -1)]
    [TestCase(1, 0)]
    [TestCase(-1, 0)]
    public void ExteriorSectorIsFiniteFacesOutwardAndDoesNotSeeThroughHull(int x, int y)
    {
        var geometry = new KiasRoomGeometry();
        var outward = new Vector2i(x, y);
        var tangent = new Vector2i(-y, x);
        for (var i = -5; i <= 5; i++) geometry.Floors.Add(tangent * i, AtmosDirection.All);
        geometry.Floors.Add(outward * 3 + tangent * 2, AtmosDirection.All);
        var cells = geometry.ExteriorCoverage(Vector2i.Zero, outward, 7);
        Assert.That(cells, Does.Contain(outward * 7));
        Assert.That(cells, Does.Not.Contain(outward * 8));
        Assert.That(cells, Does.Not.Contain(-outward));
        Assert.That(cells, Does.Not.Contain(outward * 6 + tangent * 4));
        foreach (var cell in cells)
        {
            Assert.That(cell.X * outward.X + cell.Y * outward.Y, Is.GreaterThan(0));
            Assert.That(cell.X * cell.X + cell.Y * cell.Y, Is.LessThanOrEqualTo(49));
            Assert.That(geometry.Floors.ContainsKey(cell), Is.False);
        }
        Assert.That(geometry.ExteriorCoverage(Vector2i.Zero, -outward, 7), Is.Not.Empty);
    }

    [Test]
    public void ExteriorSectorRejectsInteriorSeedsAndUnboundedReach()
    {
        var geometry = new KiasRoomGeometry();
        geometry.Floors.Add(Vector2i.Zero, AtmosDirection.All);
        geometry.Floors.Add(new(0, 1), AtmosDirection.Invalid);
        Assert.That(geometry.ExteriorCoverage(Vector2i.Zero, new(0, 1), 7), Is.Empty);
        Assert.That(geometry.ExteriorCoverage(new(5, 5), new(0, 1), 7), Is.Empty);
        var cells = geometry.ExteriorCoverage(Vector2i.Zero, new(0, -1), int.MaxValue);
        Assert.That(cells.Count, Is.LessThan(3300));
        Assert.That(cells, Does.Not.Contain(new Vector2i(0, -33)));
    }

    [Test]
    public void ExteriorSectorIncludesOpenLatticeButStopsAtItsHull()
    {
        var geometry = new KiasRoomGeometry();
        geometry.Floors.Add(Vector2i.Zero, AtmosDirection.All);
        for (var y = 1; y <= 3; y++)
        {
            geometry.Floors.Add(new(0, y), AtmosDirection.Invalid);
            geometry.SpaceDeck.Add(new(0, y));
        }
        geometry.Floors.Add(new(1, 2), AtmosDirection.All);
        var cells = geometry.ExteriorCoverage(Vector2i.Zero, new(0, 1), 7);
        Assert.That(cells, Does.Contain(new Vector2i(0, 1)));
        Assert.That(cells, Does.Contain(new Vector2i(0, 3)));
        Assert.That(cells, Does.Contain(new Vector2i(0, 7)));
        Assert.That(cells, Does.Not.Contain(new Vector2i(1, 2)));
        Assert.That(cells, Does.Not.Contain(new Vector2i(2, 4)));
    }

    [TestCase(0, 0, -1)]
    [TestCase(90, 1, 0)]
    [TestCase(180, 0, 1)]
    [TestCase(270, -1, 0)]
    public void G01_AuthoritativeRotation(int degrees, int x, int y)
    {
        Assert.That(KiasRoomTopologySystem.Inward(Angle.FromDegrees(degrees)), Is.EqualTo(new Vector2i(x, y)));
    }

    [Test]
    public void G06_LongBentInteriorHasNoRadiusCap()
    {
        var geometry = new KiasRoomGeometry();
        for (var x = 0; x < 40; x++) geometry.Floors.Add(new Vector2i(x, 0), AtmosDirection.North | AtmosDirection.South);
        for (var y = 1; y < 30; y++) geometry.Floors.Add(new Vector2i(39, y), AtmosDirection.East | AtmosDirection.West);
        geometry.Floors[new Vector2i(39, 0)] = AtmosDirection.South | AtmosDirection.East;
        foreach (var _ in geometry.Build()) { }
        Assert.That(geometry.Rooms, Has.Count.EqualTo(1));
        Assert.That(geometry.Rooms[0].Tiles, Has.Count.EqualTo(69));
        Assert.That(geometry.Contains(geometry.Rooms[0], new Vector2i(39, 29)), Is.True);
    }

    [Test]
    public void G07_CardinalEdgesBlockBothDirectionsAndCorners()
    {
        var geometry = new KiasRoomGeometry();
        geometry.Floors.Add(new(0, 0), AtmosDirection.East);
        geometry.Floors.Add(new(1, 0), AtmosDirection.Invalid);
        geometry.Floors.Add(new(2, 1), AtmosDirection.Invalid);
        foreach (var _ in geometry.Build()) { }
        Assert.That(geometry.Rooms, Has.Count.EqualTo(3));
        Assert.That(geometry.At(new(0, 0)), Is.Not.SameAs(geometry.At(new(1, 0))));
    }

    [Test]
    public void G15_OverLimitDoesNotExposePartialCoverage()
    {
        var geometry = new KiasRoomGeometry();
        for (var x = 0; x < 4100; x++) geometry.Floors.Add(new(x, 0), AtmosDirection.North | AtmosDirection.South);
        foreach (var _ in geometry.Build()) { }
        Assert.That(geometry.Rooms, Has.Count.EqualTo(1));
        Assert.That(geometry.Rooms[0].Status, Is.EqualTo(KiasRoomStatus.RoomTooLarge));
        Assert.That(geometry.Contains(geometry.Rooms[0], new(0, 0)), Is.False);
    }

    [Test]
    public void OpenAreaFallbackUsesReachableGridTilesWithinSevenAndStopsAtDoors()
    {
        var geometry = new KiasRoomGeometry();
        for (var x = -10; x <= 10; x++)
        for (var y = -10; y <= 10; y++)
        {
            geometry.Floors.Add(new(x, y), AtmosDirection.Invalid);
            if (x == 0) geometry.DoorTiles.Add(new(x, y));
        }
        foreach (var _ in geometry.Build()) { }
        var room = geometry.At(new(1, 0))!;
        Assert.That(room.Status, Is.EqualTo(KiasRoomStatus.OpenToSpace));
        var scanner = new Vector2i(1, -1);
        var cells = geometry.OpenCoverage(room, new(1, 0), scanner);
        Assert.That(cells, Does.Contain(new Vector2i(8, -1)));
        Assert.That(cells, Does.Not.Contain(new Vector2i(9, -1)));
        Assert.That(cells, Does.Contain(new Vector2i(0, -1)));
        Assert.That(cells, Does.Not.Contain(new Vector2i(-1, -1)));
        foreach (var cell in cells)
        {
            var offset = cell - scanner;
            Assert.That(offset.X * offset.X + offset.Y * offset.Y, Is.LessThanOrEqualTo(49));
            Assert.That(geometry.Floors.ContainsKey(cell), Is.True);
        }
    }

    [TestCase("Airlock")]
    [TestCase("Firelock")]
    public async Task G02_G03_G04_G05_G08_OpenDoorsRemainSharedTerminals(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var topology = em.System<KiasRoomTopologySystem>();
        EntityUid left = default, right = default, door = default, wall = default, crewServer = default, person = default;
        EntityUid Spawn(string id, int x, int y)
        {
            var uid = em.SpawnEntity(id, new EntityCoordinates(map.Grid, x + .5f, y + .5f));
            if (id.StartsWith("Kias") || id == prototype) em.RemoveComponent<ApcPowerReceiverComponent>(uid);
            if (id == prototype)
            {
                // Изолируем геометрию от сети питания, сохраняя штатный цикл двери.
                var powered = new PowerChangedEvent(true, 100);
                em.EventBus.RaiseLocalEvent(uid, ref powered);
            }
            return uid;
        }
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x <= 8; x++)
            for (var y = 0; y <= 6; y++) maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            for (var x = 0; x <= 8; x++) { Spawn("WallSolid", x, 0); Spawn("WallSolid", x, 6); Spawn("KiasDataCable", x, 6); }
            for (var y = 1; y < 6; y++)
            {
                Spawn("WallSolid", 0, y); Spawn("WallSolid", 8, y);
                if (y == 3) door = Spawn(prototype, 4, y);
                else { var separator = Spawn("WallSolid", 4, y); if (y == 2) wall = separator; }
            }
            Spawn("KiasCore", 0, 6); crewServer = Spawn("KiasCrewServer", 1, 6);
            left = Spawn("KiasRoomScanner", 2, 6); right = Spawn("KiasRoomScanner", 6, 6);
            em.System<KiasSystem>().Rebuild(map.Grid);
            topology.Invalidate(map.Grid);
        });
        await pair.RunTicksSync(90);
        uint revision = 0;
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(topology.TryGetScannerRoom(left, out var a, out var status), Is.True, status.ToString());
            Assert.That(topology.TryGetScannerRoom(right, out var b, out _), Is.True);
            Assert.That(a, Is.Not.SameAs(b));
            Assert.That(a.Tiles.Count, Is.EqualTo(15)); Assert.That(b.Tiles.Count, Is.EqualTo(15));
            Assert.That(a.Doors, Does.Contain(new Vector2i(4, 3)));
            Assert.That(b.Doors, Does.Contain(new Vector2i(4, 3)));
            Assert.That(topology.ContainsTarget(map.Grid, a, door), Is.True);
            Assert.That(topology.ContainsTarget(map.Grid, b, door), Is.True);
            revision = topology.Grids[map.Grid].Revision;
            Assert.That(em.System<SharedDoorSystem>().TryOpen(door), Is.True);
        });
        await pair.RunTicksSync(60);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<DoorComponent>(door).State, Is.EqualTo(DoorState.Open));
            Assert.That(topology.Grids[map.Grid].Revision, Is.EqualTo(revision));
            Assert.That(topology.TryGetScannerRoom(left, out var a, out _), Is.True);
            Assert.That(topology.TryGetScannerRoom(right, out var b, out _), Is.True);
            Assert.That(a, Is.Not.SameAs(b));
            person = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 4.5f, 3.5f));
            var player = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var minds = em.System<SharedMindSystem>();
            minds.TransferTo(minds.CreateMind(player.UserId), person);
            var token = em.SpawnEntity("KiasCrewTransponder", new EntityCoordinates(map.Grid, 4.5f, 3.5f));
            Assert.That(em.System<SharedHandsSystem>().TryPickup(person, token), Is.True);
            em.EventBus.RaiseLocalEvent(crewServer, new InteractUsingEvent(person, token, crewServer, em.GetComponent<TransformComponent>(crewServer).Coordinates));
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            var crew = em.System<KiasCrewSystem>();
            var runtime = em.GetComponent<KiasGridComponent>(map.Grid);
            crew.RefreshCounts(map.Grid);
            Assert.That(em.GetComponent<KiasRoomScannerComponent>(left).Entities, Is.EqualTo(1));
            Assert.That(em.GetComponent<KiasRoomScannerComponent>(right).Entities, Is.EqualTo(1));
            Assert.That(runtime.Entities, Is.EqualTo(1));
            Assert.That(runtime.Crew, Is.EqualTo(1));
            em.System<SharedTransformSystem>().SetCoordinates(person, new EntityCoordinates(map.Grid, 6.5f, 3.5f));
            crew.RefreshCounts(map.Grid);
            Assert.That(em.GetComponent<KiasRoomScannerComponent>(left).Entities, Is.Zero);
            Assert.That(em.GetComponent<KiasRoomScannerComponent>(right).Entities, Is.EqualTo(1));
            Assert.That(runtime.Entities, Is.EqualTo(1));
            Assert.That(runtime.Crew, Is.EqualTo(1));
            em.DeleteEntity(person);
            em.DeleteEntity(door);
        });
        await pair.RunTicksSync(60);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(topology.TryGetScannerRoom(left, out var a, out _), Is.True);
            Assert.That(topology.TryGetScannerRoom(right, out var b, out _), Is.True);
            Assert.That(a, Is.SameAs(b));
            door = Spawn(prototype, 4, 3);
        });
        await pair.RunTicksSync(60);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(topology.TryGetScannerRoom(left, out var a, out _), Is.True);
            Assert.That(topology.TryGetScannerRoom(right, out var b, out _), Is.True);
            Assert.That(a, Is.Not.SameAs(b));
            em.DeleteEntity(wall);
        });
        await pair.RunTicksSync(60);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(topology.TryGetScannerRoom(left, out var a, out _), Is.True);
            Assert.That(topology.TryGetScannerRoom(right, out var b, out _), Is.True);
            Assert.That(a, Is.SameAs(b));
            Spawn("WallSolid", 4, 2);
        });
        await pair.RunTicksSync(60);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(topology.TryGetScannerRoom(left, out var a, out _), Is.True);
            Assert.That(topology.TryGetScannerRoom(right, out var b, out _), Is.True);
            Assert.That(a, Is.Not.SameAs(b));
            TestContext.Out.WriteLine(JsonSerializer.Serialize(new { matrix = "G02/G03/G04/G05/G08", expectedTiles = 15,
                actualLeft = a.Tiles.Count, actualRight = b.Tiles.Count, revision = topology.Grids[map.Grid].Revision,
                latency = topology.Grids[map.Grid].MaxCommitTicks, prototype }));
        });
        await pair.CleanReturnAsync();
    }
}
