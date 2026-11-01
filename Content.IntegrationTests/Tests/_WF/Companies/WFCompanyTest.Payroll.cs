using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.Server._WF.Companies;
using Content.Server.Afk;
using Content.Server._WF.Encounters.Systems;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Voting.Managers;
using Content.Shared._WF.Companies;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WF.Companies;

public sealed partial class WFCompanyTest
{
    /// <summary>Wages for played hours, shared shortfalls, and the company's cut of bounties.</summary>
    [Test]
    public async Task PayrollPaysPlayedHoursAndTakesCuts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var em = server.EntMan;
        var companies = server.System<WFCompanySystem>();
        var ticker = server.System<GameTicker>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var owner = pair.Player!;
        var preset = ticker.Preset;
        var autoVote = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        var original = (HumanoidCharacterProfile) preferences.GetPreferences(owner.UserId).SelectedCharacter;
        var slot = preferences.GetPreferences(owner.UserId).SelectedCharacterIndex;
        var spawnedSlots = (Dictionary<NetUserId, int>) typeof(WFCompanySystem)
            .GetField("_spawnedSlots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(companies)!;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
            foreach (var vote in server.ResolveDependency<IVoteManager>().ActiveVotes.ToArray())
                vote.Cancel();
        });
        var worker = (await server.AddDummySessions(1)).Single();
        int? id = null;
        try
        {
            await WaitFor(() => companies.Loaded && worker.Status == SessionStatus.InGame && preferences.HavePreferencesLoaded(worker));
            await server.WaitPost(() =>
            {
                preferences.SetProfile(owner.UserId, slot, original.WithName("Ada Owner").WithBankBalance(10000)).Wait();
                var prefs = preferences.GetPreferences(worker.UserId);
                preferences.SetProfile(worker.UserId, prefs.SelectedCharacterIndex,
                    ((HumanoidCharacterProfile) prefs.SelectedCharacter).WithName("Dee Worker").WithBankBalance(0)).Wait();
                ticker.SetGamePreset("Sandbox");
                ticker.StartRound(force: true);
            });
            var map = await pair.CreateTestMap();

            await RequestAs(owner, new(WFCompanyAction.Create) { Name = "Payday Works", Tag = "PAY" });
            await server.WaitPost(() => id = companies.FindCompany("PAY"));
            Assert.That(id, Is.Not.Null);
            await RequestAs(owner, new(WFCompanyAction.Deposit) { CompanyId = id!.Value, Amount = 1000 });
            await RequestAs(owner, new(WFCompanyAction.Invite) { CompanyId = id.Value, TargetUser = worker.UserId });
            await RequestAs(worker, new(WFCompanyAction.AcceptInvite) { CompanyId = id.Value });
            var recruitRank = 0;
            await server.WaitPost(() => recruitRank = RankIds(id.Value).Last());
            await RequestAs(owner, new(WFCompanyAction.UpsertRank)
            {
                CompanyId = id.Value, RankId = recruitRank, Name = "Recruit", BaseJob = "Contractor", Wage = 300,
            });

            // Lobby time never counts; only a living body spawned from the member character does.
            await server.WaitPost(() => companies.SamplePayroll(WFCompanyPolicy.PayrollHourSeconds));
            await server.WaitAssertion(() => Assert.That(companies.GetBalance(id.Value), Is.EqualTo(1000)));
            await server.WaitPost(() =>
            {
                ticker.PlayerJoinGame(worker, silent: true);
                var body = em.SpawnEntity("MobHuman", map.GridCoords);
                var minds = server.System<SharedMindSystem>();
                minds.TransferTo(minds.CreateMind(worker.UserId, "Dee Worker"), body);
                spawnedSlots[worker.UserId] = preferences.GetPreferences(worker.UserId).SelectedCharacterIndex;
                // Dummy sessions send no input, so mark the worker active.
                server.ResolveDependency<IAfkManager>().PlayerDidAction(worker);
            });
            await pair.RunTicksSync(5);

            await server.WaitPost(() => companies.SamplePayroll(WFCompanyPolicy.PayrollHourSeconds - 60));
            await server.WaitAssertion(() => Assert.That(companies.GetBalance(id.Value), Is.EqualTo(1000), "No wage before a full hour."));
            await server.WaitPost(() => companies.SamplePayroll(60));
            await server.WaitAssertion(() =>
            {
                Assert.That(companies.GetBalance(id.Value), Is.EqualTo(700));
                Assert.That(WorkerBank(), Is.EqualTo(300));
            });

            await RequestAs(owner, new(WFCompanyAction.UpsertRank)
            {
                CompanyId = id.Value, RankId = recruitRank, Name = "Recruit", BaseJob = "Contractor", Wage = 1000,
            });
            await server.WaitPost(() => companies.SamplePayroll(WFCompanyPolicy.PayrollHourSeconds));
            await server.WaitAssertion(() =>
            {
                Assert.That(companies.GetBalance(id.Value), Is.EqualTo(0), "A short account pays out what it has.");
                Assert.That(WorkerBank(), Is.EqualTo(1000), "70% of the 1000 wage.");
            });

            await server.WaitPost(() =>
            {
                Assert.That(server.System<WFEncounterBountySystem>().Deposit(worker, 1000), Is.True);
                Assert.That(companies.CutFor(owner, 0), Is.EqualTo(0));
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(companies.GetBalance(id.Value), Is.EqualTo(100), "The default 10% cut goes to the company.");
                Assert.That(WorkerBank(), Is.EqualTo(1900));
            });

            // The owner pays a member directly from the account.
            var workerProfile = 0;
            await server.WaitPost(() => workerProfile = MemberProfiles(id.Value).Single(profile => profile != MemberProfiles(id.Value).First()));
            await RequestAs(owner, new(WFCompanyAction.Pay) { CompanyId = id.Value, Target = workerProfile, Amount = 40, Description = "Good work" });
            await server.WaitAssertion(() =>
            {
                Assert.That(companies.GetBalance(id.Value), Is.EqualTo(60));
                Assert.That(WorkerBank(), Is.EqualTo(1940));
            });
            await server.WaitPost(() => companies.AdminAdjust(id.Value, 40, "Test", "restore"));
            await RequestAs(owner, new(WFCompanyAction.SetCut) { CompanyId = id.Value, Amount = 0 });
            await server.WaitPost(() => Assert.That(server.System<WFEncounterBountySystem>().Deposit(worker, 500), Is.True));
            await server.WaitAssertion(() =>
            {
                Assert.That(companies.GetBalance(id.Value), Is.EqualTo(100), "No cut at 0%.");
                Assert.That(WorkerBank(), Is.EqualTo(2440));
            });
            await server.WaitAssertion(() =>
            {
                var report = new RoundEndTextAppendEvent();
                em.EventBus.RaiseEvent(EventSource.Local, report);
                Assert.That(report.Text, Does.Contain("Payday Works"));
                Assert.That(report.Text, Does.Contain("Wages paid 1000, payments 40, cuts earned 100, deposits 1000"), report.Text);
                Assert.That(report.Text, Does.Contain("1 member played 2"), report.Text);
            });
        }
        finally
        {
            if (id is { } left)
                await server.WaitPost(() => companies.AdminDisband(left));
            await Run(() => companies.Flush());
            await server.WaitPost(() =>
            {
                ticker.RestartRound();
                ticker.SetGamePreset(preset);
                preferences.SetProfile(owner.UserId, slot, original).Wait();
            });
            await server.RemoveAllDummySessions();
            await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, autoVote));
        }
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();

        int WorkerBank() => ((HumanoidCharacterProfile) preferences.GetPreferences(worker.UserId).SelectedCharacter).BankBalance;

        IEnumerable<int> MemberProfiles(int company)
        {
            var field = typeof(WFCompanySystem).GetField("_companies", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var entry = ((System.Collections.IDictionary) field.GetValue(companies)!)[company]!;
            var owner = (int) entry.GetType().GetField("Owner")!.GetValue(entry)!;
            var members = (System.Collections.IDictionary) entry.GetType().GetField("Members")!.GetValue(entry)!;
            yield return owner;
            foreach (int profile in members.Keys)
            {
                if (profile != owner)
                    yield return profile;
            }
        }

        IEnumerable<int> RankIds(int company)
        {
            var field = typeof(WFCompanySystem).GetField("_companies", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var entry = ((System.Collections.IDictionary) field.GetValue(companies)!)[company]!;
            var ranks = (System.Collections.IEnumerable) entry.GetType().GetField("Ranks")!.GetValue(entry)!;
            foreach (var rank in ranks)
                yield return (int) rank.GetType().GetField("Id")!.GetValue(rank)!;
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
