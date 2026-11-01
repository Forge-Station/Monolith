using System.Linq;
using System.Reflection;
using Content.Client._WF.RoundSetup;
using Content.Server._WF.RoundSetup;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Voting.Managers;
using Content.Shared._WF.RoundSetup;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

public sealed partial class WFRoundSetupLobbyTest
{
    /// <summary>Characters already in the round form and join crews, recruit and post without respawning.</summary>
    [Test]
    public async Task LiveCrewsFormRecruitAndPost()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var em = server.EntMan;
        var client = pair.Player!;
        var ticker = server.System<GameTicker>();
        var setup = server.System<WFRoundSetupSystem>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var timers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var autoVote = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        var preset = ticker.Preset;
        var onRequest = typeof(WFRoundSetupSystem).GetMethod("OnRequest", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var findLobby = typeof(WFRoundSetupSystem).GetMethod("FindLobby", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var cards = pair.Client.System<WFCrewPostingCardSystem>();
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
            server.CfgMan.SetCVar(CCVars.GameRoleTimers, false);
            foreach (var vote in server.ResolveDependency<IVoteManager>().ActiveVotes.ToArray())
                vote.Cancel();
        });
        await pair.RunTicksSync(2);
        var players = await server.AddDummySessions(2);
        try
        {
            var loaded = false;
            for (var attempt = 0; attempt < 300 && !loaded; attempt++)
            {
                await server.WaitPost(() => loaded = players.All(player => player.Status == SessionStatus.InGame &&
                    preferences.HavePreferencesLoaded(player) && server.ResolveDependency<UserDbDataManager>().IsLoadComplete(player)));
                if (!loaded)
                {
                    await pair.RunTicksSync(1);
                    await Task.Delay(10);
                }
            }
            Assert.That(loaded, Is.True);
            await server.WaitPost(() =>
            {
                ticker.SetGamePreset("Sandbox");
                ticker.StartRound(force: true);
            });
            var map = await pair.CreateTestMap();
            var host = players[0];
            var recruit = players[1];
            await server.WaitPost(() =>
            {
                var minds = server.System<SharedMindSystem>();
                foreach (var player in players.Append(client))
                {
                    var name = player == host ? "Crew Leader" : player == recruit ? "New Recruit" : "Drifter";
                    ticker.PlayerJoinGame(player, silent: true);
                    var body = em.SpawnEntity("MobHuman", map.GridCoords);
                    minds.TransferTo(minds.CreateMind(player.UserId, name), body);
                }
            });
            await pair.RunTicksSync(5);
            var clientBody = client.AttachedEntity!.Value;

            var state = await Request(pair, new(WFRoundSetupAction.Refresh));
            Assert.That(state.CanJoin, Is.False);
            Assert.That(state.CanJoinLive, Is.True, "A living character in the round can form or join a crew.");

            await RequestAs(host, new(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Formed, Title = "Dust Hounds" });
            state = await Request(pair, new(WFRoundSetupAction.Refresh));
            var lobby = state.Lobbies.Single();
            var id = lobby.Id;
            Assert.Multiple(() =>
            {
                Assert.That(lobby.Scenario, Is.EqualTo(WFRoundScenario.Formed));
                Assert.That(lobby.Started, Is.True, "A Formed crew has no launch step.");
                Assert.That(lobby.LiveJoin, Is.True);
                Assert.That(lobby.AllowLateJoin, Is.True);
                Assert.That(lobby.Recruiting, Is.False);
                Assert.That(lobby.Roster.Single().Name, Is.EqualTo("Crew Leader"));
                Assert.That(lobby.Roster.Single().Status, Is.EqualTo(WFCrewMemberStatus.Alive));
            });

            await RequestAs(host, new(WFRoundSetupAction.UpsertRole) { LobbyId = id, Job = "Contractor", RoleName = "Deckhand", Capacity = 2 });
            await RequestAs(host, new(WFRoundSetupAction.SetRecruiting) { LobbyId = id, Recruiting = true });
            state = await Request(pair, new(WFRoundSetupAction.Refresh));
            Assert.That(state.Lobbies.Single().Recruiting, Is.True);
            Assert.That(state.Lobbies.Single().Roles.Single().CanSelect, Is.True, "A live viewer can take any opening with a free slot.");
            await RequestAs(host, new(WFRoundSetupAction.Configure) { LobbyId = id, Access = WFLobbyAccess.Invite, AllowLateJoin = true });
            state = await Request(pair, new(WFRoundSetupAction.Refresh));
            Assert.That(state.Lobbies.Single().Recruiting, Is.False, "An invite-only crew cannot advertise openings.");
            await RequestAs(host, new(WFRoundSetupAction.Configure) { LobbyId = id, Access = WFLobbyAccess.Public, AllowLateJoin = true });

            await RequestAs(host, new(WFRoundSetupAction.Post) { LobbyId = id, Posting = "Need two hands for a salvage run." });
            await pair.RunTicksSync(2);
            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(cards.Shown?.LobbyId, Is.EqualTo(id));
                Assert.That(cards.Shown?.Message, Is.EqualTo("Need two hands for a salvage run."));
                Assert.That(cards.Shown?.Openings, Is.EqualTo(2));
            });
            TimeSpan? cooldown = null;
            await server.WaitPost(() => cooldown = (TimeSpan?) NextPosting(host));
            Assert.That(cooldown, Is.Not.Null);
            await RequestAs(host, new(WFRoundSetupAction.Post) { LobbyId = id, Posting = "Again." });
            await server.WaitAssertion(() => Assert.That(NextPosting(host), Is.EqualTo(cooldown), "Postings wait out the cooldown."));

            state = await Request(pair, new(WFRoundSetupAction.Join) { LobbyId = id, RoleId = state.Lobbies.Single().Roles.Single().Id });
            Assert.That(state.OwnLobby, Is.EqualTo(id), state.Message);
            var me = OwnLobby(state).Members.Single(member => member.UserId == client.UserId);
            Assert.Multiple(() =>
            {
                Assert.That(me.Live, Is.True);
                Assert.That(me.Spawned, Is.True);
                Assert.That(me.Name, Is.EqualTo("Drifter"));
                Assert.That(client.AttachedEntity, Is.EqualTo(clientBody), "Joining live never respawns the character.");
            });
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            await server.WaitAssertion(() => Assert.That(em.HasComponent<WFPlayerCrewRadioComponent>(clientBody), Is.True));
            await pair.Client.WaitAssertion(() => Assert.That(cards.Shown, Is.Null, "Joining a crew clears open postings."));

            await RequestAs(host, new(WFRoundSetupAction.Invite) { LobbyId = id, Target = recruit.UserId });
            await RequestAs(recruit, new(WFRoundSetupAction.AcceptInvite) { LobbyId = id });
            state = await Request(pair, new(WFRoundSetupAction.Refresh));
            Assert.That(OwnLobby(state).MemberCount, Is.EqualTo(3));
            Assert.That(OwnLobby(state).Recruiting, Is.False, "A crew with no free slot stops showing as recruiting.");

            await RequestAs(host, new(WFRoundSetupAction.Kick) { LobbyId = id, Target = recruit.UserId });
            state = await Request(pair, new(WFRoundSetupAction.Refresh));
            Assert.That(OwnLobby(state).MemberCount, Is.EqualTo(2), "Hosts can remove live members.");
            Assert.That(OwnLobby(state).Recruiting, Is.True);

            state = await Request(pair, new(WFRoundSetupAction.Leave) { LobbyId = id });
            Assert.That(state.OwnLobby, Is.Null);
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            await server.WaitAssertion(() => Assert.That(em.HasComponent<WFPlayerCrewRadioComponent>(clientBody), Is.False));

            // A launched crew with nobody left alive drops off other players' lists, and comes back with them.
            var mobStates = server.System<Content.Shared.Mobs.Systems.MobStateSystem>();
            await server.WaitPost(() => mobStates.ChangeMobState(host.AttachedEntity!.Value, Content.Shared.Mobs.MobState.Dead));
            state = await Request(pair, new(WFRoundSetupAction.Refresh));
            Assert.That(state.Lobbies.Any(entry => entry.Id == id), Is.False, "A crew whose members are all dead isn't listed.");
            await server.WaitPost(() => mobStates.ChangeMobState(host.AttachedEntity!.Value, Content.Shared.Mobs.MobState.Alive));
            state = await Request(pair, new(WFRoundSetupAction.Refresh));
            Assert.That(state.Lobbies.Any(entry => entry.Id == id), Is.True);

            await pair.Client.WaitPost(() => pair.Client.CfgMan.SetCVar(WFRoundSetupCVars.ShowPostings, false));
            await pair.RunTicksSync(5);
            await server.WaitPost(() => findLobby.Invoke(setup, new object[] { host.UserId })!.GetType().GetField("NextPosting")!
                .SetValue(findLobby.Invoke(setup, new object[] { host.UserId }), null));
            await RequestAs(host, new(WFRoundSetupAction.Post) { LobbyId = id, Posting = "Still looking." });
            await pair.RunTicksSync(2);
            await pair.Client.WaitAssertion(() => Assert.That(cards.Shown, Is.Null, "Muted players receive no postings."));
        }
        finally
        {
            await pair.Client.WaitPost(() => pair.Client.CfgMan.SetCVar(WFRoundSetupCVars.ShowPostings, true));
            await server.WaitPost(() =>
            {
                ticker.RestartRound();
                ticker.SetGamePreset(preset);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, timers);
            });
            await server.RemoveAllDummySessions();
            await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, autoVote));
        }
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();

        object NextPosting(ICommonSession player)
        {
            var found = findLobby.Invoke(setup, new object[] { player.UserId })!;
            return found.GetType().GetField("NextPosting")!.GetValue(found);
        }

        async Task RequestAs(ICommonSession player, WFRoundSetupRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await server.WaitPost(() => onRequest.Invoke(setup, new object[] { request, new EntitySessionEventArgs(player) }));
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
        }
    }
}
