#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.SectorControl;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>Patrols: routed through held space off the storyteller's budget, freeing a cell and paying when killed, recalled to a stronghold under attack.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverPatrolTest : WFReaverTestBase
{
    private List<WFCrewObjective> Orders(EntityUid encounter, string key)
    {
        var group = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships[key].Group;
        return Server.System<WFCrewObjectiveSystem>().Snapshot().Single(crew => crew.Group == group).Objectives;
    }

    /// <summary>A patrol is spawned near its stronghold, routed through held space and home, and takes no storyteller slot.</summary>
    [Test]
    public async Task PatrolIsRoutedThroughHeldSpaceAndHome()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var transform = Server.System<SharedTransformSystem>();
            var stronghold = FoundAt(Home);
            var strongholdComp = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            var strongholdLead = transform.GetWorldPosition(SEntMan.GetComponent<WFEncounterComponent>(stronghold).Ships["lead"].Grid);

            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.True);
            var (patrol, comp, encounter) = campaign.Patrols().Single();
            Assert.That(comp.Stronghold, Is.EqualTo(stronghold));
            AssertSpawnedBeside(patrol, stronghold);
            Assert.That(encounter.Lifetime, Is.EqualTo(WFEncounterLifetime.Transient));
            Assert.That(encounter.OffBudget, Is.True, "A campaign patrol takes no slot from the storyteller.");
            Assert.That(Server.System<WFEncounterSystem>().ActiveCount(), Is.Zero);
            Assert.That(strongholdComp.NextPatrol, Is.GreaterThanOrEqualTo(STiming.CurTime + TimeSpan.FromSeconds(5)), "The stronghold waits before the next.");

            Assert.That(Vector2.Distance(comp.Home, strongholdLead), Is.InRange(590f, 1010f), "Spawned 600 to 1000 m from its stronghold.");
            Assert.That(comp.Route.Count, Is.InRange(1, 5));
            var centres = territory.Held(MapData.MapId, Reavers).Select(cell => CentreOf(cell)).ToList();
            foreach (var point in comp.Route)
            {
                Assert.That(centres.Any(centre => Vector2.Distance(centre, point) <= 1500f), Is.True, "Each call is in or beside held space.");
            }

            var lead = encounter.Ships["lead"];
            Assert.That(lead.HasOrders, Is.True);
            Assert.That(lead.Flown, Is.False);
            var orders = Orders(patrol, "lead");
            Assert.That(orders.Select(order => order.Kind), Is.All.EqualTo(WFCrewObjectiveKind.GoTo));
            Assert.That(orders.Select(order => order.Position), Is.EqualTo(comp.Route.Append(comp.Home)), "The route, then home.");

            Assert.That(campaign.TryLaunchPatrol(patrol), Is.False, "Only a stronghold sends patrols.");
        });
    }

    /// <summary>A patrol killed in a cell frees it, unless a stronghold stands there, and pays those who shot it; one that finished its route does neither.</summary>
    [Test]
    public async Task KilledPatrolFreesItsCellAndPaysItsBounty()
    {
        await StartCampaign();
        var neighbour = Home.Neighbour(1);
        EntityUid stronghold = default, ship = default;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            stronghold = FoundAt(Home);
            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.True);
            var patrol = campaign.Patrols().Single().Owner;
            AssertSpawnedBeside(patrol, stronghold);
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(patrol);

            // It dies one cell over from the stronghold's.
            Place(encounter.Ships["lead"].Grid, CentreOf(neighbour));
            ShootAt(patrol, "lead", ship);
            var before = Balance();
            Assert.That(territory.Owner(MapData.MapId, neighbour)?.Id, Is.EqualTo(Reavers));

            Server.System<WFEncounterSystem>().Resolve(patrol, WFEncounterResolution.Destroyed);
            Assert.That(territory.Owner(MapData.MapId, neighbour), Is.Null, "The cell it died in is clear.");
            Assert.That(territory.Held(MapData.MapId, Reavers), Has.Count.EqualTo(6));
            Assert.That(campaign.State()!.PatrolsDestroyed, Is.EqualTo(1));
            Assert.That(Balance() - before, Is.EqualTo(PatrolBounty), "Those who shot it are paid.");

            // Another dies at the stronghold's own door: the cell stays, and nobody shot it.
            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.True);
            before = Balance();
            AssertSpawnedBeside(campaign.Patrols().Single().Owner, stronghold);
            Server.System<WFEncounterSystem>().Resolve(campaign.Patrols().Single().Owner, WFEncounterResolution.Destroyed);
            Assert.That(territory.Owner(MapData.MapId, Home)?.Id, Is.EqualTo(Reavers), "A stronghold's cell is not freed by a patrol dying in it.");
            Assert.That(campaign.State()!.PatrolsDestroyed, Is.EqualTo(2));
            Assert.That(Balance(), Is.EqualTo(before), "Nobody to pay.");

            // One that flew its route home was not killed.
            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.True);
            Server.System<WFEncounterSystem>().Resolve(campaign.Patrols().Single().Owner, WFEncounterResolution.Completed);
            Assert.That(campaign.State()!.PatrolsDestroyed, Is.EqualTo(2));
            Assert.That(territory.Held(MapData.MapId, Reavers), Has.Count.EqualTo(6));
        });
    }

    /// <summary>A bounty with credits for two navies hands each helper only the credits of their own navy.</summary>
    [Test]
    public async Task BountyPaysEachNavyItsOwnCredits()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var rewards = Server.System<WFEncounterRewardSystem>();
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            var stronghold = FoundAt(Home);
            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.True);
            var patrol = campaign.Patrols().Single().Owner;
            ShootAt(patrol, "lead", ship);

            var player = SEntMan.GetEntity(Player);
            var company = SEntMan.EnsureComponent<CompanyComponent>(player);
            var main = new WFEncounterReward
            {
                Spesos = PatrolBounty,
                Credits = 7,
                CreditEntity = "FederationMilitaryCredit1",
                CreditCompanies = new List<string> { "TSF", "TSFCivilian" },
            };
            var extra = new List<WFEncounterReward>
            {
                new() { Credits = 9, CreditEntity = "Doubloon1", CreditCompanies = new List<string> { "PDV", "PDVCivilian" } },
            };

            company.CompanyName = "TSF";
            Assert.That(rewards.PayBounty(patrol, main, "a test", null, extra), Is.EqualTo(1));
            Assert.That(Credits("FederationMilitaryCredit1"), Is.EqualTo(7), "A TSF helper gets TSF credits.");
            Assert.That(Credits("Doubloon1"), Is.Zero, "And no doubloons.");

            company.CompanyName = "PDV";
            Assert.That(rewards.PayBounty(patrol, main, "a test", null, extra), Is.EqualTo(1));
            Assert.That(Credits("FederationMilitaryCredit1"), Is.EqualTo(7), "A PDV helper gets no TSF credits.");
            Assert.That(Credits("Doubloon1"), Is.EqualTo(9), "A PDV helper gets doubloons.");

            company.CompanyName = "None";
            var before = Balance();
            Assert.That(rewards.PayBounty(patrol, main, "a test", null, extra), Is.EqualTo(1), "Spesos still go to anyone.");
            Assert.That(Balance() - before, Is.EqualTo(PatrolBounty));
            Assert.That(Credits("FederationMilitaryCredit1"), Is.EqualTo(7));
            Assert.That(Credits("Doubloon1"), Is.EqualTo(9));
        });
    }

    /// <summary>The credits in the world of one stack prototype.</summary>
    private int Credits(string prototype)
    {
        var total = 0;
        var query = SEntMan.EntityQueryEnumerator<StackComponent, MetaDataComponent>();
        while (query.MoveNext(out _, out var stack, out var meta))
        {
            if (meta.EntityPrototype?.ID == prototype)
                total += stack.Count;
        }

        return total;
    }

    /// <summary>Once a stronghold is shot at, the nearest patrol within a cell of it is called back to circle it.</summary>
    [Test]
    public async Task PatrolIsRecalledToAStrongholdUnderAttack()
    {
        await StartCampaign();
        EntityUid stronghold = default, patrol = default;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            stronghold = FoundAt(Home);
            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.True);
            patrol = campaign.Patrols().Single().Owner;
            Assert.That(SEntMan.GetComponent<WFReaverPatrolComponent>(patrol).Recalled, Is.False);
            ShootAt(stronghold, "lead", ship);
        });
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WFReaverPatrolComponent>(patrol).Recalled, Is.True);
            Assert.That(SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).Recalled, Is.True);
            var orders = Orders(patrol, "lead");
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo, WFCrewObjectiveKind.Circle }));
            var lead = SEntMan.GetComponent<WFEncounterComponent>(stronghold).Ships["lead"].Grid;
            Assert.That(orders[1].Target, Is.EqualTo(SEntMan.GetNetEntity(lead)), "It circles the stronghold's lead.");
        });
    }
}
