#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._Mono.FireControl.UI;
using Content.Client._WF.Cockpit;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Drydock;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.AsteroidMining;
using Content.Server._WF.DroneRings;
using Content.Server.Gatherable;
using Content.Server.Power.Components;
using Content.Server._WF.Shuttles.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Worldgen.Components.Debris;
using Content.Server.Worldgen.Systems.Debris;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Shuttles;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.AsteroidMining;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Access.Components;
using Content.Shared.Mining.Components;
using Content.Shared.Physics;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Configuration;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.AsteroidMining;

/// <summary>
/// Tests asteroid grids, cracking into fragments, boulders and ore, worldgen's swap of tile asteroids for clusters,
/// and the mining laser's pulses, heat window and overcharge.
/// </summary>
[TestFixture]
public sealed class AsteroidMiningTest
{
    private const string Gun = "WFWeaponTurretMiningBeam";
    private static readonly Vector2 ShipPos = new(200f, 0f);

    /// <summary>Spawns an asteroid grid at a world position and returns its rock.</summary>
    private static EntityUid SpawnAsteroid(TestPair pair, MapId map, string proto, Vector2 position)
    {
        var em = pair.Server.EntMan;
        var mapUid = pair.Server.System<SharedMapSystem>().GetMap(map);
        var grid = em.SpawnEntity(proto, new EntityCoordinates(mapUid, position));
        return em.GetComponent<WFAsteroidGridComponent>(grid).RockUid!.Value;
    }

