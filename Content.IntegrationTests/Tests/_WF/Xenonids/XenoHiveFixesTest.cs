#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.Server.Chat.Systems;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Mind;
using Content.Server.Mind.Commands;
using Content.Shared._EinsteinEngines.Language.Components;
using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Eye;
using Content.Shared._WF.Xenonids.Weeds;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared._WF.Xenonids.JoinXeno;
using Content.Shared._WF.Xenonids.Parasite;
using Content.Shared.Actions;
using Content.Shared.Chat;
using Content.Shared.Inventory;
using Content.Shared.Speech.Muting;
using Robust.Client.UserInterface;
using Robust.Client.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>Playtest fixes: hive chat for ghosts, one xeno language, no spawn mute, monkey hosts, larva offers and the join action.</summary>
[TestFixture]
public sealed class XenoHiveFixesTest
{
    private const string Basic = "TauCetiBasic";

    /// <summary>Every caste, the lesser drone included, speaks and understands the crew's language and starts unmuted.</summary>
    [Test]
    public async Task XenosShareOneLanguageAndAreNotMuted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        var ids = new[] { "CMXenoLesserDrone", "CMXenoDrone", "CMXenoLarva", "CMXenoQueen", "CMXenoParasite", "RMCXenoKing" };
        var spawned = new List<(string Id, EntityUid Uid)>();
        await server.WaitPost(() =>
        {
            foreach (var id in ids)
            {
                spawned.Add((id, entities.SpawnEntity(id, map.GridCoords)));
            }
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            foreach (var (id, uid) in spawned)
            {
                MakeSentientCommand.MakeSentient(uid, entities, true, true);
                var speaker = entities.GetComponent<LanguageSpeakerComponent>(uid);
                Assert.That(speaker.SpokenLanguages.Select(l => l.Id), Is.EquivalentTo(new[] { Basic }), $"{id} speaks the crew's language only.");
                Assert.That(speaker.UnderstoodLanguages.Select(l => l.Id), Does.Contain(Basic), $"{id} understands the crew's language.");
                Assert.That(speaker.CurrentLanguage, Is.EqualTo(Basic), $"{id} speaks in it by default.");
                Assert.That(entities.HasComponent<MutedComponent>(uid), Is.False, $"{id} must not spawn muted.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A parasite infects a monkey the way it infects a human.</summary>
    [Test]
    public async Task ParasiteInfectsMonkey()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var parasites = entities.System<SharedXenoParasiteSystem>();
        var map = await pair.CreateTestMap();

        EntityUid host = default;
        EntityUid parasite = default;
        await server.WaitPost(() =>
        {
            host = entities.SpawnEntity("MobMonkey", map.GridCoords);
            parasite = entities.SpawnEntity("CMXenoParasite", map.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<InfectableComponent>(host), "A monkey is infectable.");
            Assert.That(parasites.Infect((parasite, entities.GetComponent<XenoParasiteComponent>(parasite)), host, false, true),
                "The parasite should attach to the monkey.");
        });

        await pair.RunSeconds(17);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<VictimInfectedComponent>(host), "The monkey should be infected.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Ghosts get the join action as soon as a hive exists, and lose it with the last hive.</summary>
    [Test]
    public async Task JoinActionFollowsHives()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var actions = entities.System<SharedActionsSystem>();
        var map = await pair.CreateTestMap();

        // Earlier tests on a pooled server leave their hives behind.
        await server.WaitPost(() =>
        {
            var existing = new List<EntityUid>();
            var hives = entities.EntityQueryEnumerator<HiveComponent>();
            while (hives.MoveNext(out var hiveUid, out _))
            {
                existing.Add(hiveUid);
            }

            foreach (var hiveUid in existing)
            {
                entities.DeleteEntity(hiveUid);
            }
        });

        EntityUid ghost = default;
        await server.WaitPost(() => ghost = entities.SpawnEntity("MobObserver", map.GridCoords));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(HasJoinAction(), Is.False, "No hive, no join action.");
        });

        EntityUid hive = default;
        await server.WaitPost(() => hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(HasJoinAction(), "A hive with no burrowed larva still lets ghosts queue.");
        });

        var lateGhost = default(EntityUid);
        await server.WaitPost(() => lateGhost = entities.SpawnEntity("MobObserver", map.GridCoords));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(HasJoinAction(lateGhost), "A ghost that appears later gets it too.");
        });

        await server.WaitPost(() => entities.DeleteEntity(hive));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(HasJoinAction(), Is.False, "The action goes with the last hive.");
        });

        await pair.CleanReturnAsync();

        bool HasJoinAction(EntityUid? who = null)
        {
            foreach (var (_, action) in actions.GetActions(who ?? ghost))
            {
                if (action.BaseEvent is JoinXenoActionEvent)
                    return true;
            }

            return false;
        }
    }

    /// <summary>A ghost hears a xeno's hive message; a bystander human does not.</summary>
    [Test]
    public async Task GhostsHearHiveChat()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.ResolveDependency<IEntityManager>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var minds = entities.System<MindSystem>();
        var chat = entities.System<ChatSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.Sessions[0];

        EntityUid xeno = default;
        await server.WaitPost(() =>
        {
            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            xeno = entities.SpawnEntity("CMXenoDrone", map.GridCoords);
            hives.SetHive(xeno, hive);

            var ghost = entities.SpawnEntity("MobObserver", map.GridCoords);
            var mind = minds.GetOrCreateMind(session.UserId);
            minds.TransferTo(mind, ghost);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(session.AttachedEntity, Is.Not.Null);
            Assert.That(entities.HasComponent<Content.Shared.Ghost.GhostComponent>(session.AttachedEntity!.Value), "The player is a ghost.");
            chat.TrySendInGameICMessage(xeno, ";the hive hears", InGameICChatType.Speak, false, ignoreActionBlocker: true);
        });
        await pair.RunTicksSync(10);

        var controller = client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var heard = controller.History.Any(h => h.Msg.Message.Contains("The hive hears") || h.Msg.WrappedMessage.Contains("The hive hears"));
        Assert.That(heard, "The ghost should receive the hive message.");

        await pair.CleanReturnAsync();
    }

    /// <summary>A burst larva nobody was offered becomes a ghost role; one a queued player was offered does not.</summary>
    [Test]
    public async Task UnclaimedBurstLarvaIsAGhostRole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var parasites = entities.System<SharedXenoParasiteSystem>();
        var containers = entities.System<SharedContainerSystem>();
        var map = await pair.CreateTestMap();

        EntityUid host = default;
        EntityUid parasite = default;
        await server.WaitPost(() =>
        {
            host = entities.SpawnEntity("MobHuman", map.GridCoords);
            parasite = entities.SpawnEntity("CMXenoParasite", map.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(parasites.Infect((parasite, entities.GetComponent<XenoParasiteComponent>(parasite)), host, false, true));
        });
        await pair.RunSeconds(17);

        await server.WaitAssertion(() =>
        {
            parasites.SetBurstDelay((host, entities.GetComponent<VictimInfectedComponent>(host)), TimeSpan.FromSeconds(1));
        });
        await pair.RunSeconds(3);

        EntityUid larva = default;
        await server.WaitAssertion(() =>
        {
            var infected = entities.GetComponent<VictimInfectedComponent>(host);
            Assert.That(infected.SpawnedLarva, Is.Not.Null, "The larva should have formed.");
            larva = infected.SpawnedLarva!.Value;
            Assert.That(entities.HasComponent<GhostRoleComponent>(larva), Is.False, "Inside the host the larva is not a ghost role.");
        });

        // The host bursts on its own a minute after the larva forms.
        await pair.RunSeconds(70);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(larva));
            Assert.That(containers.IsEntityInContainer(larva), Is.False, "The larva has left the host.");
            Assert.That(entities.HasComponent<GhostRoleComponent>(larva), "With nobody queued, any ghost can take the larva.");
            Assert.That(entities.HasComponent<GhostTakeoverAvailableComponent>(larva));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The queen's eye reaches the client with its vision seeds and the camera follows it.</summary>
    [Test]
    public async Task QueenEyeReachesClient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.ResolveDependency<IEntityManager>();
        var cEntities = client.ResolveDependency<IEntityManager>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var minds = entities.System<MindSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.Sessions[0];

        EntityUid queen = default;
        EntityUid node = default;
        await server.WaitPost(() =>
        {
            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            queen = entities.SpawnEntity("CMXenoQueen", map.GridCoords);
            hives.SetHive(queen, hive);
            node = entities.SpawnEntity("XenoWeedsSource", map.GridCoords);
            hives.SetHive(node, hive);
            minds.TransferTo(minds.GetOrCreateMind(session.UserId), queen);
        });
        await pair.RunTicksSync(10);

        EntityUid eye = default;
        await server.WaitAssertion(() =>
        {
            var open = new QueenEyeActionEvent { Performer = queen };
            entities.EventBus.RaiseLocalEvent(queen, open);
            eye = entities.GetComponent<QueenEyeActionComponent>(queen).Eye!.Value;
            Assert.That(entities.HasComponent<QueenEyeComponent>(eye));
            Assert.That(entities.GetComponent<Robust.Shared.GameObjects.EyeComponent>(queen).Target, Is.EqualTo(eye), "The queen's camera follows the eye.");
        });
        await pair.RunTicksSync(15);

        await client.WaitAssertion(() =>
        {
            var cQueen = pair.ToClientUid(queen);
            var cEye = pair.ToClientUid(eye);
            Assert.That(cEntities.EntityExists(cEye), "The eye entity exists on the client.");
            Assert.That(cEntities.GetComponent<Robust.Shared.GameObjects.EyeComponent>(cQueen).Target, Is.EqualTo(cEye), "The client camera targets the eye.");
            Assert.That(cEntities.GetComponent<QueenEyeActionComponent>(cQueen).Eye, Is.EqualTo(cEye));
            Assert.That(cEntities.HasComponent<QueenEyeVisionComponent>(pair.ToClientUid(node)), "Weed nodes show the queen their surroundings.");
        });

        await server.WaitAssertion(() =>
        {
            entities.EventBus.RaiseLocalEvent(queen, new QueenEyeActionEvent { Performer = queen });
            Assert.That(entities.GetComponent<QueenEyeActionComponent>(queen).Eye, Is.Null);
            Assert.That(entities.GetComponent<Robust.Shared.GameObjects.EyeComponent>(queen).Target, Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The queen may open a shuttle console but not a gunnery console; a drone may open neither.</summary>
    [Test]
    public async Task QueenFliesButDoesNotShoot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var map = await pair.CreateTestMap();

        EntityUid queen = default;
        EntityUid drone = default;
        EntityUid shuttle = default;
        EntityUid gunnery = default;
        EntityUid vendor = default;
        await server.WaitPost(() =>
        {
            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            queen = entities.SpawnEntity("CMXenoQueen", map.GridCoords);
            drone = entities.SpawnEntity("CMXenoDrone", map.GridCoords);
            hives.SetHive(queen, hive);
            hives.SetHive(drone, hive);
            shuttle = entities.SpawnEntity("ComputerShuttle", map.GridCoords);
            gunnery = entities.SpawnEntity("ComputerGunneryConsole", map.GridCoords);
            foreach (var console in new[] { shuttle, gunnery })
            {
                var power = entities.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(console);
                power.NeedsPower = false;
                power.Powered = true;
            }
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<Content.Server.Shuttles.Components.ShuttleConsoleComponent>(shuttle));
            Assert.That(entities.HasComponent<Content.Shared._Mono.FireControl.FireControlConsoleComponent>(gunnery));

            var xform = entities.GetComponent<TransformComponent>(shuttle);
            var diag = $"anchored={xform.Anchored} powered={entities.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(shuttle).Powered}";
            Assert.That(Opens(queen, shuttle), "The queen opens the shuttle console. " + diag);
            Assert.That(Opens(queen, gunnery), Is.False, "The queen is refused a gunnery console.");
            Assert.That(Opens(drone, shuttle), Is.False, "A drone is refused the shuttle console.");
            Assert.That(Opens(drone, gunnery), Is.False, "A drone is refused a gunnery console.");
        });

        await pair.CleanReturnAsync();

        // The two checks a console UI open goes through: the user's attempt, then the console's own.
        bool Opens(EntityUid user, EntityUid console)
        {
            var userAttempt = new Content.Shared.UserInterface.UserOpenActivatableUIAttemptEvent(user, console);
            entities.EventBus.RaiseLocalEvent(user, userAttempt);
            if (userAttempt.Cancelled)
                return false;

            var consoleAttempt = new Content.Shared.UserInterface.ActivatableUIOpenAttemptEvent(user);
            entities.EventBus.RaiseLocalEvent(console, consoleAttempt);
            return !consoleAttempt.Cancelled;
        }
    }
}
