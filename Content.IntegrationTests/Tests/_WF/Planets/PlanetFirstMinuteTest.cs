#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using Content.Server._WF.Dormancy;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// Forms every round-start world of Kyphrus as the lobby does, then times each tick of the minute after, with what
/// the slow ones spent their time on; run by hand.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetPreloadSystem))]
public sealed class PlanetFirstMinuteTest
{
    /// <param name="stash">Whether idle terrain is stashed (BiomeSystem.WFStash).</param>
    /// <param name="lazy">Whether planet navmeshes are built only near someone (PathfindingSystem.WFPlanetLazy).</param>
    /// <param name="sites">Whether sites nobody is near sleep (WFSiteStashSystem).</param>
    /// <param name="only">One world to form instead of all of them.</param>
    /// <param name="slice">Whether a big site's ground is laid ahead of its merge in slices (wf.planet_merge_slice_tiles).</param>
    [Explicit]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    [TestCase(true, true, true)]
    [TestCase(true, true, false, "WFSurfaceAsclepiu", true, TestName = "AsclepiuMergeSlices")]
    [TestCase(true, true, false, "WFSurfaceAsclepiu", false, TestName = "AsclepiuNoMergeSlices")]
    public async Task FirstMinuteAfterTheWorldsForm(bool stash, bool lazy, bool sites = false, string? only = null, bool slice = true)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WFPlanetNetworkSystem>();
        var esm = server.ResolveDependency<IEntitySystemManager>();
        var grounds = new List<EntityUid>();
        var allLayers = new List<EntityUid>();
        var entitiesBefore = 0;
        long memoryBefore = 0;
        var field = typeof(EntitySystemManager).GetField("_tickUsageHistogram", BindingFlags.NonPublic | BindingFlags.Static)!;
        var histogram = (Prometheus.Histogram) field.GetValue(null)!;
        // The engine's own areas too (async continuations, timers, queued deletions), where generation work outside a system lands.
        var engine = (Prometheus.Histogram) typeof(Robust.Server.Physics.GridFixtureSystem).Assembly.GetType("Robust.Server.BaseServer")!
            .GetField("TickUsage", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Dictionary<string, double> Sums()
        {
            var sums = histogram.GetAllLabelValues().ToDictionary(l => l[0], l => histogram.WithLabels(l).Sum);
            foreach (var label in engine.GetAllLabelValues())
            {
                if (label[0] != "EntitySystems")
                    sums["engine:" + label[0]] = engine.WithLabels(label).Sum;
            }
            return sums;
        }
        string Top(Dictionary<string, double> now, Dictionary<string, double> then, int count) =>
            string.Join(", ", now.Select(p => (p.Key, Ms: (p.Value - then.GetValueOrDefault(p.Key)) * 1000))
                .OrderByDescending(p => p.Ms).Take(count).Select(p => $"{p.Key} {p.Ms:F0}"));
        var slowForming = new List<(double Ms, string Top)>();

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, true);
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.FixedCities, true);
            server.CfgMan.SetCVar(PlanetCVars.Prebuild, false);
            server.CfgMan.SetCVar(PlanetCVars.TerrainStash, stash);
            server.CfgMan.SetCVar(PlanetCVars.PathfindingLazy, lazy);
            server.CfgMan.SetCVar(PlanetCVars.SiteStash, sites);
            server.CfgMan.SetCVar(PlanetCVars.MergeSliceTiles, slice ? PlanetCVars.MergeSliceTiles.DefaultValue : 0);
            // Formed at the lobby's pace, as a real round's are.
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, server.CfgMan.GetCVar(PlanetCVars.PreloadBudgetLobby));
            server.CfgMan.SetCVar(PlanetCVars.GenerationBudget, server.CfgMan.GetCVar(PlanetCVars.GenerationBudgetLobby));
            entitiesBefore = entMan.GetEntities().Count();
            memoryBefore = GC.GetTotalMemory(true);
            esm.MetricsEnabled = true;

            var surfaces = proto.EnumeratePrototypes<WFPlanetSurfacePrototype>().ToDictionary(s => s.PlanetType.Id);
            foreach (var entry in proto.Index<StarSystemPrototype>("SystemKyphrus").Planets)
            {
                if (!surfaces.TryGetValue(entry.Planet.Id, out var surface) || !surface.BuildAtRoundStart || only != null && surface.ID != only)
                    continue;
                var centre = new Vector2(MathF.Cos(entry.Angle), MathF.Sin(entry.Angle)) * entry.Distance;
                Assert.That(networks.Prebuild(surface, centre, surface.ID), Is.True, surface.ID);
            }

            var query = entMan.EntityQueryEnumerator<WFPlanetNetworkComponent>();
            while (query.MoveNext(out _, out var comp))
            {
                grounds.Add(comp.GroundMap);
                allLayers.AddRange(comp.Layers);
            }
        });

        var formWatch = Stopwatch.StartNew();
        var worstForming = 0.0;
        var tickWatch = new Stopwatch();
        for (var i = 0; i < 100_000; i++)
        {
            var done = false;
            await server.WaitPost(() => done = grounds.All(g => entMan.HasComponent<WFPlanetPreloadedComponent>(g)));
            if (done)
                break;
            var before = Sums();
            var stages = string.Empty;
            await server.WaitPost(() => stages = string.Join("; ", server.System<WFPlanetPreloadSystem>().Forming()
                .Select(f => $"{entMan.GetComponentOrNull<MetaDataComponent>(f.Ground)?.EntityName} {f.Stage} {f.Progress:P0}")));
            tickWatch.Restart();
            await server.WaitRunTicks(1);
            var took = tickWatch.Elapsed.TotalMilliseconds;
            worstForming = Math.Max(worstForming, took);
            if (took >= 250)
                slowForming.Add((took, Top(Sums(), before, 5) + " | " + stages));
        }

        var formed = formWatch.Elapsed.TotalSeconds;
        Assert.That(grounds.All(g => entMan.HasComponent<WFPlanetPreloadedComponent>(g)), Is.True, "The worlds never formed.");

        var entitiesFormed = 0;
        long memory = 0;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, PlanetCVars.PreloadBudget.DefaultValue);
            server.CfgMan.SetCVar(PlanetCVars.GenerationBudget, PlanetCVars.GenerationBudget.DefaultValue);
            entitiesFormed = entMan.EntityCount;
            memory = GC.GetTotalMemory(true);
        });

        string Awake()
        {
            var counts = new Dictionary<string, int>();
            var total = 0;
            var query = entMan.EntityQueryEnumerator<Robust.Shared.Physics.Components.PhysicsComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var body, out var xform))
            {
                if (!body.Awake || body.BodyType == Robust.Shared.Physics.BodyType.Static)
                    continue;
                total++;
                var map = xform.MapUid is { } m ? entMan.GetComponent<MetaDataComponent>(m).EntityName : "nullspace";
                var key = $"{map}: {entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? "(none)"} {body.BodyType}";
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }

            return $"{total} awake bodies; " + string.Join(", ", counts.OrderByDescending(p => p.Value).Take(12).Select(p => $"{p.Key} {p.Value}"));
        }

        var awakeStart = string.Empty;
        await server.WaitPost(() => awakeStart = Awake());

        var ticks = new List<double>();
        var physics = new List<double>();
        var slow = new List<(int Tick, double Ms, string Top)>();
        var totals = new Dictionary<string, double>();
        var start = Sums();
        var previous = start;
        for (var tick = 0; tick < pair.SecondsToTicks(60f); tick++)
        {
            tickWatch.Restart();
            await server.WaitRunTicks(1);
            var ms = tickWatch.Elapsed.TotalMilliseconds;
            ticks.Add(ms);
            var now = Sums();
            physics.Add((now.GetValueOrDefault("PhysicsSystem") - previous.GetValueOrDefault("PhysicsSystem")) * 1000);
            if (ms >= 50)
                slow.Add((tick, ms, Top(now, previous, 4)));
            previous = now;
        }

        foreach (var (key, value) in previous)
            totals[key] = (value - start.GetValueOrDefault(key)) * 1000;

        var entitiesAfter = 0;
        long memoryAfter = 0;
        var awakeEnd = string.Empty;
        var asleep = string.Empty;
        await server.WaitPost(() =>
        {
            awakeEnd = Awake();
            entitiesAfter = entMan.EntityCount;
            memoryAfter = GC.GetTotalMemory(true);
            var sleeper = server.System<WFSiteStashSystem>();
            var regions = sleeper.Regions;
            asleep = $"{regions.Count(r => r.State == WFStashState.Stashed)} of {regions.Count} regions asleep "
                     + $"({regions.Count(r => r.Protected)} kept awake for spawn points), {regions.Sum(r => r.Paused.Count)} entities paused, "
                     + $"worst tick spent pausing and waking {sleeper.WorstTickMs:F1} ms";
        });

        var sb = new StringBuilder();
        sb.AppendLine($"Stash {(stash ? "on" : "off")}, lazy navmesh {(lazy ? "on" : "off")}, sites asleep {(sites ? "on" : "off")}. After the minute: {entitiesAfter} entities, managed memory {memoryAfter / 1048576} MB.");
        sb.AppendLine($"Sites: {asleep}.");
        sb.AppendLine($"Six worlds formed in {formed:F1} s (worst forming tick {worstForming:F0} ms). Entities: {entitiesBefore} before, {entitiesFormed} formed. Managed memory {memoryBefore / 1048576} MB before, {memory / 1048576} MB formed.");
        sb.AppendLine($"Forming ticks over 250 ms: {slowForming.Count}, over 1 s: {slowForming.Count(s => s.Ms >= 1000)}.");
        foreach (var (ms, top) in slowForming.OrderByDescending(s => s.Ms).Take(12))
            sb.AppendLine($"  forming tick {ms:F0} ms ({top})");
        sb.AppendLine($"First minute: worst tick {ticks.Max():F0} ms, mean {ticks.Average():F1} ms, ticks over 50 ms: {ticks.Count(t => t >= 50)}, over 100 ms: {ticks.Count(t => t >= 100)}.");
        foreach (var (tick, ms, top) in slow.OrderByDescending(s => s.Ms).Take(15))
            sb.AppendLine($"  tick {tick}: {ms:F0} ms ({top})");
        var bucket = Math.Max(1, ticks.Count / 6);
        for (var i = 0; i < ticks.Count; i += bucket)
        {
            var span = ticks.Skip(i).Take(bucket).ToList();
            sb.AppendLine($"  ticks {i}-{i + span.Count - 1}: mean {span.Average():F1} ms, physics mean {physics.Skip(i).Take(bucket).Average():F1} ms");
        }
        sb.AppendLine("Awake at the start: " + awakeStart);
        sb.AppendLine("Awake at the end: " + awakeEnd);
        sb.AppendLine("Top systems over the minute (ms): " + string.Join(", ", totals.OrderByDescending(p => p.Value).Take(20).Select(p => $"{p.Key} {p.Value:F0}")));
        TestContext.Out.WriteLine(sb.ToString());

        await server.WaitPost(() =>
        {
            esm.MetricsEnabled = false;
            var query = entMan.EntityQueryEnumerator<WFPlanetNetworkComponent>();
            var built = new List<EntityUid>();
            while (query.MoveNext(out var uid, out _))
                built.Add(uid);
            foreach (var network in built)
                networks.DeleteNetwork(network);
        });
        await server.WaitRunTicks(5);
        await pair.CleanReturnAsync();
    }
}
