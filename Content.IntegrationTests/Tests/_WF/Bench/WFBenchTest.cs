#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._NF.Bank;
using Content.Server._WF.Bench;
using Content.Server._WF.Drydock;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Bench;
using Content.Shared._WF.CCVar;
using Content.Shared.Paper;
using Content.Shared.Preferences;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Bench;

/// <summary>
/// A Bench ticket takes what the offender's bank holds, garnishes the rest from later gains, turns overdue, outlaws
/// with a warrant, and a pardon returns everything taken.
/// </summary>
[TestOf(typeof(WFBenchSystem))]
public sealed class WFBenchTest : InteractionTest
{
    private const string Judge = "WFBenchTestJudge";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFBenchTestJudge
  name: bench test judge
  components:
  - type: Hands
  - type: MindContainer
";

    [Test]
    public async Task TicketGarnishPardonTest()
    {
        var bench = SEntMan.System<WFBenchSystem>();
        var bank = SEntMan.System<BankSystem>();
        var prefs = Server.ResolveDependency<IServerPreferencesManager>();
        var cfg = Server.ResolveDependency<IConfigurationManager>();
        var user = ServerSession.UserId;

        // The offender's character holds 20,000 and carries their bank account.
        var save = Task.CompletedTask;
        await Server.WaitPost(() =>
        {
            var p = prefs.GetPreferences(user);
            var profile = (HumanoidCharacterProfile) p.SelectedCharacter;
            save = prefs.SetProfile(user, p.SelectedCharacterIndex, profile.WithBankBalance(20_000));
            SEntMan.EnsureComponent<BankAccountComponent>(SPlayer);
        });
        await PoolManager.WaitUntil(Server, () => save.IsCompleted, maxTicks: 300);

        EntityUid judge = default;
        await Server.WaitPost(() => judge = SEntMan.SpawnEntity(Judge, SEntMan.GetCoordinates(PlayerCoords)));

        int Balance()
        {
            bank.TryGetBalance(ServerSession, out var balance);
            return balance;
        }

        List<WFBenchChargeRequest> Charges(params (string Law, int Fine)[] laws)
        {
            return laws.Select(l => new WFBenchChargeRequest { Law = l.Law, Fine = l.Fine }).ToList();
        }

        // 10,000 + 15,000 against 20,000: all of it is taken, 5,000 is owed, and the judge gets the stamped ticket.
        WFBenchTicket? first = null;
        await Server.WaitAssertion(() =>
        {
            Assert.That(bench.IsJudge(judge), Is.False, "A mob with no judge's job is not on the Bench.");
            var error = bench.Issue(judge, SPlayer, Charges(("WFLawTrespass", 10_000), ("WFLawFalseDistress", 15_000)),
                "Test Hull", "test notes", out first);
            Assert.That(error, Is.Null);
            Assert.That(first, Is.Not.Null);
            Assert.That(first!.Total, Is.EqualTo(25_000));
            Assert.That(first.Paid, Is.EqualTo(20_000));
            Assert.That(first.Owed, Is.EqualTo(5_000));
            Assert.That(first.Status, Is.EqualTo(WFBenchTicketStatus.Owing));
            Assert.That(Balance(), Is.EqualTo(0));

            var papers = SEntMan.EntityQuery<PaperComponent>().ToList();
            Assert.That(papers.Any(p => p.Content.Contains("Ticket #") && p.StampedBy.Count > 0),
                $"No stamped ticket was handed over. Papers: {string.Join(" | ", papers.Select(p => $"{p.StampedBy.Count} stamps: {p.Content}"))}");
        });

        // A law ticked twice counts once, and a huge custom charge is held to the custom limit.
        await Server.WaitAssertion(() =>
        {
            var requests = Charges(("WFLawBrandishing", 5_000), ("WFLawBrandishing", 5_000));
            requests.Add(new WFBenchChargeRequest { Title = "Being annoying", Fine = 20_000_000 });
            Assert.That(bench.Issue(judge, SPlayer, requests, "", "", out var capped), Is.Null);
            Assert.That(capped!.Charges, Has.Count.EqualTo(2));
            Assert.That(capped.Uncapped, Is.EqualTo(5_000 + WFBenchMath.MaxCustomFine));
            Assert.That(bench.Pardon(capped.Number, "test"), Is.True);
        });

        // A fine outside the law's range is clamped to it.
        await Server.WaitAssertion(() =>
        {
            Assert.That(bench.Issue(judge, SPlayer, Charges(("WFLawBrandishing", 999_999)), "", "", out var clamped), Is.Null);
            Assert.That(clamped!.Total, Is.EqualTo(10_000));
            Assert.That(bench.Pardon(clamped.Number, "test"), Is.True);
        });

        // A 4,000 gain has half taken for the debt on the next update.
        await Server.WaitAssertion(() => Assert.That(bank.TryBankDeposit(SPlayer, 4_000, tax: false), Is.True));
        await RunSeconds(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(first!.Owed, Is.EqualTo(3_000));
            Assert.That(first.Paid, Is.EqualTo(22_000));
            Assert.That(Balance(), Is.EqualTo(2_000));
            Assert.That(bench.Owed(user), Is.EqualTo(3_000));
        });

