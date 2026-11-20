#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.Parallax;
using Content.Server.Shuttles.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCities;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.Gravity;
using Content.Shared.Mobs.Components;
using Content.Shared.Zombies;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>Carcinoma's crashed ships: off the roads at a random angle, torn open, grounded, overgrown and inhabited.</summary>
[TestFixture]
public sealed class PlanetWreckTest
{
    [Test]
    public async Task WreckedCrewRiseDressedForTheirWorld()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid zombie = default;
        await server.WaitPost(() => zombie = entities.SpawnEntity("WFWreckZombieThrascias", map.GridCoords));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<ZombieComponent>(zombie), Is.True, "The crewmember never rose.");
            Assert.That(entities.HasComponent<Content.Server.NPC.HTN.HTNComponent>(zombie), Is.True, "A risen crewmember needs a mind of its own.");
            var factions = entities.GetComponent<Content.Shared.NPC.Components.NpcFactionMemberComponent>(zombie).Factions;
            Assert.That(factions, Does.Contain(new Robust.Shared.Prototypes.ProtoId<Content.Shared.NPC.Prototypes.NpcFactionPrototype>("SimpleHostile")),
                "A risen crewmember should side with the wildlife.");
            Assert.That(factions, Does.Not.Contain(new Robust.Shared.Prototypes.ProtoId<Content.Shared.NPC.Prototypes.NpcFactionPrototype>("Zombie")),
                "A risen crewmember would brawl with the wildlife as a plain zombie.");
            Assert.That(server.System<Content.Shared.Inventory.InventorySystem>().TryGetSlotEntity(zombie, "outerClothing", out _), Is.True,
                "The crewmember should still wear its winter gear.");
            entities.DeleteEntity(zombie);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Wrecked crew only bite: no infection, no rising dead, and a zombie of any other source is a friend.</summary>
    [Test]
    public async Task WreckedCrewAreInert()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var mobStates = server.System<Content.Shared.Mobs.Systems.MobStateSystem>();
        var zombies = server.System<Content.Server.Zombies.ZombieSystem>();
        var factions = server.System<Content.Shared.NPC.Systems.NpcFactionSystem>();
        EntityUid wreck = default, plain = default, victim = default, kill = default, control = default, controlKill = default, controlVictim = default;

        await server.WaitPost(() =>
        {
            wreck = entities.SpawnEntity("WFWreckZombieThrascias", map.GridCoords);
            plain = entities.SpawnEntity("MobHuman", map.GridCoords);
            victim = entities.SpawnEntity("MobHuman", map.GridCoords);
            kill = entities.SpawnEntity("MobHuman", map.GridCoords);
            control = entities.SpawnEntity("MobHuman", map.GridCoords);
            controlKill = entities.SpawnEntity("MobHuman", map.GridCoords);
            controlVictim = entities.SpawnEntity("MobHuman", map.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            zombies.ZombifyEntity(plain);
            zombies.ZombifyEntity(control);
            mobStates.ChangeMobState(kill, Content.Shared.Mobs.MobState.Critical);
            mobStates.ChangeMobState(controlKill, Content.Shared.Mobs.MobState.Critical);

            void Bite(EntityUid user, EntityUid target)
            {
                var hit = new Content.Shared.Weapons.Melee.Events.MeleeHitEvent(
                    new List<EntityUid> { target }, user, user, new Content.Shared.Damage.DamageSpecifier(), null);
                entities.EventBus.RaiseLocalEvent(user, hit);
            }

            for (var i = 0; i < 100; i++)
            {
                Bite(wreck, victim);
                Bite(control, controlVictim);
            }

            Bite(wreck, kill);
            Bite(control, controlKill);

            Assert.That(entities.HasComponent<PendingZombieComponent>(victim), Is.False, "A wrecked crewmember infected someone.");
            Assert.That(entities.HasComponent<PendingZombieComponent>(controlVictim), Is.True,
                "An ordinary zombie no longer infects, so this test proves nothing.");
            Assert.That(entities.HasComponent<ZombieComponent>(kill), Is.False, "A body a wrecked crewmember downed rose.");
            Assert.That(entities.HasComponent<ZombieComponent>(controlKill), Is.True,
                "An ordinary zombie no longer raises what it downs, so this test proves nothing.");
            Assert.That(factions.IsEntityFriendly(wreck, plain), Is.True,
                "A wrecked crewmember would go for a zombie of any other source.");
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(1977)]
    [TestCase(8231)]
    public async Task CarcinomaHasOvergrownWrecks(int seed)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, ServerSeed = seed });
        var server = pair.Server;
        var entities = server.EntMan;
        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        var holds = new List<Task>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        const int radius = 192;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, radius);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WFPoisCarcinoma");
            var surface = prototypes.Index<WFPlanetSurfacePrototype>("WFSurfaceCarcinoma");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "WFSurfaceCarcinoma", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var preload = new WFPlanetPreloadStartingEvent(layers[0], Vector2.Zero, radius, holds);
            entities.EventBus.RaiseEvent(EventSource.Local, ref preload);
        });
        try
        {
            for (var attempt = 0; attempt < 900 && holds.Any(hold => !hold.IsCompleted); attempt++)
                await pair.RunTicksSync(10);
            await pair.RunTicksSync(5);
            await server.WaitAssertion(() =>
            {
                var ground = layers[0];
                var record = entities.GetComponent<WFPlanetPoiComponent>(ground);
                Assert.That(record.GenerationError, Is.Null);
                var wreckSites = record.Sites.Where(site => site.Name == "wf-poi-wreck").ToList();
                Assert.That(wreckSites, Is.Not.Empty, "Carcinoma got no wrecks.");

                // Pinned ground round a wreck must have been laid first, or it stays a black ring.
                var groundGrid = entities.GetComponent<MapGridComponent>(ground);
                var biomes = server.System<BiomeSystem>();
                var groundBiome = (ground, entities.GetComponent<BiomeComponent>(ground));
                var holes = 0;
                foreach (var site in wreckSites)
                {
                    for (var x = site.Bounds.Left; x < site.Bounds.Right; x++)
                    for (var y = site.Bounds.Bottom; y < site.Bounds.Top; y++)
                    {
                        var index = new Vector2i(x, y);
                        if (biomes.WfIsPinned(groundBiome, index)
                            && server.System<SharedMapSystem>().GetTileRef(ground, groundGrid, index).Tile.IsEmpty)
                            holes++;
                    }
                }
                Assert.That(holes, Is.Zero, "The ground under and round a wreck was pinned empty.");

                var map = entities.GetComponent<TransformComponent>(ground).MapID;
                var maps = server.System<SharedMapSystem>();
                var flesh = server.ResolveDependency<ITileDefinitionManager>()["WFFloorFlesh"].TileId;
                var wrecks = new List<EntityUid>();
                var query = entities.EntityQueryEnumerator<WFPlanetCityGridComponent, MapGridComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var grid, out var xform))
                {
                    if (xform.MapID != map || entities.HasComponent<WFPlanetCampComponent>(uid))
                        continue;
                    wrecks.Add(uid);
                    Assert.Multiple(() =>
                    {
                        Assert.That(entities.HasComponent<ShuttleComponent>(uid), Is.False, "A wreck must not fly.");
                        Assert.That(entities.GetComponent<PhysicsComponent>(uid).BodyType, Is.EqualTo(BodyType.Static));
                        Assert.That(entities.GetComponent<GravityComponent>(uid).Enabled, Is.True, "A wreck needs gravity.");
                        Assert.That(maps.GetAllTiles(uid, grid).Any(tile => tile.Tile.TypeId == flesh), Is.True, "The flesh never crept in.");
                    });
                }
                Assert.That(wrecks, Has.Count.EqualTo(wreckSites.Count), "Every wreck site should have its hull.");

                var fauna = wrecks.Sum(wreck => entities.EntityQuery<MobStateComponent>()
                    .Count(mob => entities.GetComponent<TransformComponent>(mob.Owner).GridUid == wreck));
                Assert.That(fauna, Is.GreaterThanOrEqualTo(wrecks.Count), "The wrecks should have wildlife in them.");
                // They wander, so they're counted across the world rather than per hull.
                var crew = entities.EntityQuery<ZombieComponent>().Count(zombie => entities.GetComponent<TransformComponent>(zombie.Owner).MapID == map);
                Assert.That(crew, Is.GreaterThanOrEqualTo(wrecks.Count), "The wrecks should have their risen crew.");
            });
        }
        finally { await PlanetFixture.Teardown(pair, layers); }
        await pair.CleanReturnAsync();
    }
}
