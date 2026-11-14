#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Server.GameTicking;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>The tier 5 citadel: it sets out after the delay at Overrun, raises a bigger stronghold, pays a scaled bounty and finishes the Reavers when it falls.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverCitadelTest : WFReaverTestBase
{
    /// <summary>Lifts the hourly payout cap for the bounty checks, so earlier tests on a pooled pair cannot cut them short.</summary>
    [SetUp]
    public async Task LiftPayoutCap()
    {
        await Server.WaitPost(() => Server.ResolveDependency<IConfigurationManager>().SetCVar(EncountersCVars.PayoutHourlyCap, int.MaxValue));
    }

    [TearDown]
    public async Task RestorePayoutCap()
    {
        await Server.WaitPost(() => Server.ResolveDependency<IConfigurationManager>()
            .SetCVar(EncountersCVars.PayoutHourlyCap, EncountersCVars.PayoutHourlyCap.DefaultValue));
    }

    /// <summary>Founds the stronghold at <see cref="WFReaverTestBase.Home"/> and takes one cell beyond its ring, the only cell a convoy can go to.</summary>
    private (EntityUid Stronghold, WFSectorCell Target) Prepare()
    {
        var campaign = Server.System<WFReaverCampaignSystem>();
        var territory = Server.System<WFSectorTerritorySystem>();
        var stronghold = FoundAt(Home);
        Assert.That(campaign.TrySpreadOnce(), Is.True);
        var target = territory.Held(MapData.MapId, Reavers).Single(cell => WFSectorHex.Distance(cell, Home) == 2);
        return (stronghold, target);
    }

    /// <summary>Sends the citadel out by hand and lands it on its target, at tier 5.</summary>
    private (EntityUid Stronghold, EntityUid Citadel, WFSectorCell Target) Raise()
    {
        var campaign = Server.System<WFReaverCampaignSystem>();
        var (stronghold, target) = Prepare();
        campaign.SetTier(WFReaverCampaignSystem.MaxTier);
        Assert.That(campaign.TryLaunchCitadel(out var citadel), Is.True);
        Assert.That(campaign.CompleteFounding(citadel), Is.True);
        return (stronghold, citadel, target);
    }

    /// <summary>Below tier 5 no citadel sets out however long it waits, and at tier 5 none does before the delay.</summary>
    [Test]
    public async Task NoCitadelBelowOverrunOrBeforeTheDelay()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            Prepare();
            Server.System<WFReaverCampaignSystem>().SetTier(4);
        });

        await RunTicks(300);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            Assert.That(campaign.CitadelConvoy(), Is.Null, "No citadel below Overrun.");
            Assert.That(State().OverrunSince, Is.Null);

            campaign.SetTier(WFReaverCampaignSystem.MaxTier);
            Assert.That(State().OverrunSince, Is.EqualTo(STiming.CurTime), "The delay runs from reaching the tier.");
        });

        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            Assert.That(campaign.CitadelConvoy(), Is.Null, "Not before the delay.");

            campaign.SetTier(4);
            Assert.That(State().OverrunSince, Is.Null, "Dropping a tier resets the delay.");
        });
    }

    /// <summary>At tier 5 past the delay the citadel sets out from the highest-level stronghold, contests its target, is announced and founds a citadel holding two rings.</summary>
    [Test]
    public async Task CitadelSetsOutAtOverrunAndRaisesACitadel()
    {
        await StartCampaign();
        var since = ChatCount();
        EntityUid stronghold = default;
        WFSectorCell target = default;
        await Server.WaitAssertion(() =>
        {
            (stronghold, target) = Prepare();
            Server.System<WFReaverCampaignSystem>().SetTier(WFReaverCampaignSystem.MaxTier);
        });

        await WaitUntilServer(() => Server.System<WFReaverCampaignSystem>().CitadelConvoy() != null, 600);
        var callsign = WFSectorHex.Callsign(target);
        EntityUid citadel = default;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            citadel = campaign.CitadelConvoy()!.Value;
            var comp = SEntMan.GetComponent<WFReaverConvoyComponent>(citadel);
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(citadel);
            Assert.That(comp.Citadel, Is.True);
            Assert.That(comp.Target, Is.EqualTo(target));
            Assert.That(comp.Source, Is.EqualTo(stronghold));
            Assert.That(encounter.Prototype.Id, Is.EqualTo(Citadel));
            Assert.That(territory.ContestOf(MapData.MapId, target)?.Source, Is.EqualTo(citadel), "Its target is contested.");
            Assert.That(campaign.TryLaunchCitadel(out _), Is.False, "One citadel at a time.");
            Assert.That(campaign.Strongholds(), Has.Count.EqualTo(1));

            Place(encounter.Ships["lead"].Grid, comp.TargetPosition);
            campaign.CheckConvoys();

            var founded = SEntMan.GetComponent<WFReaverStrongholdComponent>(citadel);
            Assert.That(founded.Citadel, Is.True);
            Assert.That(founded.Cell, Is.EqualTo(target));
            Assert.That(encounter.Name, Is.EqualTo($"Reaver citadel {callsign} (level 0)"));
            Assert.That(campaign.CitadelStanding(), Is.EqualTo(citadel));
            Assert.That(campaign.Convoys(), Is.Empty);
            Assert.That(territory.ContestOf(MapData.MapId, target), Is.Null, "The contest is withdrawn.");
            foreach (var cell in WFSectorHex.Within(target, 2))
            {
                // Past the sector's edge nothing is held.
                if (WFSectorHex.Distance(cell, WFSectorCell.Origin) > WFSectorProtectionSystem.CoreRadius && WFSectorHex.InSector(cell, territory.CellSize))
                    Assert.That(territory.Owner(MapData.MapId, cell)?.Id, Is.EqualTo(Reavers), $"{WFSectorHex.Callsign(cell)} is held");
            }

            var outer = WFSectorHex.Ring(target, 2).First(cell => WFSectorHex.Distance(cell, WFSectorCell.Origin) > WFSectorProtectionSystem.CoreRadius);
            Assert.That(territory.Sources(MapData.MapId, outer), Does.Contain(citadel), "The second ring is the citadel's own.");
            var state = State();
            Assert.That(state.Citadels, Is.EqualTo(1));
            Assert.That(state.Founded, Is.EqualTo(2));
            Assert.That(state.Score, Is.GreaterThanOrEqualTo(30f), "Two strongholds and ten more for the citadel.");
            Assert.That(Server.System<WFSectorSyncSystem>().BuildStatus().Lines.Single().Line, Does.EndWith($", citadel at {callsign}"),
                "The legend names the citadel.");
            var summary = new RoundEndTextAppendEvent();
            SEntMan.EventBus.RaiseEvent(EventSource.Local, summary);
            Assert.That(summary.Text, Does.Contain("raids were sent. They raised 1 citadel and 0 fell. Players retook"));

            // The tier's cap is 4; the citadel grows two past it, reusing the last reinforcement.
            var home = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            home.Level = 4;
            Assert.That(campaign.TryReinforce(stronghold), Is.False, "An ordinary hub stops at the tier's cap.");
            founded.Level = 4;
            Assert.That(campaign.TryReinforce(citadel), Is.True, "The citadel grows past it.");
            Assert.That(SEntMan.GetComponent<WFReaverReinforcementComponent>(founded.Inbound!.Value).Level, Is.EqualTo(5));
            founded.Inbound = null;
            founded.Level = 6;
            Assert.That(campaign.TryReinforce(citadel), Is.False, "Two levels past, no further.");
        });

        await RunTicks(30);
        var lines = ChatSince(since);
        Assert.That(lines.Any(line => line.Contains($"The Reavers are raising a citadel at {callsign}.")), Is.True, "The setting out is announced.");
        Assert.That(lines.Any(line => line.Contains($"The Reavers have raised a citadel at {callsign},")), Is.True, "The founding is announced.");
    }

    /// <summary>While a citadel stands no second one sets out, at tier 5 and past the delay.</summary>
    [Test]
    public async Task NoSecondCitadelWhileOneStands()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            Raise();
            for (var i = 0; i < 12; i++)
            {
                campaign.TrySpreadOnce();
            }

            Assert.That(campaign.TryLaunchCitadel(out _), Is.False);
            State().OverrunSince = STiming.CurTime - TimeSpan.FromMinutes(5);
        });

        await RunTicks(120);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            Assert.That(campaign.CitadelConvoy(), Is.Null);
            Assert.That(campaign.Strongholds().Count(entry => entry.Comp1.Citadel), Is.EqualTo(1));
            Assert.That(State().Citadels, Is.EqualTo(1));
        });
    }

    /// <summary>A citadel convoy stopped on the way founds nothing and pays half a level 0 citadel's bounty.</summary>
    [Test]
    public async Task StoppedCitadelConvoyPaysHalfTheCitadelBounty()
    {
        await StartCampaign();
        WFSectorCell target = default;
        var before = 0;
        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            (_, target) = Prepare();
            campaign.SetTier(WFReaverCampaignSystem.MaxTier);
            var chimes = Chimes();
            Assert.That(campaign.TryLaunchCitadel(out var convoy), Is.True);
            Assert.That(Chimes() - chimes, Is.EqualTo(2), "The setting out chimes and plays the capital sound.");
            ShootAt(convoy, "lead", ship);
            before = Balance();

            KillCrew(convoy, "lead");
            campaign.CheckConvoys();
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(convoy).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
            Assert.That(campaign.CitadelStanding(), Is.Null);
            Assert.That(State().Citadels, Is.Zero);
            Assert.That(State().Finished, Is.False, "Only a fallen citadel finishes the Reavers.");
        });

        await RunTicks(30);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Balance() - before, Is.EqualTo(Bounties[5] * 3 / 2));
        });
        Assert.That(ChatSince(since).Any(line => line.Contains($"The Reaver citadel convoy bound for {WFSectorHex.Callsign(target)} is destroyed. Bounty")),
            Is.True);
    }

    /// <summary>
    /// Breaking the citadel pays the scaled bounty and finishes the Reavers: nothing is founded, launched or raided
    /// again, however long it waits, while a stronghold still standing fights on and pays when broken.
    /// </summary>
    [Test]
    public async Task BrokenCitadelPaysAndFinishesTheReavers()
    {
        await StartCampaign();
        WFSectorCell target = default;
        EntityUid stronghold = default;
        EntityUid ship = default;
        var before = 0;
        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            (stronghold, var citadel, target) = Raise();
            var state = State();

            ShootAt(citadel, "lead", ship);
            before = Balance();
            KillCrew(citadel, "lead");
            campaign.CheckStrongholds();

            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(citadel).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
            Assert.That(campaign.Strongholds().Select(entry => entry.Owner), Is.EqualTo(new[] { stronghold }), "The ordinary hub still stands.");
            Assert.That(state.CitadelsBroken, Is.EqualTo(1));
            Assert.That(state.Finished, Is.True);
            Assert.That(campaign.IsFinished(state), Is.True);
            Assert.That(Server.System<WFSectorSyncSystem>().BuildStatus().Lines.Single().Line, Does.Contain(": finished,"),
                "The legend reads finished.");

            Assert.That(campaign.TryFound(out _), Is.False, "No founding once finished.");
            Assert.That(campaign.TryFoundAt(new MapCoordinates(CentreOf(Home), MapData.MapId), out _), Is.False);
            Assert.That(campaign.TryLaunchConvoy(out _), Is.False, "No convoy once finished.");
            Assert.That(campaign.TryLaunchConvoy(target, out _), Is.False);
            Assert.That(campaign.TryLaunchRaid(), Is.False, "No raid once finished.");
            Assert.That(campaign.TryLaunchCitadel(out _), Is.False);
            Assert.That(campaign.TrySpreadOnce(), Is.False, "No spread once finished.");
            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.False, "No patrol once finished.");
            Assert.That(campaign.TryReinforce(stronghold), Is.False, "No reinforcement once finished.");

            // The tier follows the score but a rise is not announced again.
            var announced = state.TierAnnounced;
            state.Tier = 3;
            campaign.SetTier(WFReaverCampaignSystem.MaxTier);
            Assert.That(state.Tier, Is.EqualTo(WFReaverCampaignSystem.MaxTier));
            Assert.That(state.TierAnnounced, Is.EqualTo(announced), "No rise is announced once finished.");

            var summary = new RoundEndTextAppendEvent();
            SEntMan.EventBus.RaiseEvent(EventSource.Local, summary);
            Assert.That(summary.Text, Does.Contain("The Reavers were finished when their citadel fell."));
        });

        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Balance() - before, Is.EqualTo(Bounties[5] * 3), "Three times the tier's bounty at level 0.");
            Assert.That(Server.System<WFReaverCampaignSystem>().Convoys(), Is.Empty, "Nothing sets out once finished.");
            Assert.That(Server.System<WFReaverCampaignSystem>().Patrols(), Is.Empty);

            // The ordinary hub fights on and still breaks and pays.
            before = Balance();
            ShootAt(stronghold, "lead", ship);
            KillCrew(stronghold, "lead");
            Server.System<WFReaverCampaignSystem>().CheckStrongholds();
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(stronghold).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
            Assert.That(State().Broken, Is.EqualTo(2));
            Assert.That(State().Defeated, Is.False, "A finished campaign is not broken as well.");

            // The regroup delay is seconds; once finished it never falls due.
            State().NoStrongholdSince = STiming.CurTime - TimeSpan.FromHours(1);
        });

        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            Assert.That(campaign.Strongholds(), Is.Empty, "No regroup once finished.");
            Assert.That(campaign.FoundingDue(campaign.Campaign!, State()), Is.Null);
            Assert.That(campaign.TryFound(out _), Is.False, "Founding never resumes.");
            Assert.That(Balance() - before, Is.EqualTo(Bounties[5]), "The last hub pays its own bounty.");
        });

        var lines = ChatSince(since);
        Assert.That(lines.Any(line => line.Contains(
                $"The Reaver citadel at {WFSectorHex.Callsign(target)} has fallen. Bounty {Bounties[5] * 3} thalers paid. The Ashfall Reavers are finished.")),
            Is.True, "The fall is announced as the ending.");
        Assert.That(lines.Any(line => line.Contains("The Ashfall Reavers are broken.")), Is.False, "No second ending.");
    }

    /// <summary>Every real campaign raises a citadel from a well-formed encounter, and its numbers are sane.</summary>
    [Test]
    public async Task EveryCampaignHasACitadel()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            foreach (var campaign in prototypes.EnumeratePrototypes<WFReaverCampaignPrototype>().Where(entry => !entry.ID.StartsWith("WFTest")))
            {
                var id = campaign.ID;
                Assert.That(campaign.Citadel, Is.Not.Null, $"{id} raises a citadel");
                var citadelId = campaign.Citadel!.Value;
                Assert.That(campaign.CitadelDelay, Is.GreaterThan(TimeSpan.Zero), id);
                Assert.That(campaign.CitadelRings, Is.GreaterThanOrEqualTo(1), id);
                Assert.That(campaign.CitadelExtraLevels, Is.GreaterThanOrEqualTo(0), id);
                Assert.That(campaign.CitadelBountyScale, Is.GreaterThanOrEqualTo(1f), id);

                var citadel = prototypes.Index(citadelId);
                Assert.That(citadel.Start, Is.EqualTo(WFEncounterStart.Manual), $"{citadelId}: only the campaign starts it");
                Assert.That(citadel.Lifetime, Is.EqualTo(WFEncounterLifetime.Persistent), $"{citadelId}");
                Assert.That(citadel.Ships, Has.Count.EqualTo(1), $"{citadelId}: a hub with no guard ships");
                var lead = citadel.Ships[0];
                Assert.That(lead.Key, Is.EqualTo("lead"), $"{citadelId}");
                Assert.That(lead.Evades, Is.False, $"{citadelId}");
                Assert.That(lead.Platform, Is.True, $"{citadelId}");
                Assert.That(lead.WarnRange, Is.GreaterThan(lead.AttackRange), $"{citadelId}");
                Assert.That(lead.Vessels, Is.Not.Empty, $"{citadelId}");
                foreach (var vessel in lead.Vessels)
                {
                    Assert.That(prototypes.HasIndex(vessel), Is.True, $"{citadelId}: {vessel}");
                }
            }
        });
    }
}
