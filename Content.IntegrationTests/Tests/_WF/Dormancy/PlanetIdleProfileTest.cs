#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Atmos.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.UnitTesting;
using Prometheus;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Dormancy;

/// <summary>
/// Not a check: builds every round-start world as a round would, with nobody on them, and prints what each system's
/// Update costs and how many components it walks. Run by hand when tuning idle planet costs.
/// </summary>
[TestFixture, Explicit]
public sealed class PlanetIdleProfileTest
{
    private const int WarmTicks = 300;
    private const int MeasureTicks = 600;

    private static readonly string[] Watched =
    {
        "SolutionRegenerationSystem", "FlammableSystem", "CEZLevelsSystem", "PathfindingSystem", "XenoWeedsSystem",
        "WFReactiveFloraSystem", "WFVehicleElectricalSystem", "NPCSystem",
    };

    private static readonly string[] Counted =
    {
        "SolutionRegeneration", "SolutionContainerManager", "Flammable", "CEZPhysics", "XenoWeeds", "XenoWeedsSpreading",
        "DamageOffWeeds", "WFReactiveFlora", "ApcPowerReceiver", "WFVehicleEngine", "GridPathfinding", "HTN", "ActiveNPC", "CEZGridFaller",
    };

    /// <param name="sites">Whether sites nobody is near sleep (WFSiteStashSystem).</param>
    [TestCase(null)]
    [TestCase("WFSurfaceMerak")]
    [TestCase(null, true)]
    [TestCase("WFSurfaceAsclepiu")]
    [TestCase("WFSurfaceAsclepiu", true)]
    public async Task ProfileIdleWorlds(string? only, bool sites = false)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WFPlanetNetworkSystem>();
        var grounds = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            var cfg = server.CfgMan;
            cfg.SetCVar(PlanetCVars.PlanetNetworks, true);
            cfg.SetCVar(PlanetCVars.Bounds, true);
            cfg.SetCVar(PlanetCVars.Radius, 0);
            cfg.SetCVar(PlanetCVars.Preload, true);
            cfg.SetCVar(PlanetCVars.PreloadBudget, 2000f);
            cfg.SetCVar(PlanetCVars.GenerationBudget, 2000f);
            cfg.SetCVar(PlanetCVars.TerrainLoadBudget, 0f);
            cfg.SetCVar(PlanetCVars.FixedCities, true);
            cfg.SetCVar(CavernCVars.Caverns, true);
            cfg.SetCVar(PlanetCVars.SiteStash, sites);

