using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Power.EntitySystems;
using Content.Server.Atmos.Monitor.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Power.Components;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasBriarLabTests
{
    [Test]
    public async Task SavedBriarReceivesRealBatteryPowerAndResolvesGraphTargets()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        EntityUid grid = default;
        MapId mapId = default;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out mapId);
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(mapId,
                new ResPath("/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml"), out var loaded), Is.True);
            grid = loaded!.Value.Owner;
            var batteries = em.EntityQueryEnumerator<BatteryComponent, TransformComponent>();
            while (batteries.MoveNext(out var uid, out var battery, out var transform))
                if (transform.GridUid == grid)
                    em.System<BatterySystem>().SetCharge(uid, battery.MaxCharge, battery);
        });
        await pair.RunTicksSync(600);
        await server.WaitAssertion(() =>
        {
            var entities = new List<EntityUid>();
            var query = em.EntityQueryEnumerator<TransformComponent>();
            while (query.MoveNext(out var uid, out var transform))
                if (transform.GridUid == grid)
                    entities.Add(uid);
            var io = em.System<KiasControllerIoSystem>();
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var programs = prototypes.EnumeratePrototypes<KiasControllerProgramPrototype>().OrderBy(p => p.ID).Select(p =>
            {
                var compiled = KiasGraphCompiler.Compile(p.Program, io.Schema, io.SnapshotSchema);
                var targets = p.Program.Nodes.Where(n => n.Profile.Length > 0).Select(n => new
                {
                    node = n.Id, profile = n.Profile, selected = io.Match(grid, n).Select(uid => uid.ToString()).ToArray(),
                    supporting = entities.Where(uid => io.Supports(uid, n.Profile)).Select(uid => uid.ToString()).ToArray(),
                    online = entities.Where(uid => io.Supports(uid, n.Profile) && em.System<KiasSystem>().IsOnline(uid)).Select(uid => uid.ToString()).ToArray()
                }).ToArray();
                return new { preset = p.ID, compileSuccess = compiled.Success, errors = compiled.Errors, targets };
            }).ToArray();
            var devices = entities.Where(em.HasComponent<KiasDeviceComponent>).Select(uid => new
            {
                uid = uid.ToString(), prototype = em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID,
                status = em.GetComponent<KiasDeviceComponent>(uid).Status.ToString(),
                x = em.GetComponent<TransformComponent>(uid).LocalPosition.X,
                y = em.GetComponent<TransformComponent>(uid).LocalPosition.Y,
                identifier = em.GetComponent<KiasDeviceComponent>(uid).Identifier,
                online = em.System<KiasSystem>().IsOnline(uid)
            }).ToArray();
            var cards = entities.Where(em.HasComponent<KiasControllerCardComponent>).Select(uid => new
            {
                uid = uid.ToString(), name = em.GetComponent<KiasControllerCardComponent>(uid).Program.Name,
                enabled = em.GetComponent<KiasControllerCardComponent>(uid).Enabled,
                running = em.System<KiasControllerRuntimeSystem>().Running(uid),
                fault = em.System<KiasControllerRuntimeSystem>().Fault(uid)
            }).ToArray();
            var snapshot = new { stage = "battery-powered", tick = server.ResolveDependency<IGameTiming>().CurTick.Value,
                active = em.System<KiasSystem>().ActiveGrids.Contains(grid), devices, cards, programs,
                alarms = entities.Where(uid => em.HasComponent<AirAlarmComponent>(uid) || em.HasComponent<FireAlarmComponent>(uid))
                    .Select(uid => new { uid = uid.ToString(),
                        prototype = em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID,
                        airAlarm = em.HasComponent<AirAlarmComponent>(uid), fireAlarm = em.HasComponent<FireAlarmComponent>(uid),
                        integrated = em.HasComponent<KiasIntegratedComponent>(uid),
                        controllable = em.System<KiasIntegrationSystem>().CanControl(uid) }).ToArray(),
                pdcWeapons = entities.Where(em.HasComponent<KiasPdcWeaponComponent>).Select(uid => uid.ToString()).ToArray(),
                scanners = entities.Where(em.HasComponent<KiasRoomScannerComponent>).Select(uid => new
                {
                    uid = uid.ToString(), prototype = em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID,
                    x = em.GetComponent<TransformComponent>(uid).LocalPosition.X,
                    y = em.GetComponent<TransformComponent>(uid).LocalPosition.Y,
                    rotation = em.GetComponent<TransformComponent>(uid).LocalRotation.ToString(),
                    modules = em.GetComponent<KiasRoomScannerComponent>(uid).Modules.ToString(),
                    room = em.GetComponent<KiasDeviceComponent>(uid).Room,
                    online = em.System<KiasSystem>().IsOnline(uid)
                }).ToArray() };
            var output = Environment.GetEnvironmentVariable("KIAS_LAB_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "briar-powered.json"),
                    JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            }
            Assert.That(programs, Has.Length.EqualTo(27));
            Assert.That(programs.All(p => p.compileSuccess), Is.True);
            Assert.That(snapshot.active, Is.True, "Charged real batteries must supply the core via the original power nets.");
            Assert.That(snapshot.scanners, Has.Length.EqualTo(21));
            Assert.That(snapshot.scanners.All(scanner => scanner.online), Is.True);
            Assert.That(devices.Count(device => device.prototype == "KiasSpeaker" && device.online), Is.EqualTo(10));
            Assert.That(snapshot.alarms.Count(alarm => alarm.fireAlarm && alarm.controllable), Is.EqualTo(4));
            maps.DeleteMap(mapId);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SavedBriarLoadsAndScannerModulesSurviveRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        var loader = em.System<MapLoaderSystem>();
        var slots = em.System<ItemSlotsSystem>();
        var saved = new ResPath("/Maps/Test/KiasBriarLabRoundTrip.yml");
        EntityUid grid = default;
        MapId mapId = default;
        string[] before = Array.Empty<string>();

        EntityUid[] OnGrid<T>() where T : Component
        {
            var result = new List<EntityUid>();
            var query = em.AllEntityQueryEnumerator<T, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var transform))
                if (transform.GridUid == grid)
                    result.Add(uid);
            return result.ToArray();
        }

        string[] Modules()
        {
            return OnGrid<KiasRoomScannerComponent>().Select(uid =>
            {
                var transform = em.GetComponent<TransformComponent>(uid);
                var items = Enumerable.Range(1, 6).Select(index =>
                {
                    var item = slots.GetItemOrNull(uid, $"kias-module-{index}");
                    return item == null ? "EMPTY" : em.GetComponent<MetaDataComponent>(item.Value).EntityPrototype?.ID ?? "NO_PROTO";
                });
                return $"{transform.LocalPosition}:{string.Join(',', items)}";
            }).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        void Snapshot(string stage)
        {
            var devices = OnGrid<KiasDeviceComponent>().Select(uid =>
            {
                var device = em.GetComponent<KiasDeviceComponent>(uid);
                var transform = em.GetComponent<TransformComponent>(uid);
                return new
                {
                    uid = uid.ToString(), prototype = em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID,
                    x = transform.LocalPosition.X, y = transform.LocalPosition.Y,
                    rotation = transform.LocalRotation.ToString(), room = device.Room,
                    role = device.Role.ToString(), status = device.Status.ToString(),
                    online = em.System<KiasSystem>().IsOnline(uid)
                };
            }).ToArray();
            var cards = OnGrid<KiasControllerRackComponent>().SelectMany(rack =>
                Enumerable.Range(0, KiasControllerRackComponent.SlotCount).Select(index =>
                {
                    var card = slots.GetItemOrNull(rack, KiasControllerRackComponent.SlotId(index));
                    if (card == null) return null;
                    var component = em.GetComponent<KiasControllerCardComponent>(card.Value);
                    return new { rack = rack.ToString(), slot = index + 1, card = card.ToString(),
                        name = component.Program.Name, enabled = component.Enabled,
                        running = em.System<KiasControllerRuntimeSystem>().Running(card.Value),
                        fault = em.System<KiasControllerRuntimeSystem>().Fault(card.Value) };
                })).Where(card => card != null).ToArray();
            var snapshot = new { stage, tick = server.ResolveDependency<IGameTiming>().CurTick.Value,
                scanners = Modules(), devices, cards };
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            TestContext.Out.WriteLine(json);
            var output = Environment.GetEnvironmentVariable("KIAS_LAB_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, $"briar-{stage}.json"), json);
            }
        }

        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out mapId, runMapInit: false);
            Assert.That(loader.TryLoadGrid(mapId,
                new ResPath("/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml"), out var loaded), Is.True);
            grid = loaded!.Value.Owner;
        });
        await pair.RunTicksSync(120);
        await server.WaitAssertion(() =>
        {
            Assert.That(OnGrid<KiasRoomScannerComponent>(), Has.Length.EqualTo(21));
            Assert.That(OnGrid<KiasControllerRackComponent>(), Has.Length.EqualTo(4));
            before = Modules();
            Snapshot("frozen-loaded");
            server.ResolveDependency<IResourceManager>().UserData.CreateDir(saved.Directory);
            Assert.That(maps.IsInitialized(mapId), Is.False);
            Assert.That(loader.TrySaveGrid(grid, saved), Is.True);
            maps.DeleteMap(mapId);
        });
        await server.WaitIdleAsync();
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out mapId, runMapInit: false);
            Assert.That(loader.TryLoadGrid(mapId, saved, out var loaded), Is.True);
            grid = loaded!.Value.Owner;
            Assert.That(Modules(), Is.EqualTo(before));
            Snapshot("frozen-reloaded");
            maps.InitializeMap(mapId);
        });
        await pair.RunTicksSync(120);
        await server.WaitAssertion(() =>
        {
            Snapshot("reloaded");
            Assert.That(Modules().Count(row => row.Contains("EMPTY")), Is.Zero,
                "Both scanner prototypes must fill their six existing slots after MapInit.");
            Assert.That(Modules().Count(row => row.Contains("KiasRadiationModule")), Is.EqualTo(8));
            Assert.That(OnGrid<KiasControllerRackComponent>(), Has.Length.EqualTo(4));
            maps.DeleteMap(mapId);
        });
        await pair.CleanReturnAsync();
    }
}