        // A pardon returns everything taken and drops the debt.
        await Server.WaitAssertion(() =>
        {
            Assert.That(bench.Pardon(first!.Number, "test"), Is.True);
            Assert.That(bench.Pardon(first.Number, "test"), Is.False, "A ticket is pardoned once.");
            Assert.That(first.Status, Is.EqualTo(WFBenchTicketStatus.Pardoned));
            Assert.That(bench.Owed(user), Is.EqualTo(0));
            Assert.That(Balance(), Is.EqualTo(24_000));
        });

        // With no deadline, a ticket the bank can't cover turns overdue on the next update.
        WFBenchTicket? late = null;
        await Server.WaitAssertion(() =>
        {
            cfg.SetCVar(BenchCVars.DeadlineMinutes, 0);
            Assert.That(bench.Issue(judge, SPlayer, Charges(("WFLawDeepTrespass", 60_000)), "", "", out late), Is.Null);
            Assert.That(late!.Owed, Is.EqualTo(36_000));
        });
        await RunSeconds(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(late!.Status, Is.EqualTo(WFBenchTicketStatus.Overdue));
            Assert.That(bench.Pardon(late.Number, "test"), Is.True);
            cfg.SetCVar(BenchCVars.DeadlineMinutes, BenchCVars.DeadlineMinutes.DefaultValue);
        });

        // Outlawry takes no fine and puts a warrant on the offender; a pardon lifts it.
        await Server.WaitAssertion(() =>
        {
            var before = Balance();
            Assert.That(bench.Issue(judge, SPlayer, Charges(("WFLawSedition", 50_000)), "", "", out var outlaw), Is.Null);
            Assert.That(outlaw!.Total, Is.EqualTo(0));
            Assert.That(outlaw.Status, Is.EqualTo(WFBenchTicketStatus.Outlawed));
            Assert.That(Balance(), Is.EqualTo(before));
            Assert.That(SEntMan.HasComponent<WFShipbreakerWarrantComponent>(SPlayer));
            Assert.That(bench.Pardon(outlaw.Number, "test"), Is.True);
        });
        await RunTicks(2);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<WFShipbreakerWarrantComponent>(SPlayer), Is.False));

        // A judge can't ticket themselves, and an unknown law is no charge.
        await Server.WaitAssertion(() =>
        {
            Assert.That(bench.Issue(SPlayer, SPlayer, Charges(("WFLawTrespass", 10_000)), "", "", out _), Is.Not.Null);
            Assert.That(bench.Issue(judge, SPlayer, Charges(("WFLawNoSuchLaw", 10_000)), "", "", out _), Is.Not.Null);
        });
    }

    /// <summary>A judge's disabler harpoon starts loaded and draws a fresh dart after its interval once emptied.</summary>
    [Test]
    public async Task HarpoonRearmsTest()
    {
        EntityUid gun = default;
        await Server.WaitPost(() =>
        {
            gun = SEntMan.SpawnEntity("WFBenchHarpoonGun", MapCoordinates.Nullspace);
            SEntMan.GetComponent<WFBenchHarpoonComponent>(gun).Interval = System.TimeSpan.FromSeconds(1);
        });
        await RunTicks(1);
        await Server.WaitAssertion(() =>
        {
            var ammo = SEntMan.GetComponent<BallisticAmmoProviderComponent>(gun);
            Assert.That(ammo.Count, Is.EqualTo(1), "The harpoon should start loaded.");
            SEntMan.System<Content.Server.Weapons.Ranged.Systems.GunSystem>().SetBallisticUnspawned((gun, ammo), 0);
        });
        await RunSeconds(12);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<BallisticAmmoProviderComponent>(gun).Count, Is.EqualTo(1), "No dart was drawn."));
        await Server.WaitPost(() => SEntMan.DeleteEntity(gun));
    }
}
