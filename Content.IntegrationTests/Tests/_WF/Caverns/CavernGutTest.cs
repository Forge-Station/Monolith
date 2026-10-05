#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Caverns;
using Content.Server._WF.Planets;
using Content.Server.NPC.HTN;
using Content.Shared._WF.Caverns;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>The Gut's digestive acid and choked throats, on a bare airless grid.</summary>
[TestFixture]
public sealed class CavernGutTest
{
    private const string Acid = "WFCavernDigestiveAcid";
    private const string Tendons = "WFCavernTendons";
    private const string Floor = "WFCavernFloorGut";

    /// <summary>What spawns the Gut's own creatures: its wildlife markers and its assimilation sacks.</summary>
    private static readonly string[] NativeSpawners = { "WFCavernFaunaCarcinoma", "WFCarcinomaAssimilationSack" };

    /// <summary>Seconds the mobs stand still before they are checked.</summary>
    private const float StandSeconds = 3f;

    /// <summary>The Caustic a human must take in that time; the acid deals 5 a second.</summary>
    private const int MinCaustic = 10;

    /// <summary>The loop digestive acid hisses on whoever it burns.</summary>
    private const string Hiss = "/Audio/Nyanotrasen/Ambience/Objects/deepfryer_sizzling.ogg";

    /// <summary>
    /// With no air to burn in, a human standing in acid takes Caustic damage, while a human on a catwalk over the acid
    /// and every creature the Gut's wildlife markers and assimilation sacks can spawn, each standing in its own pool,
    /// take none; only the burned human hisses; the stomach floor under the pools is still there with nothing spilled on it.
    /// </summary>
    [Test]
    [TestOf(typeof(WFDigestiveAcidSystem))]
    public async Task AcidDigestsIntrudersAndSparesNatives()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();
        var map = await pair.CreateTestMap(true, Floor);
        var maps = server.System<SharedMapSystem>();
        var physics = server.System<SharedPhysicsSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var natives = new SortedSet<string>();

        await server.WaitAssertion(() =>
        {
            foreach (var spawnerId in NativeSpawners)
            {
                Assert.That(protoMan.Index<EntityPrototype>(spawnerId).TryGetComponent<WFPlanetFaunaSpawnerComponent>(out var spawner, factory), Is.True,
                    $"{spawnerId} spawns no wildlife.");
                CollectSpawns(protoMan, protoMan.Index(spawner!.Table).Table, natives);
            }
        });

        var pools = new List<Vector2i> { Vector2i.Zero, new(2, 0) };
        var human = EntityUid.Invalid;
        var covered = EntityUid.Invalid;
        var catwalk = EntityUid.Invalid;
        var spared = new Dictionary<string, EntityUid>();

        await server.WaitPost(() =>
        {
            LayFloor(maps, tileDefs, map.Grid, 4 + 2 * natives.Count);
            catwalk = entMan.SpawnEntity("Catwalk", new EntityCoordinates(map.Grid, 2.5f, 0.5f));
            human = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            covered = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 2.5f, 0.5f));

            foreach (var native in natives)
            {
                var tile = new Vector2i(4 + 2 * spared.Count, 0);
                pools.Add(tile);
                var mob = entMan.SpawnEntity(native, new EntityCoordinates(map.Grid, tile.X + 0.5f, 0.5f));
                // Asleep, so it stands in its pool instead of wandering out.
                entMan.RemoveComponent<HTNComponent>(mob);
                spared[native] = mob;
            }