            var i = 0;
            foreach (var surface in proto.EnumeratePrototypes<WFPlanetSurfacePrototype>().Where(s => s.BuildAtRoundStart && (only == null || s.ID == only)).OrderBy(s => s.ID))
            {
                var built = networks.BuildNetwork(surface, new Vector2(4000f * ++i, 0f), surface.ID, null);
                Assert.That(built, Is.Not.Null, $"{surface.ID} failed to build.");
                grounds.Add(entMan.GetComponent<WFPlanetNetworkComponent>(built!.Value).GroundMap);
            }
        });

        var formed = Stopwatch.StartNew();
        for (var tick = 0; tick < 40000 && grounds.Any(g => !entMan.HasComponent<WFPlanetPreloadedComponent>(g)); tick++)
        {
            await server.WaitRunTicks(5);
        }

        Assert.That(grounds.All(entMan.HasComponent<WFPlanetPreloadedComponent>), Is.True, "Not every world formed.");
        TestContext.Out.WriteLine($"{grounds.Count} worlds formed in {formed.Elapsed.TotalSeconds:F0} s; {entMan.EntityCount} entities.");

        await server.WaitRunTicks(WarmTicks);

        var systems = server.ResolveDependency<IEntitySystemManager>();
        var histogram = (Histogram) typeof(EntitySystemManager)
            .GetField("_tickUsageHistogram", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var names = systems.GetEntitySystemTypes().Select(t => t.Name).Distinct().ToList();
        var before = names.ToDictionary(n => n, n => histogram.WithLabels(n).Sum);

        var physicsSystem = server.System<SharedPhysicsSystem>();
        var metricsSetter = typeof(SharedPhysicsSystem).GetProperty("MetricsEnabled")!.GetSetMethod(true)!;
        var controllerHistograms = new[] { SharedPhysicsSystem.TickUsageControllerBeforeSolveHistogram, SharedPhysicsSystem.TickUsageControllerAfterSolveHistogram };
        Dictionary<string, double> ControllerSums() => controllerHistograms
            .SelectMany(h => h.GetAllLabelValues().Select(l => (Name: l[0], Sum: h.WithLabels(l).Sum)))
            .GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.Sum(p => p.Sum));
        var controllersBefore = ControllerSums();

        systems.MetricsEnabled = true;
        metricsSetter.Invoke(physicsSystem, new object[] { true });
        await server.WaitRunTicks(MeasureTicks);
        systems.MetricsEnabled = false;
        metricsSetter.Invoke(physicsSystem, new object[] { false });
        var controllersAfter = ControllerSums();

        var minutes = MeasureTicks / (double) server.Timing.TickRate / 60.0;
        var spent = names.ToDictionary(n => n, n => histogram.WithLabels(n).Sum - before[n]);
        TestContext.Out.WriteLine($"Total update {spent.Values.Sum() / MeasureTicks * 1000:F2} ms/tick");
        foreach (var (name, seconds) in spent.OrderByDescending(p => p.Value).Take(25))
        {
            TestContext.Out.WriteLine($"  {name,-40} {seconds / MeasureTicks * 1000,8:F3} ms/tick {seconds / minutes,8:F3} s/min");
        }

        TestContext.Out.WriteLine("Physics controllers:");
        foreach (var (name, seconds) in controllersAfter.Select(p => (p.Key, p.Value - controllersBefore.GetValueOrDefault(p.Key))).OrderByDescending(p => p.Item2).Take(12))
        {
            TestContext.Out.WriteLine($"  {name,-40} {seconds / MeasureTicks * 1000,8:F3} ms/tick");
        }

        await ProfilePhysicsCore(server);

        TestContext.Out.WriteLine("Watched:");
        foreach (var name in Watched)
        {
            var seconds = spent.GetValueOrDefault(name);
            TestContext.Out.WriteLine($"  {name,-40} {seconds / MeasureTicks * 1000,8:F3} ms/tick {seconds / minutes,8:F3} s/min");
        }

        await server.WaitPost(() =>
        {
            var factory = entMan.ComponentFactory;
            foreach (var compName in Counted)
            {
                if (!factory.TryGetRegistration(compName, out var reg))
                    continue;

                var total = 0;
                var onPlanets = 0;
                var byProto = new Dictionary<string, int>();
                foreach (var (uid, _) in entMan.GetAllComponents(reg.Type, includePaused: true))
                {
                    total++;
                    var xform = entMan.GetComponent<TransformComponent>(uid);
                    if (xform.MapUid is not { } map || !entMan.HasComponent<CEZMapComponent>(map))
                        continue;

                    onPlanets++;
                    var id = entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? "(none)";
                    byProto[id] = byProto.GetValueOrDefault(id) + 1;
                }

                var top = string.Join(", ", byProto.OrderByDescending(p => p.Value).Take(12).Select(p => $"{p.Key} {p.Value}"));
                TestContext.Out.WriteLine($"{compName}: {total} total, {onPlanets} on planets: {top}");
            }

            var pathfinding = server.System<PathfindingSystem>();
            var requests = typeof(PathfindingSystem).GetField("_pathRequests", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pathfinding);
            TestContext.Out.WriteLine($"Pending path requests: {(requests as System.Collections.ICollection)?.Count}");
        });

        await server.WaitPost(() =>
        {
            var burning = new Dictionary<string, int>();
            var flammables = entMan.EntityQueryEnumerator<FlammableComponent, MetaDataComponent>();
            while (flammables.MoveNext(out _, out var flammable, out var meta))
            {
                if (flammable.OnFire)
                    burning[meta.EntityPrototype?.ID ?? "(none)"] = burning.GetValueOrDefault(meta.EntityPrototype?.ID ?? "(none)") + 1;
            }

            TestContext.Out.WriteLine($"On fire: {string.Join(", ", burning.OrderByDescending(p => p.Value).Take(12).Select(p => $"{p.Key} {p.Value}"))}");

            var awake = new Dictionary<string, int>();
            var bodies = entMan.EntityQueryEnumerator<PhysicsComponent, MetaDataComponent>();
            while (bodies.MoveNext(out _, out var body, out var meta))
            {
                if (body.Awake)
                    awake[meta.EntityPrototype?.ID ?? "(none)"] = awake.GetValueOrDefault(meta.EntityPrototype?.ID ?? "(none)") + 1;
            }

            TestContext.Out.WriteLine($"Awake bodies {awake.Values.Sum()}: {string.Join(", ", awake.OrderByDescending(p => p.Value).Take(15).Select(p => $"{p.Key} {p.Value}"))}");
        });

        // What moves, and which grids the pathfinder rebuilds, over a few seconds.
        var moved = new Dictionary<string, int>();
        var samples = new Dictionary<string, List<string>>();
        var xformSys = server.System<SharedTransformSystem>();
        void OnMove(ref MoveEvent ev)
        {
            var id = entMan.GetComponentOrNull<MetaDataComponent>(ev.Sender)?.EntityPrototype?.ID ?? "(none)";
            moved[id] = moved.GetValueOrDefault(id) + 1;
            if (!samples.TryGetValue(id, out var list))
                samples[id] = list = new List<string>();
            if (list.Count < 4)
            {
                var parent = ev.Component.ParentUid;
                var parentMeta = entMan.GetComponentOrNull<MetaDataComponent>(parent);
                var trace = string.Join(" | ", Environment.StackTrace.Split('\n').Skip(4).Take(7).Select(l => l.Trim()));
                list.Add($"{ev.OldPosition.Position}->{ev.NewPosition.Position} parent {parentMeta?.EntityPrototype?.ID ?? parentMeta?.EntityName} anchored {ev.Component.Anchored} at {trace}");
            }
        }

        var dirt = new Dictionary<string, int>();
        await server.WaitPost(() => xformSys.OnGlobalMoveEvent += OnMove);
        for (var i = 0; i < 90; i++)
        {
            await server.WaitPost(() =>
            {
                var grids = entMan.EntityQueryEnumerator<GridPathfindingComponent, MetaDataComponent>();
                while (grids.MoveNext(out var uid, out var path, out var meta))
                {
                    if (path.DirtyChunks.Count == 0)
                        continue;
                    var map = entMan.GetComponent<TransformComponent>(uid).MapUid;
                    var key = $"{meta.EntityName}@{(map is { } m ? entMan.GetComponent<MetaDataComponent>(m).EntityName : "?")}";
                    dirt[key] = dirt.GetValueOrDefault(key) + path.DirtyChunks.Count;
                }
            });
            await server.WaitRunTicks(1);
        }

        await server.WaitPost(() => xformSys.OnGlobalMoveEvent -= OnMove);
        TestContext.Out.WriteLine($"Moves in 90 ticks: {string.Join(", ", moved.OrderByDescending(p => p.Value).Take(15).Select(p => $"{p.Key} {p.Value}"))}");
        foreach (var (id, _) in moved.OrderByDescending(p => p.Value).Take(6))
        {
            foreach (var line in samples[id])
            {
                TestContext.Out.WriteLine($"  {id}: {line}");
            }
        }

        TestContext.Out.WriteLine($"Dirty pathfinding chunks seen in 90 ticks: {string.Join(", ", dirt.OrderByDescending(p => p.Value).Take(10).Select(p => $"{p.Key} {p.Value}"))}");

        // CEZLevelsSystem's parts, each run on its own.
        await server.WaitPost(() =>
        {
            var levels = server.System<CEZLevelsSystem>();
            var frame = 1f / server.Timing.TickRate;
            var parts = new (string Name, Type Owner, bool Frame)[]
            {
                ("UpdateGridGravity", typeof(CEZLevelsSystem), true),
                ("UpdateGroundFriction", typeof(CEZLevelsSystem), false),
                ("UpdateWallCollision", typeof(CEZLevelsSystem), false),
                ("UpdateView", typeof(CEZLevelsSystem), true),
                ("CleanupOrphanedTransitMaps", typeof(CEZLevelsSystem), false),
                ("UpdateZPhysics", typeof(CESharedZLevelsSystem), true),
            };

            foreach (var (name, owner, takesFrame) in parts)
            {
                var method = owner.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (method == null)
                {
                    TestContext.Out.WriteLine($"  {name}: not found");
                    continue;
                }

                var args = takesFrame ? new object[] { frame } : Array.Empty<object>();
                var watch = Stopwatch.StartNew();
                for (var i = 0; i < 100; i++)
                {
                    method.Invoke(levels, args);
                }

                TestContext.Out.WriteLine($"  CEZLevels.{name,-28} {watch.Elapsed.TotalMilliseconds / 100,8:F3} ms/call");
            }
        });

        await server.WaitPost(() =>
        {
            foreach (var ground in grounds)
            {
                if (entMan.GetComponent<WFPlanetLayerComponent>(ground).Network is { } net)
                    networks.DeleteNetwork(entMan.GetEntity(net));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Times the physics step's own parts, a step at a time between real ticks, and counts what they walk.</summary>
    private static async Task ProfilePhysicsCore(RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var entMan = server.EntMan;
        var physics = server.System<SharedPhysicsSystem>();
        var broadphase = server.System<SharedBroadphaseSystem>();
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        var find = typeof(SharedBroadphaseSystem).GetMethod("FindNewContacts", flags)!;
        var collide = typeof(SharedPhysicsSystem).GetMethod("CollideContacts", flags)!;
        var step = typeof(SharedPhysicsSystem).GetMethod("Step", flags, new[] { typeof(float), typeof(bool) })!;
        var contactsField = typeof(SharedPhysicsSystem).GetField("_activeContacts", flags)!;
        var moveBuffer = typeof(SharedPhysicsSystem).GetField("MoveBuffer", flags)!.GetValue(physics)!;
        var moveBufferCount = moveBuffer.GetType().GetProperty("Count")!;
        var movedGrids = (HashSet<EntityUid>) typeof(SharedPhysicsSystem).GetField("MovedGrids", flags)!.GetValue(physics)!;
        var frame = 1f / server.Timing.TickRate;
        double findMs = 0, collideMs = 0, stepMs = 0;
        long moved = 0, grids = 0;
        var movedGridNames = new Dictionary<string, int>();
        const int samples = 60;

        for (var i = 0; i < samples; i++)
        {
            await server.WaitPost(() =>
            {
                moved += (int) moveBufferCount.GetValue(moveBuffer)!;
                grids += movedGrids.Count;
                foreach (var grid in movedGrids)
                {
                    var name = entMan.GetComponentOrNull<MetaDataComponent>(grid)?.EntityName ?? "?";
                    movedGridNames[name] = movedGridNames.GetValueOrDefault(name) + 1;
                }

                var watch = Stopwatch.StartNew();
                find.Invoke(broadphase, null);
                findMs += watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                collide.Invoke(physics, null);
                collideMs += watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                step.Invoke(physics, new object[] { frame, false });
                stepMs += watch.Elapsed.TotalMilliseconds;
            });
            await server.WaitRunTicks(1);
        }

        TestContext.Out.WriteLine($"Physics core per step: broadphase {findMs / samples:F3} ms, collide {collideMs / samples:F3} ms, solve {stepMs / samples:F3} ms; "
            + $"moved proxies {moved / (double) samples:F1}, moved grids {grids / (double) samples:F1}, awake bodies {physics.AwakeBodies.Count}");
        TestContext.Out.WriteLine("  Moved grids: " + string.Join(", ", movedGridNames.OrderByDescending(p => p.Value).Take(10).Select(p => $"{p.Key} {p.Value}")));

        await server.WaitPost(() =>
        {
            var contacts = (System.Collections.IEnumerable) contactsField.GetValue(physics)!;
            var pairs = new Dictionary<string, int>();
            var total = 0;
            var touching = 0;
            var asleep = 0;
            foreach (Robust.Shared.Physics.Dynamics.Contacts.Contact contact in contacts)
            {
                total++;
                if (contact.IsTouching)
                    touching++;
                if (contact.BodyA is { Awake: false } && contact.BodyB is { Awake: false })
                    asleep++;
                string Name(EntityUid uid) => entMan.GetComponentOrNull<MetaDataComponent>(uid) is { } meta
                    ? meta.EntityPrototype?.ID ?? (entMan.HasComponent<Robust.Shared.Map.Components.MapGridComponent>(uid) ? "grid" : "(none)")
                    : "?";
                var key = $"{Name(contact.EntityA)} / {Name(contact.EntityB)}";
                pairs[key] = pairs.GetValueOrDefault(key) + 1;
            }

            TestContext.Out.WriteLine($"Contacts: {total}, touching {touching}, both asleep {asleep}: "
                + string.Join(", ", pairs.OrderByDescending(p => p.Value).Take(12).Select(p => $"{p.Key} {p.Value}")));
        });
    }
}
