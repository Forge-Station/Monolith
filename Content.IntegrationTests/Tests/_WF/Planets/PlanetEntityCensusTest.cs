#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.NPC.HTN;
using Content.Server.Power.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Decals;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Counts what a formed world holds once nobody is near it, by prototype and by where it came from.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetPreloadSystem))]
public sealed class PlanetEntityCensusTest
{
    /// <summary>Builds a round-start world as the lobby does and prints its census; run by hand.</summary>
    [Explicit]
    [TestCase("WFSurfaceAsclepiu")]
    [TestCase("WFSurfaceMerak")]
    [TestCase("WFSurfaceFervidus")]
    [TestCase("WFSurfaceThrascias")]
    [TestCase("WFSurfaceAerumna")]
    [TestCase("WFSurfaceCarcinoma")]
    [TestCase("WFSurfaceAsclepiu", false)]
    public async Task Census(string surfaceId, bool sites = true)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var census = await FormWorld(pair, surfaceId, sites: sites);

        try
        {
            TestContext.Out.WriteLine(census.Report);
        }
        finally
        {
            await PlanetFixture.Teardown(pair, census.Layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// What a formed road world holds with nobody near: its sites, roads, camps and wrecks, but not its terrain's
    /// trees and rocks. Set from Merak's count once the stash went in (about 27,000, against 60,000 before it), so it can't silently
    /// grow back.
    /// </summary>
    public const int EagerLimit = 32_000;

    [Test]
    public async Task AFormedWorldKeepsLittleOutWithNobodyNear()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var census = await FormWorld(pair, "WFSurfaceMerak", settleSeconds: 5f);

        try
        {
            TestContext.Out.WriteLine(census.Report);
            Assert.Multiple(() =>
            {
                Assert.That(census.Total, Is.LessThan(EagerLimit), "The formed world holds more than it should with nobody near.");
                // What stays is beside the sites' built floors and under the wrecks and camps: about 3,500 of 42,500.
                Assert.That(census.Categories.GetValueOrDefault("biome terrain"), Is.LessThan(6000), "The terrain's own entities stayed out with nobody near.");
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, census.Layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>What one formed world holds and how its forming went.</summary>
    public sealed class WorldCensus
    {
        public List<EntityUid> Layers = new();
        public int Total;
        public int Ground;
        public Dictionary<string, int> Categories = new();
        public Dictionary<string, int> Prototypes = new();
        public string Report = string.Empty;
    }

    /// <summary>
    /// Prebuilds a surface with every round-start feature on, ticks until its preload is done and a further minute
    /// has passed, and counts every entity it added.
    /// </summary>
    /// <param name="sites">Whether sites nobody is near sleep (WFSiteStashSystem).</param>
    public static async Task<WorldCensus> FormWorld(Pair.TestPair pair, string surfaceId, float settleSeconds = 60f, bool sites = false)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WFPlanetNetworkSystem>();
        var preload = server.System<WFPlanetPreloadSystem>();
        var census = new WorldCensus();
        var before = new HashSet<EntityUid>();
        long memoryBefore = 0;
        WFPlanetSurfacePrototype surface = default!;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, true);
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.FixedCities, true);
            server.CfgMan.SetCVar(PlanetCVars.Prebuild, false);
            server.CfgMan.SetCVar(PlanetCVars.SiteStash, sites);
            // Formed at the lobby's pace, as a real round's are.
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, server.CfgMan.GetCVar(PlanetCVars.PreloadBudgetLobby));
            server.CfgMan.SetCVar(PlanetCVars.GenerationBudget, server.CfgMan.GetCVar(PlanetCVars.GenerationBudgetLobby));

            foreach (var uid in entMan.GetEntities())
                before.Add(uid);

            memoryBefore = GC.GetTotalMemory(true);
            surface = proto.Index<WFPlanetSurfacePrototype>(surfaceId);
            Assert.That(networks.Prebuild(surface, Vector2.Zero, surfaceId), Is.True);

            var query = entMan.EntityQueryEnumerator<WFPlanetNetworkComponent>();
            while (query.MoveNext(out _, out var comp))
            {
                census.Layers.AddRange(comp.Layers);
                census.Layers.AddRange(comp.LowerLayers);
            }
        });

        var ground = census.Layers[0];
        var watch = new Stopwatch();
        var formingTicks = new List<double>();
        var total = Stopwatch.StartNew();

