#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.AdsIncursion.Components;
using Content.Server._WF.AdsIncursion.Systems;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.SectorControl;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.AdsIncursion;

/// <summary>The sector conflict: ADS challenges on Reaver cells, won, lost and dropped, and the Reavers' response packs.</summary>
[TestOf(typeof(WFAdsCampaignSystem))]
public sealed class WFAdsChallengeTest : WFAdsTestBase
{
    /// <summary>A Reaver cell two out from the ADS node: adjacent to ADS space, away from the core.</summary>
    private static readonly WFSectorCell Rival = FarCell(AdsHome, 2);

    /// <summary>Founds a node, gives the Reavers a cell beside ADS space and has the campaign challenge it by its own rules.</summary>
    private EntityUid Launch(out EntityUid node)
    {
        var ads = Server.System<WFAdsCampaignSystem>();
        node = FoundNode(AdsHome);
        ClaimForReavers(Rival);
        ads.SetTier(2);
        Assert.That(ads.TryChallenge(out var interdictor), Is.True);
        return interdictor;
    }

    /// <summary>Flies an interdictor's lead to its cell's centre and has the campaign see it arrive.</summary>
    private void Arrive(EntityUid interdictor)
    {
        Place(LeadOf(interdictor), CentreOf(SEntMan.GetComponent<WFAdsInterdictorComponent>(interdictor).Target));
        Server.System<WFAdsCampaignSystem>().CheckChallenges();
        Assert.That(SEntMan.GetComponent<WFAdsInterdictorComponent>(interdictor).Arrived, Is.True);
        Assert.That(Orders(interdictor).Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.Hold }), "Arrived, it holds.");
    }

    /// <summary>An interdictor is sent to a Reaver cell beside ADS space; with no Reaver ship there at the deadline, the cell is the ADS's and the interdictor stays as its picket.</summary>
    [Test]
    public async Task ChallengeWonByAbsenceLeavesAPicket()
    {
        await StartAds();
        EntityUid interdictor = default, node = default;
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var transform = Server.System<SharedTransformSystem>();
            var map = MapData.MapId;
            interdictor = Launch(out node);
            var comp = SEntMan.GetComponent<WFAdsInterdictorComponent>(interdictor);
            Assert.That(comp.Target, Is.EqualTo(Rival));
            Assert.That(comp.Holder?.Id, Is.EqualTo(Reavers));
            Assert.That(comp.Node, Is.EqualTo(node));
            Assert.That(Vector2.Distance(transform.GetWorldPosition(LeadOf(interdictor)), transform.GetWorldPosition(LeadOf(node))),
                Is.LessThanOrEqualTo(SpawnReach), "It sets out beside its node.");
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(interdictor).OffBudget, Is.True);

            var contest = territory.ContestOf(map, Rival)!;
            Assert.That(contest.Faction.Id, Is.EqualTo(Ads));
            Assert.That(contest.Holder?.Id, Is.EqualTo(Reavers));
            Assert.That(contest.Source, Is.EqualTo(interdictor));
            var orders = Orders(interdictor);
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo }));
            Assert.That(orders[0].Range, Is.EqualTo(300f));
            Assert.That(Vector2.Distance(orders[0].Position, CentreOf(Rival)), Is.LessThan(1f));
            Assert.That(AdsState().Challenges, Is.EqualTo(1));

            Arrive(interdictor);
        });

        // Let the campaign report the interdictor in its cell before the challenge is decided.
        await RunTicks(60);
        await Server.WaitPost(() => Server.System<WFSectorTerritorySystem>().SetContestDeadline(MapData.MapId, Rival, STiming.CurTime));
        await RunTicks(120);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            Assert.That(territory.Owner(map, Rival)?.Id, Is.EqualTo(Ads), "The cell went to the ADS.");
            Assert.That(territory.ContestOf(map, Rival), Is.Null);
            Assert.That(territory.Sources(map, Rival), Is.EquivalentTo(new[] { interdictor }), "The picket holds it.");
            Assert.That(SEntMan.HasComponent<WFAdsInterdictorComponent>(interdictor), Is.False);
            var picket = SEntMan.GetComponent<WFAdsPicketComponent>(interdictor);
            Assert.That(picket.Cell, Is.EqualTo(Rival));
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(interdictor).Resolution, Is.Null);
            Assert.That(AdsState().ChallengesWon, Is.EqualTo(1));

            // It dies, and the cell goes with it.
            Server.System<WFEncounterSystem>().Resolve(interdictor, WFEncounterResolution.Destroyed);
            Assert.That(territory.Owner(map, Rival), Is.Null);
        });
    }

    /// <summary>A Reaver ship in the cell at the deadline keeps it Reaver; the interdictor withdraws.</summary>
    [Test]
    public async Task ChallengeLostByPresenceWithdraws()
    {
        await StartAds();
        EntityUid interdictor = default;
        await Server.WaitAssertion(() =>
        {
            interdictor = Launch(out _);
            Arrive(interdictor);
        });

        await RunTicks(60);
        await Server.WaitPost(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            territory.SetPresence(Reavers, MapData.MapId, new[] { Rival });
            territory.SetContestDeadline(MapData.MapId, Rival, STiming.CurTime);
        });
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            Assert.That(territory.Owner(MapData.MapId, Rival)?.Id, Is.EqualTo(Reavers), "The Reavers keep the cell.");
            Assert.That(territory.ContestOf(MapData.MapId, Rival), Is.Null);
            Assert.That(!SEntMan.EntityExists(interdictor)
                || SEntMan.GetComponent<WFEncounterComponent>(interdictor).Resolution == WFEncounterResolution.Completed, Is.True,
                "The interdictor withdraws.");
            Assert.That(Server.System<WFAdsCampaignSystem>().Interdictors(), Is.Empty);
            Assert.That(Server.System<WFAdsCampaignSystem>().Pickets(), Is.Empty);
            var state = AdsState();
            Assert.That(state.ChallengesWon, Is.Zero);
            Assert.That(state.ChallengesLost, Is.EqualTo(1));
        });
    }

    /// <summary>An interdictor out of the fight drops its challenge and pays its bounty to those who hit it.</summary>
    [Test]
    public async Task ChallengeDroppedWhenTheInterdictorDies()
    {
        await StartAds();
        EntityUid interdictor = default;
        var before = 0;
        await Server.WaitAssertion(() =>
        {
            var ship = SpawnGrid(CentreOf(AdsHome) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            interdictor = Launch(out _);
            ShootAt(interdictor, "lead", ship);
            before = Balance();
            KillCrew(interdictor, "lead");
        });
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(interdictor).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
            Assert.That(territory.ContestOf(MapData.MapId, Rival), Is.Null, "The challenge is dropped.");
            Assert.That(territory.Owner(MapData.MapId, Rival)?.Id, Is.EqualTo(Reavers));
            Assert.That(Balance() - before, Is.EqualTo(InterdictorBounty));
        });
    }

    /// <summary>A challenge on a cell next to a Reaver stronghold scrambles a response pack at the interdictor; once the cell is lost it turns for home and the loss is counted.</summary>
    [Test]
    public async Task ReaverResponsePackAttacksAndGoesHome()
    {
        await StartAds(reavers: true);
        var target = FarCell(Home, 1);
        EntityUid interdictor = default, pack = default, stronghold = default;
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var reavers = Server.System<WFReaverCampaignSystem>();
            Assert.That(reavers.Running, Is.True, "The Reavers run beside the ADS.");
            stronghold = FoundAt(Home);
            FoundNode(AdsHome);
            Assert.That(ads.TryChallenge(Home, out _), Is.False, "A stronghold's own cell is never challenged.");
            Assert.That(ads.TryChallenge(target, out interdictor), Is.True);

            var (uid, response, _) = reavers.Responses().Single();
            pack = uid;
            Assert.That(response.Cell, Is.EqualTo(target));
            Assert.That(response.Target, Is.EqualTo(interdictor));
            Assert.That(response.Stronghold, Is.EqualTo(stronghold));
            var orders = Orders(pack, "raider");
            Assert.That(orders.Select(order => order.Kind),
                Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo, WFCrewObjectiveKind.Attack, WFCrewObjectiveKind.GoTo }));
            Assert.That(Vector2.Distance(orders[0].Position, CentreOf(target)), Is.LessThan(1f));
            Assert.That(orders[0].Range, Is.EqualTo(600f));
            Assert.That(orders[1].Target, Is.EqualTo(SEntMan.GetNetEntity(LeadOf(interdictor))), "It goes for the interdictor.");
            Assert.That(orders[2].Position, Is.EqualTo(response.Home), "Then home.");

            // A second contested event for the same cell sends nobody else.
            Assert.That(reavers.TryRespond(target, out _), Is.False);
            Arrive(interdictor);
        });

        await RunTicks(60);
        await Server.WaitPost(() => Server.System<WFSectorTerritorySystem>().SetContestDeadline(MapData.MapId, target, STiming.CurTime));
        await RunTicks(120);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var reavers = Server.System<WFReaverCampaignSystem>();
            Assert.That(territory.Owner(MapData.MapId, target)?.Id, Is.EqualTo(Ads), "The pack never reached the cell, so the ADS took it.");
            Assert.That(territory.Owner(MapData.MapId, Home)?.Id, Is.EqualTo(Reavers), "The stronghold keeps its other cells.");
            var response = SEntMan.GetComponent<WFReaverResponseComponent>(pack);
            Assert.That(response.Returning, Is.True);
            var orders = Orders(pack, "raider");
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo }));
            Assert.That(orders[0].Position, Is.EqualTo(response.Home), "It turns for home.");
            Assert.That(reavers.State()!.CellsLost, Is.EqualTo(1));
        });
    }
}