    /// <summary>Rocks on their own asteroid grids, leaving out boulders.</summary>
    private static List<WFAsteroidComponent> GridRocks(IEntityManager em)
    {
        var rocks = new List<WFAsteroidComponent>();
        var query = em.EntityQueryEnumerator<WFAsteroidComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid is { } grid && em.TryGetComponent(grid, out WFAsteroidGridComponent? builder) &&
                builder.RockUid == uid)
                rocks.Add(comp);
        }

        return rocks;
    }

    private static List<EntityUid> Boulders(IEntityManager em)
    {
        var boulders = new List<EntityUid>();
        var query = em.EntityQueryEnumerator<WFAsteroidComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == "WFAsteroidBoulder")
                boulders.Add(uid);
        }

        return boulders;
    }

    private static List<string> MetallicNames(IEntityManager em)
    {
        var names = new List<string>();
        var query = em.EntityQueryEnumerator<WFAsteroidComponent, MetaDataComponent>();
        while (query.MoveNext(out _, out var comp, out var meta))
        {
            if (comp.CompositionId?.Id == "WFAsteroidMetallic")
                names.Add(meta.EntityName);
        }

        return names;
    }

    private static int OreItems(IEntityManager em)
    {
        var tags = em.System<TagSystem>();
        var count = 0;
        var query = em.EntityQueryEnumerator<TagComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (tags.HasTag(uid, "Ore"))
                count++;
        }

        return count;
    }

    [Test]
    public async Task CleanCrackSplitsIntoFragmentsAndOre()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var asteroids = server.System<WFAsteroidSystem>();

        EntityUid rock = default, grid = default, passenger = default;
        float mass = 0f, share = 0f;
        await server.WaitAssertion(() =>
        {
            rock = SpawnAsteroid(pair, map.MapId, "WFAsteroidLarge", ShipPos);
            asteroids.Roll(rock, "WFAsteroidRock");
            var comp = em.GetComponent<WFAsteroidComponent>(rock);
            var xform = em.GetComponent<TransformComponent>(rock);
            grid = xform.GridUid!.Value;
            mass = comp.Mass;
            share = comp.FragmentShare;
            Assert.Multiple(() =>
            {
                Assert.That(xform.Anchored, Is.True, "The rock should be anchored at the grid's centre.");
                // Irregular variants fill about half the disc and hold correspondingly less rock.
                Assert.That(server.System<SharedMapSystem>().GetAllTiles(grid, em.GetComponent<MapGridComponent>(grid)).Count(),
                    Is.GreaterThan(30), "A radius 5 rock should sit on a field of tiles.");
                Assert.That(mass, Is.InRange(comp.MinMass * comp.MassScale, comp.MaxMass * comp.MassScale),
                    "The rolled mass should be in the rock's range for its silhouette.");
                Assert.That(comp.Ores, Is.Not.Empty, "The rock should hold at least one ore.");
                Assert.That(comp.Ores.Sum(o => o.Fraction), Is.LessThan(1f), "Part of the rock should be inert.");
            });

            passenger = em.SpawnEntity("Crowbar", new EntityCoordinates(grid, 4.5f, 0.5f));
            comp.BaseCharge = 0.5f;
            asteroids.QueueCrack(rock, WFAsteroidCrack.Clean);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var fragments = GridRocks(em);
            Assert.That(em.Deleted(passenger), Is.False, "Things left on a cracked asteroid should stay behind in space.");
            var boulders = Boulders(em);
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(grid), Is.True, "Cracking should remove the rock's grid.");
                Assert.That(fragments, Has.Count.EqualTo(2), "A large rock should split into two fragments.");
                Assert.That(fragments.Sum(f => f.Mass), Is.EqualTo(mass * share).Within(0.01f),
                    "Fragments should carry the fragment share of the mass.");
                Assert.That(fragments.All(f => f.Ores.Count > 0), Is.True, "Fragments should keep the parent's ores.");
                Assert.That(fragments.All(f => f.BaseCharge > 0f), Is.True, "Fragments should come off hot.");
                Assert.That(boulders, Is.Not.Empty, "Part of the dropped ore should come out as boulders.");
                Assert.That(OreItems(em), Is.GreaterThan(0), "The rest of the mass should drop as ore.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MetallicRockDropsMoreOrePerVein()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var asteroids = server.System<WFAsteroidSystem>();
        var offset = new Vector2(0f, 100f);

        await server.WaitAssertion(() =>
        {
            // The same pure-iron rock twice, once of each kind, light enough to drop no boulders.
            foreach (var (kind, position) in new[] { ("WFAsteroidRock", ShipPos), ("WFAsteroidMetallic", ShipPos + offset) })
            {
                var rock = SpawnAsteroid(pair, map.MapId, "WFAsteroidSmall", position);
                asteroids.Roll(rock, kind);
                var comp = em.GetComponent<WFAsteroidComponent>(rock);
                comp.Mass = 18f;
                comp.Ores = new List<WFAsteroidOre> { new("NFOreSteel", 1f, Color.Brown, 0f) };
                asteroids.QueueCrack(rock, WFAsteroidCrack.Clean);
            }
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var tags = server.System<TagSystem>();
            var xforms = server.System<SharedTransformSystem>();
            int plain = 0, metallic = 0;
            var query = em.EntityQueryEnumerator<StackComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var stack, out var xform))
            {
                if (!tags.HasTag(uid, "Ore"))
                    continue;

                if (xforms.GetWorldPosition(xform).Y > ShipPos.Y + offset.Y / 2f)
                    metallic += stack.Count;
                else
                    plain += stack.Count;
            }

            Assert.That(plain, Is.GreaterThan(0), "The plain rock should drop ore.");
            // Half again as much, less the ±10% roll on each.
            Assert.That(metallic, Is.GreaterThan(plain * 1.2f), "A metallic rock should drop more ore per vein.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GridCarriesItsRocksLookForFarClients()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var asteroids = server.System<WFAsteroidSystem>();

        // Whether every asteroid grid's copy matches the rock that clients out of view range draw it from.
        bool AllMatch()
        {
            var query = em.EntityQueryEnumerator<WFAsteroidGridComponent, WFAsteroidGridVisualsComponent>();
            while (query.MoveNext(out _, out var grid, out var visuals))
            {
                var rock = em.GetComponent<WFAsteroidComponent>(grid.RockUid!.Value);
                if (visuals.Rock != em.GetComponent<MetaDataComponent>(grid.RockUid.Value).EntityPrototype!.ID ||
                    visuals.Variant != rock.Variant || visuals.Look != rock.Look ||
                    visuals.RockColor != rock.RockColor || visuals.VeinColor != rock.VeinColor)
                    return false;
            }

            return true;
        }

        await server.WaitAssertion(() =>
        {
            var rock = SpawnAsteroid(pair, map.MapId, "WFAsteroidLarge", ShipPos);
            asteroids.Roll(rock, "WFAsteroidMetallic");
            Assert.That(AllMatch(), Is.True, "A rolled rock's grid should carry its look.");
            asteroids.QueueCrack(rock, WFAsteroidCrack.Clean);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(GridRocks(em), Has.Count.EqualTo(2), "The rock should split into two fragments.");
                Assert.That(AllMatch(), Is.True, "Each fragment's grid should carry the look it inherited.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WorldgenSwapsOreAsteroidsButKeepsScrap()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var basalt = new WFDebrisPlacingEvent("NFAsteroidBasaltDebrisMassive", new MapCoordinates(ShipPos, map.MapId));
            server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref basalt);
            var scrap = new WFDebrisPlacingEvent("NFAsteroidScrapDebrisMassive", new MapCoordinates(ShipPos, map.MapId));
            server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref scrap);
            Assert.Multiple(() =>
            {
                Assert.That(basalt.Proto, Is.EqualTo("WFAsteroidClusterBasalt"), "A basalt tile asteroid should become a basalt cluster.");
                Assert.That(scrap.Proto, Is.EqualTo("NFAsteroidScrapDebrisMassive"), "Scrap asteroids should stay as tiles.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GasAsteroidsSometimesStayAsBarrenTiles()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        // Away from ShipPos, so the budgets these swaps leave behind can't feed another test's cluster.
        var at = new MapCoordinates(new Vector2(-4000f, -4000f), map.MapId);

        await server.WaitAssertion(() =>
        {
            var outcomes = new Dictionary<string, HashSet<string>>();
            foreach (var proto in new[] { "NFAsteroidDebrisMassive", "NFAsteroidRockDebrisGigantic", "NFAsteroidRockDebrisSmall" })
            {
                outcomes[proto] = new HashSet<string>();
                for (var i = 0; i < 64; i++)
                {
                    var ev = new WFDebrisPlacingEvent(proto, at);
                    server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
                    outcomes[proto].Add(ev.Proto);
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(outcomes["NFAsteroidDebrisMassive"],
                    Is.EquivalentTo(new[] { "WFAsteroidClusterRock", "WFAsteroidBarrenDebrisMassive" }),
                    "A gas-bearing plain asteroid should become either a cluster or a barren tile asteroid.");
                Assert.That(outcomes["NFAsteroidRockDebrisGigantic"],
                    Is.EquivalentTo(new[] { "WFAsteroidClusterRock", "WFAsteroidBarrenRockDebrisGigantic" }),
                    "A gas-bearing rock asteroid should become either a cluster or a barren tile asteroid.");
                Assert.That(outcomes["NFAsteroidRockDebrisSmall"], Is.EquivalentTo(new[] { "WFAsteroidClusterRock" }),
                    "A rock asteroid too small for gas should always become a cluster.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BarrenAndScrapAsteroidsHoldNoOre()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();
        var random = server.ResolveDependency<IRobustRandom>();

        await server.WaitAssertion(() =>
        {
            // Each kind of tile asteroid, and the only ore its walls may hold.
            var allowed = new Dictionary<string, string>
            {
                ["WFAsteroidBarrenDebrisMassive"] = "OreAnomalySpawner",
                ["WFAsteroidBarrenRockDebrisMassive"] = "OreAnomalySpawner",
                ["NFAsteroidScrapDebrisMassive"] = "NFOreScrap",
            };

            Assert.Multiple(() =>
            {
                foreach (var (debris, ore) in allowed)
                {
                    Assert.That(protos.Index(debris).TryComp<SimpleFloorPlanPopulatorComponent>(out var populator, factory),
                        Is.True, $"{debris} should populate its floor.");
                    var spawns = new HashSet<string>();
                    var rolled = new List<string?>();
                    foreach (var cache in populator!.Caches.Values)
                    {
                        for (var i = 0; i < 20000; i++)
                        {
                            rolled.Clear();
                            cache.GetSpawns(random, ref rolled);
                            spawns.UnionWith(rolled.OfType<string>());
                        }
                    }

                    foreach (var wall in spawns)
                    {
                        if (protos.Index(wall).TryComp<OreVeinComponent>(out var vein, factory))
                        {
                            Assert.That(vein.OreRarityPrototypeId, Is.Null, $"{wall} on {debris} should not roll random ore.");
                            Assert.That(vein.CurrentOre?.Id ?? ore, Is.EqualTo(ore), $"{wall} on {debris} should hold no ore.");
                        }
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClusterFillsItsBudgetThenRemovesItself()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid marker = default;
        float budget = 0f;
        await server.WaitAssertion(() =>
        {
            var mapUid = server.System<SharedMapSystem>().GetMap(map.MapId);
            marker = em.SpawnEntity("WFAsteroidClusterRock", new EntityCoordinates(mapUid, ShipPos));
            var cluster = em.GetComponent<WFAsteroidClusterComponent>(marker);
            cluster.RareChance = 0f;
            budget = cluster.Mass;
        });
        await server.WaitRunTicks(10); // Rocks spawn a few per tick.

        await server.WaitAssertion(() =>
        {
            var rocks = GridRocks(em);
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(marker), Is.True, "The marker should remove itself once it has spawned its rocks.");
                Assert.That(rocks, Has.Count.GreaterThan(1), "The cluster should spawn several rocks.");
                Assert.That(rocks.All(r => r.CompositionId?.Id == "WFAsteroidRock" && r.Mass > 0f), Is.True,
                    "Every rock should roll from the cluster's composition.");
                Assert.That(rocks.Sum(r => r.Mass), Is.InRange(budget * 0.7f, budget * 1.05f),
                    "The rocks should add up to about the cluster's budget.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClusterRocksStayOwnedByTheirChunk()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        DebrisFeaturePlacerControllerComponent placer = default!;
        await server.WaitAssertion(() =>
        {
            // Stands in for a worldgen chunk, owning the marker as debris the way the placer does when it spawns one.
            var mapUid = server.System<SharedMapSystem>().GetMap(map.MapId);
            var chunk = em.SpawnEntity(null, new EntityCoordinates(mapUid, Vector2.Zero));
            placer = em.AddComponent<DebrisFeaturePlacerControllerComponent>(chunk);
            var marker = em.SpawnEntity("WFAsteroidClusterRock", new EntityCoordinates(mapUid, ShipPos));
            em.GetComponent<WFAsteroidClusterComponent>(marker).RareChance = 0f;
            server.System<DebrisFeaturePlacerSystem>().AdoptDebris(chunk, marker);
            Assert.That(placer.OwnedDebris[ShipPos], Is.EqualTo(marker));
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var grids = new List<EntityUid>();
            var query = em.EntityQueryEnumerator<WFAsteroidGridComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapID == map.MapId)
                    grids.Add(uid);
            }

            var owned = placer.OwnedDebris.Values.OfType<EntityUid>().ToHashSet();
            Assert.That(grids, Has.Count.GreaterThan(1), "The cluster should spawn several rocks.");
            Assert.That(grids.Where(g => !owned.Contains(g)), Is.Empty,
                "Every rock, the one on the marker's own spot included, should belong to the chunk so it unloads with it.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BlockedClusterCentreLeavesNoEmptyChunkSlot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        DebrisFeaturePlacerControllerComponent placer = default!;
        await server.WaitAssertion(() =>
        {
            // A rock already on the marker's spot, such as one from a neighbouring cluster, keeps the first rock off it.
            SpawnAsteroid(pair, map.MapId, "WFAsteroidMedium", ShipPos);
            var mapUid = server.System<SharedMapSystem>().GetMap(map.MapId);
            var chunk = em.SpawnEntity(null, new EntityCoordinates(mapUid, Vector2.Zero));
            placer = em.AddComponent<DebrisFeaturePlacerControllerComponent>(chunk);
            var marker = em.SpawnEntity("WFAsteroidClusterRock", new EntityCoordinates(mapUid, ShipPos));
            server.System<DebrisFeaturePlacerSystem>().AdoptDebris(chunk, marker);
        });
        await server.WaitRunTicks(10); // Rocks spawn a few per tick.

        await server.WaitAssertion(() =>
        {
            Assert.That(placer.OwnedDebris, Has.Count.GreaterThan(0), "The cluster should still spawn rocks around its spot.");
            // A null slot survives the chunk unloading, and a chunk with any slots left never places debris again.
            Assert.That(placer.OwnedDebris.Where(kv => kv.Value == null).Select(kv => kv.Key), Is.Empty,
                "The marker's slot should be dropped when no rock takes it.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RareRollMakesMetallicRocks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mapUid = server.System<SharedMapSystem>().GetMap(map.MapId);
            var marker = em.SpawnEntity("WFAsteroidClusterRock", new EntityCoordinates(mapUid, ShipPos));
            em.GetComponent<WFAsteroidClusterComponent>(marker).RareChance = 1f;
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var rocks = GridRocks(em);
            Assert.Multiple(() =>
            {
                Assert.That(rocks, Is.Not.Empty, "The cluster should spawn rocks.");
                Assert.That(rocks.All(r => r.CompositionId?.Id == "WFAsteroidMetallic"), Is.True,
                    "A certain rare roll should make every rock metallic.");
                Assert.That(rocks.All(r => r.Look == "metallic"), Is.True, "Metallic rocks should use their own sprite.");
                Assert.That(rocks.All(r => r.Resistance >= 0.45f), Is.True, "Metallic rocks should be tough.");
                Assert.That(MetallicNames(em).All(n => n.StartsWith("metallic")), Is.True,
                    "Metallic rocks should say so in their name.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MetallicRocksGrowCommonerFurtherOut()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var far = new Vector2(3000f, 0f);

        await server.WaitAssertion(() =>
        {
            var mapUid = server.System<SharedMapSystem>().GetMap(map.MapId);
            foreach (var position in new[] { Vector2.Zero, far })
            {
                var marker = em.SpawnEntity("WFAsteroidClusterRock", new EntityCoordinates(mapUid, position));
                var cluster = em.GetComponent<WFAsteroidClusterComponent>(marker);
                cluster.RareChance = 0f;
                cluster.RareChancePerKm = 0.4f;
            }
        });
        await server.WaitRunTicks(10); // Rocks spawn a few per tick.

        await server.WaitAssertion(() =>
        {
            var xforms = server.System<SharedTransformSystem>();
            var near = new List<WFAsteroidComponent>();
            var distant = new List<WFAsteroidComponent>();
            var query = em.EntityQueryEnumerator<WFAsteroidComponent, TransformComponent>();
            while (query.MoveNext(out _, out var comp, out var xform))
            {
                (xforms.GetWorldPosition(xform).X > far.X / 2f ? distant : near).Add(comp);
            }

            Assert.Multiple(() =>
            {
                Assert.That(near, Is.Not.Empty, "The cluster at the centre should spawn rocks.");
                Assert.That(distant, Is.Not.Empty, "The far cluster should spawn rocks.");
                Assert.That(near.Any(r => r.CompositionId?.Id == "WFAsteroidMetallic"), Is.False,
                    "No rock at the sector centre should roll metallic at a zero base chance.");
                Assert.That(distant.All(r => r.CompositionId?.Id == "WFAsteroidMetallic"), Is.True,
                    "Three kilometres out at 40% a kilometre, every rock should roll metallic.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BeamRayHitsBoulderInSpace()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid boulder = default;
        await server.WaitAssertion(() =>
        {
            var mapUid = server.System<SharedMapSystem>().GetMap(map.MapId);
            boulder = em.SpawnEntity("WFAsteroidBoulder", new EntityCoordinates(mapUid, ShipPos + new Vector2(0f, 10f)));
            server.System<WFAsteroidSystem>().Roll(boulder, "WFAsteroidRock");
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            // The same ray the laser's pulse casts.
            var ray = new CollisionRay(ShipPos, new Vector2(0f, 1f), (int) CollisionGroup.Opaque);
            var hits = server.System<SharedPhysicsSystem>().IntersectRay(map.MapId, ray, 20f, returnOnFirstHit: true)
                .Select(h => h.HitEntity).ToList();
            Assert.That(hits, Does.Contain(boulder), "The mining laser's ray should hit a boulder rather than pass over it.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BoulderBreaksIntoOreWithoutTouchingItsShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var asteroids = server.System<WFAsteroidSystem>();

        EntityUid ship = default, mined = default, beamed = default;
        await server.WaitAssertion(() =>
        {
            ship = DrydockTest.CreateShip(pair, map.MapId, ShipPos);
            DrydockTest.SetFloor(pair, ship, 5);
            mined = em.SpawnEntity("WFAsteroidBoulder", new EntityCoordinates(ship, 1.5f, 1.5f));
            beamed = em.SpawnEntity("WFAsteroidBoulder", new EntityCoordinates(ship, 3.5f, 3.5f));
            asteroids.Roll(mined, "WFAsteroidRock");
            asteroids.Roll(beamed, "WFAsteroidRock");
            Assert.That(em.GetComponent<TransformComponent>(mined).GridUid, Is.EqualTo(ship), "The boulder should be aboard.");

            server.System<GatherableSystem>().Gather(mined);
            asteroids.QueueCrack(beamed, WFAsteroidCrack.Clean);
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(mined), Is.True, "Gathering should break the boulder.");
                Assert.That(em.Deleted(beamed), Is.True, "Cracking should break the boulder.");
                Assert.That(em.Deleted(ship), Is.False, "Breaking a boulder aboard must never delete the ship's grid.");
                Assert.That(OreItems(em), Is.GreaterThan(0), "The boulders should drop their ore.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BoulderOnAnotherRocksGridLeavesThatRockAlone()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var asteroids = server.System<WFAsteroidSystem>();

        EntityUid rock = default, grid = default, boulder = default;
        await server.WaitAssertion(() =>
        {
            rock = SpawnAsteroid(pair, map.MapId, "WFAsteroidLarge", ShipPos);
            grid = em.GetComponent<TransformComponent>(rock).GridUid!.Value;
            // Where a crack's boulders land when every open spot near it is taken by its fragments.
            boulder = em.SpawnEntity("WFAsteroidBoulder", new EntityCoordinates(grid, 1.5f, 0.5f));
            Assert.That(em.GetComponent<TransformComponent>(boulder).GridUid, Is.EqualTo(grid),
                "The boulder should sit on the asteroid's grid.");

            asteroids.Roll(boulder, "WFAsteroidRock");
            Assert.That(em.GetComponent<WFAsteroidGridVisualsComponent>(grid).Rock?.Id, Is.EqualTo("WFAsteroidRockLarge"),
                "A boulder must not overwrite the look of the asteroid it sits on.");
            asteroids.QueueCrack(boulder, WFAsteroidCrack.Clean);
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(boulder), Is.True, "Cracking should break the boulder.");
                Assert.That(em.Deleted(grid), Is.False, "Breaking a boulder must never delete another rock's grid.");
                Assert.That(em.GetComponent<TransformComponent>(rock).ParentUid, Is.EqualTo(grid),
                    "The asteroid's own rock should stay on its grid.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BoulderKeepsItsOreThroughAShipSave()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var shipyard = server.System<ShipyardSystem>();

        WFAsteroidComponent saved = default!;
        string? data = null;
        await server.WaitAssertion(() =>
        {
            var ship = DrydockTest.CreateShip(pair, map.MapId, ShipPos);
            DrydockTest.SetFloor(pair, ship, 5);
            var boulder = em.SpawnEntity("WFAsteroidBoulder", new EntityCoordinates(ship, 2.5f, 2.5f));
            server.System<WFAsteroidSystem>().Roll(boulder, "WFAsteroidMetallic");
            saved = em.GetComponent<WFAsteroidComponent>(boulder);
            Assert.That(shipyard.TrySaveShip(ship, out data), Is.True, "The ship should save.");
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(shipyard.TryAddSavedShip(data!, out var loaded), Is.True, "The ship should load.");
            WFAsteroidComponent? copy = null;
            var query = em.EntityQueryEnumerator<WFAsteroidComponent, TransformComponent>();
            while (query.MoveNext(out var comp, out var xform))
            {
                if (xform.GridUid == loaded)
                    copy = comp;
            }

            Assert.That(copy, Is.Not.Null, "The boulder should come back with the ship.");
            Assert.Multiple(() =>
            {
                Assert.That(copy!.Mass, Is.EqualTo(saved.Mass).Within(0.001f), "The boulder should keep its mass.");
                Assert.That(copy.CompositionId, Is.EqualTo(saved.CompositionId));
                Assert.That(copy.Ores.Select(o => o.Ore), Is.EqualTo(saved.Ores.Select(o => o.Ore)), "The boulder should keep its ores.");
                Assert.That(copy.Ores.Sum(o => o.Fraction), Is.EqualTo(saved.Ores.Sum(o => o.Fraction)).Within(0.001f));
                Assert.That(copy.Variant, Is.EqualTo(saved.Variant));
                Assert.That(copy.Look, Is.EqualTo(saved.Look).And.Not.Null, "A metallic boulder should stay metallic.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Sets up a ship with a mining laser and a small, fully predictable rock 17 m north of it.</summary>
    private static (EntityUid Gun, EntityUid Rock) SetUpBeam(TestPair pair, MapId map)
    {
        var em = pair.Server.EntMan;
        var ship = DrydockTest.CreateShip(pair, map, ShipPos);
        DrydockTest.SetFloor(pair, ship, 5);
        var gun = DrydockTest.SpawnAnchored(pair, ship, Gun, new Vector2i(2, 2));
        var rock = SpawnAsteroid(pair, map, "WFAsteroidSmall", ShipPos + new Vector2(2f, 17f));
        pair.Server.System<WFAsteroidSystem>().Roll(rock, "WFAsteroidRock");
        var comp = em.GetComponent<WFAsteroidComponent>(rock);
        comp.Resistance = 0f;
        comp.Instability = 0f;
        comp.WindowCenter = 0.55f;
        comp.WindowHalfWidth = 0.12f;
        return (gun, rock);
    }

    /// <summary>Holds the gun's fire on the rock, one pulse a tick, until the rock is gone or time runs out.</summary>
    private static async Task HoldFire(TestPair pair, EntityUid gun, EntityUid rock, float seconds)
    {
        var em = pair.Server.EntMan;
        var beams = pair.Server.System<WFMiningBeamSystem>();
        var ticks = (int) (seconds * pair.Server.ResolveDependency<IGameTiming>().TickRate);
        for (var i = 0; i < ticks && !em.Deleted(rock); i++)
        {
            await pair.Server.WaitPost(() =>
            {
                var beam = em.GetComponent<WFMiningBeamEmitterComponent>(gun);
                if (!em.Deleted(rock) && pair.Server.ResolveDependency<IGameTiming>().CurTime >= beam.CooldownUntil)
                    beams.Pulse(gun, beam, rock, 17f, new Vector2(0f, 1f));
            });
            await pair.Server.WaitRunTicks(1);
        }
    }

    [Test]
    public async Task PulseHeatsTheRockItHitsInsteadOfDamagingIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (gun, rock) = SetUpBeam(pair, map.MapId);
            var pulse = em.SpawnEntity("WFMiningBeamPulse", MapCoordinates.Nullspace);
            var hits = new HashSet<EntityUid> { rock };
            var fired = new HitscanRaycastFiredEvent
            {
                FromCoordinates = em.GetComponent<TransformComponent>(gun).Coordinates,
                ShotDirection = new Vector2(0f, 1f),
                HitEntities = hits,
                Gun = gun,
                DistanceTried = 17f,
            };
            em.EventBus.RaiseLocalEvent(pulse, ref fired);

            var beam = em.GetComponent<WFMiningBeamEmitterComponent>(gun);
            Assert.Multiple(() =>
            {
                Assert.That(hits, Does.Not.Contain(rock), "The rock should leave the hit list, so it isn't damaged.");
                Assert.That(beam.Active, Is.True, "A pulse should light the beam.");
                Assert.That(beam.Target, Is.EqualTo(rock), "The beam should hold on the rock it hit.");
                Assert.That(em.GetComponent<DamageableComponent>(rock).TotalDamage, Is.EqualTo(FixedPoint2.Zero),
                    "The rock should take no damage from the pulse.");
            });

            var boulder = em.SpawnEntity("WFAsteroidBoulder", new MapCoordinates(ShipPos + new Vector2(-3f, 10f), map.MapId));
            var boulderHits = new HashSet<EntityUid> { boulder };
            var boulderFired = fired with { HitEntities = boulderHits };
            em.EventBus.RaiseLocalEvent(pulse, ref boulderFired);
            Assert.Multiple(() =>
            {
                Assert.That(boulderHits, Does.Not.Contain(boulder), "A boulder should be heated like a rock, not gathered.");
                Assert.That(beam.Target, Is.EqualTo(boulder), "The beam should hold on the boulder it hit.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BeamInsideWindowCracksRock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid gun = default, rock = default;
        await server.WaitAssertion(() =>
        {
            (gun, rock) = SetUpBeam(pair, map.MapId);
            var beam = em.GetComponent<WFMiningBeamEmitterComponent>(gun);
            // Settles the heat at the window's centre.
            beam.Power = 0.55f / beam.Strength;
        });

        await HoldFire(pair, gun, rock, 40f);
        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(rock), Is.True, "Heat held in the window should crack the rock.");
                Assert.That(OreItems(em), Is.GreaterThan(0), "A small rock should crack straight into ore.");
                Assert.That(em.GetComponent<WFMiningBeamEmitterComponent>(gun).Active, Is.False,
                    "The beam should go out once the gun stops firing.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HotBandCracksFasterButCostsOre()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid gun = default, rock = default;
        await server.WaitAssertion(() =>
        {
            (gun, rock) = SetUpBeam(pair, map.MapId);
            var beam = em.GetComponent<WFMiningBeamEmitterComponent>(gun);
            // Settles between the window's top (0.67) and the overcharge line; slow enough to crack not to finish yet.
            beam.Power = 0.76f / beam.Strength;
            em.GetComponent<WFAsteroidComponent>(rock).CrackTimeBase = 30f;
        });

        await HoldFire(pair, gun, rock, 4f);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WFAsteroidComponent>(rock);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Overcharging, Is.False, "The hot band is below the overcharge line.");
                Assert.That(comp.HotProgress, Is.GreaterThan(0f), "Cracking in the hot band should count against the yield.");
                Assert.That(WFAsteroidHeat.Yield(comp.Progress, comp.HotProgress), Is.LessThan(0.8f),
                    "A crack made mostly hot should lose a good share of the ore.");
                Assert.That(em.GetComponent<WFMiningBeamEmitterComponent>(gun).Hot, Is.True, "The beam should show it is running hot.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SmallLaserNeverOverchargesPlainRock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid gun = default, rock = default;
        await server.WaitAssertion(() =>
        {
            var ship = DrydockTest.CreateShip(pair, map.MapId, ShipPos);
            DrydockTest.SetFloor(pair, ship, 5);
            gun = DrydockTest.SpawnAnchored(pair, ship, "WFWeaponTurretMiningLaserSmall", new Vector2i(2, 2));
            rock = SpawnAsteroid(pair, map.MapId, "WFAsteroidSmall", ShipPos + new Vector2(2f, 17f));
            server.System<WFAsteroidSystem>().Roll(rock, "WFAsteroidRock");
            var comp = em.GetComponent<WFAsteroidComponent>(rock);
            comp.Resistance = 0f;
            comp.Instability = 0f;
            comp.WindowCenter = 0.55f;
            comp.WindowHalfWidth = 0.09f;
            em.GetComponent<WFMiningBeamEmitterComponent>(gun).Power = 1f;
        });

        await HoldFire(pair, gun, rock, 60f);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(rock), Is.True, "Full power from a light laser should crack a soft rock.");
                Assert.That(em.GetComponent<WFMiningBeamEmitterComponent>(gun).CooldownUntil, Is.EqualTo(TimeSpan.Zero),
                    "A light laser on full power should never overcharge it.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task IrregularRocksFillTheirOutline()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var irregular = 0;
            // Half the variants are irregular, so a dozen rocks all but surely include one.
            for (var i = 0; i < 12; i++)
            {
                var rock = SpawnAsteroid(pair, map.MapId, "WFAsteroidMedium", ShipPos + new Vector2(i * 20f, 0f));
                var comp = em.GetComponent<WFAsteroidComponent>(rock);
                var grid = em.GetComponent<TransformComponent>(rock).GridUid!.Value;
                var tiles = maps.GetAllTiles(grid, em.GetComponent<MapGridComponent>(grid)).Count();
                var fixture = em.GetComponent<Robust.Shared.Physics.FixturesComponent>(rock).Fixtures["fix1"];
                if (!comp.Shapes.ContainsKey(comp.Variant))
                {
                    Assert.That(fixture.Shape, Is.TypeOf<Robust.Shared.Physics.Collision.Shapes.PhysShapeCircle>(),
                        "A round rock keeps its round fixture.");
                    continue;
                }

                irregular++;
                Assert.Multiple(() =>
                {
                    Assert.That(tiles, Is.InRange(8, 28), "An irregular rock should fill less than the 29-tile disc.");
                    Assert.That(fixture.Shape, Is.TypeOf<Robust.Shared.Physics.Collision.Shapes.PolygonShape>(),
                        "An irregular rock's fixture should follow its hull.");
                    Assert.That(comp.MassScale, Is.LessThan(1f), "A smaller silhouette should hold less rock.");
                });
            }

            Assert.That(irregular, Is.GreaterThan(0), "Some rocks should come out irregular.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OverchargeBlowsRockAndOverloadsGun()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var timing = server.ResolveDependency<IGameTiming>();

        EntityUid gun = default, rock = default;
        await server.WaitAssertion(() =>
        {
            (gun, rock) = SetUpBeam(pair, map.MapId);
            // Full power on a rock with no resistance settles above the overcharge line.
            em.GetComponent<WFMiningBeamEmitterComponent>(gun).Power = 1f;
        });

        await HoldFire(pair, gun, rock, 40f);

        await server.WaitAssertion(() =>
        {
            var beam = em.GetComponent<WFMiningBeamEmitterComponent>(gun);
            var attempt = new AttemptShootEvent(gun, null);
            em.EventBus.RaiseLocalEvent(gun, ref attempt);
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(rock), Is.True, "Holding the rock over the line should blow it up.");
                Assert.That(beam.Active, Is.False, "The blast should put the beam out.");
                Assert.That(beam.CooldownUntil, Is.GreaterThan(timing.CurTime), "The gun should be cooling down.");
                Assert.That(attempt.Cancelled, Is.True, "The gun shouldn't fire while it cools down.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GunnerySetsThePowerOfTheShipsLasers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var ship = DrydockTest.CreateShip(pair, map.MapId, ShipPos);
            DrydockTest.SetFloor(pair, ship, 6);
            var first = DrydockTest.SpawnAnchored(pair, ship, Gun, new Vector2i(1, 1));
            var second = DrydockTest.SpawnAnchored(pair, ship, Gun, new Vector2i(4, 1));
            var console = DrydockTest.SpawnAnchored(pair, ship, "ComputerGunneryConsole", new Vector2i(2, 4));
            em.RemoveComponent<AccessReaderComponent>(console);
            var power = em.GetComponent<ApcPowerReceiverComponent>(console);
            power.Powered = true;
            var actor = em.SpawnEntity("MobHuman", new EntityCoordinates(ship, 2.5f, 3.5f));
            em.System<SharedUserInterfaceSystem>().OpenUi(console, FireControlConsoleUiKey.Key, actor);

            Send(0.9f);
            Assert.Multiple(() =>
            {
                Assert.That(em.GetComponent<WFMiningBeamEmitterComponent>(first).Power, Is.EqualTo(0.9f), "The gunner should set the laser's power.");
                Assert.That(em.GetComponent<WFMiningBeamEmitterComponent>(second).Power, Is.EqualTo(0.9f), "Every laser on the ship takes it.");
            });

            power.Powered = false;
            Send(0.2f);
            Assert.That(em.GetComponent<WFMiningBeamEmitterComponent>(first).Power, Is.EqualTo(0.9f), "A dead console can't set the power.");

            void Send(float value)
            {
                var message = new WFMiningBeamPowerMessage(value)
                {
                    Actor = actor,
                    Entity = em.GetNetEntity(console),
                    UiKey = FireControlConsoleUiKey.Key,
                };
                em.EventBus.RaiseLocalEvent(console, (object) message, true);
            }
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MiningPanelShowsOnlyWithLasersAboard()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var gunnery = new FireControlWindow();
            var panel = WFCockpitLease.Descendants(gunnery).Single(control => control.Name == "WfGunneryMining");
            Assert.That(panel.Visible, Is.False, "A ship with no mining laser has no mining panel.");

            var rock = new WFMiningBeamTargetEntry("medium asteroid", 34f,
                new[] { new WFMiningBeamOreEntry("iron ore", 0.4f, Color.Brown) }, 0.2f, 0.4f, 0.6f, 0.5f, 0.3f, 0f, 0.9f);
            gunnery.WfUpdateMining(new WFMiningBeamGunneryState(1, 0.7f, 0f, rock));
            Assert.That(panel.Visible, Is.True);

            var size = gunnery.MinSize;
            gunnery.Measure(size);
            gunnery.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
            Assert.That(panel.Width, Is.EqualTo(FireControlWindow.WfShieldPanelWidth).Within(1f));
            foreach (var child in WFCockpitLease.Descendants(panel).Where(control => control != panel && control.VisibleInTree))
            {
                Assert.That(child.GlobalPosition.X + child.Width, Is.LessThanOrEqualTo(panel.GlobalPosition.X + panel.Width + 1),
                    $"{child.GetType().Name} sticks out of the mining panel.");
            }

            gunnery.WfUpdateMining(new WFMiningBeamGunneryState(0, 0f, 0f, null));
            Assert.That(panel.Visible, Is.False, "Losing the lasers takes the panel away again.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LaserRayHitsEveryRockShape()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        var rocks = new List<(EntityUid Rock, Vector2 Centre, bool Irregular)>();
        await server.WaitAssertion(() =>
        {
            for (var i = 0; i < 16; i++)
            {
                var size = i % 2 == 0 ? "WFAsteroidMedium" : "WFAsteroidLarge";
                var centre = ShipPos + new Vector2(i * 30f, 0f);
                var rock = SpawnAsteroid(pair, map.MapId, size, centre);
                var comp = em.GetComponent<WFAsteroidComponent>(rock);
                rocks.Add((rock, centre, comp.Shapes.ContainsKey(comp.Variant)));
            }
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var physics = server.System<SharedPhysicsSystem>();
            Assert.That(rocks.Any(r => r.Irregular), Is.True, "Some rocks should come out irregular.");
            Assert.Multiple(() =>
            {
            foreach (var (rock, centre, irregular) in rocks)
            {
                // The same ray the laser's pulse casts, straight down onto the rock's centre from 20 m above it.
                var ray = new CollisionRay(centre + new Vector2(0f, 20f), new Vector2(0f, -1f), (int) CollisionGroup.Opaque);
                var hits = physics.IntersectRay(map.MapId, ray, 25f, returnOnFirstHit: false).Select(h => h.HitEntity).ToList();
                Assert.That(hits, Does.Contain(rock), $"The laser's ray should hit a {(irregular ? "irregular" : "round")} rock.");
            }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShipClosingOnAnAsteroidIsWarned()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        EntityUid ship = default, grid = default;
        await server.WaitAssertion(() =>
        {
            cfg.SetCVar(CollisionWarningCVars.Hysteresis, 0f);
            ship = DrydockTest.CreateShip(pair, map.MapId, ShipPos);
            DrydockTest.SetFloor(pair, ship, 10);
            em.EnsureComponent<ShuttleComponent>(ship);
            em.SpawnEntity("ComputerShuttle", new EntityCoordinates(ship, new Vector2(0.5f, 0.5f)));
            var rock = SpawnAsteroid(pair, map.MapId, "WFAsteroidMedium", ShipPos + new Vector2(150f, 5f));
            grid = em.GetComponent<TransformComponent>(rock).GridUid!.Value;
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var physics = server.System<SharedPhysicsSystem>();
            var tiles = server.System<SharedMapSystem>().GetAllTiles(grid, em.GetComponent<MapGridComponent>(grid)).Count();
            Assert.That(em.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(grid).FixturesMass, Is.GreaterThan(tiles * 2f),
                "An asteroid should be far denser than a hull of the same footprint.");

            physics.SetBodyType(ship, BodyType.Dynamic);
            // Fast enough for a crash into a ship of that size to hurt, but no faster than old tile asteroids warned at.
            physics.SetLinearVelocity(ship, new Vector2(40f, 0f));
            server.System<CollisionWarningSystem>().Sweep();
            Assert.That(em.TryGetComponent<CollisionWarningComponent>(ship, out var warning), Is.True,
                "A ship closing on an asteroid at speed should be warned.");
            Assert.That(warning!.Threat, Is.EqualTo(grid), "The warning should name the asteroid.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LightLaserStripsAMetallicBoulder()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid gun = default, boulder = default;
        await server.WaitAssertion(() =>
        {
            var ship = DrydockTest.CreateShip(pair, map.MapId, ShipPos);
            DrydockTest.SetFloor(pair, ship, 5);
            gun = DrydockTest.SpawnAnchored(pair, ship, "WFWeaponTurretMiningLaserSmall", new Vector2i(2, 2));
            var mapUid = server.System<SharedMapSystem>().GetMap(map.MapId);
            boulder = em.SpawnEntity("WFAsteroidBoulder", new EntityCoordinates(mapUid, ShipPos + new Vector2(2f, 15f)));
            // The toughest rock there is, at the default power.
            server.System<WFAsteroidSystem>().Roll(boulder, "WFAsteroidMetallic");
            Assert.That(em.GetComponent<WFAsteroidComponent>(boulder).Resistance, Is.EqualTo(0f),
                "A boulder should take none of its rock's resistance.");
        });

        await HoldFire(pair, gun, boulder, 3f);

        await server.WaitAssertion(() =>
            Assert.That(em.Deleted(boulder), Is.True, "An M12 at default power should strip a boulder within a few seconds."));

        await pair.CleanReturnAsync();
    }
}
