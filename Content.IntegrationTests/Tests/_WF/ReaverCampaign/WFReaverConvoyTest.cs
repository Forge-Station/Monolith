#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>Founding convoys: a later stronghold flies out from a standing one, founds itself on arrival and founds nothing if stopped.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverConvoyTest : WFReaverTestBase
{
    private List<WFCrewObjective> Orders(EntityUid encounter, string key)
    {
        var group = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships[key].Group;
        return Server.System<WFCrewObjectiveSystem>().Snapshot().Single(crew => crew.Group == group).Objectives;
    }

    /// <summary>Founds the stronghold at <see cref="WFReaverTestBase.Home"/>, takes one cell beyond its ring and sends a convoy there.</summary>
    private (EntityUid Stronghold, EntityUid Convoy, WFSectorCell Target) Launch()
    {
        var campaign = Server.System<WFReaverCampaignSystem>();
        var territory = Server.System<WFSectorTerritorySystem>();
        var stronghold = FoundAt(Home);
        Assert.That(campaign.TrySpreadOnce(), Is.True);
        var target = territory.Held(MapData.MapId, Reavers).Single(cell => WFSectorHex.Distance(cell, Home) == 2);
        Assert.That(campaign.TryLaunchConvoy(out var convoy), Is.True);
        return (stronghold, convoy, target);
    }

    /// <summary>A convoy spawns beside the stronghold nearest its target, flies there, contests the cell meanwhile and is announced.</summary>
    [Test]
    public async Task ConvoyLaunchesBesideItsSourceAndContestsTheTarget()
    {
        await StartCampaign();
        var since = ChatCount();
        var callsign = string.Empty;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            var (stronghold, convoy, target) = Launch();
            callsign = WFSectorHex.Callsign(target);

            var comp = SEntMan.GetComponent<WFReaverConvoyComponent>(convoy);
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(convoy);
            Assert.That(comp.Target, Is.EqualTo(target));
            Assert.That(comp.Source, Is.EqualTo(stronghold));
            Assert.That(comp.Lead, Is.EqualTo("lead"));
            Assert.That(comp.Launched, Is.EqualTo(STiming.CurTime));
            Assert.That(encounter.Prototype.Id, Is.EqualTo(Stronghold), "The stronghold it will be.");
            Assert.That(encounter.Leash, Is.Zero, "No leash turns it back under way.");
            Assert.That(campaign.ShieldOf(encounter.Ships["lead"].Grid), Is.Not.Null, "A hub sets out shielded.");
            AssertSpawnedBeside(convoy, stronghold);

            Assert.That(SEntMan.HasComponent<WFReaverStrongholdComponent>(convoy), Is.False, "Under way it is no stronghold yet.");
            Assert.That(campaign.Strongholds().Select(entry => entry.Owner), Is.EqualTo(new[] { stronghold }));
            Assert.That(campaign.Convoys().Select(entry => entry.Owner), Is.EqualTo(new[] { convoy }));
            Assert.That(campaign.State()!.Founded, Is.EqualTo(1), "Founding counts on arrival.");

            Assert.That(Vector2.Distance(comp.TargetPosition, CentreOf(target)), Is.LessThanOrEqualTo(1500f));
            var lead = Orders(convoy, "lead").Single();
            Assert.That(lead.Kind, Is.EqualTo(WFCrewObjectiveKind.GoTo));
            Assert.That(lead.Position, Is.EqualTo(comp.TargetPosition));
            Assert.That(lead.Range, Is.EqualTo(300f));

            var contest = territory.ContestOf(map, target);
            Assert.That(contest, Is.Not.Null, "The target is shown contested.");
            Assert.That(contest!.Faction.Id, Is.EqualTo(Reavers));
            Assert.That(contest.Source, Is.EqualTo(convoy));
            Assert.That(contest.Deadline, Is.GreaterThan(STiming.CurTime + TimeSpan.FromMinutes(3)), "Past its arrival, with slack.");
            Assert.That(territory.Owner(map, target)?.Id, Is.EqualTo(Reavers), "The cell stays held meanwhile.");

            // Still under way, a check keeps the contest and founds nothing.
            campaign.CheckConvoys();
            Assert.That(campaign.Convoys(), Has.Count.EqualTo(1));
            Assert.That(territory.ContestOf(map, target)?.Source, Is.EqualTo(convoy));
        });

        await RunTicks(30);
        Assert.That(ChatSince(since).Any(line => line.Contains($"A Reaver convoy is under way to {callsign},")), Is.True,
            "The sector watch announces it.");
    }

    /// <summary>A convoy whose lead reaches its target becomes the stronghold: the cell and ring are claimed, the contest goes and the hub holds.</summary>
    [Test]
    public async Task ArrivedConvoyFoundsTheStronghold()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            var (_, convoy, target) = Launch();
            var position = SEntMan.GetComponent<WFReaverConvoyComponent>(convoy).TargetPosition;
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(convoy);
            var leadGrid = encounter.Ships["lead"].Grid;
            var tier = campaign.State()!.Tier;

            Place(leadGrid, position + new Vector2(150f, 0f));
            campaign.CheckConvoys();

            Assert.That(SEntMan.HasComponent<WFReaverConvoyComponent>(convoy), Is.False);
            var stronghold = SEntMan.GetComponent<WFReaverStrongholdComponent>(convoy);
            Assert.That(stronghold.Cell, Is.EqualTo(target));
            Assert.That(stronghold.Lead, Is.EqualTo("lead"));
            Assert.That(stronghold.Tier, Is.EqualTo(tier));
            Assert.That(stronghold.NextPatrol, Is.GreaterThan(STiming.CurTime));
            Assert.That(campaign.Strongholds(), Has.Count.EqualTo(2));
            Assert.That(campaign.Convoys(), Is.Empty);
            Assert.That(encounter.Resolution, Is.Null);
            Assert.That(encounter.Leash, Is.EqualTo(2000f), "Its station leashes it again.");
            Assert.That(encounter.Origin.Position, Is.EqualTo(position));

            Assert.That(territory.ContestOf(map, target), Is.Null, "The contest is withdrawn.");
            Assert.That(territory.Sources(map, target), Does.Contain(convoy));
            foreach (var cell in WFSectorHex.Ring(target, 1))
            {
                if (WFSectorHex.Distance(cell, WFSectorCell.Origin) > WFSectorProtectionSystem.CoreRadius)
                    Assert.That(territory.Owner(map, cell)?.Id, Is.EqualTo(Reavers), WFSectorHex.Callsign(cell));
            }

            Assert.That(Orders(convoy, "lead").Single().Kind, Is.EqualTo(WFCrewObjectiveKind.Hold));
            var state = campaign.State()!;
            Assert.That(state.Founded, Is.EqualTo(2));
            Assert.That(state.LastFounded, Is.EqualTo(STiming.CurTime), "The founding interval runs from the arrival.");
        });
    }

    /// <summary>A convoy whose lead is out of the fight is stopped: it founds nothing, drops its contest and pays half a stronghold's bounty.</summary>
    [Test]
    public async Task DestroyedConvoyFoundsNothingAndPaysHalfABounty()
    {
        await StartCampaign();
        EntityUid convoy = default;
        WFSectorCell target = default;
        var before = 0;
        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            (_, convoy, target) = Launch();
            campaign.SetTier(3);
            ShootAt(convoy, "lead", ship);
            before = Balance();

            KillCrew(convoy, "lead");
            campaign.CheckConvoys();

            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(convoy).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
            Assert.That(campaign.Convoys(), Is.Empty);
            Assert.That(campaign.Strongholds(), Has.Count.EqualTo(1), "Nothing is founded.");
            Assert.That(SEntMan.HasComponent<WFReaverStrongholdComponent>(convoy), Is.False);
            Assert.That(territory.ContestOf(map, target), Is.Null, "The contest is withdrawn.");
            Assert.That(territory.Owner(map, target)?.Id, Is.EqualTo(Reavers), "The source stronghold still holds the cell.");
            var state = campaign.State()!;
            Assert.That(state.Founded, Is.EqualTo(1));
            Assert.That(state.ConvoysDestroyed, Is.EqualTo(1));
        });

        await RunTicks(30);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Balance() - before, Is.EqualTo(Bounties[3] / 2), "Half the stronghold bounty of the tier now.");
        });
        Assert.That(ChatSince(since).Any(line => line.Contains($"The Reaver convoy bound for {WFSectorHex.Callsign(target)} is destroyed. Bounty")),
            Is.True, "The sector watch says it was stopped and paid for.");
    }

    /// <summary>The campaign sends a convoy for a later founding rather than founding at once, and starts no other founding while it is out.</summary>
    [Test]
    public async Task CampaignSendsOneConvoyAtATime()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            stronghold = FoundAt(Home);
            for (var i = 0; i < 12; i++)
            {
                campaign.TrySpreadOnce();
            }

            campaign.State()!.LastFounded = STiming.CurTime - TimeSpan.FromHours(1);
        });

        await WaitUntilServer(() => Server.System<WFReaverCampaignSystem>().Convoys().Count > 0, 300);
        EntityUid convoy = default;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            convoy = campaign.Convoys().Single().Owner;
            Assert.That(campaign.Strongholds().Select(entry => entry.Owner), Is.EqualTo(new[] { stronghold }), "Sent out, not founded at once.");
            Assert.That(SEntMan.GetComponent<WFReaverConvoyComponent>(convoy).Source, Is.EqualTo(stronghold));
            Assert.That(campaign.TryLaunchConvoy(out _), Is.False, "One convoy at a time.");
            campaign.State()!.LastFounded = STiming.CurTime - TimeSpan.FromHours(1);
        });

        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            Assert.That(campaign.Convoys().Select(entry => entry.Owner), Is.EqualTo(new[] { convoy }), "No other founding starts while it is out.");
            Assert.That(campaign.Strongholds(), Has.Count.EqualTo(1));
            Assert.That(campaign.State()!.Founded, Is.EqualTo(1));
        });
    }

    /// <summary>A convoy whose lead sits short of its cell past its time is ended, so it stops blocking the next founding.</summary>
    [Test]
    public async Task OverdueConvoyShortOfItsCellIsEnded()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var (_, convoy, target) = Launch();
            var comp = SEntMan.GetComponent<WFReaverConvoyComponent>(convoy);
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(convoy);
            Assert.That(comp.Overdue, Is.GreaterThan(STiming.CurTime + TimeSpan.FromMinutes(6)), "Twice the reckoned trip, plus slack.");

            Place(encounter.Ships["lead"].Grid, comp.TargetPosition + new Vector2(20000f, 0f));
            campaign.CheckConvoys();
            Assert.That(campaign.Convoys(), Has.Count.EqualTo(1), "Not overdue yet.");

            comp.Overdue = STiming.CurTime - TimeSpan.FromSeconds(1);
            campaign.CheckConvoys();
            Assert.That(encounter.Resolution, Is.EqualTo(WFEncounterResolution.Ended));
            Assert.That(campaign.Convoys(), Is.Empty, "The one-at-a-time gate is open again.");
            Assert.That(campaign.Strongholds(), Has.Count.EqualTo(1));
            Assert.That(territory.ContestOf(MapData.MapId, target), Is.Null, "The contest is withdrawn.");
            var state = campaign.State()!;
            Assert.That(state.Founded, Is.EqualTo(1));
            Assert.That(state.ConvoysDestroyed, Is.Zero, "Stopped by time, not by players.");
        });
    }

    /// <summary>An overdue lead that stopped inside the target cell founds the stronghold where it stands.</summary>
    [Test]
    public async Task OverdueConvoyInItsCellFoundsWhereItStands()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var (_, convoy, target) = Launch();
            var comp = SEntMan.GetComponent<WFReaverConvoyComponent>(convoy);
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(convoy);
            var stopped = Vector2.Zero;
            foreach (var offset in new[] { new Vector2(900f, 0f), new Vector2(-900f, 0f), new Vector2(0f, 900f), new Vector2(0f, -900f), new Vector2(1500f, 0f), new Vector2(-1500f, 0f) })
            {
                var candidate = comp.TargetPosition + offset;
                if (territory.CellOf(new MapCoordinates(candidate, MapData.MapId)) == target)
                {
                    stopped = candidate;
                    break;
                }
            }

            Assert.That(stopped, Is.Not.EqualTo(Vector2.Zero), "A spot in the cell, beyond arrival range.");
            Place(encounter.Ships["lead"].Grid, stopped);
            campaign.CheckConvoys();
            Assert.That(campaign.Convoys(), Has.Count.EqualTo(1), "Short of its point and not overdue, it flies on.");

            comp.Overdue = STiming.CurTime - TimeSpan.FromSeconds(1);
            campaign.CheckConvoys();
            Assert.That(SEntMan.HasComponent<WFReaverConvoyComponent>(convoy), Is.False);
            var stronghold = SEntMan.GetComponent<WFReaverStrongholdComponent>(convoy);
            Assert.That(stronghold.Cell, Is.EqualTo(target));
            Assert.That(encounter.Origin.Position, Is.EqualTo(stopped), "Its station is where it stands.");
            Assert.That(encounter.Resolution, Is.Null);
        });
    }

    /// <summary>A lead whose crew gave up the flight is sent again, not taken to have arrived anywhere.</summary>
    [Test]
    public async Task ConvoyThatGaveUpTheFlightIsSentAgain()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var (_, convoy, _) = Launch();
            var comp = SEntMan.GetComponent<WFReaverConvoyComponent>(convoy);
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(convoy);
            Place(encounter.Ships["lead"].Grid, comp.TargetPosition + new Vector2(20000f, 0f));
            encounter.Ships["lead"].Flown = true;

            campaign.CheckConvoys();
            Assert.That(SEntMan.HasComponent<WFReaverConvoyComponent>(convoy), Is.True, "It founds nothing short of its cell.");
            Assert.That(encounter.Ships["lead"].Flown, Is.False);
            var lead = Orders(convoy, "lead").Single();
            Assert.That(lead.Kind, Is.EqualTo(WFCrewObjectiveKind.GoTo));
            Assert.That(lead.Position, Is.EqualTo(comp.TargetPosition));
        });
    }

    /// <summary>Hits a convoy took on its way are carried into the stronghold, not seen as a fresh attack on it.</summary>
    [Test]
    public async Task HitsOnTheWayAreNotAFreshAttack()
    {
        await StartCampaign();
        EntityUid convoy = default, ship = default;
        var hits = 0;
        var lastHit = TimeSpan.Zero;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            (_, convoy, _) = Launch();
            ShootAt(convoy, "lead", ship);
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(convoy);
            hits = encounter.ShipHits.Values.Sum();
            Assert.That(hits, Is.GreaterThan(0));

            Place(encounter.Ships["lead"].Grid, SEntMan.GetComponent<WFReaverConvoyComponent>(convoy).TargetPosition);
            campaign.CheckConvoys();
            var stronghold = SEntMan.GetComponent<WFReaverStrongholdComponent>(convoy);
            lastHit = stronghold.LastHit;
            Assert.That(stronghold.Hits, Is.EqualTo(hits));
            Assert.That(stronghold.AttackerHits[ship], Is.EqualTo(hits));
            Assert.That(stronghold.Recalled, Is.True, "No patrol is called back for hits taken before it stood.");
            Assert.That(campaign.IsMarked(ship), Is.True, "The ship that shot the convoy is marked once.");
            Assert.That(STiming.CurTime - lastHit, Is.GreaterThan(TimeSpan.FromSeconds(120)), "Not within the support window.");
        });

        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            var stronghold = SEntMan.GetComponent<WFReaverStrongholdComponent>(convoy);
            Assert.That(stronghold.Hits, Is.EqualTo(hits));
            Assert.That(stronghold.LastHit, Is.EqualTo(lastHit), "No fresh hit was seen.");
            Assert.That(stronghold.Support, Is.Empty);
        });
    }

    /// <summary>A new cell size re-aims a convoy at the cell its point is in now.</summary>
    [Test]
    public async Task CellSizeChangeReaimsTheConvoy()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var config = Server.ResolveDependency<IConfigurationManager>();
            var map = MapData.MapId;
            var (_, convoy, target) = Launch();
            var comp = SEntMan.GetComponent<WFReaverConvoyComponent>(convoy);

            config.SetCVar(SectorControlCVars.CellSize, 2000f);
            Assert.That(comp.Target, Is.EqualTo(territory.CellOf(new MapCoordinates(comp.TargetPosition, map))));
            Assert.That(comp.Target, Is.Not.EqualTo(target), "The old cell is a different place now.");
            campaign.CheckConvoys();
            Assert.That(territory.ContestOf(map, comp.Target)?.Source, Is.EqualTo(convoy), "The new cell is contested.");

            config.SetCVar(SectorControlCVars.CellSize, SectorControlCVars.CellSize.DefaultValue);
            Assert.That(comp.Target, Is.EqualTo(territory.CellOf(new MapCoordinates(comp.TargetPosition, map))));
        });
    }

    /// <summary>With every stronghold gone and a convoy out, status says the founding waits for it.</summary>
    [Test]
    public async Task StatusSaysTheFoundingWaitsForTheConvoy()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var (stronghold, _, _) = Launch();
            SEntMan.System<Content.Server._WF.Encounters.Systems.WFEncounterSystem>().Resolve(stronghold, WFEncounterResolution.Destroyed);
            Assert.That(campaign.Strongholds(), Is.Empty);
            Assert.That(campaign.Convoys(), Has.Count.EqualTo(1));
            Server.ConsoleHost.ExecuteCommand("wf_reavers status");
        });
    }

    /// <summary>The admin command sends a convoy and lists it; found still places one at once.</summary>
    [Test]
    public async Task CommandSendsAConvoy()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var console = Server.ConsoleHost;
            var campaign = Server.System<WFReaverCampaignSystem>();
            FoundAt(Home);
            console.ExecuteCommand("wf_reavers convoy");
            Assert.That(campaign.Convoys(), Is.Empty, "No held cell is two from the stronghold yet.");

            Assert.That(campaign.TrySpreadOnce(), Is.True);
            console.ExecuteCommand("wf_reavers convoy");
            Assert.That(campaign.Convoys(), Has.Count.EqualTo(1));
            Assert.That(campaign.Strongholds(), Has.Count.EqualTo(1));
            console.ExecuteCommand("wf_reavers status");

            console.ExecuteCommand("wf_reavers clear");
            Assert.That(campaign.Convoys(), Is.Empty, "Clearing ends convoys too.");
        });
    }
}
