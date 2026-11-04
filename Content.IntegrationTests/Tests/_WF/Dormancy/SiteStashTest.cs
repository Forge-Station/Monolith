#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.PlanetCities;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Dormancy;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Server.Spawners.Components;
using Content.Shared._WF.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Dormancy;

/// <summary>
/// A site nobody is near sleeps where it stands and wakes as it was, NPCs' wounds and grudges included, before anyone
/// coming can see it; a city never sleeps where players spawn.
/// </summary>
[TestFixture]
[TestOf(typeof(WFSiteStashSystem))]
public sealed class SiteStashTest
{
    private const int Radius = 128;
    private const string ShopPath = "/Maps/_WF/PlanetPois/mechanic_shop.yml";
    private const float Restore = 48f;
    private const float Stash = 64f;
    private const float View = 25f;

    [TestPrototypes]
    private const string Prototypes = @"
- type: wfPlanetPoiTable
  id: WFPoisStashTest
  minCount: 1
  maxCount: 1
  spacing: 32
  edgeMargin: 56
  centreClear: 24
  footprint: 24
  revealRange: 48
  entries:
  - mapPath: /Maps/_WF/PlanetPois/mechanic_shop.yml
    name: wf-poi-mechanic-shop
    expeditions: false
    footprint: 0,-10,15,11
    connection: Isolated

- type: entity
  id: WFTestStashGrudger
  parent: MobMouse
  components:
  - type: NPCRetaliation
    attackMemoryLength: 5
";

    [Test]
    public async Task ASiteSleepsAndWakesAsItWasLeft()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var stash = server.System<WFSiteStashSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        // Nobody walks off, so the shop can be compared spot for spot.
        await server.WaitPost(() => server.CfgMan.SetCVar(Shared.CCVar.CCVars.NPCEnabled, false));
        var layers = await BuildWorld(pair);
        var ground = layers[0];