        for (var tick = 0; tick < 200_000; tick++)
        {
            var done = false;
            await server.WaitPost(() => done = entMan.HasComponent<WFPlanetPreloadedComponent>(ground) && !preload.IsPreloading(ground));
            if (done)
                break;

            watch.Restart();
            await server.WaitRunTicks(1);
            formingTicks.Add(watch.Elapsed.TotalMilliseconds);
        }

        var formedIn = total.Elapsed.TotalSeconds;
        Assert.That(entMan.HasComponent<WFPlanetPreloadedComponent>(ground), Is.True, $"{surfaceId} never formed.");

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, PlanetCVars.PreloadBudget.DefaultValue);
            server.CfgMan.SetCVar(PlanetCVars.GenerationBudget, PlanetCVars.GenerationBudget.DefaultValue);
        });

        // The minute after: whatever the world still does with nobody near it.
        var settleTicks = new List<double>();
        for (var tick = 0; tick < pair.SecondsToTicks(settleSeconds); tick++)
        {
            watch.Restart();
            await server.WaitRunTicks(1);
            settleTicks.Add(watch.Elapsed.TotalMilliseconds);
        }

        await server.WaitPost(() =>
        {
            var memoryAfter = GC.GetTotalMemory(true);
            var layerSet = census.Layers.ToHashSet();
            var lowers = entMan.GetComponent<WFPlanetNetworkComponent>(entMan.GetComponent<WFPlanetLayerComponent>(ground).Network is { } net
                ? entMan.GetEntity(net)
                : EntityUid.Invalid).LowerLayers.ToHashSet();
            var biome = entMan.GetComponent<BiomeComponent>(ground);
            var biomeLoaded = new HashSet<EntityUid>();
            foreach (var layer in census.Layers)
            {
                if (!entMan.TryGetComponent<BiomeComponent>(layer, out var layerBiome))
                    continue;
                foreach (var chunk in layerBiome.LoadedEntities.Values)
                    biomeLoaded.UnionWith(chunk.Keys);
            }

            entMan.TryGetComponent<WFPlanetPoiComponent>(ground, out var poi);
            var furniture = poi?.Furniture.ToHashSet() ?? new HashSet<EntityUid>();
            var tunnels = poi?.Tunnels.SelectMany(t => t).ToHashSet() ?? new HashSet<Vector2i>();
            var maps = server.System<SharedMapSystem>();
            var groundGrid = entMan.GetComponent<MapGridComponent>(ground);
            var components = new Dictionary<string, int>();
            var biomeProtos = new Dictionary<string, int>();
            var siteProtos = new Dictionary<string, Dictionary<string, int>>();
            var noProto = new Dictionary<string, int>();
            var offWorld = new Dictionary<string, int>();
            var paused = new Dictionary<string, int>();

            void Bump(Dictionary<string, int> dict, string key) => dict[key] = dict.GetValueOrDefault(key) + 1;

            foreach (var uid in entMan.GetEntities())
            {
                if (before.Contains(uid) || !entMan.TryGetComponent<TransformComponent>(uid, out var xform))
                    continue;

                if (xform.MapUid is not { } map || !layerSet.Contains(map))
                {
                    if (!layerSet.Contains(uid))
                    {
                        census.Total++;
                        Bump(census.Categories, "off the world's maps (network, nullspace)");
                        var where = xform.MapUid is { } other ? entMan.GetComponent<MetaDataComponent>(other).EntityName : "nullspace";
                        Bump(offWorld, $"{where}: {entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? "(no prototype)"}");
                    }
                    else
                    {
                        census.Total++;
                        Bump(census.Categories, "maps & grids");
                    }

                    continue;
                }

                census.Total++;
                var meta = entMan.GetComponent<MetaDataComponent>(uid);
                var id = meta.EntityPrototype?.ID ?? "(no prototype)";
                Bump(census.Prototypes, id);
                if (meta.EntityPrototype == null)
                {
                    var names = entMan.GetComponents(uid).Select(c => c.GetType().Name)
                        .Where(n => n is not ("MetaDataComponent" or "TransformComponent")).OrderBy(n => n).Take(4);
                    Bump(noProto, string.Join("+", names));
                }

                if (entMan.HasComponent<Robust.Server.GameObjects.PointLightComponent>(uid)) Bump(components, "PointLight");
                if (entMan.HasComponent<CableComponent>(uid)) Bump(components, "Cable");
                if (entMan.HasComponent<SolutionContainerManagerComponent>(uid)) Bump(components, "SolutionContainerManager");
                if (entMan.HasComponent<MobStateComponent>(uid)) Bump(components, "MobState");
                if (entMan.HasComponent<HTNComponent>(uid)) Bump(components, "HTN (NPC)");
                if (entMan.HasComponent<Content.Shared.Audio.AmbientSoundComponent>(uid)) Bump(components, "AmbientSound");
                if (entMan.HasComponent<Content.Server.Spawners.Components.EntityTableSpawnerComponent>(uid)
                    || entMan.HasComponent<Content.Server.Spawners.Components.ConditionalSpawnerComponent>(uid)
                    || entMan.HasComponent<Content.Server.Spawners.Components.RandomSpawnerComponent>(uid))
                    Bump(components, "spawner (still standing)");
                if (entMan.HasComponent<MapGridComponent>(uid)) Bump(components, "grid");
                if (!entMan.HasComponent<Robust.Shared.Physics.Components.PhysicsComponent>(uid) && entMan.HasComponent<MapGridComponent>(xform.ParentUid))
                    Bump(components, "on a grid without physics (marker-like)");

                if (layerSet.Contains(uid) || entMan.HasComponent<MapGridComponent>(uid))
                {
                    Bump(census.Categories, "maps & grids");
                    continue;
                }

                // The entity placed on a grid that holds this one, if it is inside something.
                var root = uid;
                var rootXform = xform;
                while (rootXform.ParentUid.IsValid()
                       && !entMan.HasComponent<MapGridComponent>(rootXform.ParentUid)
                       && !entMan.HasComponent<MapComponent>(rootXform.ParentUid))
                {
                    root = rootXform.ParentUid;
                    rootXform = entMan.GetComponent<TransformComponent>(root);
                }

                var inside = root != uid ? " (inside)" : string.Empty;
                string category;

                if (lowers.Contains(map))
                    category = biomeLoaded.Contains(root) ? "caverns: biome terrain" : "caverns: other";
                else if (map != ground)
                    category = "air/orbit layers";
                else if (rootXform.ParentUid != ground)
                    category = "separate grids on the ground (wrecks, camps, vehicles)";
                else if (biomeLoaded.Contains(root))
                {
                    category = "biome terrain";
                    if (root == uid)
                        Bump(biomeProtos, id);
                }
                else if (furniture.Contains(root))
                    category = "road dressing";
                else if (meta.EntityPrototype?.ID == surface.BoundaryWall)
                    category = "boundary ring";
                else
                {
                    var tile = maps.LocalToTile(ground, groundGrid, rootXform.Coordinates);
                    var site = poi?.Sites.FirstOrDefault(s => s.Bounds.Contains(tile));
                    if (site != null)
                    {
                        var name = site.MapPath ?? site.Dungeon?.Id ?? site.Name.Id;
                        category = $"site: {name}";
                        if (!siteProtos.TryGetValue(name, out var protos))
                            siteProtos[name] = protos = new Dictionary<string, int>();
                        Bump(protos, id);
                    }
                    else if (tunnels.Contains(tile))
                        category = "tunnels";
                    else if (entMan.HasComponent<MobStateComponent>(root))
                        category = "mobs elsewhere on the ground";
                    else if (poi != null && (poi.RoadTiles.Contains(tile) || poi.ShoulderTiles.Contains(tile)))
                        category = "on roads, not dressing";
                    else
                        category = "other on the ground";
                }

                if (map == ground)
                    census.Ground++;

                Bump(census.Categories, category + inside);
                if (meta.EntityPaused)
                    Bump(paused, category);
            }

            var decals = 0;
            if (entMan.TryGetComponent<DecalGridComponent>(ground, out var decalGrid))
                decals = decalGrid.ChunkCollection.ChunkCollection.Values.Sum(chunk => chunk.Decals.Count);

            var sb = new StringBuilder();
            sb.AppendLine($"## {surfaceId} (radius {entMan.GetComponent<WFPlanetBoundsComponent>(ground).Radius})");
            sb.AppendLine();
            sb.AppendLine($"Entities added: {census.Total} ({census.Ground} on the ground map). Decals on the ground: {decals}. "
                          + $"Biome chunks loaded on the ground: {biome.LoadedChunks.Count}.");
            sb.AppendLine($"Managed memory: {memoryBefore / 1048576} MB before, {memoryAfter / 1048576} MB after (+{(memoryAfter - memoryBefore) / 1048576} MB).");
            sb.AppendLine($"Formed in {formedIn:F1} s wall over {formingTicks.Count} ticks; worst forming tick {formingTicks.DefaultIfEmpty(0).Max():F0} ms, "
                          + $"ticks over 50 ms: {formingTicks.Count(t => t > 50)}, over 100 ms: {formingTicks.Count(t => t > 100)}.");
            sb.AppendLine($"Next {settleSeconds:F0} s idle: worst tick {settleTicks.DefaultIfEmpty(0).Max():F1} ms, mean {settleTicks.DefaultIfEmpty(0).Average():F2} ms.");
            sb.AppendLine($"Sites asleep: {(sites ? "on" : "off")}; paused: {paused.Values.Sum()} ("
                          + string.Join(", ", paused.OrderByDescending(p => p.Value).Take(8).Select(p => $"{p.Key} {p.Value}")) + ").");
            var sleeper = server.System<Content.Server._WF.Dormancy.WFSiteStashSystem>();
            sb.AppendLine($"Worst tick spent pausing and waking: {sleeper.WorstTickMs:F2} ms ({sleeper.WorstTickWork.Trim()}).");
            // What the per-tick systems still walk: an entity of these paused costs its system nothing.
            var walked = new List<string>();
            foreach (var name in new[] { "Metabolizer", "Respirator", "Bloodstream", "Stomach", "HTN", "ActiveNPC", "ApcPowerReceiver", "Flammable", "SolutionRegeneration", "PointLight" })
            {
                if (!entMan.ComponentFactory.TryGetRegistration(name, out var registration))
                    continue;

                var all = 0;
                var asleep = 0;
                foreach (var (owner, _) in entMan.GetAllComponents(registration.Type, includePaused: true))
                {
                    if (before.Contains(owner) || entMan.GetComponent<TransformComponent>(owner).MapUid is not { } where || !layerSet.Contains(where))
                        continue;

                    all++;
                    if (entMan.GetComponent<MetaDataComponent>(owner).EntityPaused)
                        asleep++;
                }

                walked.Add($"{name} {all - asleep} of {all}");
            }

            sb.AppendLine("Awake on the world (of all): " + string.Join(", ", walked) + ".");
            sb.AppendLine();
            sb.AppendLine("| Category | Entities |");
            sb.AppendLine("|---|---|");
            foreach (var (key, count) in census.Categories.OrderByDescending(p => p.Value))
                sb.AppendLine($"| {key} | {count} |");
            sb.AppendLine();
            sb.AppendLine("| Component | Entities |");
            sb.AppendLine("|---|---|");
            foreach (var (key, count) in components.OrderByDescending(p => p.Value))
                sb.AppendLine($"| {key} | {count} |");
            sb.AppendLine();
            sb.AppendLine("| Prototype (top 40) | Entities |");
            sb.AppendLine("|---|---|");
            foreach (var (key, count) in census.Prototypes.OrderByDescending(p => p.Value).Take(40))
                sb.AppendLine($"| {key} | {count} |");
            sb.AppendLine();
            sb.AppendLine("Biome terrain by prototype (top 15): " + string.Join(", ", biomeProtos.OrderByDescending(p => p.Value).Take(15).Select(p => $"{p.Key} {p.Value}")));
            sb.AppendLine("Off the world's maps (top 8): " + string.Join(", ", offWorld.OrderByDescending(p => p.Value).Take(8).Select(p => $"{p.Key} {p.Value}")));
            sb.AppendLine("No prototype, by components (top 8): " + string.Join(", ", noProto.OrderByDescending(p => p.Value).Take(8).Select(p => $"{p.Key} {p.Value}")));
            foreach (var (site, protos) in siteProtos.OrderByDescending(p => p.Value.Values.Sum()).Take(6))
                sb.AppendLine($"Site {site} (top 10): " + string.Join(", ", protos.OrderByDescending(p => p.Value).Take(10).Select(p => $"{p.Key} {p.Value}")));
            census.Report = sb.ToString();
        });

        return census;
    }
}
