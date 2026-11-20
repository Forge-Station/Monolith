#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Birds;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.Birds;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Players;
using Robust.Shared.Audio;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Birds;

/// <summary>Birds take off when someone comes near and land again, fly over walls, and the ash hawk dives at a lone player.</summary>
[TestFixture]
public sealed class BirdTest
{
    private const string AudioRoot = "/Audio/_WF/Birds/";

    private static readonly string[] Birds =
    {
        "WFMobBirdVerdaSongbird", "WFMobBirdAsclepiuGull", "WFMobBirdGlassSwift", "WFMobBirdSnowGrouse",
        "WFMobBirdAshHawk", "WFMobBirdCarrion", "WFMobBirdFleshKite",
    };

    /// <summary>A breathable plating floor from -12 to 12, optionally with a wall line at x = 3.</summary>
    private static async Task<TestMapData> Rig(TestPair pair, bool wall = false)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        await server.WaitPost(() =>
        {
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -12; x <= 12; x++)
            for (var y = -12; y <= 12; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            if (wall)
            {
                for (var y = -12; y <= 12; y++)
                    server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, 3.5f, y + 0.5f));
            }
        });
        return map;
    }

    /// <summary>Spawns a bird and removes the rest of its flock, so the test watches one bird.</summary>
    private static async Task<EntityUid> SpawnLoneBird(TestPair pair, TestMapData map, string proto, Vector2 position)
    {
        var entMan = pair.Server.EntMan;
        EntityUid bird = default;
        await pair.Server.WaitPost(() =>
        {
            bird = entMan.SpawnEntity(proto, new EntityCoordinates(map.Grid.Owner, position));
            var query = entMan.EntityQueryEnumerator<WFBirdComponent>();
            var members = new List<EntityUid>();
            while (query.MoveNext(out var uid, out var comp))
            {
                if (comp.Leader == bird)
                    members.Add(uid);
            }
            foreach (var member in members)
                entMan.DeleteEntity(member);
        });
        return bird;
    }

    private static async Task<bool> RunUntil(TestPair pair, Func<bool> condition, float seconds)
    {
        for (var elapsed = 0f; elapsed < seconds; elapsed += 0.25f)
        {
            var done = false;
            await pair.Server.WaitPost(() => done = condition());
            if (done)
                return true;
            await pair.RunSeconds(0.25f);
        }

        return false;
    }

    private static int Mask(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<FixturesComponent>(uid).Fixtures.Values.First().CollisionMask;
    }

    [Test]
    public async Task TakesOffWhenSomeoneRunsNearAndLandsAgain()
    {
        // A player, since a perched bird nobody is near only checks in now and then.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Rig(pair);
        var bird = await SpawnLoneBird(pair, map, "WFMobBirdVerdaSongbird", new Vector2(0.5f, 0.5f));
        var birds = server.System<WFBirdSystem>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var mindSys = entMan.System<SharedMindSystem>();
        EntityUid human = default;
        int groundMask = 0;

        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            human = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 4.5f, 0.5f));
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, human);
            groundMask = Mask(entMan, bird);
        });
        await pair.RunSeconds(1f);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFBirdComponent>(bird);
            var xform = entMan.GetComponent<TransformComponent>(bird);
            Assert.That(comp.State, Is.EqualTo(WFBirdState.Perched), "A person standing 4 m off should not startle the bird.");
            Assert.That(birds.FindStartler(bird, comp, xform), Is.Null);

            // The same person running at that distance does.
            server.System<SharedPhysicsSystem>().SetLinearVelocity(human, new Vector2(-4.5f, 0f));
            Assert.That(birds.FindStartler(bird, comp, xform), Is.Not.Null, "A runner 4 m off should startle the bird.");
            server.System<SharedPhysicsSystem>().SetLinearVelocity(human, Vector2.Zero);

            // Walking right up to it puts it up.
            server.System<SharedTransformSystem>().SetCoordinates(human, new EntityCoordinates(map.Grid.Owner, 2f, 0.5f));
        });

        Assert.That(await RunUntil(pair, () => entMan.GetComponent<WFBirdComponent>(bird).State != WFBirdState.Perched, 2f),
            Is.True, "The bird did not take off when someone came near.");
        await server.WaitAssertion(() =>
        {
            Assert.That(Mask(entMan, bird), Is.EqualTo(0), "An airborne bird should collide with nothing.");
            Assert.That(server.System<SharedAppearanceSystem>().TryGetData<bool>(bird, WFBirdVisuals.Flying, out var flying) && flying, Is.True);
            entMan.DeleteEntity(human);
        });

        // Its flight away and its circling are rolled, so give it a few rounds to find a clear spot.
        Assert.That(await RunUntil(pair, () => entMan.GetComponent<WFBirdComponent>(bird).State == WFBirdState.Perched, 120f),
            Is.True, "The bird never landed again.");
        await server.WaitAssertion(() =>
        {
            var coords = entMan.GetComponent<TransformComponent>(bird).Coordinates;
            Assert.That(birds.IsClear(coords), Is.True, "The bird landed somewhere it can't stand.");
            Assert.That(Mask(entMan, bird), Is.EqualTo(groundMask), "Landing should restore the bird's collisions.");
            Assert.That((coords.Position - new Vector2(0.5f, 0.5f)).Length(), Is.GreaterThan(1f), "The bird should have flown somewhere.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A shot aimed where a flying bird is drawn hits it, hitscan and lasers included.</summary>
    [Test]
    public async Task HitscanHitsAFlyingBird()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Rig(pair, wall: false);
        var bird = await SpawnLoneBird(pair, map, "WFMobBirdAsclepiuGull", new Vector2(4.5f, 0.5f));
        await pair.RunSeconds(0.5f);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFBirdComponent>(bird);
            comp.CircleTime = Vector2.Zero;
            server.System<WFBirdSystem>().TakeOff(bird, comp, new Vector2(4.5f, 30.5f));
            Assert.That(comp.State, Is.Not.EqualTo(WFBirdState.Perched));

            var physics = server.System<SharedPhysicsSystem>();
            var at = server.System<SharedTransformSystem>().GetMapCoordinates(bird);
            // Level with the drawn bird, half a tile above its shadow, as the hitscan raycast does.
            var from = at.Position + new Vector2(-4f, 0.5f);
            var ray = new CollisionRay(from, Vector2.UnitX, (int) Content.Shared.Physics.CollisionGroup.Opaque);
            var hits = physics.IntersectRay(at.MapId, ray, 8f, returnOnFirstHit: false).Select(hit => hit.HitEntity);
            Assert.That(hits, Does.Contain(bird), "A hitscan shot at the drawn bird missed it.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FlyingBirdPassesOverAWall()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Rig(pair, wall: true);
        var bird = await SpawnLoneBird(pair, map, "WFMobBirdAsclepiuGull", new Vector2(0.5f, 0.5f));
        await pair.RunSeconds(0.5f);

        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<WFBirdComponent>(bird);
            // No circling, so it lands where it was sent.
            comp.CircleTime = Vector2.Zero;
            server.System<WFBirdSystem>().TakeOff(bird, comp, new Vector2(7.5f, 0.5f));
        });

        Assert.That(await RunUntil(pair, () => entMan.GetComponent<TransformComponent>(bird).LocalPosition.X > 5f, 10f),
            Is.True, "The bird did not get past the wall.");
        Assert.That(await RunUntil(pair, () => entMan.GetComponent<WFBirdComponent>(bird).State == WFBirdState.Perched, 10f),
            Is.True, "The bird never landed beyond the wall.");
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(bird).LocalPosition.X, Is.GreaterThan(4f),
                "The bird should land on the far side of the wall.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AshHawkDivesAtALonePlayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Rig(pair);
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var mindSys = entMan.System<SharedMindSystem>();
        EntityUid human = default;
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            human = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 6.5f, 0.5f));
            var mind = mindSys.CreateMind(session.UserId).Owner;
            mindSys.TransferTo(mind, human);
        });
        await pair.RunTicksSync(5);
        Assert.That(session.AttachedEntity, Is.EqualTo(human), "The player did not attach to the body.");

        var hawk = await SpawnLoneBird(pair, map, "WFMobBirdAshHawk", new Vector2(-1.5f, 0.5f));
        var before = FixedPoint2.Zero;
        await server.WaitPost(() => before = entMan.GetComponent<DamageableComponent>(human).TotalDamage);
        var dived = false;
        var hit = await RunUntil(pair, () =>
        {
            dived |= entMan.GetComponent<WFBirdComponent>(hawk).State == WFBirdState.Diving;
            return dived && entMan.GetComponent<DamageableComponent>(human).TotalDamage > before;
        }, 20f);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFBirdComponent>(hawk);
            var hostile = entMan.GetComponent<WFBirdHostileComponent>(hawk);
            var at = entMan.GetComponent<TransformComponent>(hawk).LocalPosition;
            var preyAt = entMan.GetComponent<TransformComponent>(human).LocalPosition;
            Assert.That(dived, Is.True,
                $"The hawk never dived: state {comp.State}, dead {comp.Dead}, target {hostile.Target}, hit {hit}, hawk at {at}, prey at {preyAt}, " +
                $"prey actor {entMan.HasComponent<ActorComponent>(human)}.");
            Assert.That(hit, Is.True, "The hawk never hurt the lone player.");
            Assert.That(entMan.GetComponent<WFBirdHostileComponent>(hawk).Target, Is.EqualTo(human));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryBirdSpawnsWithItsSpritesAndSounds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await Rig(pair);

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var protos = server.ResolveDependency<IPrototypeManager>();
            var resources = server.ResolveDependency<IResourceManager>();
            foreach (var id in Birds)
            {
                var uid = entMan.SpawnEntity(id, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
                Assert.That(entMan.HasComponent<MobStateComponent>(uid), Is.True, $"{id} is not a mob.");
                Assert.That(entMan.HasComponent<WFBirdComponent>(uid), Is.True, $"{id} is not a bird.");
                Assert.That(protos.Index<EntityPrototype>(id).Components.ContainsKey("HTN"), Is.False, $"{id} would be steered by an NPC too.");
            }

            foreach (var rsi in new[] { "songbird", "lake_gull", "glass_swift", "snow_grouse", "ash_hawk", "carrion_bird", "flesh_kite" })
            foreach (var state in new[] { "fly", "sit", "dead", "shadow" })
            {
                var path = new ResPath($"/Textures/_WF/Birds/{rsi}.rsi/{state}.png");
                Assert.That(resources.ContentFileExists(path), Is.True, $"{path} is missing.");
            }

            // Every imported call is used, and the bird import stays small.
            var used = new HashSet<string>();
            foreach (var collection in protos.EnumeratePrototypes<SoundCollectionPrototype>().Where(c => c.ID.StartsWith("WFBird")))
            {
                Assert.That(collection.PickFiles, Is.Not.Empty, $"{collection.ID} is empty.");
                foreach (var file in collection.PickFiles)
                {
                    Assert.That(resources.ContentFileExists(file), Is.True, $"{collection.ID} names missing {file}.");
                    used.Add(file.ToString());
                }
            }

            var imported = resources.ContentFindFiles(new ResPath(AudioRoot)).Where(p => p.Extension == "ogg").Select(p => p.ToString()).ToList();
            Assert.That(imported, Is.Not.Empty);
            Assert.That(imported.Count, Is.LessThanOrEqualTo(20), "Keep the bird import curated.");
            Assert.That(imported.Where(p => !used.Contains(p)), Is.Empty, "An imported bird sound is used by no collection.");
        });
        await pair.CleanReturnAsync();
    }
}
