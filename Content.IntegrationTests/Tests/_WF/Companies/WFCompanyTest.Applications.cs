using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._WF.Companies;
using Content.Server._WF.Companies;
using Content.Server.Preferences.Managers;
using Content.Shared._WF.Companies;
using Content.Shared.Preferences;
using Content.Shared.StatusIcon;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Companies;

public sealed partial class WFCompanyTest
{
    /// <summary>Applications with a rank and message, open access, rank loadouts and job icon presets.</summary>
    [Test]
    public async Task ApplicationsOpenAccessAndLoadouts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var em = server.EntMan;
        var companies = server.System<WFCompanySystem>();
        var client = pair.Client.System<WFCompanyClientSystem>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var owner = pair.Player!;
        var original = (HumanoidCharacterProfile) preferences.GetPreferences(owner.UserId).SelectedCharacter;
        var slot = preferences.GetPreferences(owner.UserId).SelectedCharacterIndex;
        var players = await server.AddDummySessions(2);
        var applicant = players[0];
        var walkIn = players[1];
        int? id = null;
        try
        {
            await WaitFor(() => companies.Loaded && players.All(player => player.Status == SessionStatus.InGame &&
                preferences.HavePreferencesLoaded(player)));
            await server.WaitPost(() =>
            {
                preferences.SetProfile(owner.UserId, slot, original.WithName("Ada Owner").WithBankBalance(100000)).Wait();
                foreach (var (player, name) in new[] { (applicant, "Bea Applicant"), (walkIn, "Cal Walkin") })
                {
                    var prefs = preferences.GetPreferences(player.UserId);
                    preferences.SetProfile(player.UserId, prefs.SelectedCharacterIndex,
                        ((HumanoidCharacterProfile) prefs.SelectedCharacter).WithName(name).WithBankBalance(100000)).Wait();
                }
            });
            // Creating needs enough hours played and the fee from the founder's bank.
            await server.WaitPost(() => server.CfgMan.SetCVar(WFCompanyCVars.CreateHours, 100000));
            var state = await Request(new(WFCompanyAction.Create) { Name = "Open Door Freight", Tag = "DOOR" });
            Assert.That(state.Own, Is.Null, "Too few hours played.");
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(WFCompanyCVars.CreateHours, 0);
                server.CfgMan.SetCVar(WFCompanyCVars.CreateFee, 20000);
            });
            state = await Request(new(WFCompanyAction.Create) { Name = "Open Door Freight", Tag = "DOOR" });
            await server.WaitPost(() => server.CfgMan.SetCVar(WFCompanyCVars.CreateFee, 0));
            Assert.That(state.Own, Is.Not.Null, state.Message);
            Assert.That(state.CharacterBalance, Is.EqualTo(80000), "The fee comes out of the founder's bank.");
            id = state.Own!.Id;
            var member = state.Own.Ranks[1];
            var recruit = state.Own.Ranks[^1];

            // An application carries the rank asked for and why, and survives a reload.
            await RequestAs(applicant, new(WFCompanyAction.Apply) { CompanyId = id.Value, RankId = member.Id, Description = new string('x', 501) });
            state = await Request(new(WFCompanyAction.Refresh));
            Assert.That(state.Own!.Applications, Is.Empty, "Over-long applications are refused.");
            await RequestAs(applicant, new(WFCompanyAction.Apply) { CompanyId = id.Value, RankId = member.Id, Description = "I fly cargo.\nReliable." });
            await Run(() => companies.Reload());
            state = await Request(new(WFCompanyAction.Refresh));
            var application = state.Own!.Applications.Single();
            Assert.Multiple(() =>
            {
                Assert.That(application.Name, Is.EqualTo("Bea Applicant"));
                Assert.That(application.RankId, Is.EqualTo(member.Id));
                Assert.That(application.Message, Is.EqualTo("I fly cargo.\nReliable."));
                Assert.That(state.Directory.Single().Ranks!.Select(rank => rank.Id), Does.Contain(member.Id), "Listings show ranks to apply for.");
            });
            await RenderWindow();
            await pair.Client.WaitPost(() =>
            {
                var form = new WFCompanyApplyWindow(state.Directory.Single(), _ => { });
                form.OpenCentered();
                form.Close();
            });
            state = await Request(new(WFCompanyAction.AcceptApplication) { CompanyId = id.Value, Target = application.Profile });
            Assert.Multiple(() =>
            {
                Assert.That(state.Own!.Applications, Is.Empty);
                Assert.That(state.Own.Members.Single(entry => entry.Name == "Bea Applicant").Rank, Is.EqualTo(member.Id),
                    "Accepted applicants get the rank they asked for.");
            });

            // Open access lets anyone join straight in at the owner's chosen rank.
            await RequestAs(walkIn, new(WFCompanyAction.JoinOpen) { CompanyId = id.Value });
            state = await Request(new(WFCompanyAction.Refresh));
            Assert.That(state.Own!.Members, Has.Length.EqualTo(2), "Joining needs open access.");
            await RequestAs(applicant, new(WFCompanyAction.SetOpenRank) { CompanyId = id.Value, RankId = recruit.Id });
            state = await Request(new(WFCompanyAction.Refresh));
            Assert.That(state.Own!.OpenRankId, Is.Null, "Only the owner sets open access.");
            state = await Request(new(WFCompanyAction.SetOpenRank) { CompanyId = id.Value, RankId = recruit.Id });
            Assert.That(state.Directory.Single().OpenRankId, Is.EqualTo(recruit.Id));
            await RequestAs(walkIn, new(WFCompanyAction.JoinOpen) { CompanyId = id.Value });
            state = await Request(new(WFCompanyAction.Refresh));
            Assert.That(state.Own!.Members.Single(entry => entry.Name == "Cal Walkin").Rank, Is.EqualTo(recruit.Id));

            // A player has one character per company: another of the walk-in's characters is turned away.
            var join = typeof(WFCompanySystem).GetMethod("Join", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var companyField = typeof(WFCompanySystem).GetField("_companies", BindingFlags.Instance | BindingFlags.NonPublic)!;
            string joinError = null;
            await server.WaitPost(() =>
            {
                var company = ((System.Collections.IDictionary) companyField.GetValue(companies)!)[id.Value]!;
                joinError = (string) join.Invoke(companies, new object[] { company, int.MaxValue, walkIn.UserId, "Cal Alt", 5, null, null });
            });
            Assert.That(joinError, Is.EqualTo("wf-company-error-same-player"));

            // Profiles list members, unless the owner made the company private; members still see their own.
            state = await Request(new(WFCompanyAction.SetPrivate) { CompanyId = id.Value, Private = true });
            Assert.That(state.Own!.Private, Is.True);
            WFCompanyProfileEvent profile = null;
            await pair.Client.WaitPost(() =>
            {
                client.ProfileReceived += received => profile = received;
                var window = new WFCompanyProfileWindow(state.Directory.Single(), null);
                window.OpenCentered();
            });
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            Assert.That(profile?.Members, Has.Length.EqualTo(3), "Members see their own company's roster.");
            var profileOf = typeof(WFCompanySystem).GetMethod("Profile", BindingFlags.Instance | BindingFlags.NonPublic)!;
            WFCompanyProfileEvent outsider = null;
            await server.WaitPost(() =>
            {
                var company = ((System.Collections.IDictionary) companyField.GetValue(companies)!)[id.Value]!;
                outsider = (WFCompanyProfileEvent) profileOf.Invoke(companies, new object[] { company, false })!;
            });
            Assert.That(outsider.Hidden, Is.True);
            Assert.That(outsider.Members, Is.Empty, "Outsiders don't see a private company's members.");

            // Rank loadouts: only loadouts a crew job can take, saved with the rank.
            var allowed = WFCompanyLoadouts.Catalogue(prototypes).Select(entry => entry.Loadout).ToList();
            Assert.That(allowed.Count, Is.GreaterThan(WFCompanyLoadouts.Catalogue(prototypes).Count(entry => entry.Group.ID.StartsWith("Contractor"))),
                "The catalogue spans every crew job, not one.");
            var paid = allowed.First(loadout => loadout.Price > 0 && WFCompanyLoadouts.Items(prototypes, loadout).Count > 0);
            state = await Request(new(WFCompanyAction.SetRankLoadout) { CompanyId = id.Value, RankId = recruit.Id, Loadouts = new[] { "NotALoadout" } });
            Assert.That(state.Own!.Ranks[^1].Loadouts, Is.Empty, "Unknown loadouts are refused.");
            await Request(new(WFCompanyAction.SetRankLoadout) { CompanyId = id.Value, RankId = recruit.Id, Loadouts = new[] { paid.ID } });
            await Run(() => companies.Reload());
            state = await Request(new(WFCompanyAction.Refresh));
            Assert.That(state.Own!.Ranks[^1].Loadouts, Is.EqualTo(new[] { paid.ID }));
            await RenderWindow();
            await pair.Client.WaitPost(() =>
            {
                var picker = new WFCompanyLoadoutWindow("Loadout", new[] { paid.ID }, _ => { });
                picker.OpenCentered();
                picker.Close();
                var lateJoin = new Content.Client._NF.LateJoin.Windows.PickerWindow();
                lateJoin.OpenCentered();
                lateJoin.Close();
            });

            // Issuing: the company pays while it can, then the member's own bank.
            var map = await pair.CreateTestMap();
            var issue = typeof(WFCompanySystem).GetMethod("IssueRankLoadout", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var item = WFCompanyLoadouts.Items(prototypes, paid)[0];
            var walkInSlot = preferences.GetPreferences(walkIn.UserId).SelectedCharacterIndex;
            await Request(new(WFCompanyAction.Deposit) { CompanyId = id.Value, Amount = paid.Price * 2 });
            await server.WaitPost(() =>
            {
                issue.Invoke(companies, new object[] { walkIn, em.SpawnEntity("MobHuman", map.GridCoords), walkInSlot });
                issue.Invoke(companies, new object[] { walkIn, em.SpawnEntity("MobHuman", map.GridCoords), walkInSlot });
            });
            await Run(() => companies.Flush());
            await server.WaitAssertion(() =>
            {
                Assert.That(companies.GetBalance(id.Value), Is.EqualTo(paid.Price), "The company pays for one spawn per member per round.");
                Assert.That(((HumanoidCharacterProfile) preferences.GetPreferences(walkIn.UserId).SelectedCharacter).BankBalance,
                    Is.EqualTo(100000 - paid.Price), "The member paid for the second.");
                var issued = 0;
                var query = em.EntityQueryEnumerator<MetaDataComponent>();
                while (query.MoveNext(out var meta))
                {
                    if (meta.EntityPrototype?.ID == item.Id)
                        issued++;
                }
                Assert.That(issued, Is.GreaterThanOrEqualTo(2));
            });
            state = await Request(new(WFCompanyAction.Refresh));
            Assert.That(state.Own!.Ledger[0].Kind, Is.EqualTo(WFCompanyLedgerKind.Loadout));

            // The client reads job icons for the rank icon editor, from textures a packaged build still has.
            var icon = prototypes.EnumeratePrototypes<JobIconPrototype>().First(entry => entry.Icon is SpriteSpecifier.Rsi);
            byte[] preset = null;
            await pair.Client.WaitPost(() => preset = client.ReadIconPreset(icon.Icon));
            Assert.That(preset, Has.Length.EqualTo(WFCompanyPolicy.IconBytes));
        }
        finally
        {
            if (id is { } left)
                await server.WaitPost(() => companies.AdminDisband(left));
            await Run(() => companies.Flush());
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(WFCompanyCVars.CreateHours, 0);
                server.CfgMan.SetCVar(WFCompanyCVars.CreateFee, 0);
                preferences.SetProfile(owner.UserId, slot, original).Wait();
            });
            await server.RemoveAllDummySessions();
        }
        await pair.CleanReturnAsync();

        async Task<WFCompanyState> Request(WFCompanyRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await pair.Client.WaitPost(() => client.Send(request));
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            await Run(() => companies.Flush());
            await pair.RunTicksSync(5);
            WFCompanyState result = null;
            await pair.Client.WaitPost(() => result = client.State);
            Assert.That(result, Is.Not.Null);
            return result;
        }

        async Task RenderWindow()
        {
            WFCompanyWindow window = null;
            await pair.Client.WaitPost(() =>
            {
                window = new WFCompanyWindow();
                window.OpenCentered();
            });
            await pair.RunTicksSync(pair.SecondsToTicks(0.3f));
            await pair.Client.WaitPost(() => window.Close());
        }

        async Task RequestAs(ICommonSession player, WFCompanyRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await Run(() => companies.HandleRequest(request, player));
            await Run(() => companies.Flush());
        }

        async Task Run(System.Func<Task> start)
        {
            Task task = null;
            await server.WaitPost(() => task = start());
            for (var i = 0; i < 600 && !task.IsCompleted; i++)
            {
                await pair.RunTicksSync(1);
                await Task.Delay(5);
            }
            Assert.That(task.IsCompleted, Is.True, "Server task timed out.");
            await task;
        }

        async Task WaitFor(System.Func<bool> condition)
        {
            var done = false;
            for (var i = 0; i < 600 && !done; i++)
            {
                await server.WaitPost(() => done = condition());
                if (!done)
                {
                    await pair.RunTicksSync(1);
                    await Task.Delay(10);
                }
            }
            Assert.That(done, Is.True);
        }
    }
}