        try
        {
            var region = await WaitStashed(pair, ShopPath);
            var keeper = EntityUid.Invalid;
            await server.WaitPost(() =>
            {
                var awake = Contents(pair, region, skipInert: true).Where(uid => !Paused(entMan, uid)).ToList();
                Assert.That(awake, Is.Empty, $"Left awake with nobody near: {string.Join(", ", awake.Take(8).Select(uid => entMan.ToPrettyString(uid)))}");
                keeper = Contents(pair, region).FirstOrDefault(uid => entMan.HasComponent<MobStateComponent>(uid));
                Assert.That(keeper, Is.Not.EqualTo(EntityUid.Invalid), "The shop has nobody in it.");
                Assert.That(Paused(entMan, keeper), Is.True, "The shopkeeper was left awake.");
            });

            // Someone comes in: everything wakes.
            var centre = region.Box.Center;
            var viewer = await PlanetFixture.AttachViewer(pair, ground, centre);
            await pair.RunTicksSync(pair.SecondsToTicks(1f));
            Assert.That(region.State, Is.EqualTo(WFStashState.Awake), "The shop stayed asleep round a visitor.");
            await server.WaitPost(() =>
            {
                Assert.That(Contents(pair, region).Where(uid => Paused(entMan, uid)), Is.Empty, "Something in the shop stayed paused.");
            });

            // The visitor hurts the keeper, takes something, drops something and picks a fight with a mouse.
            EntityUid taken = default;
            EntityUid dropped = default;
            EntityUid grudger = default;
            var damage = 0f;
            await server.WaitPost(() =>
            {
                var hurt = new DamageSpecifier { DamageDict = { ["Blunt"] = 7 } };
                server.System<DamageableSystem>().TryChangeDamage(keeper, hurt, true);
                damage = entMan.GetComponent<DamageableComponent>(keeper).TotalDamage.Float();
                Assert.That(damage, Is.GreaterThan(0f));

                taken = Contents(pair, region).First(uid => entMan.HasComponent<ItemComponent>(uid)
                    && entMan.GetComponent<TransformComponent>(uid).ParentUid == ground
                    && !entMan.GetComponent<TransformComponent>(uid).Anchored);
                xforms.SetCoordinates(taken, new EntityCoordinates(viewer, Vector2.Zero));

                dropped = entMan.SpawnEntity("SheetSteel1", new EntityCoordinates(ground, centre + new Vector2(0.3f, 0.3f)));
                grudger = entMan.SpawnEntity("WFTestStashGrudger", new EntityCoordinates(ground, centre + new Vector2(-0.6f, 0.4f)));
                Assert.That(server.System<NPCRetaliationSystem>().TryRetaliate((grudger, entMan.GetComponent<NPCRetaliationComponent>(grudger)), viewer), Is.True);
            });

            // The visitor leaves the planet with the loot: the shop sleeps again, and for longer than the mouse remembers.
            var away = await pair.CreateTestMap();
            await server.WaitPost(() => xforms.SetCoordinates(viewer, new EntityCoordinates(away.MapUid, Vector2.Zero)));
            await pair.RunTicksSync(pair.SecondsToTicks(3f));
            Assert.That(region.State, Is.EqualTo(WFStashState.Stashed), "The shop didn't sleep once the visitor left.");
            await server.WaitPost(() =>
            {
                Assert.That(Paused(entMan, keeper) && Paused(entMan, dropped) && Paused(entMan, grudger), Is.True, "What the visitor left behind was left awake.");
                Assert.That(Paused(entMan, taken), Is.False, "What the visitor took was paused with the shop.");
            });
            var before = await Snapshot(pair, region);
            Assert.That(before, Does.Contain(Describe(entMan, dropped)));
            await server.WaitPost(() => damage = entMan.GetComponent<DamageableComponent>(keeper).TotalDamage.Float());
            await pair.RunTicksSync(pair.SecondsToTicks(8f));

            // Back again: all as it was left, the keeper still hurt and the mouse still angry.
            await server.WaitPost(() => xforms.SetCoordinates(viewer, new EntityCoordinates(ground, centre + new Vector2(0f, 2f))));
            for (var i = 0; i < 60 && region.State != WFStashState.Awake; i++)
                await server.WaitRunTicks(1);
            var after = await Snapshot(pair, region);
            var woken = 0f;
            await server.WaitPost(() => woken = entMan.GetComponent<DamageableComponent>(keeper).TotalDamage.Float());
            Assert.That(region.State, Is.EqualTo(WFStashState.Awake));
            Assert.That(after, Is.EquivalentTo(before), "The shop came back different.");
            Assert.That(woken, Is.EqualTo(damage), "The keeper's wounds changed in his sleep.");
            await pair.RunTicksSync(pair.SecondsToTicks(1f));
            await server.WaitPost(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(taken).ParentUid, Is.EqualTo(viewer), "The loot went back to the shop.");
                    Assert.That(server.System<NpcFactionSystem>().GetHostiles(new Entity<FactionExceptionComponent?>(grudger, null)), Does.Contain(viewer), "The mouse forgot its grudge in its sleep.");
                    Assert.That(entMan.GetComponent<NPCRetaliationComponent>(grudger).AttackMemories[viewer], Is.GreaterThan(timing.CurTime),
                        "The mouse's memory of the fight ran out while it slept.");
                });
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Someone driving at a site finds it awake before it comes into view, and no tick wakes it in one costly go.</summary>
    [Test]
    public async Task ASiteWakesBeforeAnyoneDrivingUpSeesIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var stash = server.System<WFSiteStashSystem>();
        var xforms = server.System<SharedTransformSystem>();

        var layers = await BuildWorld(pair);
        var ground = layers[0];

        try
        {
            var region = await WaitStashed(pair, ShopPath);
            var centre = region.Box.Center;
            var inward = centre.LengthSquared() < 1f ? Vector2.UnitX : -Vector2.Normalize(centre);
            var start = centre - inward * 70f;
            if (start.Length() > Radius - 8)
                start = centre + inward * 70f;
            var heading = Vector2.Normalize(centre - start);

            var viewer = await PlanetFixture.AttachViewer(pair, ground, start);
            await pair.RunTicksSync(pair.SecondsToTicks(1f));
            Assert.That(region.State, Is.EqualTo(WFStashState.Stashed), "The shop woke for someone well out of range.");

            // A mouse roams the open ground on the way; with nobody about it sleeps too.
            var mouse = EntityUid.Invalid;
            await server.WaitPost(() => mouse = entMan.SpawnEntity("WFTestStashGrudger", new EntityCoordinates(ground, start + heading * 20f)));
            var away = await pair.CreateTestMap();
            await server.WaitPost(() => xforms.SetCoordinates(viewer, new EntityCoordinates(away.MapUid, Vector2.Zero)));
            await pair.RunTicksSync(pair.SecondsToTicks(3f));
            await server.WaitPost(() => Assert.That(Paused(entMan, mouse), Is.True, "A mouse in the open stayed awake with nobody on the world."));
            await server.WaitPost(() => xforms.SetCoordinates(viewer, new EntityCoordinates(ground, start)));
            await pair.RunTicksSync(pair.SecondsToTicks(1f));

            await server.WaitPost(() => stash.WorstTickMs = 0);
            const float speed = 15f;
            var step = speed / pair.SecondsToTicks(1f);
            var position = start;
            var seen = new List<string>();
            for (var travelled = 0f; travelled < 70f; travelled += step)
            {
                position += heading * step;
                var here = position;
                await server.WaitPost(() =>
                {
                    xforms.SetCoordinates(viewer, new EntityCoordinates(ground, here));
                    foreach (var uid in stash.Regions.SelectMany(r => r.Paused))
                    {
                        if (entMan.EntityExists(uid) && Paused(entMan, uid)
                            && Vector2.Distance(xforms.GetWorldPosition(uid), here) <= View)
                            seen.Add(entMan.ToPrettyString(uid).ToString());
                    }
                });
                await server.WaitRunTicks(1);
            }

            Assert.That(seen, Is.Empty, $"Paused things came into view: {string.Join(", ", seen.Distinct().Take(8))}");
            Assert.That(region.State, Is.EqualTo(WFStashState.Awake));
            await server.WaitPost(() => Assert.That(Paused(entMan, mouse), Is.False, "The mouse on the way slept on."));
            TestContext.Out.WriteLine($"Worst stash tick on the way in: {stash.WorstTickMs:F2} ms ({stash.WorstTickWork.Trim()})");
            // A busy CI runner can stall any tick for a tenth of a second, so there this only catches a wake in one go.
            var limit = Environment.GetEnvironmentVariable("CI") == null ? 8.0 : 250.0;
            Assert.That(stash.WorstTickMs, Is.LessThan(limit), "Waking the shop cost a tick too much.");
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Crocus sleeps a block at a time, never where players spawn, and holds what it paused.</summary>
    [Test]
    public async Task CrocusNeverSleepsWherePlayersSpawn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var stash = server.System<WFSiteStashSystem>();
        await PlanetFixture.EnableFeature(pair);
        await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.SiteStash, true));

        var (layers, lower, holds) = await FixedCityTest.BuildCrocus(pair, false);
        var ground = layers[0];
        try
        {
            Assert.That(holds.All(hold => hold.IsCompletedSuccessfully), Is.True, "Crocus failed to build.");
            await server.WaitPost(() => stash.Register(ground));
            for (var i = 0; i < 200 && stash.Regions.Any(r => r.Ground == ground && r.State is WFStashState.Awake or WFStashState.Stashing && !r.Protected); i++)
                await pair.RunTicksSync(5);

            await server.WaitPost(() =>
            {
                var regions = stash.Regions.Where(r => r.Ground == ground && r.Label.StartsWith("/Maps/_Mono/Crocus")).ToList();
                var paused = regions.SelectMany(r => r.Paused).ToHashSet();
                // The ground is level 0, the air above counts up and what is below counts down.
                var depths = layers.Select((layer, i) => (layer, i)).Concat(lower.Select((layer, i) => (layer, -i - 1)))
                    .ToDictionary(pair => pair.Item1, pair => pair.Item2);
                var xforms = server.System<SharedTransformSystem>();
                var spawns = new List<(Vector2 Position, int Depth)>();
                var points = entMan.AllEntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
                while (points.MoveNext(out _, out _, out var xform))
                {
                    if (xform.MapUid is { } map && depths.TryGetValue(map, out var depth))
                        spawns.Add((xforms.GetWorldPosition(xform), depth));
                }

                var mobs = paused.Count(entMan.HasComponent<MobStateComponent>);
                TestContext.Out.WriteLine($"Crocus: {regions.Count} blocks, {regions.Count(r => r.Protected)} kept awake for {spawns.Count} spawn points, "
                                          + $"{regions.Count(r => r.State == WFStashState.Stashed)} asleep holding {paused.Count} paused entities ({mobs} mobs); world has {entMan.EntityCount}.");
                foreach (var level in regions.GroupBy(r => r.Label[(r.Label.LastIndexOf("level ", StringComparison.Ordinal) + 6)..]).OrderBy(g => int.Parse(g.Key)))
                {
                    var all = level.SelectMany(r => Contents(pair, r)).ToList();
                    TestContext.Out.WriteLine($"  level {level.Key}: {level.Count()} blocks, {level.Count(r => r.Protected)} kept awake; {all.Count} entities, "
                                              + $"{all.Count(uid => Inert(entMan, uid))} walls and cables, {level.Sum(r => r.Paused.Count)} paused, "
                                              + $"{all.Count(uid => entMan.HasComponent<MobStateComponent>(uid) && !entMan.GetComponent<TransformComponent>(uid).Anchored)} mobs "
                                              + $"({level.SelectMany(r => r.Paused).Count(entMan.HasComponent<MobStateComponent>)} paused)");
                }

                Assert.That(regions, Is.Not.Empty, "Crocus was not cut into blocks.");
                Assert.That(spawns, Is.Not.Empty, "Crocus has no spawn points.");
                Assert.That(regions.Count(r => r.Protected), Is.GreaterThan(0), "No block was kept awake for the spawn points.");
                Assert.That(regions.Count(r => r.State == WFStashState.Stashed), Is.GreaterThan(0), "No block of Crocus slept.");
                Assert.That(mobs, Is.GreaterThan(0), "Nobody in Crocus slept.");

                // On a spawn point's level, or the one under it that shows through the stairwells.
                var near = paused.Where(uid => entMan.EntityExists(uid)
                                               && entMan.GetComponent<TransformComponent>(uid).MapUid is { } map
                                               && spawns.Any(spawn => (spawn.Depth == depths[map] || spawn.Depth == depths[map] + 1)
                                                                      && Vector2.Distance(spawn.Position, xforms.GetWorldPosition(uid)) < WFSiteStashSystem.SpawnClearance))
                    .ToList();
                Assert.That(near, Is.Empty, $"Paused beside a spawn point: {string.Join(", ", near.Take(8).Select(uid => entMan.ToPrettyString(uid)))}");
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A small world with the mechanic's shop on it, formed with nobody near and site stashing on.</summary>
    private static async Task<List<EntityUid>> BuildWorld(TestPair pair)
    {
        var server = pair.Server;
        await PlanetFixture.EnableFeature(pair);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, Radius);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WFPoisStashTest");
            server.CfgMan.SetCVar(PlanetCVars.SiteStash, true);
            server.CfgMan.SetCVar(PlanetCVars.SiteStashIdle, 1f);
            server.CfgMan.SetCVar(PlanetCVars.SiteRestoreRange, Restore);
            server.CfgMan.SetCVar(PlanetCVars.SiteStashRange, Stash);
        });

        var layers = await PlanetFixture.BuildStandalone(pair);
        for (var i = 0; i < 600 && !pair.Server.EntMan.HasComponent<Server._WF.Planets.Bounds.WFPlanetPreloadedComponent>(layers[0]); i++)
            await pair.RunTicksSync(10);

        Assert.That(pair.Server.EntMan.HasComponent<Server._WF.Planets.Bounds.WFPlanetPreloadedComponent>(layers[0]), Is.True, "The world never formed.");
        return layers;
    }

    /// <summary>Waits for a site's region to be registered and asleep.</summary>
    private static async Task<WFStashRegion> WaitStashed(TestPair pair, string label)
    {
        var stash = pair.Server.System<WFSiteStashSystem>();
        WFStashRegion? region = null;
        for (var i = 0; i < 100 && region?.State != WFStashState.Stashed; i++)
        {
            await pair.RunTicksSync(5);
            region = stash.Regions.FirstOrDefault(r => r.Label == label);
        }

        Assert.That(region, Is.Not.Null, "The site was never registered.");
        Assert.That(region!.State, Is.EqualTo(WFStashState.Stashed), "The site never slept with nobody near.");
        return region;
    }

    /// <summary>Everything standing in a region's box on its layers, and all it holds.</summary>
    private static List<EntityUid> Contents(TestPair pair, WFStashRegion region, bool skipInert = false)
    {
        var entMan = pair.Server.EntMan;
        var lookup = pair.Server.System<EntityLookupSystem>();
        var result = new List<EntityUid>();
        var found = new HashSet<EntityUid>();
        foreach (var parent in region.Parents.Concat(region.Hulls))
        {
            found.Clear();
            lookup.GetLocalEntitiesIntersecting(parent, region.Box, found, LookupFlags.Uncontained | LookupFlags.Approximate);
            foreach (var uid in found)
            {
                var xform = entMan.GetComponent<TransformComponent>(uid);
                if (xform.ParentUid != parent || !region.Box.Contains(xform.LocalPosition) || entMan.HasComponent<Robust.Shared.Map.Components.MapGridComponent>(uid)
                    || entMan.HasComponent<Robust.Shared.Player.ActorComponent>(uid)
                    || skipInert && (Inert(entMan, uid) || entMan.HasComponent<Shared._WF.Vehicles.Core.VehicleEnterComponent>(uid)
                        // A passing sound (wind, a hiss) is a short-lived cue, not something the site keeps.
                        || entMan.HasComponent<Robust.Shared.Audio.Components.AudioComponent>(uid)))
                    continue;

                var stack = new Stack<EntityUid>();
                stack.Push(uid);
                while (stack.TryPop(out var next))
                {
                    result.Add(next);
                    var children = entMan.GetComponent<TransformComponent>(next).ChildEnumerator;
                    while (children.MoveNext(out var child))
                        stack.Push(child);
                }
            }
        }

        return result;
    }

    /// <summary>What stands in a region, by prototype, parent and spot.</summary>
    private static async Task<List<string>> Snapshot(TestPair pair, WFStashRegion region)
    {
        var result = new List<string>();
        await pair.Server.WaitPost(() =>
        {
            foreach (var uid in Contents(pair, region))
                result.Add(Describe(pair.Server.EntMan, uid));
        });
        return result;
    }

    private static string Describe(IEntityManager entMan, EntityUid uid)
    {
        var xform = entMan.GetComponent<TransformComponent>(uid);
        var proto = entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? "(none)";
        return $"{proto} @ {xform.LocalPosition.X:F2},{xform.LocalPosition.Y:F2} r{xform.LocalRotation.Degrees:F0}";
    }

    private static bool Paused(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<MetaDataComponent>(uid).EntityPaused;
    }

    /// <summary>A wall, window, door or cable, which the stash leaves as it is.</summary>
    private static bool Inert(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<TransformComponent>(uid).Anchored
               && (entMan.HasComponent<Server.Atmos.Components.AirtightComponent>(uid) || entMan.HasComponent<Server.Power.Components.CableComponent>(uid));
    }
}
