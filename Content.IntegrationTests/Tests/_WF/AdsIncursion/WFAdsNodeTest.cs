#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.AdsIncursion.Components;
using Content.Server._WF.AdsIncursion.Systems;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Server.GameTicking;
using Content.Shared._WF.AdsIncursion;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.AdsIncursion;

/// <summary>Nodes, sentinels, sweeps, interdictions and the tier: where nodes go, what breaking them pays and how the ADS grows.</summary>
[TestOf(typeof(WFAdsCampaignSystem))]
public sealed class WFAdsNodeTest : WFAdsTestBase
{
    /// <summary>With the cvar off the campaign idles; the rule running turns it on.</summary>
    [Test]
    public async Task CampaignFollowsTheRule()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var config = Server.ResolveDependency<IConfigurationManager>();
            var ticker = Server.System<GameTicker>();
            var ads = Server.System<WFAdsCampaignSystem>();
            Assert.That(ads.Campaign?.ID, Is.EqualTo(AdsCampaign));
            Assert.That(ads.Running, Is.True);

            config.SetCVar(AdsIncursionCVars.Enabled, false);
            Assert.That(ads.Idle(), Is.EqualTo(WFAdsIdle.CampaignOff));
            ticker.StartGameRule("WFAdsCampaignRule", out var rule);
            Assert.That(ads.Running, Is.True);
            ticker.EndGameRule(rule);
            Assert.That(ads.Idle(), Is.EqualTo(WFAdsIdle.CampaignOff));
        });
    }

    /// <summary>The first node goes 16 to 26 km out and three cells from any cell another faction holds.</summary>
    [Test]
    public async Task FirstNodeKeepsClearOfOtherFactions()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var band = WFSectorHex.Within(WFSectorCell.Origin, 8)
                .Where(cell => WFSectorHex.Centre(cell, 3000f).Length() is >= 16000f and <= 26000f).ToList();
            var rival = band[0];
            ClaimForReavers(rival);

            Assert.That(ads.TryFound(out var node), Is.True);
            var cell = SEntMan.GetComponent<WFAdsNodeComponent>(node).Cell;
            Assert.That(band, Does.Contain(cell));
            Assert.That(WFSectorHex.Distance(cell, rival), Is.GreaterThanOrEqualTo(3), "It starts three cells from Reaver space.");
            Assert.That(AdsState().Founded, Is.EqualTo(1));
        });
    }

    /// <summary>A node claims its cell and ring, leaving out a cell with a station in it, which it can't claim at all.</summary>
    [Test]
    public async Task FoundingClaimsTheRingAndSkipsStationCells()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            var stationCell = FarCell(AdsHome, 1);
            SpawnStation(CentreOf(stationCell), "Test Station");

            var node = FoundNode(AdsHome);
            Assert.That(territory.Held(map, Ads), Has.Count.EqualTo(6), "The cell and its ring, less the station's cell.");
            Assert.That(territory.Owner(map, stationCell), Is.Null);
            Assert.That(territory.ContestOf(map, stationCell), Is.Null, "No blockade either.");
            foreach (var cell in territory.Held(map, Ads))
            {
                Assert.That(territory.Sources(map, cell), Is.EquivalentTo(new[] { node }));
            }

            Assert.That(territory.TryClaim(map, stationCell, Ads, node), Is.False, "The Asset Preservation Directive vetoes it.");
            Assert.That(territory.TryClaim(map, stationCell, Reavers, null), Is.True, "Others may.");
            var state = AdsState();
            Assert.That(state.Score, Is.GreaterThan(16f));
            Assert.That(state.Tier, Is.GreaterThanOrEqualTo(1));
            var legend = Server.System<WFSectorSyncSystem>().BuildStatus().Lines.Single(line => line.Faction == Ads);
            Assert.That(legend.Line, Does.StartWith("Automated Defense System: "));
        });
    }

    /// <summary>A node whose lead is out of the fight is broken: its cells are freed and its bounty paid to those who hit it.</summary>
    [Test]
    public async Task BrokenNodePaysAndFreesItsCells()
    {
        await StartAds();
        EntityUid node = default;
        var before = 0;
        await Server.WaitAssertion(() =>
        {
            var ship = SpawnGrid(CentreOf(AdsHome) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            node = FoundNode(AdsHome);
            ShootAt(node, "lead", ship);
            before = Balance();
            Server.System<WFAdsCampaignSystem>().SetTier(3);
        });
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(node).Resolution, Is.Null);
            Assert.That(SEntMan.GetComponent<WFAdsNodeComponent>(node).Hits, Is.EqualTo(WFEncounterRewardSystem.MinimumHits));
            KillCrew(node, "lead");
        });
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(node).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
            Assert.That(ads.Nodes(), Is.Empty);
            Assert.That(Server.System<WFSectorTerritorySystem>().Held(MapData.MapId, Ads), Is.Empty, "Its cells are freed.");
            var state = AdsState();
            Assert.That(state.Broken, Is.EqualTo(1));
            Assert.That(state.Defeated, Is.True, "The last node down sends the ADS dormant.");
            Assert.That(state.NoNodeSince, Is.Not.Null, "The regroup clock starts.");
            Assert.That(Balance() - before, Is.EqualTo(NodeBounties[3]));
        });
    }

    /// <summary>A sentinel flies in, joins its node and circles it; breaking the node then pays half as much again.</summary>
    [Test]
    public async Task SentinelJoinsAndScalesTheBounty()
    {
        await StartAds();
        EntityUid node = default, sentinel = default, ship = default;
        var before = 0;
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var transform = Server.System<SharedTransformSystem>();
            ship = SpawnGrid(CentreOf(AdsHome) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            node = FoundNode(AdsHome);
            var comp = SEntMan.GetComponent<WFAdsNodeComponent>(node);
            Assert.That(ads.TrySentinel(node), Is.True);
            Assert.That(ads.TrySentinel(node), Is.False, "One on its way at a time.");
            sentinel = ads.Sentinels().Single().Owner;
            Assert.That(comp.Inbound, Is.EqualTo(sentinel));
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(sentinel).OffBudget, Is.True);

            var orders = Orders(sentinel);
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo, WFCrewObjectiveKind.Circle }));
            Assert.That(orders[0].Range, Is.EqualTo(500f));
            Assert.That(orders[1].Target, Is.EqualTo(SEntMan.GetNetEntity(LeadOf(node))), "It circles the node's lead.");
            Assert.That(orders[1].Range, Is.EqualTo(400f));

            Place(LeadOf(sentinel), transform.GetWorldPosition(LeadOf(node)) + new Vector2(300f, 0f));
            ads.CheckSentinels();
            Assert.That(SEntMan.GetComponent<WFAdsSentinelComponent>(sentinel).Phase, Is.EqualTo(WFAdsSentinelPhase.Circling));
            Assert.That(comp.Sentinels, Is.EquivalentTo(new[] { sentinel }));
            Assert.That(comp.Inbound, Is.Null);
            Assert.That(comp.NextSentinel, Is.GreaterThan(STiming.CurTime + TimeSpan.FromMinutes(29)), "The clock restarts.");

            ads.SetTier(3);
            ShootAt(node, "lead", ship);
            before = Balance();
            KillCrew(node, "lead");
        });
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(node).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
            Assert.That(Balance() - before, Is.EqualTo((int) (NodeBounties[3] * 1.5f)), "Half as much again for the sentinel alive.");
            Assert.That(Server.System<WFAdsCampaignSystem>().Sentinels(), Is.Empty, "Its sentinel goes with it.");
        });
    }

    /// <summary>A sweep killed in a cell frees it, unless a node stands there, and pays those who shot it.</summary>
    [Test]
    public async Task KilledSweepFreesItsCell()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var neighbour = AdsHome.Neighbour(1);
            var ship = SpawnGrid(CentreOf(AdsHome) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            var node = FoundNode(AdsHome);
            Assert.That(ads.TryLaunchSweep(node), Is.True);
            var (sweep, comp, encounter) = ads.Sweeps().Single();
            Assert.That(comp.Node, Is.EqualTo(node));
            Assert.That(encounter.Lifetime, Is.EqualTo(WFEncounterLifetime.Transient));
            Assert.That(Orders(sweep).Select(order => order.Position), Is.EqualTo(comp.Route.Append(comp.Home)), "Its route, then home.");

            Place(LeadOf(sweep), CentreOf(neighbour));
            ShootAt(sweep, "lead", ship);
            var before = Balance();
            Server.System<WFEncounterSystem>().Resolve(sweep, WFEncounterResolution.Destroyed);
            Assert.That(territory.Owner(MapData.MapId, neighbour), Is.Null, "The cell it died in is clear.");
            Assert.That(territory.Held(MapData.MapId, Ads), Has.Count.EqualTo(6));
            Assert.That(AdsState().SweepsDestroyed, Is.EqualTo(1));
            Assert.That(Balance() - before, Is.EqualTo(SweepBounty));

            Assert.That(ads.TryLaunchSweep(node), Is.True);
            var second = ads.Sweeps().Single().Owner;
            Place(LeadOf(second), CentreOf(AdsHome));
            Server.System<WFEncounterSystem>().Resolve(second, WFEncounterResolution.Destroyed);
            Assert.That(territory.Owner(MapData.MapId, AdsHome)?.Id, Is.EqualTo(Ads), "A node's own cell is not freed by a sweep dying in it.");
        });
    }

    /// <summary>The tier rises at once when held space grows past the next threshold, and matches the score.</summary>
    [Test]
    public async Task TierRisesWithHeldSpace()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            var campaign = prototypes.Index<WFAdsCampaignPrototype>(AdsCampaign);
            var node = FoundNode(AdsHome);
            var state = AdsState();
            var first = state.Tier;
            Assert.That(first, Is.EqualTo(WFAdsCampaignSystem.TierFor(campaign, state.Score)));

            foreach (var cell in WFSectorHex.Ring(AdsHome, 2).Concat(WFSectorHex.Ring(AdsHome, 3)))
            {
                territory.TryClaim(MapData.MapId, cell, Ads, node);
            }

            ads.Recompute();
            Assert.That(state.Tier, Is.GreaterThan(first), "More held space, nearer the core, raises the tier.");
            Assert.That(state.Tier, Is.EqualTo(WFAdsCampaignSystem.TierFor(campaign, state.Score)));
            Assert.That(state.TierAnnounced, Is.EqualTo(STiming.CurTime), "A rise is announced at once.");
        });
    }

    /// <summary>A crewed ship in ADS space past the dwell has the nearest sweep sent to hold beside it and query it, never to attack it, and those aboard are warned.</summary>
    [Test]
    public async Task ShipPastTheDwellIsInterdicted()
    {
        await StartAds();
        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var node = FoundNode(AdsHome);
            ads.SetTier(1);
            Assert.That(ads.TryLaunchSweep(node), Is.True);
            var sweep = ads.Sweeps().Single().Owner;
            var ship = SpawnGrid(CentreOf(AdsHome.Neighbour(1)), "Test Hauler");
            Board(ship);

            ads.CheckInterdicts();
            var state = AdsState();
            Assert.That(state.InsideSince.ContainsKey(ship), Is.True);
            Assert.That(SEntMan.GetComponent<WFAdsSweepComponent>(sweep).Interdicting, Is.Null, "Not before the dwell is up.");

            state.InsideSince[ship] = STiming.CurTime - TimeSpan.FromSeconds(91);
            ads.CheckInterdicts();
            Assert.That(SEntMan.GetComponent<WFAdsSweepComponent>(sweep).Interdicting, Is.EqualTo(ship));
            var orders = Orders(sweep);
            Assert.That(orders.Take(2).Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo, WFCrewObjectiveKind.Hold }));
            Assert.That(orders[0].Range, Is.EqualTo(600f));
            Assert.That(orders[1].Duration, Is.EqualTo(180f), "It holds beside the ship while its zone puts the question.");
            Assert.That(orders.Any(order => order.Kind == WFCrewObjectiveKind.Attack), Is.False, "It is never ordered to attack.");
            Assert.That(ads.Sweeps(), Has.Count.EqualTo(1), "The sweep that was out went; no other was sent.");
        });

        await RunTicks(30);
        Assert.That(ChatSince(since).Any(line => line.Contains("DISARMAMENT DIRECTIVE. ADS interdictor closing on Test Hauler to query it, bearing")), Is.True,
            "Those aboard are warned in chat.");
        await Server.WaitPost(Disembark);
    }
}
