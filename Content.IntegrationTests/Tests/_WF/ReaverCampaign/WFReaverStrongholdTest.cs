#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>Strongholds: they break when their lead is out of the fight and pay then, free their cells when they go, and are founded again.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverStrongholdTest : WFReaverTestBase
{
    /// <summary>The lead out of the fight breaks a stronghold; its bounty goes to those who shot at it.</summary>
    [Test]
    public async Task LeadDownBreaksTheStrongholdAndPaysTheBounty()
    {
        await StartCampaign();
        EntityUid stronghold = default, ship = default;
        var before = 0;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            stronghold = FoundAt(Home);
            ShootAt(stronghold, "lead", ship);
            before = Balance();
            campaign.SetTier(3);
        });
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(stronghold);
            Assert.That(encounter.Resolution, Is.Null, "Shot at, but the lead lives.");
            Assert.That(campaign.Strongholds(), Has.Count.EqualTo(1));
            Assert.That(SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).Hits,
                Is.EqualTo(WFEncounterRewardSystem.MinimumHits), "The campaign counts the hits on it.");
            Assert.That(Balance(), Is.EqualTo(before));

            KillCrew(stronghold, "lead");
        });
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(stronghold);
            Assert.That(encounter.Resolution, Is.EqualTo(WFEncounterResolution.Destroyed), "Lead down is broken.");
            Assert.That(campaign.Strongholds(), Is.Empty);
            Assert.That(territory.Held(MapData.MapId, Reavers), Is.Empty, "Its cells are freed.");
            var state = campaign.State()!;
            Assert.That(state.Broken, Is.EqualTo(1));
            Assert.That(state.NoStrongholdSince, Is.Not.Null, "The regroup clock starts.");
            Assert.That(Balance() - before, Is.EqualTo(Bounties[3]), "The bounty of the higher of the founding tier and the tier now.");
        });
    }

    /// <summary>A stronghold an admin ends is gone with its cells and pays nothing.</summary>
    [Test]
    public async Task EndedStrongholdFreesItsCellsAndPaysNothing()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        var before = 0;
        await Server.WaitAssertion(() =>
        {
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            stronghold = FoundAt(Home);
            ShootAt(stronghold, "lead", ship);
            before = Balance();
            Assert.That(Server.System<WFSectorTerritorySystem>().Held(MapData.MapId, Reavers), Has.Count.EqualTo(7));
            Server.System<WFEncounterSystem>().End(stronghold);
        });
        await RunTicks(30);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFSectorTerritorySystem>().Held(MapData.MapId, Reavers), Is.Empty);
            Assert.That(Server.System<WFReaverCampaignSystem>().State()!.Broken, Is.Zero, "Ending one is not breaking it.");
            Assert.That(Balance(), Is.EqualTo(before));
        });
    }

    /// <summary>Sector control switched off and on again leaves a standing stronghold holding its cell and ring once more.</summary>
    [Test]
    public async Task StrongholdClaimsItsCellsAgainWhenSectorControlComesBack()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var config = Server.ResolveDependency<IConfigurationManager>();
            FoundAt(Home);
            Assert.That(territory.Held(MapData.MapId, Reavers), Has.Count.EqualTo(7));

            config.SetCVar(SectorControlCVars.Enabled, false);
            Assert.That(territory.Held(MapData.MapId, Reavers), Is.Empty);
            config.SetCVar(SectorControlCVars.Enabled, true);
            Assert.That(territory.Held(MapData.MapId, Reavers), Has.Count.EqualTo(7), "The stronghold stands, so it holds its cells.");
        });
    }

    /// <summary>The first stronghold is founded on schedule after the stations are generated, and a fresh one after a lull when it is broken.</summary>
    [Test]
    public async Task FoundsOnScheduleAndRegroupsAfterABreak()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            Assert.That(campaign.StationsAt, Is.Null);
            Assert.That(campaign.Strongholds(), Is.Empty);

            // The stations were generated ten seconds ago; the delay is three.
            typeof(WFReaverCampaignSystem).GetField("_stationsAt", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(campaign, STiming.CurTime - TimeSpan.FromSeconds(10));
        });
        await WaitUntilServer(() => Server.System<WFReaverCampaignSystem>().Strongholds().Count == 1, 900);

        EntityUid first = default;
        WFReaverCampaignComponent state = default!;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            first = campaign.Strongholds().Single().Owner;
            Assert.That(campaign.Convoys(), Is.Empty, "The first appears at once; nothing stands to send it from.");
            state = campaign.State()!;
            Assert.That(state.Founded, Is.EqualTo(1));
            Assert.That(state.NoStrongholdSince, Is.Null);
            KillCrew(first, "lead");
        });

        await WaitUntilServer(() => SEntMan.GetComponent<WFEncounterComponent>(first).Resolution == WFEncounterResolution.Destroyed, 300);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFReaverCampaignSystem>().Strongholds(), Is.Empty);
            Assert.That(state.Broken, Is.EqualTo(1));
            Assert.That(state.NoStrongholdSince, Is.Not.Null, "The lull begins.");
        });

        await WaitUntilServer(() => Server.System<WFReaverCampaignSystem>().Strongholds().Count == 1, 900);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var fresh = campaign.Strongholds().Single().Owner;
            Assert.That(fresh, Is.Not.EqualTo(first));
            Assert.That(campaign.Convoys(), Is.Empty, "A regroup appears at once too.");
            Assert.That(WFSectorHex.Centre(SEntMan.GetComponent<WFReaverStrongholdComponent>(fresh).Cell, 3000f).Length(),
                Is.InRange(14000f, 24000f), "Far out, as the first was.");
            Assert.That(state.Founded, Is.EqualTo(2), "Regrouped after the lull.");
            Assert.That(state.NoStrongholdSince, Is.Null);
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(fresh).Prototype.Id, Is.EqualTo(Stronghold), "A light one, as the first was.");
            Assert.That(Server.System<WFSectorTerritorySystem>().Held(MapData.MapId, Reavers), Has.Count.EqualTo(7));
        });
    }
}
