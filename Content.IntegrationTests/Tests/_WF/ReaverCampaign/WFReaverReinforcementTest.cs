#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server.Shuttles.Systems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.SectorControl;
using Content.Server.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>Stronghold levels: a shielded hub with no guards, reinforcement convoys that dock to raise its level, and the hub breaking them apart under fire.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverReinforcementTest : WFReaverTestBase
{
    private List<WFCrewObjective> Orders(EntityUid encounter, string key)
    {
        var group = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships[key].Group;
        return Server.System<WFCrewObjectiveSystem>().Snapshot().Single(crew => crew.Group == group).Objectives;
    }

    private EntityUid HubGrid(EntityUid stronghold)
    {
        return SEntMan.GetComponent<WFEncounterComponent>(stronghold).Ships["lead"].Grid;
    }

    /// <summary>The centre of a floor tile of a grid.</summary>
    private EntityCoordinates AnyTile(EntityUid grid)
    {
        var maps = Server.System<SharedMapSystem>();
        var gridComp = SEntMan.GetComponent<MapGridComponent>(grid);
        return maps.GridTileToLocal(grid, gridComp, maps.GetAllTiles(grid, gridComp).First(tile => !tile.Tile.IsEmpty).GridIndices);
    }

    /// <summary>Founds a hub at <see cref="WFReaverTestBase.Home"/> and sends it its next reinforcement.</summary>
    private (EntityUid Stronghold, EntityUid Reinforcement) Launch()
    {
        var stronghold = FoundAt(Home);
        Assert.That(Server.System<WFReaverCampaignSystem>().TryReinforce(stronghold), Is.True);
        var inbound = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).Inbound;
        Assert.That(inbound, Is.Not.Null);
        return (stronghold, inbound!.Value);
    }

    /// <summary>A hub is founded at level 0 with no guard ships and a shield generator anchored on its hull, and the shield comes up.</summary>
    [Test]
    public async Task FoundingPlacesAShieldedHubWithNoGuards()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            stronghold = FoundAt(Home);
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(stronghold);
            var comp = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            Assert.That(encounter.Prototype.Id, Is.EqualTo(Stronghold));
            Assert.That(encounter.Ships.Keys, Is.EqualTo(new[] { "lead" }), "No guard ships at founding.");
            Assert.That(comp.Level, Is.Zero);
            Assert.That(comp.Reinforcements, Is.Empty);
            Assert.That(encounter.Name, Does.EndWith("(level 0)"), "The map shows the level.");
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(stronghold).EntityName, Is.EqualTo(encounter.Name));

            var grid = encounter.Ships["lead"].Grid;
            var emitter = campaign.ShieldOf(grid);
            Assert.That(emitter, Is.Not.Null, "The campaign placed a shield generator on the hub.");
            Assert.That(SEntMan.HasComponent<ShipShieldEmitterComponent>(emitter), Is.True);
            var xform = SEntMan.GetComponent<TransformComponent>(emitter!.Value);
            Assert.That(xform.GridUid, Is.EqualTo(grid));
            Assert.That(xform.Anchored, Is.True, "Anchored, so the power net feeds it.");

            // Nothing solid beside it, so it cannot plug a corridor or doorway.
            var maps = Server.System<SharedMapSystem>();
            var gridComp = SEntMan.GetComponent<MapGridComponent>(grid);
            var tile = maps.TileIndicesFor(grid, gridComp, xform.Coordinates);
            foreach (var offset in new[] { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right })
            {
                foreach (var anchored in maps.GetAnchoredEntities((grid, gridComp), tile + offset))
                {
                    Assert.That(SEntMan.TryGetComponent(anchored, out PhysicsComponent? body) && body!.CanCollide && body.Hard, Is.False,
                        "No wall, door or machine beside the shield generator.");
                }
            }
        });

        // The shield comes up a few seconds after the plant does.
        await WaitUntilServer(() => SEntMan.HasComponent<ShipShieldedComponent>(HubGrid(stronghold)), 600);
        await RunTicks(5);
        Assert.That(ChatSince(since).Any(line => line.Contains("built a stronghold")), Is.True);
    }

    /// <summary>A hull that already carries a generator is left alone: no second one is placed.</summary>
    [Test]
    public async Task HullWithAGeneratorIsNotGivenAnother()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            var grid = HubGrid(stronghold);
            var first = campaign.ShieldOf(grid);
            Assert.That(first, Is.Not.Null);
            Assert.That(SEntMan.EntityQuery<ShipShieldEmitterComponent, TransformComponent>()
                .Count(pair => pair.Item2.GridUid == grid), Is.EqualTo(1));
        });
    }

    /// <summary>A reinforcement spawns at the sector edge on the hub's side, is ordered to the hub's standoff and then to dock, and is announced.</summary>
    [Test]
    public async Task TryReinforceSendsAConvoyToTheHub()
    {
        await StartCampaign();
        var since = ChatCount();
        var callsign = string.Empty;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var transform = Server.System<SharedTransformSystem>();
            campaign.SetTier(1);
            var (stronghold, reinforcement) = Launch();
            var home = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            callsign = WFSectorHex.Callsign(home.Cell);
            var comp = SEntMan.GetComponent<WFReaverReinforcementComponent>(reinforcement);
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(reinforcement);
            Assert.That(comp.Stronghold, Is.EqualTo(stronghold));
            Assert.That(comp.Level, Is.EqualTo(1));
            Assert.That(comp.Phase, Is.EqualTo(WFReaverReinforcementPhase.Inbound));
            Assert.That(encounter.Prototype.Id, Is.EqualTo(Reinforcement));
            Assert.That(encounter.OffBudget, Is.True);
            Assert.That(campaign.Reinforcements().Select(entry => entry.Owner), Is.EqualTo(new[] { reinforcement }));

            var hub = HubGrid(stronghold);
            var hubAt = transform.GetWorldPosition(hub);
            var at = transform.GetWorldPosition(encounter.Ships["lead"].Grid);
            Assert.That(at.Length(), Is.InRange(24500f, 28500f), "At the sector edge.");
            Assert.That(Vector2.Dot(Vector2.Normalize(at), Vector2.Normalize(hubAt)), Is.GreaterThan(0.8f), "On the hub's side.");

            var orders = Orders(reinforcement, "lead");
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo, WFCrewObjectiveKind.Dock }));
            Assert.That(orders[0].Position, Is.EqualTo(hubAt));
            Assert.That(orders[0].Range, Is.EqualTo(600f));
            Assert.That(orders[1].Target, Is.EqualTo(SEntMan.GetNetEntity(hub)));

            Assert.That(campaign.TryReinforce(stronghold), Is.False, "One at a time.");
            Assert.That(home.Level, Is.Zero, "The level comes on docking.");
        });

        await RunTicks(30);
        var lines = ChatSince(since);
        Assert.That(lines.Any(line => line.Contains($"Reaver reinforcements are inbound to {callsign}, ETA")), Is.True,
            "The sector watch announces it.");
    }

    /// <summary>A real dock raises the level, a tier's cap stops the next, and the clock restarts.</summary>
    [Test]
    public async Task DockingRaisesTheLevelUpToTheTierCap()
    {
        await StartCampaign();
        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var docking = Server.System<DockingSystem>();
            campaign.SetTier(1);
            var (stronghold, reinforcement) = Launch();
            var home = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            var comp = SEntMan.GetComponent<WFReaverReinforcementComponent>(reinforcement);
            var hub = HubGrid(stronghold);
            var ship = SEntMan.GetComponent<WFEncounterComponent>(reinforcement).Ships["lead"].Grid;

            // Not docked yet: a check changes nothing.
            campaign.CheckReinforcements();
            Assert.That(comp.Landed, Is.False);
            Assert.That(home.Level, Is.Zero);

            Place(ship, Server.System<SharedTransformSystem>().GetWorldPosition(hub) + new Vector2(0f, 40f));
            var hubDock = SEntMan.SpawnEntity("AirlockShuttle", AnyTile(hub));
            var shipDock = SEntMan.SpawnEntity("AirlockShuttle", AnyTile(ship));
            docking.Dock((hubDock, SEntMan.GetComponent<DockingComponent>(hubDock)), (shipDock, SEntMan.GetComponent<DockingComponent>(shipDock)));
            Assert.That(docking.AreGridsDocked(ship, hub), Is.True);

            campaign.CheckReinforcements();
            Assert.That(comp.Landed, Is.True);
            Assert.That(comp.Phase, Is.EqualTo(WFReaverReinforcementPhase.Docked));
            Assert.That(home.Level, Is.EqualTo(1));
            Assert.That(home.Inbound, Is.Null);
            Assert.That(home.Reinforcements, Is.EqualTo(new[] { reinforcement }));
            Assert.That(home.NextReinforcement, Is.EqualTo(STiming.CurTime + campaign.Campaign!.ReinforcementInterval), "The clock restarts.");
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(stronghold).Name, Does.EndWith("(level 1)"));

            Assert.That(campaign.TryReinforce(stronghold), Is.False, "Tier 1 stops at level 1.");
            campaign.SetTier(2);
            Assert.That(campaign.TryReinforce(stronghold), Is.True, "Tier 2 allows level 2.");
            var second = home.Inbound!.Value;
            Assert.That(SEntMan.GetComponent<WFReaverReinforcementComponent>(second).Level, Is.EqualTo(2));
            Assert.That(campaign.CompleteDock(second), Is.True);
            Assert.That(home.Level, Is.EqualTo(2));
            Assert.That(campaign.CompleteDock(second), Is.True, "Docking twice counts once.");
            Assert.That(home.Level, Is.EqualTo(2));
            Assert.That(home.Reinforcements, Has.Count.EqualTo(2));
            Assert.That(campaign.TryReinforce(stronghold), Is.False, "Tier 2 stops at level 2.");

            docking.Undock((hubDock, SEntMan.GetComponent<DockingComponent>(hubDock)));
        });

        await RunTicks(30);
        var lines = ChatSince(since);
        Assert.That(lines.Any(line => line.Contains("has grown to level 1")), Is.True);
        Assert.That(lines.Any(line => line.Contains("has grown to level 2")), Is.True);
    }

    /// <summary>A hub sends its next reinforcement when its interval is up, and not before.</summary>
    [Test]
    public async Task TickSendsReinforcementsWhenTheIntervalHasPassed()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        await Server.WaitAssertion(() =>
        {
            Server.System<WFReaverCampaignSystem>().SetTier(2);
            stronghold = FoundAt(Home);
            var home = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            Assert.That(home.NextReinforcement, Is.EqualTo(STiming.CurTime + Server.System<WFReaverCampaignSystem>().Campaign!.ReinforcementInterval));
        });

        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).Inbound, Is.Null, "Its interval has not passed.");
            SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).NextReinforcement = STiming.CurTime;
        });

        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var home = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            Assert.That(home.Inbound, Is.Not.Null, "Due, it sets out.");
            Assert.That(Server.System<WFReaverCampaignSystem>().Reinforcements(), Has.Count.EqualTo(1));
        });
    }

    /// <summary>A hub at its tier's cap sends nothing however long it has stood.</summary>
    [Test]
    public async Task HubAtTheTierCapSendsNothing()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        await Server.WaitAssertion(() =>
        {
            Server.System<WFReaverCampaignSystem>().SetTier(1);
            stronghold = FoundAt(Home);
            var home = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            home.Level = 1;
            home.NextReinforcement = STiming.CurTime;
            Assert.That(Server.System<WFReaverCampaignSystem>().TryReinforce(stronghold), Is.False);
        });

        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).Inbound, Is.Null, "Tier 1 stops at level 1.");
        });
    }

    /// <summary>A reinforcement short of the hub does not hold on a timer; only a crew that gave up docking sends it to hold, and it still counts.</summary>
    [Test]
    public async Task ReinforcementThatCannotDockHoldsBesideTheHub()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var transform = Server.System<SharedTransformSystem>();
            campaign.SetTier(1);
            var (stronghold, reinforcement) = Launch();
            var home = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            var comp = SEntMan.GetComponent<WFReaverReinforcementComponent>(reinforcement);
            var hub = HubGrid(stronghold);
            var ship = SEntMan.GetComponent<WFEncounterComponent>(reinforcement).Ships["lead"].Grid;
            Place(ship, transform.GetWorldPosition(hub) + new Vector2(0f, 500f));

            comp.Overdue = STiming.CurTime - TimeSpan.FromMinutes(6);
            campaign.CheckReinforcements();
            Assert.That(comp.Landed, Is.False, "A slow dock is not cut short by a timer.");
            Assert.That(comp.Overdue, Is.GreaterThan(STiming.CurTime), "At the hub it is given more time.");
            Assert.That(Orders(reinforcement, "lead").Select(order => order.Kind),
                Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo, WFCrewObjectiveKind.Dock }), "Its dock is left alone.");

            var failed = new WFPilotDockFailedEvent(reinforcement, ship, hub);
            SEntMan.EventBus.RaiseLocalEvent(reinforcement, ref failed, true);
            Assert.That(comp.DockFailed, Is.True);
            campaign.CheckReinforcements();
            Assert.That(comp.Landed, Is.True);
            Assert.That(comp.Phase, Is.EqualTo(WFReaverReinforcementPhase.Holding));
            Assert.That(home.Level, Is.EqualTo(1), "It counts all the same.");
            var orders = Orders(reinforcement, "lead");
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo, WFCrewObjectiveKind.Hold }));
            Assert.That(orders[0].Range, Is.EqualTo(300f));
        });
    }

    /// <summary>A reinforcement that never gets near the hub is ended past its deadline, which frees the hub to send another after the interval.</summary>
    [Test]
    public async Task OverdueReinforcementShortOfTheHubIsEnded()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            campaign.SetTier(1);
            var (stronghold, reinforcement) = Launch();
            var home = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            var comp = SEntMan.GetComponent<WFReaverReinforcementComponent>(reinforcement);
            Assert.That(comp.Overdue, Is.GreaterThan(STiming.CurTime + TimeSpan.FromMinutes(6)), "Twice the reckoned trip, plus slack.");

            campaign.CheckReinforcements();
            Assert.That(home.Inbound, Is.EqualTo(reinforcement), "Not yet due.");

            comp.Overdue = STiming.CurTime - TimeSpan.FromSeconds(1);
            campaign.CheckReinforcements();
            Assert.That(home.Inbound, Is.Null, "Ending it lifts the one-at-a-time gate.");
            Assert.That(!SEntMan.EntityExists(reinforcement) || SEntMan.GetComponent<WFEncounterComponent>(reinforcement).Resolution != null, Is.True);
            Assert.That(home.Level, Is.Zero);
            Assert.That(home.NextReinforcement, Is.GreaterThan(STiming.CurTime), "The hub tries again after the interval.");
        });
    }

    /// <summary>A reinforcement whose crew gave up the flight is ordered to dock again, no more often than the reorder delay.</summary>
    [Test]
    public async Task ReinforcementWhoseFlightEndedIsOrderedToDockAgain()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            campaign.SetTier(1);
            var (stronghold, reinforcement) = Launch();
            var comp = SEntMan.GetComponent<WFReaverReinforcementComponent>(reinforcement);
            var lead = SEntMan.GetComponent<WFEncounterComponent>(reinforcement).Ships["lead"];
            var hub = HubGrid(stronghold);
            Assert.That(Server.System<WFEncounterSystem>().SetOrders(reinforcement, "lead",
                new List<WFCrewObjective> { new() { Kind = WFCrewObjectiveKind.Hold } }), Is.True);

            lead.Flown = true;
            campaign.CheckReinforcements();
            Assert.That(lead.Flown, Is.False, "New orders were given.");
            var orders = Orders(reinforcement, "lead");
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo, WFCrewObjectiveKind.Dock }));
            Assert.That(orders[1].Target, Is.EqualTo(SEntMan.GetNetEntity(hub)));
            Assert.That(comp.NextOrders, Is.GreaterThan(STiming.CurTime));
            Assert.That(comp.Phase, Is.EqualTo(WFReaverReinforcementPhase.Inbound), "It is not held on a timer.");

            lead.Flown = true;
            campaign.CheckReinforcements();
            Assert.That(lead.Flown, Is.True, "Not again before the delay.");
        });
    }

    /// <summary>A hub under fire sends nothing; a reinforcement that docks while it is under fire goes straight out.</summary>
    [Test]
    public async Task HubUnderFireSendsNothingAndSendsArrivalsOut()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        await Server.WaitAssertion(() =>
        {
            Server.System<WFReaverCampaignSystem>().SetTier(2);
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            stronghold = FoundAt(Home);
            ShootAt(stronghold, "lead", ship);
            Server.System<WFReaverCampaignSystem>().CheckStrongholds();
            SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).NextReinforcement = STiming.CurTime;
        });

        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var home = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            Assert.That(home.Inbound, Is.Null, "A hub with a fight on does not launch.");
            home.LastHit = STiming.CurTime - TimeSpan.FromMinutes(3);
        });

        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).Inbound, Is.Not.Null, "Quiet again, it sets out.");
        });
    }

    /// <summary>A reinforcement that docks while its hub is under fire breaks away at once onto the ship that hit it.</summary>
    [Test]
    public async Task ReinforcementArrivingMidFightGoesStraightOut()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            campaign.SetTier(1);
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 4000f), "Test Gunship");
            Board(ship);
            var (stronghold, reinforcement) = Launch();
            var home = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            ShootAt(stronghold, "lead", ship);
            campaign.CheckStrongholds();
            Assert.That(home.Scattered, Is.False, "Nothing had docked to break away.");

            Assert.That(campaign.CompleteDock(reinforcement), Is.True);
            var comp = SEntMan.GetComponent<WFReaverReinforcementComponent>(reinforcement);
            Assert.That(home.Scattered, Is.True);
            Assert.That(comp.Phase, Is.EqualTo(WFReaverReinforcementPhase.Sortie));
            Assert.That(comp.Target, Is.EqualTo(ship));
        });
    }

    /// <summary>The reinforcements turn on the ship that is hitting the hub now, not the one with the most hits over the hub's life.</summary>
    [Test]
    public async Task ReinforcementsTurnOnTheShipThatIsHittingNow()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            campaign.SetTier(2);
            var old = SpawnGrid(CentreOf(Home) + new Vector2(0f, 4000f), "Old Gunship");
            var current = SpawnGrid(CentreOf(Home) + new Vector2(0f, -4000f), "Current Gunship");
            Board(old);
            var (stronghold, reinforcement) = Launch();
            Assert.That(campaign.CompleteDock(reinforcement), Is.True);
            var comp = SEntMan.GetComponent<WFReaverReinforcementComponent>(reinforcement);

            ShootAt(stronghold, "lead", old);
            ShootAt(stronghold, "lead", old);
            campaign.CheckStrongholds();
            Assert.That(comp.Target, Is.EqualTo(old));

            // The old attacker is done; a different ship hits once.
            Board(current);
            ShootAt(stronghold, "lead", current);
            campaign.CheckStrongholds();
            Assert.That(comp.Target, Is.EqualTo(current), "The ship firing now is the target.");
            var orders = Orders(reinforcement, "lead");
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.Attack }));
            Assert.That(orders[0].Target, Is.EqualTo(SEntMan.GetNetEntity(current)));
        });
    }

    /// <summary>A reinforcement destroyed on the way founds no level, pays the patrol bounty, and the hub tries again after its interval.</summary>
    [Test]
    public async Task DestroyedReinforcementFoundsNoLevelAndPays()
    {
        await StartCampaign();
        EntityUid stronghold = default, reinforcement = default;
        var before = 0;
        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            Server.System<WFReaverCampaignSystem>().SetTier(1);
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            (stronghold, reinforcement) = Launch();
            ShootAt(reinforcement, "lead", ship);
            before = Balance();
            KillCrew(reinforcement, "lead");
        });

        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var home = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(reinforcement).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
            Assert.That(home.Level, Is.Zero, "It never docked.");
            Assert.That(home.Inbound, Is.Null);
            Assert.That(home.Reinforcements, Is.Empty);
            Assert.That(home.NextReinforcement, Is.GreaterThan(STiming.CurTime), "The hub tries again after the interval.");
            Assert.That(campaign.Strongholds(), Has.Count.EqualTo(1), "Killing a reinforcement does not break the stronghold.");
            Assert.That(Balance() - before, Is.EqualTo(PatrolBounty));
        });

        Assert.That(ChatSince(since).Any(line => line.Contains("Reaver reinforcements bound for") && line.Contains("destroyed")), Is.True);
    }

    /// <summary>Hits on the hub break its reinforcements away onto the ship that hit most; with the attackers gone and quiet, they come back to dock.</summary>
    [Test]
    public async Task HitsOnTheHubBreakReinforcementsAwayAndTheyReturn()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            campaign.SetTier(2);
            var near = SpawnGrid(CentreOf(Home) + new Vector2(0f, 4000f), "Test Gunship");
            var other = SpawnGrid(CentreOf(Home) + new Vector2(0f, -4000f), "Other Gunship");
            Board(near);
            var (stronghold, reinforcement) = Launch();
            Assert.That(campaign.CompleteDock(reinforcement), Is.True);
            var home = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            Assert.That(home.Level, Is.EqualTo(1));

            // The first ship hits the hub; a second player ship hits it more.
            ShootAt(stronghold, "lead", near);
            Board(other);
            ShootAt(stronghold, "lead", other);
            ShootAt(stronghold, "lead", other);
            campaign.CheckStrongholds();

            var comp = SEntMan.GetComponent<WFReaverReinforcementComponent>(reinforcement);
            Assert.That(home.Scattered, Is.True);
            Assert.That(comp.Phase, Is.EqualTo(WFReaverReinforcementPhase.Sortie));
            var orders = Orders(reinforcement, "lead");
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.Undock, WFCrewObjectiveKind.Attack }));
            Assert.That(orders[1].Target, Is.EqualTo(SEntMan.GetNetEntity(other)), "It attacks the ship with the most hits.");
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(stronghold).Resolution, Is.Null, "The hub keeps holding.");

            // Just hit: they stay out.
            campaign.RegroupReinforcements(stronghold, false);
            Assert.That(home.Scattered, Is.True);

            // Quiet for three minutes but an attacker still near the hub: they stay out.
            home.LastHit = STiming.CurTime - TimeSpan.FromMinutes(4);
            Place(other, Server.System<SharedTransformSystem>().GetWorldPosition(HubGrid(stronghold)) + new Vector2(0f, 2000f));
            campaign.RegroupReinforcements(stronghold, false);
            Assert.That(home.Scattered, Is.True);

            // The attackers gone: they return to dock.
            Place(other, CentreOf(Home) + new Vector2(0f, -7000f));
            Place(near, CentreOf(Home) + new Vector2(0f, 7000f));
            campaign.RegroupReinforcements(stronghold, false);
            Assert.That(home.Scattered, Is.False);
            Assert.That(comp.Phase, Is.EqualTo(WFReaverReinforcementPhase.Returning));
            var back = Orders(reinforcement, "lead");
            Assert.That(back.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo, WFCrewObjectiveKind.Dock }));
            Assert.That(back[1].Target, Is.EqualTo(SEntMan.GetNetEntity(HubGrid(stronghold))));
        });
    }

    /// <summary>Breaking the hub ends its reinforcements and pays the stronghold bounty scaled by its level, hits on a reinforcement included.</summary>
    [Test]
    public async Task BreakingTheHubEndsReinforcementsAndPaysTheScaledBounty()
    {
        await StartCampaign();
        EntityUid stronghold = default, first = default, second = default;
        var before = 0;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            campaign.SetTier(2);
            var ship = SpawnGrid(CentreOf(Home) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            (stronghold, first) = Launch();
            Assert.That(campaign.CompleteDock(first), Is.True);
            Assert.That(campaign.TryReinforce(stronghold), Is.True);
            second = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).Inbound!.Value;
            Assert.That(campaign.CompleteDock(second), Is.True);
            Assert.That(SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).Level, Is.EqualTo(2));

            // The player only ever shot a reinforcement; the hub's bounty is still theirs.
            ShootAt(first, "lead", ship);
            before = Balance();
            KillCrew(stronghold, "lead");
        });

        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(stronghold).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
            Assert.That(campaign.Strongholds(), Is.Empty);
            Assert.That(campaign.Reinforcements(), Is.Empty, "Its reinforcements went with it.");
            foreach (var reinforcement in new[] { first, second })
            {
                Assert.That(!SEntMan.EntityExists(reinforcement) || SEntMan.GetComponent<WFEncounterComponent>(reinforcement).Resolution != null, Is.True);
            }

            Assert.That(Balance() - before, Is.EqualTo((int) (Bounties[2] * 2f)), "The tier's bounty at level 2: base times one plus half the level.");
        });
    }

    /// <summary>The admin command sends a reinforcement to a stronghold and the status lists the level and the convoy.</summary>
    [Test]
    public async Task CommandReinforcesAStronghold()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var console = Server.ConsoleHost;
            var campaign = Server.System<WFReaverCampaignSystem>();
            campaign.SetTier(1);
            var stronghold = FoundAt(Home);
            var home = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            console.ExecuteCommand("wf_reavers reinforce nonsense");
            Assert.That(home.Inbound, Is.Null);
            console.ExecuteCommand($"wf_reavers reinforce {SEntMan.GetNetEntity(stronghold)}");
            Assert.That(home.Inbound, Is.Not.Null);
            console.ExecuteCommand("wf_reavers status");
            console.ExecuteCommand("wf_reavers clear");
            Assert.That(campaign.Reinforcements(), Is.Empty, "Clearing ends reinforcements too.");
        });
    }
}
