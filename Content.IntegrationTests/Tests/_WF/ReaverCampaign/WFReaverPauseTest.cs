#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Shared._WF.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>A paused, held or switched-off campaign owes nothing on resuming: every clock is pushed on by the time it sat, so no pirates arrive at once.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverPauseTest : WFReaverTestBase
{
    /// <summary>How far ahead of now the clocks are set.</summary>
    private static readonly TimeSpan Lead = TimeSpan.FromSeconds(3);

    private EntityUid _stronghold;
    private TimeSpan _before;

    /// <summary>Founds a stronghold at tier 3 and sets every clock to fall due a few seconds from now.</summary>
    private async Task Arm()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            campaign.SetTier(3);
            _stronghold = FoundAt(Home);
            _before = STiming.CurTime;
            var comp = SEntMan.GetComponent<WFReaverStrongholdComponent>(_stronghold);
            comp.NextPatrol = _before + Lead;
            comp.NextReinforcement = _before + Lead;
            var state = State();
            state.NextSpread = _before + Lead;
            state.NextRaid = _before + Lead;
            state.LastFounded = _before - TimeSpan.FromHours(2);
        });
    }

    /// <summary>The state's and the stronghold's clocks are each at least a hold's span past where they were set, less a little slack.</summary>
    private void AssertHeld(TimeSpan span)
    {
        var slack = TimeSpan.FromSeconds(2);
        var comp = SEntMan.GetComponent<WFReaverStrongholdComponent>(_stronghold);
        var state = State();
        Assert.That(comp.NextPatrol, Is.GreaterThanOrEqualTo(_before + Lead + span - slack));
        Assert.That(comp.NextReinforcement, Is.GreaterThanOrEqualTo(_before + Lead + span - slack));
        Assert.That(state.NextSpread, Is.GreaterThanOrEqualTo(_before + Lead + span - slack));
        Assert.That(state.NextRaid, Is.GreaterThanOrEqualTo(_before + Lead + span - slack));
        Assert.That(state.LastFounded, Is.GreaterThanOrEqualTo(_before - TimeSpan.FromHours(2) + span - slack));
    }

    /// <summary>With the campaign paused nothing is launched, and after resuming the schedule picks up where it left off, not all at once.</summary>
    [Test]
    public async Task PausedCampaignOwesNothingOnResuming()
    {
        await Arm();
        await Server.WaitPost(() => Server.System<WFReaverCampaignSystem>().Paused = true);
        await RunTicks(1200);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            Assert.That(campaign.Patrols(), Is.Empty, "Nothing launched while paused.");
            Assert.That(SEntMan.GetComponent<WFReaverStrongholdComponent>(_stronghold).Inbound, Is.Null);
            AssertHeld(STiming.CurTime - _before);
            campaign.Paused = false;
        });

        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFReaverCampaignSystem>().Patrols(), Is.Empty, "A second after resuming nothing has fallen due.");
        });

        await WaitUntilServer(() => Server.System<WFReaverCampaignSystem>().Patrols().Count > 0, 900);
    }

    /// <summary>Pausing the event runner pauses the campaign and holds the timers of running encounters.</summary>
    [Test]
    public async Task PausedEventRunnerHoldsTheCampaignAndEncounterTimers()
    {
        await Arm();
        EntityUid patrol = default;
        TimeSpan expires = default;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            Assert.That(campaign.TryLaunchPatrol(_stronghold), Is.True);
            patrol = campaign.Patrols().Single().Owner;
            expires = SEntMan.GetComponent<WFEncounterComponent>(patrol).Expires!.Value;
            Assert.That(campaign.Held, Is.False);
            Server.System<WFEncounterSchedulerSystem>().Paused = true;
            Assert.That(campaign.Held, Is.True, "The event runner holds the campaign.");
            Assert.That(campaign.RunnerPaused, Is.True);
        });

        await RunTicks(1200);
        await Server.WaitAssertion(() =>
        {
            var held = STiming.CurTime - _before;
            AssertHeld(held);
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(patrol).Expires!.Value, Is.GreaterThanOrEqualTo(expires + held - TimeSpan.FromSeconds(2)),
                "The patrol's time to live did not run while the runner was paused.");
            Assert.That(Server.System<WFReaverCampaignSystem>().Patrols(), Has.Count.EqualTo(1), "No further patrol went out.");
        });
    }

    /// <summary>A campaign switched off holds its clocks too, so switching it back on does not launch everything at once.</summary>
    [Test]
    public async Task SwitchedOffCampaignOwesNothingOnResuming()
    {
        await Arm();
        var config = Server.ResolveDependency<IConfigurationManager>();
        await Server.WaitPost(() => config.SetCVar(EncountersCVars.Enabled, false));
        await RunTicks(1200);
        await Server.WaitAssertion(() =>
        {
            AssertHeld(STiming.CurTime - _before);
            Assert.That(Server.System<WFReaverCampaignSystem>().Patrols(), Is.Empty);
            config.SetCVar(EncountersCVars.Enabled, true);
        });

        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFReaverCampaignSystem>().Patrols(), Is.Empty, "A second after switching on nothing has fallen due.");
        });
    }
}
