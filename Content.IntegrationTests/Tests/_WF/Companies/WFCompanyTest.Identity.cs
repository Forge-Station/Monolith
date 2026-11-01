using System.Linq;
using System.Threading.Tasks;
using Content.Client._WF.Companies;
using Content.Client._WF.RoundSetup;
using Content.Server._WF.Companies;
using Content.Server._WF.RoundSetup;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Voting.Managers;
using Content.Shared._WF.Companies;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Companies;

public sealed partial class WFCompanyTest
{
    /// <summary>Joining as a rank spawns the base job with the rank title, marks the ID and grants the company channel.</summary>
    [Test]
    public async Task MembersJoinAsTheirRank()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var em = server.EntMan;
        var companies = server.System<WFCompanySystem>();
        var client = pair.Client.System<WFCompanyClientSystem>();
        var ticker = server.System<GameTicker>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var owner = pair.Player!;
        var preset = ticker.Preset;
        var autoVote = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        var timers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var original = (HumanoidCharacterProfile) preferences.GetPreferences(owner.UserId).SelectedCharacter;
        var slot = preferences.GetPreferences(owner.UserId).SelectedCharacterIndex;
        int? id = null;
        var paidLoadouts = 0;
        var boughtShip = EntityUid.Invalid;
        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, false);
                foreach (var vote in server.ResolveDependency<IVoteManager>().ActiveVotes.ToArray())
                    vote.Cancel();
                // A bought loadout replaces the stored profile mid-spawn, which must not lose the character's company.
                var prototypes = server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>();
                var role = prototypes.Index<Content.Shared.Preferences.Loadouts.RoleLoadoutPrototype>("JobContractor");
                var loadout = new Content.Shared.Preferences.Loadouts.RoleLoadout(role.ID);
                var named = original.WithName("Ada Owner").WithBankBalance(100000);
                loadout.SetDefault(named, owner, prototypes);
                foreach (var group in role.Groups)
                {
                    var paid = prototypes.Index(group).Loadouts.Select(entry => prototypes.Index(entry)).FirstOrDefault(entry => entry.Price > 0);
                    if (paid != null && loadout.AddLoadout(group, paid.ID, prototypes))
                        break;
                }
                paidLoadouts = loadout.SelectedLoadouts.Values.SelectMany(picked => picked)
                    .Sum(picked => prototypes.Index(picked.Prototype).Price);
                preferences.SetProfile(owner.UserId, slot, named.WithLoadout(loadout)).Wait();
            });
            for (var i = 0; i < 300 && !companies.Loaded; i++)
                await pair.RunTicksSync(1);
            var state = await Send(new(WFCompanyAction.Create) { Name = "Rankline", Tag = "RANK" });
            id = state.Own!.Id;
            Assert.That(state.CanJoinRound, Is.False, "Joining as a rank needs a running round.");

            // Starting a round reads the database off the main thread, which the synchronous test database refuses mid-request.
            await FlushCompanies();
            await server.WaitPost(() =>
            {
                ticker.SetGamePreset("Sandbox");
                ticker.StartRound(force: true);
                var jobs = server.System<StationJobsSystem>();
                var setup = server.System<WFRoundSetupSystem>();
                var query = em.EntityQueryEnumerator<StationJobsComponent, StationSpawningComponent>();
                while (query.MoveNext(out var station, out _, out _))
                {
                    if (setup.IsStandardStation(station))
                        jobs.TrySetJobSlot(station, "Contractor", 4, createSlot: true);
                }
            });
            // A custom icon on the top rank, which the owner shows.
            var icon = new byte[WFCompanyPolicy.IconBytes];
            icon[0] = 200;
            icon[3] = 255;
            // The owner sets up their own rank: title, base job and icon.
            var topRank = state.Own!.OwnerRank!.Id;
            Assert.That(state.Own.OwnerRank.Title, Is.EqualTo("Owner"));
            state = await Send(new(WFCompanyAction.UpsertRank) { CompanyId = id.Value, RankId = topRank, Name = "Captain", BaseJob = "Contractor" });
            Assert.That(state.Own!.OwnerRank!.Title, Is.EqualTo("Captain"), state.Message);
            state = await Send(new(WFCompanyAction.SetRankIcon) { CompanyId = id.Value, RankId = topRank, Logo = icon });
            Assert.That(state.Own!.OwnerRank!.IconVersion, Is.GreaterThan(0), state.Message);
            state = await Send(new(WFCompanyAction.Refresh));
            Assert.That(state.Characters.Single().RankId, Is.EqualTo(topRank), "The character creator shows the owner rank's icon.");
            Assert.That(state.Characters.Single().IconVersion, Is.GreaterThan(0));
            Assert.That(state.CanJoinRound, Is.True);
            await Send(new(WFCompanyAction.JoinAsRank) { CompanyId = id.Value });
            await pair.RunTicksSync(pair.SecondsToTicks(1.5f));
            await server.WaitAssertion(() =>
            {
                Assert.That(ticker.UserHasJoinedGame(owner), Is.True);
                Assert.That(paidLoadouts, Is.GreaterThan(0), "The character spawns with a paid loadout.");
                Assert.That(((HumanoidCharacterProfile) preferences.GetPreferences(owner.UserId).SelectedCharacter).BankBalance, Is.LessThan(100000),
                    "The paid loadout was bought.");
                var body = owner.AttachedEntity!.Value;
                Assert.That(server.System<SharedIdCardSystem>().TryFindIdCard(body, out var card), Is.True);
                Assert.That(card.Comp.LocalizedJobTitle, Is.EqualTo("Captain"), "The rank title replaces the job title on the ID.");
                var marker = em.GetComponent<WFCompanyIdComponent>(card);
                Assert.That(marker.Tag, Is.EqualTo("RANK"));
                Assert.That(marker.Rank, Is.EqualTo("Captain"));
                Assert.That(em.GetComponent<WFCompanyRankIconComponent>(card).RankId, Is.EqualTo(topRank));
                var examine = server.System<Content.Server._WF.ExamineCard.ExamineCardSystem>().BuildCard(body, body, true);
                companies.FillExamineCard(body, examine);
                Assert.That(examine.Company, Is.EqualTo("[RANK] Rankline"), "The personal record shows the company.");
                Assert.That(examine.CompanyRank, Is.EqualTo("Captain"));
                Assert.That(examine.CompanyRankId, Is.EqualTo(topRank));
                Assert.That(em.HasComponent<WFCompanyRankHudComponent>(body), Is.True);
            });
            // The HUD swaps the ID's job icon for the rank icon once the client has fetched it.
            await pair.Client.WaitPost(() => client.GetRankIconPath(topRank));
            await pair.RunTicksSync(pair.SecondsToTicks(1f));
            await pair.Client.WaitAssertion(() =>
            {
                var clientEm = pair.Client.EntMan;
                var clientBody = clientEm.GetEntity(em.GetNetEntity(owner.AttachedEntity!.Value));
                var job = pair.Client.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>().Index<JobIconPrototype>("JobIconContractor");
                var icons = new GetStatusIconsEvent(new System.Collections.Generic.List<StatusIconData> { job });
                clientEm.EventBus.RaiseLocalEvent(clientBody, ref icons);
                Assert.That(client.GetRankIconPath(topRank), Is.Not.Null);
                Assert.That(icons.StatusIcons.OfType<JobIconPrototype>(), Is.Empty, "The rank icon replaces the job icon.");
                Assert.That(icons.StatusIcons[0].Icon, Is.EqualTo(new Robust.Shared.Utility.SpriteSpecifier.Texture(client.GetRankIconPath(topRank)!.Value)));
            });
            await pair.RunTicksSync(pair.SecondsToTicks(1.5f));
            await server.WaitAssertion(() =>
            {
                var body = owner.AttachedEntity!.Value;
                Assert.That(em.GetComponent<ActiveRadioComponent>(body).Channels, Does.Contain(WFCompanySystem.CompanyChannel));
                Assert.That(companies.CompanyRadioName(body), Is.EqualTo("Company · RANK"));
                // The channel's key has to reach it; `:h` is taken by the headset default and never would.
                var radio = server.ProtoMan.Index<RadioChannelPrototype>(WFCompanySystem.CompanyChannel);
                server.System<ChatSystem>().TryProccessRadioMessage(body, $"{SharedChatSystem.RadioChannelPrefix}{radio.KeyCode} hello", out _, out var typed, quiet: true);
                Assert.That(typed?.ID, Is.EqualTo(WFCompanySystem.CompanyChannel));
            });
            // The client's instant card already names the company and rank, so the basic job doesn't flash first.
            await pair.Client.WaitAssertion(() =>
            {
                var clientBody = pair.Client.EntMan.GetEntity(em.GetNetEntity(owner.AttachedEntity!.Value));
                var predicted = pair.Client.System<Content.Client._WF.ExamineCard.ExamineCardSystem>().BuildCard(clientBody, clientBody, true, predicted: true);
                Assert.That(predicted.Company, Is.EqualTo("[RANK] Rankline"));
                Assert.That(predicted.CompanyRank, Is.EqualTo("Captain"));
                Assert.That(predicted.CompanyRankId, Is.EqualTo(topRank));
            });
            // The station record carries the rank, so the crew manifest and records show its icon.
            await pair.RunTicksSync(pair.SecondsToTicks(2.5f));
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<SharedIdCardSystem>().TryFindIdCard(owner.AttachedEntity!.Value, out var card), Is.True);
                var key = em.GetComponent<Content.Shared.StationRecords.StationRecordKeyStorageComponent>(card).Key;
                Assert.That(key, Is.Not.Null);
                Assert.That(server.System<Content.Server.StationRecords.Systems.StationRecordsSystem>()
                    .TryGetRecord<Content.Shared.StationRecords.GeneralStationRecord>(key!.Value, out var record), Is.True);
                Assert.That(record!.WFCompanyRank, Is.EqualTo(topRank));

                // A ship bought with the member's ID flies under the company, name and colour included.
                var bought = em.SpawnEntity(null, Robust.Shared.Map.MapCoordinates.Nullspace);
                em.AddComponent<Content.Shared.Shuttles.Components.IFFComponent>(bought);
                em.EnsureComponent<Content.Shared._Mono.Company.CompanyComponent>(bought);
                companies.MarkBoughtShip(bought, card);
                boughtShip = bought;
                Assert.That(em.GetComponent<Content.Shared._WF.Companies.WFCompanyShipComponent>(bought).Tag, Is.EqualTo("RANK"));
                Assert.That(record.JobTitle, Is.EqualTo("Captain"), "The manifest shows the rank, not the job.");
            });
            await pair.RunTicksSync(pair.SecondsToTicks(2.5f));
            await server.WaitAssertion(() =>
            {
                Assert.That(string.IsNullOrEmpty(em.GetComponent<Content.Shared._Mono.Company.CompanyComponent>(boughtShip).CompanyName), Is.True,
                    "The faction company can't override the company's colour on radar labels.");
                Assert.That(em.GetComponent<Content.Shared.Shuttles.Components.IFFComponent>(boughtShip).Color,
                    Is.EqualTo(em.GetComponent<Content.Shared._WF.Companies.WFCompanyShipComponent>(boughtShip).Colour));
            });
            await pair.Client.WaitAssertion(() => Assert.That(Content.Client._WF.Companies.WFCompanyRankIcons.Get(topRank), Is.Not.Null,
                "Screens that draw job icons get the rank icon."));

            // A Formed crew run by the owner flies under the company.
            var crews = pair.Client.System<WFRoundSetupClientSystem>();
            await CrewSend(new(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Formed, Title = "Rank Runners" });
            var crewState = await CrewSend(new(WFRoundSetupAction.Refresh));
            Assert.That(crewState.FieldCompanies.Single().Tag, Is.EqualTo("RANK"));
            crewState = await CrewSend(new(WFRoundSetupAction.SetCompany) { LobbyId = crewState.OwnLobby!.Value, CompanyId = id.Value });
            var crew = crewState.Lobbies.Single(lobby => lobby.Id == crewState.OwnLobby);
            Assert.Multiple(() =>
            {
                Assert.That(crew.CompanyTag, Is.EqualTo("RANK"));
                Assert.That(crew.CompanyName, Is.EqualTo("Rankline"));
                Assert.That(crewState.OwnCompany, Is.EqualTo(id.Value));
            });

            async Task<WFRoundSetupState> CrewSend(WFRoundSetupRequest request)
            {
                await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
                await pair.Client.WaitPost(() => crews.Send(request));
                await pair.RunTicksSync(pair.SecondsToTicks(0.75f));
                WFRoundSetupState result = null;
                await pair.Client.WaitPost(() => result = crews.State);
                return result;
            }
        }
        finally
        {
            if (id is { } left)
                await server.WaitPost(() => companies.AdminDisband(left));
            // The synchronous test database takes one request at a time: let company writes land, save the profile, then restart.
            await FlushCompanies();
            await server.WaitPost(() => preferences.SetProfile(owner.UserId, slot, original).Wait());
            await server.WaitPost(() =>
            {
                ticker.RestartRound();
                ticker.SetGamePreset(preset);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, timers);
                server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, autoVote);
            });
            await FlushCompanies();
        }
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();

        async Task FlushCompanies()
        {
            Task task = null;
            await server.WaitPost(() => task = companies.Flush());
            for (var i = 0; i < 600 && !task.IsCompleted; i++)
                await pair.RunTicksSync(1);
            await task;
        }

        async Task<WFCompanyState> Send(WFCompanyRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await pair.Client.WaitPost(() => client.Send(request));
            await pair.RunTicksSync(pair.SecondsToTicks(0.75f));
            WFCompanyState result = null;
            await pair.Client.WaitPost(() => result = client.State);
            return result;
        }
    }
}
