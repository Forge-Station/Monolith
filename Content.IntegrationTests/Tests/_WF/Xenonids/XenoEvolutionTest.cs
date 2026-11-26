#nullable enable
using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Evolution;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared._WF.Xenonids.JoinXeno;
using Content.Shared._WF.Xenonids.Pheromones;
using Content.Shared._WF.Xenonids.Plasma;
using Content.Shared.Actions;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>The xeno growth lane: a larva evolves into a drone that keeps its mind and hive, and the drone emits pheromones.</summary>
[TestFixture]
public sealed class XenoEvolutionTest
{
    /// <summary>A larva with enough points on its hive's weeds evolves into a drone; the old larva is deleted.</summary>
    [Test]
    public async Task LarvaEvolvesIntoDroneAndEmitsPheromones()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var evolution = entities.System<XenoEvolutionSystem>();
        var pheromones = entities.System<SharedXenoPheromonesSystem>();
        var minds = entities.System<SharedMindSystem>();
        var map = await pair.CreateTestMap();

        // The drone unlocks one minute into the round, which the dummy ticker counts from time zero.
        await pair.RunSeconds(65);

        EntityUid hive = default;
        EntityUid larva = default;
        EntityUid mind = default;
        await server.WaitPost(() =>
        {
            hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);

            // Larvae may only evolve on weeds early in the round.
            var weeds = entities.SpawnEntity("XenoWeeds", map.GridCoords);
            hives.SetHive(weeds, hive);

            larva = entities.SpawnEntity("CMXenoLarva", map.GridCoords);
            hives.SetHive(larva, hive);
            mind = minds.CreateMind(null);
            minds.TransferTo(mind, larva);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var evo = entities.GetComponent<XenoEvolutionComponent>(larva);
            Assert.That(evo.EvolvesTo, Does.Contain((Robust.Shared.Prototypes.EntProtoId) "CMXenoDrone"), "A larva can evolve into a drone.");

            evolution.SetPoints((larva, evo), evo.Max);
            Assert.That(evolution.TryEvolve((larva, evo), "CMXenoDrone"), "The evolve should start.");
        });

        await pair.RunSeconds(6);

        EntityUid drone = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(larva), "The larva is gone after evolving.");

            var query = entities.EntityQueryEnumerator<XenoComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out _, out var meta))
            {
                if (meta.EntityPrototype?.ID == "CMXenoDrone")
                    drone = uid;
            }

            Assert.That(drone, Is.Not.EqualTo(default(EntityUid)), "A drone exists.");
            Assert.That(entities.HasComponent<XenoComponent>(drone));
            Assert.That(entities.GetComponent<HiveMemberComponent>(drone).Hive, Is.EqualTo(hive), "The drone stays in the hive.");
            Assert.That(minds.TryGetMind(drone, out var droneMind, out _) && droneMind == mind, "The mind moves to the drone.");
        });

        await server.WaitAssertion(() =>
        {
            var phero = entities.GetComponent<XenoPheromonesComponent>(drone);
            var plasma = entities.GetComponent<XenoPlasmaComponent>(drone);
            var before = plasma.Plasma;

            Assert.That(pheromones.TryActivatePheromones((drone, phero), XenoPheromones.Recovery), "Emitting pheromones should start.");
            Assert.That(entities.GetComponent<XenoActivePheromonesComponent>(drone).Pheromones, Is.EqualTo(XenoPheromones.Recovery));
            Assert.That(plasma.Plasma, Is.LessThanOrEqualTo(before - FixedPoint2.New(phero.PheromonesPlasmaCost)), "Starting pheromones costs plasma.");

            pheromones.DeactivatePheromones((drone, phero));
            Assert.That(entities.HasComponent<XenoActivePheromonesComponent>(drone), Is.False, "Pheromones stop when deactivated.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A ghost carries the join-as-xeno action and can take a burrowed larva of a hive that has a living xeno.</summary>
    [Test]
    public async Task GhostJoinsHiveAsBurrowedLarva()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var actions = entities.System<SharedActionsSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.Sessions[0];

        EntityUid hive = default;
        EntityUid ghost = default;
        await server.WaitPost(() =>
        {
            hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            var host = entities.SpawnEntity("CMXenoLarva", map.GridCoords);
            hives.SetHive(host, hive);

            ghost = entities.SpawnEntity("MobObserver", map.GridCoords);
            hives.ChangeBurrowedLarva((hive, entities.GetComponent<HiveComponent>(hive)), 1);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<JoinXenoComponent>(ghost), "Ghosts can join a hive.");
            var found = false;
            foreach (var (_, action) in actions.GetActions(ghost))
            {
                if (action.BaseEvent is JoinXenoActionEvent)
                    found = true;
            }

            Assert.That(found, "The ghost has the join-as-xeno action.");

            var hiveComp = entities.GetComponent<HiveComponent>(hive);
            Assert.That(hiveComp.BurrowedLarva, Is.EqualTo(1));
            Assert.That(hives.JoinBurrowedLarva((hive, hiveComp), session), "The ghost takes the burrowed larva.");
            Assert.That(hiveComp.BurrowedLarva, Is.EqualTo(0));

            var larva = session.AttachedEntity;
            Assert.That(larva, Is.Not.Null);
            Assert.That(entities.HasComponent<XenoComponent>(larva!.Value), "The player now plays a xeno.");
            Assert.That(entities.GetComponent<HiveMemberComponent>(larva.Value).Hive, Is.EqualTo(hive));
        });

        await pair.CleanReturnAsync();
    }
}
