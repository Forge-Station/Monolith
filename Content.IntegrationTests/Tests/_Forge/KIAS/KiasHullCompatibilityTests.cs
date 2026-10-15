using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Atmos.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Doors.Components;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasHullCompatibilityTests
{
    [TestCase("Striks")]
    [TestCase("SPunisher")]
    [TestCase("seeker")]
    [TestCase("Incinerator")]
    [TestCase("dreadnought")]
    [TestCase("Corvet")]
    [TestCase("briar")]
    [TestCase("achilles")]
    public async Task RealHullPowersPhysicalGraphAndRecoversData(string hull)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, ServerSeed = 20261011 });
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        var kias = em.System<KiasSystem>();
        var runtime = em.System<KiasControllerRuntimeSystem>();
        var topology = em.System<KiasRoomTopologySystem>();
        var path = new ResPath($"/SharedMaps/_Forge/Shuttles/Mercenary/{hull}.yml");
        EntityUid grid = default, core = default, rack = default, recorder = default, scanner = default, card = default;
        MapId map = default;
        var floors = new Dictionary<Vector2i, Tile>();
        var cables = new List<EntityUid>();
        var placements = new Dictionary<string, Vector2i>();
        var available = new List<Vector2i>();
        var checkpoints = new List<object>();
        string sourceHash = string.Empty;
        var marker = $"AUX_FIXTURE/{hull}/physical-graph";
        int beforeData = 0, beforePower = 0;

        EntityCoordinates At(Vector2i tile) => new(grid, tile.X + .5f, tile.Y + .5f);
        int Recorded() => em.GetComponent<KiasRecorderComponent>(recorder).Entries.Count(entry => entry.Contains(marker));
        void Capture(string stage) => checkpoints.Add(new
        {
            stage, active = kias.ActiveGrids.Contains(grid), running = runtime.Running(card), recorded = Recorded(),
            devices = new[] { core, rack, recorder, scanner }.Select(uid => new
            {
                uid = uid.ToString(), online = kias.IsOnline(uid), status = em.GetComponent<KiasDeviceComponent>(uid).Status.ToString(),
                needsPower = em.GetComponent<ApcPowerReceiverComponent>(uid).NeedsPower,
                powered = em.GetComponent<ApcPowerReceiverComponent>(uid).Powered,
                nativeProvider = em.GetComponent<ApcPowerReceiverComponent>(uid).Provider != null
            }).ToArray()
        });
        void AssertOnline()
        {
            foreach (var uid in new[] { core, rack, recorder, scanner })
            {
                var power = em.GetComponent<ApcPowerReceiverComponent>(uid);
                Assert.That(power.NeedsPower && power.Powered && power.Provider != null, Is.True, $"{hull}: native power {uid}");
                Assert.That(kias.IsOnline(uid), Is.True, $"{hull}: device {uid}");
            }
            Assert.That(runtime.Running(card), Is.True);
        }

        await server.WaitAssertion(() =>
        {
            using (var stream = server.ResolveDependency<IResourceManager>().ContentFileRead(path))
                sourceHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            maps.CreateMap(out map, runMapInit: false);
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map, path, out var loaded), Is.True);
            grid = loaded!.Value.Owner;
            var component = em.GetComponent<MapGridComponent>(grid);
            var tiles = maps.GetAllTiles(grid, component);
            while (tiles.MoveNext(out var tile))
                if (tile is { } value && !value.Tile.IsEmpty) floors.Add(value.GridIndices, value.Tile);
            var free = new List<Vector2i>();
            foreach (var tile in floors.Keys.OrderBy(tile => tile.X).ThenBy(tile => tile.Y))
            {
                var blocked = false;
                var anchored = maps.GetAnchoredEntities(grid, component, tile);
                while (anchored.MoveNext(out var uid))
                    blocked |= em.HasComponent<AirtightComponent>(uid) || em.HasComponent<DoorComponent>(uid)
                        || em.TryGetComponent<PhysicsComponent>(uid, out var physics) && physics.CanCollide;
                if (!blocked) free.Add(tile);
            }
            Assert.That(free.Count, Is.GreaterThanOrEqualTo(5), $"{hull}: five distinct existing floor cells required");
            available.AddRange(free);
            foreach (var tile in floors.Keys)
            {
                cables.Add(em.SpawnEntity("KiasDataCable", At(tile)));
                var anchored = maps.GetAnchoredEntities(grid, component, tile);
                var hasPowerCable = false;
                while (anchored.MoveNext(out var uid))
                    hasPowerCable |= em.TryGetComponent<CableComponent>(uid, out var cable) && cable.CableType == CableType.Apc;
                if (!hasPowerCable) em.SpawnEntity("CableApcExtension", At(tile));
            }
            EntityUid Install(string prototype, int index)
            {
                placements.Add(prototype, free[index]);
                return em.SpawnEntity(prototype, At(free[index]));
            }
            core = Install("KiasCore", 0);
            rack = Install("KiasControllerRack", 1);
            recorder = Install("KiasRecorder", 2);
            scanner = Install("KiasRoomScanner", 3);
            Install("APCHyperCapacity", 4);
            card = em.SpawnEntity("KiasProgrammableController", At(free[1]));
            var program = em.GetComponent<KiasControllerCardComponent>(card).Program;
            program.Name = marker;
            program.Nodes.Add(new() { Id = 1, Kind = KiasNodeKind.OnStart });
            program.Nodes.Add(new() { Id = 2, Kind = KiasNodeKind.All, Profile = "Recorder" });
            program.Nodes.Add(new() { Id = 3, Kind = KiasNodeKind.StringConstant, Config = new() { Text = marker } });
            program.Wires.Add(new() { FromNode = 1, FromPort = "Started", ToNode = 2, ToPort = "Record" });
            program.Wires.Add(new() { FromNode = 3, FromPort = "Value", ToNode = 2, ToPort = "Message" });
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
            maps.InitializeMap(map);
            var batteries = em.AllEntityQueryEnumerator<BatteryComponent, TransformComponent>();
            while (batteries.MoveNext(out var uid, out var battery, out var transform))
                if (transform.GridUid == grid) em.System<BatterySystem>().SetCharge(uid, battery.MaxCharge);
        });
        await pair.RunTicksSync(600);
        await server.WaitAssertion(() =>
        {
            if (topology.ScannerCells(scanner).Any()) return;
            var geometry = topology.Grids[grid].Geometry;
            var occupied = placements.Where(item => item.Key != "KiasRoomScanner").Select(item => item.Value).ToHashSet();
            foreach (var tile in available.Where(tile => !occupied.Contains(tile)))
            for (var turn = 0; turn < 4; turn++)
            {
                var rotation = Angle.FromDegrees(turn * 90);
                var seed = geometry.ScannerSeed(tile, KiasRoomTopologySystem.Inward(rotation));
                if (geometry.At(seed) is not { Usable: true }) continue;
                var transforms = em.System<SharedTransformSystem>();
                transforms.Unanchor(scanner);
                transforms.SetCoordinates(scanner, At(tile));
                transforms.SetLocalRotation(scanner, rotation);
                transforms.AnchorEntity(scanner);
                placements["KiasRoomScanner"] = tile;
                return;
            }
            Assert.Fail($"{hull}: no existing free mount faces a usable native room");
        });
        await pair.RunTicksSync(180);
        await server.WaitAssertion(() =>
        {
            AssertOnline();
            Assert.That(Recorded(), Is.GreaterThan(0), "Physical OnStart graph must dispatch into the native recorder");
            Assert.That(topology.ScannerCells(scanner), Is.Not.Empty, "Scanner must bind to actual hull geometry");
            Capture("powered-physical-graph");
            beforeData = Recorded();
            foreach (var cable in cables) em.DeleteEntity(cable);
        });
        await pair.RunTicksSync(180);
        await server.WaitAssertion(() =>
        {
            Assert.That(kias.IsOnline(rack), Is.False);
            Assert.That(runtime.Running(card), Is.False);
            Capture("data-cut");
            foreach (var tile in floors.Keys) em.SpawnEntity("KiasDataCable", At(tile));
        });
        await pair.RunTicksSync(300);
        await server.WaitAssertion(() =>
        {
            AssertOnline();
            Assert.That(Recorded(), Is.GreaterThan(beforeData));
            Capture("data-restored");
            beforePower = Recorded();
            em.System<SharedPowerReceiverSystem>().SetPowerDisabled(core, true);
        });
        await pair.RunTicksSync(180);
        await server.WaitAssertion(() =>
        {
            Assert.That(kias.ActiveGrids, Does.Not.Contain(grid));
            Assert.That(runtime.Running(card), Is.False);
            Capture("core-power-cut");
            em.System<SharedPowerReceiverSystem>().SetPowerDisabled(core, false);
        });
        await pair.RunTicksSync(300);
        await server.WaitAssertion(() =>
        {
            AssertOnline();
            Assert.That(Recorded(), Is.GreaterThan(beforePower));
            Capture("core-power-restored");
            var component = em.GetComponent<MapGridComponent>(grid);
            foreach (var (tile, expected) in floors)
                Assert.That(maps.GetTileRef(grid, component, tile).Tile, Is.EqualTo(expected), "Original hull floor changed");
            using var stream = server.ResolveDependency<IResourceManager>().ContentFileRead(path);
            Assert.That(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), Is.EqualTo(sourceHash));
            if (Environment.GetEnvironmentVariable("KIAS_LAB_OUTPUT") is { Length: > 0 } output)
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, $"hull-{hull}.json"), JsonSerializer.Serialize(new
                {
                    status = "PASS", hull, sourceMap = path.ToString(), sourceHash, fixture = "AUX_FIXTURE",
                    scope = "Real hull, native power, physical card/recorder, scanner geometry, DATA and core-power recovery; not full combat/FTL coverage",
                    originalFloorCount = floors.Count,
                    placements = placements.ToDictionary(item => item.Key, item => new[] { item.Value.X, item.Value.Y }),
                    scannerRotation = em.GetComponent<TransformComponent>(scanner).LocalRotation.Degrees,
                    checkpoints, roomGeometry = topology.DebugSnapshot(grid)
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
        });
        await pair.CleanReturnAsync();
    }
}
