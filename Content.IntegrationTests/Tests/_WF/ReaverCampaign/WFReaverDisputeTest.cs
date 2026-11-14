#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.SectorControl;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>Disputed cells: a player ship sharing a stronghold's cell holds its reinforcement clock, and spread never takes a disputed cell.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverDisputeTest : WFReaverTestBase
{
    /// <summary>A player ship in the hub's cell holds its next reinforcement back past the time it was due.</summary>
    [Test]
    public async Task PlayerShipInTheHubsCellHoldsTheReinforcementClock()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        TimeSpan due = default;
        await Server.WaitAssertion(() =>
        {
            stronghold = FoundAt(Home);
            var comp = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            due = STiming.CurTime + TimeSpan.FromSeconds(3);
            comp.NextReinforcement = due;
            Board(SpawnGrid(CentreOf(Home) + new Vector2(1500f, 0f), "Test Hauler"));
        });

        await RunTicks(270);
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            Assert.That(Server.System<WFSectorTerritorySystem>().IsDisputed(MapData.MapId, Home), Is.True, "The campaign reported the hub and the ship shares its cell.");
            Assert.That(comp.Inbound, Is.Null, "No reinforcement set out while disputed.");
            Assert.That(comp.NextReinforcement, Is.GreaterThan(due + TimeSpan.FromSeconds(3)), "The clock was pushed back, not left to run out.");
            Disembark();
        });
    }

    /// <summary>A disputed hub's sortie cooldown keeps running, so a siege gets a sortie every five minutes and not one.</summary>
    [Test]
    public async Task DisputeDoesNotHoldTheSortieClock()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        TimeSpan due = default;
        await Server.WaitAssertion(() =>
        {
            stronghold = FoundAt(Home);
            var comp = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            due = STiming.CurTime + TimeSpan.FromSeconds(30);
            comp.NextSortie = due;
            Board(SpawnGrid(CentreOf(Home) + new Vector2(1500f, 0f), "Test Hauler"));
        });

        await RunTicks(270);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFSectorTerritorySystem>().IsDisputed(MapData.MapId, Home), Is.True);
            Assert.That(SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).NextSortie, Is.EqualTo(due));
            Disembark();
        });
    }

    /// <summary>Spread skips a disputed frontier cell and takes it once the dispute is over.</summary>
    [Test]
    public async Task SpreadSkipsADisputedCell()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            var contested = new WFSectorCell(6, -2);
            FoundAt(Home);
            Assert.That(WFSectorHex.Distance(Home, contested), Is.EqualTo(2));

            Board(SpawnGrid(CentreOf(contested) + new Vector2(500f, 0f), "Test Hauler"));
            territory.SetPresence(Reavers, map, new[] { contested });
            territory.CheckDisputes(map);
            Assert.That(territory.IsDisputed(map, contested), Is.True);

            // The dispute stamps the cell as flown; clear that so only the dispute keeps spread out.
            territory.Territory(map)!.Activity.Clear();
            for (var i = 0; i < 60; i++)
            {
                campaign.TrySpreadOnce();
            }

            Assert.That(territory.Owner(map, contested), Is.Null, "Spread did not take the disputed cell.");
            Assert.That(territory.ContestOf(map, contested), Is.Null);
            Assert.That(territory.Held(map, Reavers).Count, Is.GreaterThan(7), "It took other cells.");

            Disembark();
            territory.CheckDisputes(map);
            Assert.That(territory.IsDisputed(map, contested), Is.False);
            territory.Territory(map)!.Activity.Clear();
            for (var i = 0; i < 60; i++)
            {
                campaign.TrySpreadOnce();
            }

            Assert.That(territory.Owner(map, contested) != null || territory.ContestOf(map, contested) != null, Is.True,
                "With the dispute over the cell is taken.");
        });
    }
}
