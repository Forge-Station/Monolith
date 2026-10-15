using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Robust.Shared.Timing;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Numerics;
using Content.Server._Forge.KIAS;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._Forge.KIAS;
using Content.Shared.Anomaly;
using Content.Shared.Anomaly.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasFleetTests
{
    [Test]
    public async Task FleetSchedulingAndEventStress()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var kias = em.System<KiasSystem>();
        var fleet = new List<(Entity<MapGridComponent> Grid, EntityUid Core, EntityUid Cable)>();
        var defence = new List<EntityUid>();
        var projectiles = new List<EntityUid>();
        var anomalies = new List<EntityUid>();
        var measurements = new List<string>();
        EntityUid Spawn(string prototype, EntityUid grid, int x)
        {
            var uid = em.SpawnEntity(prototype, new EntityCoordinates(grid, x + 0.5f, 0.5f));
            em.RemoveComponent<ApcPowerReceiverComponent>(uid);
            return uid;
        }
        void Measure(string label, Action action)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            action();
            measurements.Add($"KIAS {label}: {watch.Elapsed.TotalMilliseconds:F2} ms, {GC.GetAllocatedBytesForCurrentThread() - allocated} server-thread bytes");
        }
        async Task Ticks(string label, int ticks = 60)
        {
            var samples = new List<double>();
            var bytes = new List<long>();
            var ownSamples = new List<double>();
            var ownBytes = new List<long>();
            long start = 0;
            long startBytes = 0;
            object? loop = null;
            EventHandler<FrameEventArgs> before = (_, _) =>
            {
                start = Stopwatch.GetTimestamp(); startBytes = GC.GetAllocatedBytesForCurrentThread();
                kias.MeasuredUpdateMilliseconds = 0; kias.MeasuredUpdateBytes = 0;
            };
            EventHandler<FrameEventArgs> after = (_, _) =>
            {
                samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                bytes.Add(GC.GetAllocatedBytesForCurrentThread() - startBytes);
                ownSamples.Add(kias.MeasuredUpdateMilliseconds);
                ownBytes.Add(kias.MeasuredUpdateBytes);
            };
            await pair.Server.WaitAssertion(() =>
            {
                for (var type = pair.Server.GetType(); type != null; type = type.BaseType)
                {
                    if (type.GetField("GameLoop", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is not { } field) continue;
                    loop = field.GetValue(pair.Server);
                    break;
                }
                Assert.That(loop, Is.Not.Null);
                kias.MeasureUpdates = true;
                loop!.GetType().GetEvent("Input")!.AddEventHandler(loop, before);
                loop.GetType().GetEvent("Update")!.AddEventHandler(loop, after);
            });
            var allocated = GC.GetTotalAllocatedBytes();
            var watch = Stopwatch.StartNew();
            try { await pair.RunTicksSync(ticks); }
            finally
            {
                await pair.Server.WaitAssertion(() =>
                {
                    loop!.GetType().GetEvent("Input")!.RemoveEventHandler(loop, before);
                    loop.GetType().GetEvent("Update")!.RemoveEventHandler(loop, after);
                    kias.MeasureUpdates = false;
                });
            }
            TestContext.WriteLine($"KIAS {label}, {ticks} complete engine ticks: {watch.Elapsed.TotalMilliseconds:F2} ms, {GC.GetTotalAllocatedBytes() - allocated} total bytes (server + client + harness)");
            samples.Sort();
            TestContext.WriteLine($"KIAS {label}, server ticks: max {samples.Max():F3} ms, p95 {samples[(int) (samples.Count * 0.95)]:F3} ms, max {bytes.Max()} bytes, sum {bytes.Sum()} bytes");
            ownSamples.Sort();
            TestContext.WriteLine($"KIAS {label}, periodic KIAS updates: max {ownSamples.Max():F3} ms, p95 {ownSamples[(int) (ownSamples.Count * 0.95)]:F3} ms, max {ownBytes.Max()} bytes, sum {ownBytes.Sum()} bytes");
        }
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            var transform = em.System<SharedTransformSystem>();
            for (var i = 0; i < 200; i++)
            {
                var grid = maps.CreateGridEntity(map.MapId);
                transform.SetLocalPosition(grid, new Vector2(i % 20 * 600, i / 20 * 600));
                EntityUid cable = default;
                for (var x = 0; x < 10; x++)
                {
                    maps.SetTile(grid, grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                    var uid = em.SpawnEntity("KiasDataCable", new EntityCoordinates(grid, x + 0.5f, 0.5f));
                    if (x == 5)
                        cable = uid;
                }
                var core = Spawn("KiasCore", grid, 0);
                kias.Rebuild(grid);
                kias.SetEnabled(core, false);
                fleet.Add((grid, core, cable));
            }
            Assert.That(kias.ActiveGrids.Count, Is.Zero);
        });
        await Ticks("200 hard-off ships");
        await pair.Server.WaitAssertion(() => Measure("200 core boot/topology rebuilds", () =>
        {
            foreach (var ship in fleet)
                kias.SetEnabled(ship.Core, true);
            Assert.That(kias.ActiveGrids.Count, Is.EqualTo(200));
        }));
        await Ticks("200 idle-active cores");
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var ship in fleet)
            {
                var scanner = Spawn("KiasRoomScanner", ship.Grid, 1);
                em.System<SharedMapSystem>().SetTile(ship.Grid, em.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(ship.Grid), new Vector2i(1, 1), map.Tile.Tile);
                em.System<SharedTransformSystem>().SetLocalRotation(scanner, Angle.FromDegrees(180));
                Spawn("KiasCrewServer", ship.Grid, 2);
                Spawn("KiasRecorder", ship.Grid, 3);
                Spawn("KiasAtmosServer", ship.Grid, 4);
                Spawn("KiasNavigationServer", ship.Grid, 6);
                Spawn("KiasHorizon", ship.Grid, 7);
                defence.Add(Spawn("KiasDefenceServer", ship.Grid, 8));
                Spawn("KiasPdcRadar", ship.Grid, 9);
                Spawn("KiasPowerServer", ship.Grid, 2);
                Spawn("KiasProximitySensor", ship.Grid, 7);
                Spawn("KiasWeaponFlashDetector", ship.Grid, 8);
                var containers = em.System<SharedContainerSystem>();
                var module = em.SpawnEntity("KiasSpectralModule", new EntityCoordinates(ship.Grid, 1.5f, 0.5f));
                containers.Insert(module, containers.GetContainer(scanner, "kias-module-3"));
                for (var person = 0; person < 4; person++)
                {
                    var body = em.SpawnEntity(null, new EntityCoordinates(ship.Grid, 2.5f, 0.5f));
                    em.AddComponent<KiasTrackedEntityComponent>(body);
                }
                var anomaly = em.SpawnEntity(null, new EntityCoordinates(ship.Grid, 2.5f, 0.5f));
                em.AddComponent<AnomalyComponent>(anomaly);
                em.System<SharedAnomalySystem>().ChangeAnomalyStability(anomaly, -1);
                anomalies.Add(anomaly);
                kias.Rebuild(ship.Grid);
            }
        });
        await Ticks("200 scanners / 800 indexed sapient bodies");
        await Ticks("200 equipped grids warm (crew/proximity/power)", 360);
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var ship in fleet)
            {
                em.System<KiasCrewSystem>().RefreshCounts(ship.Grid);
                Assert.That(em.GetComponent<KiasGridComponent>(ship.Grid).Entities, Is.EqualTo(4));
            }
        });
        foreach (var scenario in new[] { (Radars: 1, Shots: 10), (Radars: 50, Shots: 200), (Radars: 200, Shots: 500), (Radars: 200, Shots: 0) })
        {
            await pair.Server.WaitAssertion(() =>
            {
                foreach (var uid in projectiles)
                    if (em.EntityExists(uid))
                        em.DeleteEntity(uid);
                projectiles.Clear();
                for (var i = 0; i < defence.Count; i++)
                    em.System<KiasDefenceSystem>().SetAutomatic(defence[i], i < scenario.Radars);
                for (var i = 0; i < scenario.Shots; i++)
                {
                    var ship = fleet[i % scenario.Radars];
                    var position = em.System<SharedTransformSystem>().GetMapCoordinates(ship.Grid).Position;
                    var projectile = em.SpawnEntity("20mmBullet", new EntityCoordinates(map.MapUid, position + new Vector2(100 + i % 10, 0.5f)));
                    em.RemoveComponent<TimedDespawnComponent>(projectile);
                    em.System<SharedPhysicsSystem>().SetLinearVelocity(projectile, new Vector2(-20, 0));
                    projectiles.Add(projectile);
                }
            });
            await Ticks($"{scenario.Radars} active PDC radars / {scenario.Shots} projectiles");
            if (scenario.Radars == 200 && scenario.Shots == 500) await Ticks("200 PDC / 500 projectiles warm", 120);
        }
        await pair.Server.WaitAssertion(() =>
        {
            Measure("100 weapon flashes / 200 online detectors", () =>
            {
                for (var i = 0; i < 100; i++)
                {
                    var shot = new KiasWeaponFiredEvent(fleet[100].Core);
                    em.EventBus.RaiseLocalEvent(fleet[100].Core, ref shot, true);
                }
            });
            Measure("200 FTL arrivals", () =>
            {
                foreach (var ship in fleet)
                {
                    var ev = new FTLCompletedEvent(ship.Grid, map.MapUid);
                    em.EventBus.RaiseLocalEvent(ship.Grid, ref ev, true);
                }
            });
            Measure("200 anomaly growth crossings", () =>
            {
                foreach (var uid in anomalies)
                    em.System<SharedAnomalySystem>().ChangeAnomalyStability(uid, 0.6f);
            });
            Measure("200 cable cuts/rebuilds", () =>
            {
                foreach (var ship in fleet)
                {
                    em.DeleteEntity(ship.Cable);
                    kias.Rebuild(ship.Grid);
                    Assert.That(kias.IsOnline(defence[fleet.IndexOf(ship)]), Is.False);
                }
            });
            Measure("200 cable reconnects/rebuilds", () =>
            {
                foreach (var ship in fleet)
                {
                    em.SpawnEntity("KiasDataCable", new EntityCoordinates(ship.Grid, 5.5f, 0.5f));
                    kias.Rebuild(ship.Grid);
                }
            });
            foreach (var ship in fleet)
                kias.SetEnabled(ship.Core, false);
            Assert.That(kias.ActiveGrids.Count, Is.Zero);
        });
        await Ticks("200 fully equipped hard-off ships");
        foreach (var measurement in measurements)
            TestContext.WriteLine(measurement);
        await pair.CleanReturnAsync();
    }
}
