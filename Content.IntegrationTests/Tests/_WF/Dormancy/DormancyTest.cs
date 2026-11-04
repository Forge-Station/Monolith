#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._WF.Dormancy;
using Content.Server._WF.NpcVehicleCrew;
using Content.Server.Chemistry.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.LivingWorld;
using Content.Shared._WF.NpcVehicleCrew;
using Content.Shared._WF.Xenonids.Weeds;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Dormancy;

/// <summary>Planet ground nobody is near goes quiet, and wakes when a player comes.</summary>
[TestFixture]
[TestOf(typeof(WFDormancySystem))]
public sealed class DormancyTest
{
    /// <summary>Planet entities are active only near a player; off-planet ones always are.</summary>
    [Test]
    public async Task OnlyGroundNearAPlayerIsActive()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var dormancy = server.System<WFDormancySystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var offPlanet = await pair.CreateTestMap();
        var near = EntityUid.Invalid;
        var far = EntityUid.Invalid;
        var below = EntityUid.Invalid;
        var station = EntityUid.Invalid;

        try
        {
            await LayTiles(pair, ground, new Vector2i(-4, -4), new Vector2i(12, 4));
            await server.WaitPost(() =>
            {
                near = entMan.SpawnEntity(null, new EntityCoordinates(ground, new Vector2(10f, 0.5f)));
                far = entMan.SpawnEntity(null, new EntityCoordinates(ground, new Vector2(400f, 0.5f)));
                station = entMan.SpawnEntity(null, offPlanet.GridCoords);
                if (layers.Count > 1 && entMan.GetComponent<CEZMapComponent>(ground).MapAbove is { } above)
                    below = entMan.SpawnEntity(null, new EntityCoordinates(above, new Vector2(10f, 0.5f)));

                Assert.Multiple(() =>
                {
                    Assert.That(dormancy.IsActive(near), Is.False, "Ground with nobody on the planet is active.");
                    Assert.That(dormancy.IsActive(station), Is.True, "An entity off-planet went dormant.");
                    Assert.That(dormancy.IsMapActive(ground), Is.False);
                });
            });

            await AttachViewer(pair, ground, new Vector2(0.5f, 0.5f));

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(dormancy.IsActive(near), Is.True, "Ground beside a player is dormant.");
                    Assert.That(dormancy.IsActive(far), Is.False, "Ground far from the only player is active.");
                    Assert.That(dormancy.IsActive(station), Is.True);
                    Assert.That(dormancy.IsMapActive(ground), Is.True);
                    if (below.IsValid())
                        Assert.That(dormancy.IsActive(below), Is.True, "The layer over a player's spot is dormant.");
                });
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A bramble ignores a mob beside it while no player is near, and lashes it once one comes.</summary>
    [Test]
    public async Task FloraReactsOnlyNearAPlayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var plant = EntityUid.Invalid;
        var mob = EntityUid.Invalid;

        try
        {
            await LayTiles(pair, ground, new Vector2i(-8, -8), new Vector2i(8, 8));
            await server.WaitPost(() =>
            {
                plant = entMan.SpawnEntity("WFVerdaPlantBramble", new EntityCoordinates(ground, new Vector2(0.5f, 0.5f)));
                mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ground, new Vector2(1.5f, 0.5f)));
            });

            await server.WaitRunTicks(pair.SecondsToTicks(3f));
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<WFReactiveFloraComponent>(plant).State, Is.EqualTo(WFReactiveFloraState.Idle),
                    "The bramble reacted with no player near.");
                Assert.That(Slash(entMan, mob), Is.Zero, "The bramble lashed a mob with no player near.");
            });

            await AttachViewer(pair, ground, new Vector2(0.5f, 6.5f));
            await server.WaitRunTicks(pair.SecondsToTicks(3f));
            await server.WaitAssertion(() =>
            {
                Assert.That(Slash(entMan, mob), Is.GreaterThan(0f), "The bramble never lashed the mob beside it once a player was near.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A crewed vehicle on a planet stays parked while nobody is near, and drives off once a player comes.</summary>
    [Test]
    public async Task VehicleCrewDrivesOnlyNearAPlayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        var crews = server.System<WFVehicleCrewSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var vehicle = EntityUid.Invalid;
        var start = Vector2.Zero;

        try
        {
            await LayTiles(pair, ground, new Vector2i(-8, -6), new Vector2i(48, 10));
            await server.WaitPost(() =>
            {
                Assert.That(crews.TrySpawn("WFVehicleCrewCivilianTruck", new EntityCoordinates(ground, 2.5f, 0.5f), out vehicle,
                    WFVehicleBehaviour.Hold), Is.True);
                crews.SetHome(vehicle, xforms.ToMapCoordinates(new EntityCoordinates(ground, 40.5f, 0.5f)));
            });

            await pair.RunSeconds(6f);
            await server.WaitAssertion(() =>
            {
                start = xforms.GetWorldPosition(vehicle);
                Assert.That(Vector2.Distance(start, new Vector2(2.5f, 0.5f)), Is.LessThan(1f), "The vehicle drove off with nobody near.");
            });

            await AttachViewer(pair, ground, new Vector2(2.5f, 8.5f));
            await pair.RunSeconds(10f);
            await server.WaitAssertion(() =>
            {
                Assert.That(Vector2.Distance(xforms.GetWorldPosition(vehicle), start), Is.GreaterThan(3f),
                    "The vehicle stayed parked with a player beside it.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Slash damage only: the air or the cold may hurt the mob too.</summary>
    private static float Slash(IEntityManager entMan, EntityUid mob)
    {
        return entMan.GetComponent<DamageableComponent>(mob).Damage.DamageDict.GetValueOrDefault("Slash").Float();
    }

    /// <summary>
    /// A resting hull's ground contact is sampled now and then instead of every tick, and a moving one's every tick.
    /// </summary>
    [Test]
    public async Task RestingHullSamplesGroundContactSlowly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var levels = server.System<CEZLevelsSystem>();
        var physics = server.System<SharedPhysicsSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];

        try
        {
            await LayTiles(pair, ground, new Vector2i(-8, -8), new Vector2i(24, 24));
            var hull = await BuildDebris(pair, entMan.GetComponent<MapComponent>(ground).MapId);
            await MapInitHull(pair, hull);
            await server.WaitRunTicks(5);

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<CEZGridFallerComponent>(hull), Is.True, "The hull isn't a z-level faller.");
            });

            // Resting: pin it static, clear its contact away from its sampling tick, and it stays clear for a while.
            var cleared = false;
            for (var attempt = 0; attempt < 20 && !cleared; attempt++)
            {
                await server.WaitPost(() =>
                {
                    var tick = timing.CurTick.Value + (uint) hull.Id;
                    var soon = false;
                    for (var k = 0u; k <= 4; k++)
                    {
                        soon |= (tick + k) % 60 == 0;
                    }

                    if (soon)
                        return;

                    physics.SetBodyType(hull, BodyType.Static);
                    levels.SetGroundContact(hull, false);
                    cleared = true;
                });

                if (!cleared)
                    await server.WaitRunTicks(5);
            }

            Assert.That(cleared, Is.True);
            await server.WaitRunTicks(2);
            await server.WaitAssertion(() =>
                Assert.That(entMan.GetComponent<CEZPhysicsComponent>(hull).GroundContact, Is.False, "A resting hull was sampled every tick."));

            await server.WaitRunTicks(62);
            await server.WaitAssertion(() =>
                Assert.That(entMan.GetComponent<CEZPhysicsComponent>(hull).GroundContact, Is.True, "A resting hull was never sampled."));

            // Moving: sampled the next tick.
            await server.WaitPost(() =>
            {
                physics.SetBodyType(hull, BodyType.Dynamic);
                physics.SetLinearVelocity(hull, new Vector2(1f, 0f));
                levels.SetGroundContact(hull, false);
            });
            await server.WaitRunTicks(2);
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<PhysicsComponent>(hull).Awake, Is.True);
                Assert.That(entMan.GetComponent<CEZPhysicsComponent>(hull).GroundContact, Is.True, "A moving hull's contact lagged.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Planet water holds an endless pool and nothing ticks to refill it; wild weeds carry no spreading timer.</summary>
    [Test]
    public async Task SceneryCarriesNoIdleTimers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var solutions = server.System<SharedSolutionContainerSystem>();
        var map = await pair.CreateTestMap();
        var spawned = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            foreach (var id in new[] { "FloorWaterEntity", "MonoFloorWaterEntity", "WFBloodRiver", "WFBloodOcean" })
            {
                var water = entMan.SpawnEntity(id, map.GridCoords);
                spawned.Add(water);
                Assert.That(entMan.HasComponent<SolutionRegenerationComponent>(water), Is.False, $"{id} still regenerates.");
                Assert.That(solutions.TryGetSolution(water, "pool", out _, out var pool), Is.True, $"{id} has no pool.");
                Assert.That(pool!.Volume.Float(), Is.GreaterThan(9_000_000f), $"{id}'s pool isn't endless.");
            }

            var wild = entMan.SpawnEntity("WFXenoWeedsWild", map.GridCoords);
            var hive = entMan.SpawnEntity("XenoWeeds", map.GridCoords);
            spawned.Add(wild);
            spawned.Add(hive);
            Assert.That(entMan.HasComponent<XenoWeedsSpreadingComponent>(wild), Is.False, "Wild weeds carry a spreading timer.");
            Assert.That(entMan.HasComponent<XenoWeedsSpreadingComponent>(hive), Is.True, "Hive weeds lost their spreading.");

            foreach (var uid in spawned)
            {
                entMan.DeleteEntity(uid);
            }
        });

        await pair.CleanReturnAsync();
    }
}
