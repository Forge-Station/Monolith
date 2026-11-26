using System.Linq;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Players;
using Content.Server._CorvaxNext.Silicons.Borgs;
using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared._Forge.Silicons.StationAi;
using Content.Shared.Mind.Components;
using Content.Shared.Silicons.StationAi;
using Content.Shared.CCVar;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Forge.StationAi;

[TestFixture]
public sealed class StationAiGhostRoleTests
{
    [Test]
    public async Task RemoteBorgControlKeepsCoreReserved()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true,
            DummyTicker = false,
            Connected = true,
        });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
        var session = players.Sessions.Single();
        var mindId = session.ContentData()!.Mind!.Value;
        var minds = server.System<SharedMindSystem>();
        var remote = server.System<AiRemoteControlSystem>();
        var roles = server.System<GhostRoleSystem>();
        EntityUid core = default, brain = default, borg = default;
        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            for (var x = -2; x <= 4; x++)
            for (var y = -2; y <= 2; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp,
                    new EntityCoordinates(map.Grid.Owner, x, y), map.Tile.Tile);
            core = entities.SpawnEntity("PlayerStationAiForerunner", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            Assert.That(server.System<SharedStationAiSystem>().TryGetHeld(
                (core, entities.GetComponent<StationAiCoreComponent>(core)), out brain), Is.True);
            borg = entities.SpawnEntity("PlayerBorgForerunnerAiRemote", new EntityCoordinates(map.Grid.Owner, 2.5f, 0.5f));
            entities.EnsureComponent<AiRemoteControllerComponent>(borg);
            minds.TransferTo(mindId, brain, true);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<AiRemoteControllerComponent>(borg), Is.True, "Remote borg must have its controller");
            Assert.That(entities.HasComponent<StationAiHeldComponent>(brain), Is.True);
            Assert.That(entities.GetComponent<TransformComponent>(borg).GridUid,
                Is.EqualTo(entities.GetComponent<TransformComponent>(brain).GridUid));
            remote.AiTakeControl(brain, borg);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(session.AttachedEntity, Is.EqualTo(borg));
            Assert.That(entities.GetComponent<MindComponent>(mindId).OwnedEntity, Is.EqualTo(brain));
            Assert.That(entities.GetComponent<GhostRoleComponent>(brain).Taken, Is.True);
            Assert.That(roles.GhostRoles.Select(role => role.Owner), Does.Not.Contain(brain));
            Assert.That(entities.GetComponent<StationAiScreenComponent>(core).Occupied, Is.True);
            remote.ReturnMindIntoAi(borg);
            Assert.That(session.AttachedEntity, Is.EqualTo(brain));
            Assert.That(entities.GetComponent<AiRemoteControllerComponent>(borg).LinkedMind, Is.Null);
        });
        await pair.RunTicksSync(5);
        await server.WaitPost(() => remote.AiTakeControl(brain, borg));
        await pair.RunTicksSync(5);
        pair.Client.ResolveDependency<IConsoleHost>().ExecuteCommand("ghost");
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<GhostComponent>(session.AttachedEntity), Is.True);
            Assert.That(entities.GetComponent<GhostRoleComponent>(brain).Taken, Is.False);
            Assert.That(roles.GhostRoles.Select(role => role.Owner), Does.Contain(brain));
            Assert.That(entities.GetComponent<AiRemoteControllerComponent>(borg).LinkedMind, Is.Null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GhostCommandReopensTakenCoreRole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true,
            DummyTicker = false,
            Connected = true,
        });

        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var playerManager = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
        var console = client.ResolveDependency<IConsoleHost>();
        var mindSystem = entityManager.System<SharedMindSystem>();
        var ghostRoleSystem = entityManager.System<GhostRoleSystem>();
        var session = playerManager.Sessions.Single();
        var originalMind = session.ContentData()!.Mind!.Value;
        server.CfgMan.SetCVar(CCVars.GhostQuickLottery, true);

        EntityUid originalMob = default;
        await server.WaitPost(() =>
        {
            originalMob = entityManager.SpawnEntity(null, map.GridCoords);
            mindSystem.TransferTo(originalMind, originalMob, true);
        });
        await pair.RunTicksSync(10);

        console.ExecuteCommand("ghost");
        await pair.RunTicksSync(10);
        Assert.That(entityManager.HasComponent<GhostComponent>(session.AttachedEntity), Is.True);

        EntityUid brain = default;
        await server.WaitPost(() =>
        {
            brain = entityManager.SpawnEntity("StationAiBrain", map.GridCoords);
            var role = entityManager.GetComponent<GhostRoleComponent>(brain);
            ghostRoleSystem.Request(session, role.Identifier);
        });
        await pair.RunTicksSync(60);

        var brainRole = entityManager.GetComponent<GhostRoleComponent>(brain);
        Assert.Multiple(() =>
        {
            Assert.That(session.AttachedEntity, Is.EqualTo(brain));
            Assert.That(brainRole.Taken, Is.True);
            Assert.That(ghostRoleSystem.GhostRoles.Select(role => role.Owner), Does.Not.Contain(brain));
        });

        console.ExecuteCommand("ghost");
        await pair.RunTicksSync(10);

        Assert.Multiple(() =>
        {
            Assert.That(entityManager.HasComponent<GhostComponent>(session.AttachedEntity), Is.True);
            Assert.That(brainRole.Taken, Is.False);
            Assert.That(ghostRoleSystem.GhostRoles.Select(role => role.Owner), Does.Contain(brain));
        });

        await server.WaitPost(() =>
        {
            ghostRoleSystem.Request(session, brainRole.Identifier);
            Assert.DoesNotThrow(() => ghostRoleSystem.GetGhostRolesInfo(session));
            ghostRoleSystem.LeaveRaffle(session, brainRole.Identifier);
        });
        await pair.RunTicksSync(2);
        server.CfgMan.SetCVar(CCVars.GhostQuickLottery, false);

        await pair.CleanReturnAsync();
    }
}