            foreach (var pool in pools)
            {
                entMan.SpawnEntity(Acid, new EntityCoordinates(map.Grid, pool.X + 0.5f, 0.5f));
            }
        });

        await pair.RunSeconds(StandSeconds);

        await server.WaitAssertion(() =>
        {
            var burned = Caustic(entMan, human);
            TestContext.Out.WriteLine($"Human {burned} Caustic after {StandSeconds} s in acid; {natives.Count} natives: {string.Join(", ", natives)}.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(natives, Has.Count.GreaterThanOrEqualTo(10), "The Gut's spawners name too few creatures; the table walk missed some.");
                Assert.That(burned, Is.GreaterThanOrEqualTo(MinCaustic), $"A human in acid for {StandSeconds} s took only {burned} Caustic.");
                AssertHissing(entMan, human);
                Assert.That(entMan.GetComponent<TransformComponent>(catwalk).Anchored, Is.True, "Precondition: the catwalk is not anchored over the acid.");

                foreach (var (name, mob) in spared.Append(new KeyValuePair<string, EntityUid>("a human on a catwalk", covered)))
                {
                    Assert.That(physics.GetContactingEntities(mob).Any(contact => entMan.HasComponent<WFDigestiveAcidComponent>(contact)), Is.True,
                        $"Precondition: {name} is not standing in its pool.");
                    Assert.That(entMan.HasComponent<DamagedByContactComponent>(mob), Is.False, $"The acid latched onto {name}.");
                    Assert.That(Caustic(entMan, mob), Is.Zero, $"{name} took {Caustic(entMan, mob)} Caustic in the Gut's acid.");
                    Assert.That(entMan.HasComponent<WFDigestiveAcidHissComponent>(mob), Is.False, $"{name} hisses in the Gut's acid.");
                }

                foreach (var index in pools)
                {
                    Assert.That(maps.TryGetTileRef(map.Grid, map.Grid.Comp, index, out var tile) && !tile.Tile.IsEmpty, Is.True,
                        $"The floor under the acid at {index} is gone.");
                    Assert.That(tileDefs[tile.Tile.TypeId].ID, Is.EqualTo(Floor), $"The floor under the acid at {index} changed.");
                }

                var puddles = entMan.EntityQueryEnumerator<PuddleComponent>();
                Assert.That(puddles.MoveNext(out _), Is.False, "The acid spilled a puddle.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A human burning in acid hisses; once it walks out onto bare floor the hiss stops and its stream is gone.</summary>
    [Test]
    [TestOf(typeof(WFDigestiveAcidHissSystem))]
    public async Task HissStopsOutOfTheAcid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap(true, Floor);
        var maps = server.System<SharedMapSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var human = EntityUid.Invalid;
        EntityUid? stream = null;

        await server.WaitPost(() =>
        {
            LayFloor(maps, tileDefs, map.Grid, 3);
            entMan.SpawnEntity(Acid, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            human = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
        });

        await pair.RunSeconds(1f);

        await server.WaitAssertion(() =>
        {
            AssertHissing(entMan, human);
            stream = entMan.GetComponent<WFDigestiveAcidHissComponent>(human).Stream;
        });

        await server.WaitPost(() =>
        {
            xforms.SetCoordinates(human, new EntityCoordinates(map.Grid, 3.5f, 0.5f));
            // A teleport leaves the body asleep, and a sleeping body keeps its stale contacts; walking out wakes it.
            server.System<SharedPhysicsSystem>().WakeBody(human);
        });
        await pair.RunSeconds(1f);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<DamagedByContactComponent>(human), Is.False, "Precondition: the acid still burns the human out of it.");
                Assert.That(entMan.HasComponent<WFDigestiveAcidHissComponent>(human), Is.False, "The human still hisses out of the acid.");
                Assert.That(entMan.EntityExists(stream!.Value), Is.False, "The hiss stream outlived the burning.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A human standing in the tendons walks slower than one on bare floor beside them.</summary>
    [Test]
    public async Task TendonsSlowWalkers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap(true, Floor);
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var slowed = EntityUid.Invalid;
        var free = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            LayFloor(maps, tileDefs, map.Grid, 3);
            entMan.SpawnEntity(Tendons, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            slowed = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            free = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 3.5f, 0.5f));
        });

        await pair.RunSeconds(1f);

        await server.WaitAssertion(() =>
        {
            var inTendons = entMan.GetComponent<MovementSpeedModifierComponent>(slowed).CurrentWalkSpeed;
            var onFloor = entMan.GetComponent<MovementSpeedModifierComponent>(free).CurrentWalkSpeed;
            TestContext.Out.WriteLine($"Walk speed {inTendons} in tendons, {onFloor} on bare floor.");

            Assert.That(inTendons, Is.LessThan(onFloor * 0.8f), $"Tendons leave the walk speed at {inTendons} against {onFloor} on bare floor.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The sampler counts acid as a barrier like lava, and tendons and glow flora as open ground.</summary>
    [Test]
    [TestOf(typeof(WFCavernSampler))]
    public async Task SamplerClassifiesGutFeatures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var sampler = server.System<WFCavernSampler>();

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sampler.IsHazard(Acid), Is.True, "Digestive acid is not a walkability barrier.");
                Assert.That(sampler.IsSolid(Acid), Is.False, "Digestive acid counts as rock.");
                Assert.That(sampler.IsHazard(Tendons), Is.False, "Tendons count as a barrier; they only slow you.");
                Assert.That(sampler.IsSolid(Tendons), Is.False, "Tendons count as rock.");
                Assert.That(sampler.IsLight("WFCavernNerveCluster"), Is.True, "Nerve clusters give no light.");
                Assert.That(sampler.IsLight(Tendons), Is.False, "Tendons count as a light.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every entity a table can spawn, through nested tables and groups.</summary>
    private static void CollectSpawns(IPrototypeManager protoMan, EntityTableSelector selector, ISet<string> ids)
    {
        switch (selector)
        {
            case EntSelector ent:
                ids.Add(ent.Id);
                break;
            case NestedSelector nested:
                CollectSpawns(protoMan, protoMan.Index(nested.TableId).Table, ids);
                break;
            case GroupSelector group:
                foreach (var child in group.Children)
                {
                    CollectSpawns(protoMan, child, ids);
                }

                break;
            case AllSelector all:
                foreach (var child in all.Children)
                {
                    CollectSpawns(protoMan, child, ids);
                }

                break;
            default:
                Assert.Fail($"The Gut's wildlife tables use a {selector.GetType().Name}, which this test can't list.");
                break;
        }
    }

    /// <summary>Lays the stomach floor from the test tile east to <paramref name="toX"/>, unbroken so the grid doesn't split.</summary>
    private static void LayFloor(SharedMapSystem maps, ITileDefinitionManager tileDefs, Entity<MapGridComponent> grid, int toX)
    {
        for (var x = 1; x <= toX; x++)
        {
            maps.SetTile(grid, new Vector2i(x, 0), new Tile(tileDefs[Floor].TileId));
        }
    }

    /// <summary>The mob carries a live, looping acid hiss attached to it.</summary>
    private static void AssertHissing(IEntityManager entMan, EntityUid mob)
    {
        Assert.That(entMan.TryGetComponent<WFDigestiveAcidHissComponent>(mob, out var hiss), Is.True, "The burning mob does not hiss.");
        Assert.That(hiss!.Stream is { } stream && entMan.EntityExists(stream), Is.True, "The hiss has no stream.");

        var audio = entMan.GetComponent<AudioComponent>(hiss.Stream!.Value);
        Assert.That(audio.FileName, Is.EqualTo(Hiss), "The hiss plays the wrong sound.");
        Assert.That(audio.Params.Loop, Is.True, "The hiss does not loop.");
        Assert.That(entMan.GetComponent<TransformComponent>(hiss.Stream.Value).ParentUid, Is.EqualTo(mob), "The hiss is not attached to the mob.");
    }

    private static int Caustic(IEntityManager entMan, EntityUid mob)
    {
        return entMan.GetComponent<DamageableComponent>(mob).Damage.DamageDict.GetValueOrDefault("Caustic", FixedPoint2.Zero).Int();
    }
}
