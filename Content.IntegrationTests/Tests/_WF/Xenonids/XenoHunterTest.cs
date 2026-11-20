#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Empower;
using Content.Shared._WF.Xenonids.Hide;
using Content.Shared._WF.Xenonids.Invisibility;
using Content.Shared._WF.Xenonids.Leap;
using Content.Shared.Actions;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>The runner, lurker, warrior and ravager spawn with their actions and their abilities start.</summary>
[TestFixture]
public sealed class XenoHunterTest
{
    private static readonly string[] Castes =
    [
        "CMXenoRunner", "RMCXenoRunnerAcider", "CMXenoLurker", "RMCXenoLurkerVampire", "CMXenoWarrior",
        "CMXenoRavager", "RMCXenoRavagerBerserker", "RMCXenoRavagerHedgehog",
    ];

    /// <summary>Performs the user's action with this prototype ID; false when it has none.</summary>
    private static bool Perform(IEntityManager entities, EntityUid user, string actionId, Action<BaseActionEvent>? setup = null)
    {
        var actions = entities.System<SharedActionsSystem>();
        foreach (var (id, comp) in actions.GetActions(user))
        {
            if (entities.GetComponent<MetaDataComponent>(id).EntityPrototype?.ID != actionId)
                continue;

            if (comp.BaseEvent is not { } ev)
                return false;

            setup?.Invoke(ev);
            actions.PerformAction(user, null, id, comp, ev, IoCManager.Resolve<IGameTiming>().CurTime);
            return true;
        }

        return false;
    }

    /// <summary>Every hunter caste spawns as a xeno and is granted each action it lists.</summary>
    [Test]
    public async Task HunterCastesSpawnWithActions()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var actions = entities.System<SharedActionsSystem>();
        var map = await pair.CreateTestMap();

        var xenos = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            foreach (var id in Castes)
            {
                xenos.Add(entities.SpawnEntity(id, map.GridCoords));
            }
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            for (var i = 0; i < Castes.Length; i++)
            {
                var xeno = xenos[i];
                Assert.That(entities.TryGetComponent(xeno, out XenoComponent? comp), Castes[i]);

                var granted = new HashSet<string>();
                foreach (var (id, _) in actions.GetActions(xeno))
                {
                    granted.Add(entities.GetComponent<MetaDataComponent>(id).EntityPrototype?.ID ?? string.Empty);
                }

                foreach (var action in comp!.ActionIds)
                {
                    Assert.That(granted, Does.Contain(action.Id), $"{Castes[i]} should have {action}");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The lurker's turn invisible action cloaks it and a second use uncloaks it.</summary>
    [Test]
    public async Task LurkerInvisibilityToggles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid lurker = default;
        await server.WaitPost(() => lurker = entities.SpawnEntity("CMXenoLurker", map.GridCoords));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<XenoActiveInvisibleComponent>(lurker), Is.False);
            Assert.That(Perform(entities, lurker, "ActionXenoTurnInvisible"), "The lurker should have the action.");
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<XenoActiveInvisibleComponent>(lurker), "The lurker should be invisible.");
            Assert.That(Perform(entities, lurker, "ActionXenoTurnInvisible"));
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<XenoActiveInvisibleComponent>(lurker), Is.False, "A second use should uncloak.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The runner's leap sets it airborne towards the target and its hide action toggles.</summary>
    [Test]
    public async Task RunnerLeapsAndHides()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid runner = default;
        await server.WaitPost(() => runner = entities.SpawnEntity("CMXenoRunner", map.GridCoords));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(Perform(entities, runner, "ActionXenoHide"));
            Assert.That(entities.GetComponent<XenoHideComponent>(runner).Hiding, "The runner should be hiding.");
            Assert.That(Perform(entities, runner, "ActionXenoHide"));
            Assert.That(entities.GetComponent<XenoHideComponent>(runner).Hiding, Is.False);

            var target = map.GridCoords.Offset(new Vector2(3, 0));
            Assert.That(Perform(entities, runner, "ActionXenoLeap", ev => ((WorldTargetActionEvent) ev).Target = target),
                "The runner should have the leap action.");
        });

        // The leap starts after its do-after and ends within a fraction of a second, so watch for it tick by tick.
        var leaped = false;
        for (var i = 0; i < 30 && !leaped; i++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => leaped = entities.HasComponent<XenoLeapingComponent>(runner));
        }

        Assert.That(leaped, "The runner should have been leaping.");

        await pair.CleanReturnAsync();
    }

    /// <summary>The ravager's empower action spends plasma and starts the empower.</summary>
    [Test]
    public async Task RavagerEmpowerStarts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid ravager = default;
        await server.WaitPost(() => ravager = entities.SpawnEntity("CMXenoRavager", map.GridCoords));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<XenoEmpowerComponent>(ravager).ActivatedOnce, Is.False);
            Assert.That(Perform(entities, ravager, "ActionXenoEmpower"), "The ravager should have the action.");
            Assert.That(entities.GetComponent<XenoEmpowerComponent>(ravager).ActivatedOnce, "The empower should have started.");
        });

        await pair.CleanReturnAsync();
    }
}
