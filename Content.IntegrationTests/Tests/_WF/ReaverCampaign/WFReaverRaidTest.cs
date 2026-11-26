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
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.SectorControl;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>Raids: aimed at crewed ships in or beside held space or at stations, off the budget, one at a time per target.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverRaidTest : WFReaverTestBase
{
    private List<WFCrewObjective> Orders(EntityUid encounter, string key)
    {
        var group = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships[key].Group;
        return Server.System<WFCrewObjectiveSystem>().Snapshot().Single(crew => crew.Group == group).Objectives;
    }

    /// <summary>With no stronghold, no held space or no one to raid, nothing is launched.</summary>
    [Test]
    public async Task NothingIsLaunchedWithoutAStrongholdOrATarget()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            Assert.That(campaign.TryLaunchRaid(), Is.False, "No stronghold.");
            FoundAt(Home);
            Assert.That(campaign.TryLaunchRaid(), Is.False, "Nobody is in or beside held space: the player is at the origin.");
            Assert.That(campaign.State()!.Raids, Is.Zero);
            Assert.That(Raids(), Is.Empty);
        });
    }

    /// <summary>A raid on a player ship goes out from the stronghold, off the budget, with orders for the target, and warns it; the ship is then left alone for a while.</summary>
    [Test]
    public async Task RaidGoesForACrewedShipBesideHeldSpace()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var transform = Server.System<SharedTransformSystem>();
            var stronghold = FoundAt(Home);
            var target = SpawnGrid(CentreOf(Home.Neighbour(1)) + new Vector2(0f, 1500f), "Test Hauler");
            Board(target);

            Assert.That(campaign.TryLaunchRaid(), Is.True);
            var raid = Raids().Single();
            var comp = SEntMan.GetComponent<WFReaverRaidComponent>(raid);
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(raid);
            Assert.That(comp.Target, Is.EqualTo(target));
            Assert.That(comp.Station, Is.False);
            Assert.That(comp.Stronghold, Is.EqualTo(stronghold));
            AssertSpawnedBeside(raid, stronghold);
            Assert.That(encounter.OffBudget, Is.True);
            Assert.That(Server.System<WFEncounterSystem>().ActiveCount(), Is.Zero, "A raid takes no slot from the storyteller.");
            Assert.That(campaign.State()!.Raids, Is.EqualTo(1));
            Assert.That(campaign.State()!.Raided.ContainsKey(target), Is.True);

            var lead = SEntMan.GetComponent<WFEncounterComponent>(stronghold).Ships["lead"].Grid;
            var firstShip = encounter.Ships.Values.First();
            Assert.That(Vector2.Distance(transform.GetWorldPosition(firstShip.Grid), transform.GetWorldPosition(lead)), Is.InRange(590f, 1010f),
                "Launched from the stronghold.");

            if (encounter.Prototype.Id == Pack)
            {
                var orders = Orders(raid, "raider");
                Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo, WFCrewObjectiveKind.Attack }));
                Assert.That(orders[0].Position, Is.EqualTo(transform.GetWorldPosition(target)).Using<Vector2>((a, b) => Vector2.Distance(a, b) < 1f ? 0 : 1));
                Assert.That(orders[1].Target, Is.EqualTo(SEntMan.GetNetEntity(target)));
            }
            else
            {
                Assert.That(encounter.Prototype.Id, Is.EqualTo(Boarders));
                Assert.That(encounter.Ships["boarder"].Prey, Is.EqualTo(target), "The hunters go for this ship, not the nearest to their stronghold.");
            }

            // The same ship is not raided again, and nobody else is there to be.
            Assert.That(campaign.TryLaunchRaid(), Is.False);
            Assert.That(Raids(), Has.Count.EqualTo(1));
        });
    }

    /// <summary>Every third raid goes for a station, prowling round it; a core station only from the fourth tier, and a raided one is left alone.</summary>
    [Test]
    public async Task EveryThirdRaidGoesForAStation()
    {
        await StartCampaign();
        var far = new WFSectorCell(-6, 3);
        var core = new WFSectorCell(1, 0);
        EntityUid farStation = default, coreStation = default;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var transform = Server.System<SharedTransformSystem>();
            var stronghold = FoundAt(Home);
            farStation = SpawnStation(CentreOf(far), "Outpost Theta");
            coreStation = SpawnStation(CentreOf(core), "Core Depot");
            var state = campaign.State()!;

            // Two ships raids go first; the third goes for a station, and the core's is not on the list below tier four.
            state.Raids = 2;
            campaign.SetTier(3);
            Assert.That(campaign.TryLaunchRaid(), Is.True);
            var raid = Raids().Single();
            var comp = SEntMan.GetComponent<WFReaverRaidComponent>(raid);
            Assert.That(comp.Station, Is.True);
            Assert.That(comp.Stronghold, Is.EqualTo(stronghold));
            AssertSpawnedBeside(raid, stronghold);
            Assert.That(comp.Target, Is.EqualTo(farStation), "The core is protected below the fourth tier.");
            Assert.That(state.Raided.ContainsKey(farStation), Is.True);
            Assert.That(state.Raids, Is.EqualTo(3));

            var orders = Orders(raid, "raider");
            Assert.That(orders, Has.Count.InRange(1, 5), "A standoff point, then up to four legs round the station.");
            Assert.That(orders.Select(order => order.Kind), Is.All.EqualTo(WFCrewObjectiveKind.GoTo));
            var at = transform.GetWorldPosition(farStation);
            Assert.That(Vector2.Distance(orders[0].Position, at), Is.InRange(500f, 800f), "The standoff is off the hull.");
            foreach (var order in orders.Skip(1))
            {
                Assert.That(Vector2.Distance(order.Position, at), Is.InRange(500f, 1700f));
            }

            Server.System<WFEncounterSystem>().End(raid);

            // A raided station is left alone: the next station slot has nothing to go for but the ships.
            state.Raids = 5;
            Assert.That(campaign.TryLaunchRaid(), Is.False, "The far station is cooling, the core is out of bounds and no ship is in reach.");

            campaign.SetTier(4);
            Assert.That(campaign.TryLaunchRaid(), Is.True);
            AssertSpawnedBeside(Raids().Single(), stronghold);
            Assert.That(SEntMan.GetComponent<WFReaverRaidComponent>(Raids().Single()).Target, Is.EqualTo(coreStation),
                "From the fourth tier a core station is the target.");
        });
    }
}
