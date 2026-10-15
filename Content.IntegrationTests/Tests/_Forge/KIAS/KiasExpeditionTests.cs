using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Content.Server._Forge.KIAS;
using Content.Server.Parallax;
using Content.Server.Power.EntitySystems;
using Content.Server.Procedural;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Server.Weather;
using Content.Shared._Forge.KIAS;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Station.Components;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasExpeditionTests
{
    [Test]
    public async Task NativeExpeditionLandingKeepsShipCoverageAndExcludesTerrainThenReturns()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        var topology = em.System<KiasRoomTopologySystem>();
        var transforms = em.System<SharedTransformSystem>();
        var shuttles = em.System<ShuttleSystem>();
        EntityUid ship = default, expedition = default, person = default, scanner = default;
        EntityUid nearbyGrid = default;
        var proximitySensors = new List<EntityUid>();
        MapId space = default;
        SpawnSalvageMissionJob job = default!;
        var beforeCoverage = new Dictionary<EntityUid, HashSet<Vector2i>>();
        await pair.Server.WaitAssertion(() =>
        {
            maps.CreateMap(out space, runMapInit: false);
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(space,
                new ResPath("/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml"), out var loaded), Is.True);
            ship = loaded!.Value.Owner;
            maps.InitializeMap(space);
            var batteries = em.AllEntityQueryEnumerator<BatteryComponent, TransformComponent>();
            while (batteries.MoveNext(out var uid, out var battery, out var transform))
                if (transform.GridUid == ship) em.System<BatterySystem>().SetCharge(uid, battery.MaxCharge);
            var station = em.SpawnEntity(null, MapCoordinates.Nullspace);
            em.EnsureComponent<StationDataComponent>(station);
            job = new SpawnSalvageMissionJob(.002, em, pair.Server.ResolveDependency<IGameTiming>(), maps,
                pair.Server.ResolveDependency<IPrototypeManager>(), em.System<AnchorableSystem>(), em.System<BiomeSystem>(),
                em.System<WeatherSystem>(), em.System<DungeonSystem>(), shuttles, em.System<StationSystem>(),
                em.System<MetaDataSystem>(), em.System<SalvageSystem>(), transforms, station, null,
                new SalvageMissionParams { Seed = 20261010, MissionType = SalvageMissionType.Destruction, Difficulty = DifficultyRating.Moderate });
        });
        for (var tick = 0; tick < 3600 && job.Status != JobStatus.Finished; tick++)
        {
            await pair.Server.WaitAssertion(() =>
            {
                if (job.Status is JobStatus.Pending or JobStatus.Paused) job.Run();
            });
            await pair.RunTicksSync(1);
        }
        await pair.RunTicksSync(1800);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(job.Status, Is.EqualTo(JobStatus.Finished), "Native expedition generation timed out.");
            Assert.That(job.Exception, Is.Null);
            Assert.That(job.Result, Is.True);
            var query = em.AllEntityQueryEnumerator<SalvageExpeditionComponent>();
            while (query.MoveNext(out var uid, out var component))
                if (component.MissionParams.Seed == 20261010) expedition = uid;
            Assert.That(expedition.Valid, Is.True);
            var allScanners = em.AllEntityQueryEnumerator<KiasRoomScannerComponent, TransformComponent>();
            while (allScanners.MoveNext(out var uid, out _, out var transform))
                if (transform.GridUid == ship) beforeCoverage.Add(uid, topology.ScannerCells(uid).ToHashSet());
            Assert.That(beforeCoverage, Has.Count.EqualTo(21));
            var sensors = em.AllEntityQueryEnumerator<KiasRoomScannerComponent, TransformComponent>();
            while (sensors.MoveNext(out var uid, out var component, out var transform))
            {
                if (transform.GridUid != ship || (component.Modules & KiasScannerModules.Motion) == 0
                    || !topology.TryGetScannerRoom(uid, out var room, out _)) continue;
                scanner = uid;
                var tile = room.Tiles.First();
                person = em.SpawnEntity("BorgChassisGeneric", new EntityCoordinates(ship, tile.X + .5f, tile.Y + .5f));
                break;
            }
            Assert.That(scanner.Valid, Is.True);
            shuttles.FTLToCoordinates(ship, em.GetComponent<ShuttleComponent>(ship),
                new EntityCoordinates(expedition, Vector2.Zero), Angle.Zero, startupTime: 1, hyperspaceTime: 1);
        });
        await pair.RunTicksSync(600);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<TransformComponent>(ship).MapUid, Is.EqualTo(expedition));
            Assert.That(em.System<KiasSystem>().ActiveGrids, Does.Contain(ship));
            Assert.That(topology.Contains(scanner, person, KiasScannerModules.Motion), Is.True);
            foreach (var (uid, cells) in beforeCoverage)
                Assert.That(topology.ScannerCells(uid), Is.EquivalentTo(cells), $"Scanner {uid} changed its grid coverage on landing.");
            var coordinates = transforms.GetMapCoordinates(person);
            var outsider = em.SpawnEntity("BorgChassisGeneric", new EntityCoordinates(expedition, coordinates.Position));
            // Закрепляем принадлежность поверхности для проверки перекрывающихся гридов.
            transforms.SetParent(outsider, expedition);
            Assert.That(em.GetComponent<TransformComponent>(outsider).GridUid, Is.EqualTo(expedition));
            Assert.That(topology.Contains(scanner, outsider, KiasScannerModules.Motion), Is.False);
            em.System<KiasCrewSystem>().RefreshCounts(ship);
            Assert.That(em.GetComponent<KiasGridComponent>(ship).Entities, Is.EqualTo(1));
            var query = em.AllEntityQueryEnumerator<KiasProximityComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var proximity, out var transform))
            {
                if (transform.GridUid != ship) continue;
                Assert.That(em.System<KiasSystem>().IsOnline(uid), Is.True);
                Assert.That(proximity.Contacts, Does.Not.Contain(expedition));
                proximitySensors.Add(uid);
            }
            Assert.That(proximitySensors, Is.Not.Empty);
            var foreign = maps.CreateGridEntity(em.GetComponent<TransformComponent>(ship).MapID);
            nearbyGrid = foreign.Owner;
            var shipMap = em.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(ship);
            var floor = maps.GetAllTiles(ship, shipMap);
            Assert.That(floor.MoveNext(out var tile), Is.True);
            Assert.That(tile.HasValue, Is.True);
            maps.SetTile(foreign, Vector2i.Zero, tile!.Value.Tile);
            transforms.SetWorldPosition(foreign, transforms.GetMapCoordinates(proximitySensors[0]).Position + new Vector2(100, 0));
            TestContext.Out.WriteLine(JsonSerializer.Serialize(new { stage = "native-expedition-landed", ship = ship.ToString(),
                expedition = expedition.ToString(), scanner = scanner.ToString(), ownPersonCovered = true,
                terrainPersonCovered = false, globalEntities = 1, terrainProximityContact = false }));
        });
        await pair.RunTicksSync(300);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(proximitySensors.Any(uid => em.GetComponent<KiasProximityComponent>(uid).Contacts.Contains(nearbyGrid)), Is.True,
                "A landed ship must still detect an actual nearby grid.");
            foreach (var uid in proximitySensors)
                Assert.That(em.GetComponent<KiasProximityComponent>(uid).Contacts, Does.Not.Contain(expedition));
            TestContext.Out.WriteLine("native-expedition-proximity: nearby grid detected; planetary terrain excluded; sensors online");
            shuttles.FTLToCoordinates(ship, em.GetComponent<ShuttleComponent>(ship),
                new EntityCoordinates(maps.GetMap(space), Vector2.Zero), Angle.Zero, startupTime: 1, hyperspaceTime: 1);
        });
        await pair.RunTicksSync(600);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<TransformComponent>(ship).MapID, Is.EqualTo(space));
            Assert.That(em.System<KiasSystem>().ActiveGrids, Does.Contain(ship));
            Assert.That(topology.Contains(scanner, person, KiasScannerModules.Motion), Is.True);
            foreach (var (uid, cells) in beforeCoverage)
                Assert.That(topology.ScannerCells(uid), Is.EquivalentTo(cells), $"Scanner {uid} changed its grid coverage after return.");
            TestContext.Out.WriteLine("native-expedition-return: own coverage restored, core active");
        });
        await pair.CleanReturnAsync();
    }
}
