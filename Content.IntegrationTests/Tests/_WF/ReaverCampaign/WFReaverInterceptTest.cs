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
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.SectorControl;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>Intercepts and marks: a ship that stays in Reaver space gets a patrol sent after it, and a ship that fights the Reavers is hunted by raids.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverInterceptTest : WFReaverTestBase
{
    /// <summary>A cell two out from the stronghold: not held, but within a patrol's reach.</summary>
    private static readonly WFSectorCell Outside = Home.Neighbour(1).Neighbour(1);

    /// <summary>A cell far from any held space.</summary>
    private static readonly WFSectorCell Far = new(-6, 3);

    private List<WFCrewObjective> Orders(EntityUid encounter, string key)
    {
        var group = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships[key].Group;
        return Server.System<WFCrewObjectiveSystem>().Snapshot().Single(crew => crew.Group == group).Objectives;
    }

    /// <summary>Whether the orders close on a ship and attack it, then fly on and home.</summary>
    private void AssertIntercepting(EntityUid patrol, EntityUid ship)
    {
        var comp = SEntMan.GetComponent<WFReaverPatrolComponent>(patrol);
        Assert.That(comp.Intercepting, Is.EqualTo(ship));
        var orders = Orders(patrol, "lead");
        Assert.That(orders.Take(2).Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo, WFCrewObjectiveKind.Attack }));
        Assert.That(Vector2.Distance(orders[0].Position, Server.System<SharedTransformSystem>().GetWorldPosition(ship)), Is.LessThan(1f),
            "It closes on where the ship is.");
        Assert.That(orders[0].Range, Is.EqualTo(600f));
        Assert.That(orders[1].Target, Is.EqualTo(SEntMan.GetNetEntity(ship)), "Then attacks it.");
        Assert.That(orders.Skip(2).Select(order => order.Kind), Is.All.EqualTo(WFCrewObjectiveKind.GoTo));
        Assert.That(orders[^1].Position, Is.EqualTo(comp.Home), "Then flies the rest of its route and home.");
    }

    /// <summary>A crewed ship in held space past the dwell has the nearest patrol turned on it, and those aboard are warned.</summary>
    [Test]
    public async Task ShipInHeldSpacePastTheDwellIsIntercepted()
    {
        await StartCampaign();
        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            campaign.SetTier(1);
            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.True);
            var patrol = campaign.Patrols().Single().Owner;
            AssertSpawnedBeside(patrol, stronghold);
            var route = SEntMan.GetComponent<WFReaverPatrolComponent>(patrol).Route.ToList();
            var ship = SpawnGrid(CentreOf(Home.Neighbour(1)), "Test Hauler");
            Board(ship);

            campaign.CheckIntercepts();
            var state = campaign.State()!;
            Assert.That(state.InsideSince.ContainsKey(ship), Is.True, "Its time in held space is counted.");
            Assert.That(SEntMan.GetComponent<WFReaverPatrolComponent>(patrol).Intercepting, Is.Null, "Not before the dwell is up.");

            state.InsideSince[ship] = STiming.CurTime - TimeSpan.FromSeconds(61);
            campaign.CheckIntercepts();
            AssertIntercepting(patrol, ship);
            var comp = SEntMan.GetComponent<WFReaverPatrolComponent>(patrol);
            Assert.That(comp.InterceptSince, Is.EqualTo(STiming.CurTime));
            Assert.That(comp.Resume, Is.EqualTo(route), "It had not called anywhere yet, so its whole route is kept.");
            Assert.That(campaign.Patrols(), Has.Count.EqualTo(1), "The patrol that was out went; no other was sent.");

            // Another pass sends nobody else after it.
            campaign.CheckIntercepts();
            Assert.That(campaign.Patrols().Count(entry => entry.Comp1.Intercepting == ship), Is.EqualTo(1));
        });

        await RunTicks(30);
        Assert.That(ChatSince(since).Any(line => line.Contains("Reaver patrol turning to intercept Test Hauler, bearing")), Is.True,
            "Those aboard are warned in chat.");
    }

    /// <summary>A ship whose IFF hides it, as a secret station does to those docked at it, is neither intercepted nor named to the sector.</summary>
    [Test]
    public async Task ShipHiddenByIffIsNeverIntercepted()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            FoundAt(Home);
            campaign.SetTier(1);
            var ship = SpawnGrid(CentreOf(Home.Neighbour(1)), "Test Hauler");
            Board(ship);
            Server.System<SharedShuttleSystem>().AddIFFFlag(ship, IFFFlags.HideLabelAlways);
            var state = campaign.State()!;

            campaign.CheckIntercepts();
            Assert.That(state.InsideSince.ContainsKey(ship), Is.False, "Its time in held space is not counted.");
            state.InsideSince[ship] = STiming.CurTime - TimeSpan.FromMinutes(10);
            campaign.CheckIntercepts();
            Assert.That(campaign.Patrols(), Is.Empty, "Nobody is sent after it.");
            Assert.That(campaign.TryIntercept(ship), Is.False, "Not even when asked.");
        });
    }

    /// <summary>A ship out of held space is not intercepted however long it was in; back in, with no patrol out, a fresh one is sent.</summary>
    [Test]
    public async Task ShipOutsideHeldSpaceIsNotIntercepted()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            campaign.SetTier(1);
            var ship = SpawnGrid(CentreOf(Outside), "Test Hauler");
            Board(ship);
            var state = campaign.State()!;

            // Long inside once, but outside now.
            state.InsideSince[ship] = STiming.CurTime - TimeSpan.FromMinutes(10);
            campaign.CheckIntercepts();
            Assert.That(state.InsideSince.ContainsKey(ship), Is.False, "Out of held space, its time there is forgotten.");
            Assert.That(campaign.Patrols(), Is.Empty, "Nobody is sent.");

            // Back in held space past the dwell, with no patrol out: one is launched from the stronghold for it.
            Place(ship, CentreOf(Home.Neighbour(1)));
            campaign.CheckIntercepts();
            state.InsideSince[ship] = STiming.CurTime - TimeSpan.FromSeconds(61);
            campaign.CheckIntercepts();
            var patrol = campaign.Patrols().Single();
            Assert.That(patrol.Comp1.Stronghold, Is.EqualTo(stronghold));
            AssertSpawnedBeside(patrol.Owner, stronghold);
            AssertIntercepting(patrol.Owner, ship);
            Assert.That(patrol.Comp1.Resume, Is.EqualTo(patrol.Comp1.Route), "A fresh patrol flies its whole route afterwards.");
        });
    }

    /// <summary>A ship out of held space for a minute is given up: the patrol flies the rest of its route, and the ship is left alone for the cooldown.</summary>
    [Test]
    public async Task LeavingHeldSpaceRestoresTheRouteAndTheCooldownHolds()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            campaign.SetTier(1);
            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.True);
            var patrol = campaign.Patrols().Single().Owner;
            var comp = SEntMan.GetComponent<WFReaverPatrolComponent>(patrol);
            var ship = SpawnGrid(CentreOf(Home.Neighbour(1)), "Test Hauler");
            Board(ship);
            Assert.That(campaign.TryIntercept(ship), Is.True);
            AssertIntercepting(patrol, ship);
            var resume = comp.Resume.ToList();
            var state = campaign.State()!;

            // Out of held space but still in reach: the clock starts.
            Place(ship, CentreOf(Outside));
            campaign.CheckIntercepts();
            Assert.That(comp.Intercepting, Is.EqualTo(ship), "Not given up straight away.");
            Assert.That(comp.TargetOutsideSince, Is.EqualTo(STiming.CurTime));
            Assert.That(comp.TargetFarSince, Is.Null, "Still within three cells.");

            comp.TargetOutsideSince = STiming.CurTime - TimeSpan.FromSeconds(61);
            campaign.CheckIntercepts();
            Assert.That(comp.Intercepting, Is.Null, "Out of held space for a minute, it is given up.");
            var orders = Orders(patrol, "lead");
            Assert.That(orders.Select(order => order.Kind), Is.All.EqualTo(WFCrewObjectiveKind.GoTo));
            Assert.That(orders.Select(order => order.Position), Is.EqualTo(resume.Append(comp.Home)), "Its route, then home.");
            Assert.That(state.InterceptCooldowns[ship], Is.EqualTo(STiming.CurTime + TimeSpan.FromMinutes(10)));

            // Back in held space and long past the dwell, it is still left alone while the cooldown runs.
            Place(ship, CentreOf(Home.Neighbour(1)));
            campaign.CheckIntercepts();
            state.InsideSince[ship] = STiming.CurTime - TimeSpan.FromMinutes(5);
            campaign.CheckIntercepts();
            Assert.That(comp.Intercepting, Is.Null, "On cooldown.");

            state.InterceptCooldowns[ship] = STiming.CurTime;
            campaign.CheckIntercepts();
            Assert.That(comp.Intercepting, Is.EqualTo(ship), "Once the cooldown is over it is hunted again.");
        });
    }

    /// <summary>Killing a patrol or hitting a stronghold marks the ship that did it, and raids then go for it wherever it is.</summary>
    [Test]
    public async Task FightingTheReaversMarksTheShipForRaids()
    {
        await StartCampaign();
        var since = ChatCount();
        EntityUid ship = default, stronghold = default;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            ship = SpawnGrid(CentreOf(Far), "Test Gunship");
            Board(ship);
            stronghold = FoundAt(Home);
            Assert.That(campaign.TryLaunchRaid(), Is.False, "Unmarked and far from held space, nobody is raided.");

            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.True);
            var patrol = campaign.Patrols().Single().Owner;
            ShootAt(patrol, "lead", ship);
            Server.System<WFEncounterSystem>().Resolve(patrol, WFEncounterResolution.Destroyed);
            Assert.That(SEntMan.TryGetComponent<WFReaverMarkComponent>(ship, out var mark), Is.True, "The ship that killed the patrol is marked.");
            Assert.That(mark!.Until, Is.EqualTo(STiming.CurTime + WFReaverCampaignSystem.MarkTime));
            Assert.That(campaign.IsMarked(ship), Is.True);

            Assert.That(campaign.TryLaunchRaid(), Is.True, "A marked ship is hunted anywhere.");
            Assert.That(SEntMan.GetComponent<WFReaverRaidComponent>(Raids().Single()).Target, Is.EqualTo(ship));

            // Marks run out.
            mark.Until = STiming.CurTime;
        });

        await RunTicks(90);
        Assert.That(ChatSince(since).Any(line => line.Contains("Reaver raiders are hunting Test Gunship, bearing")), Is.True,
            "The warning says it is being hunted.");
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WFReaverMarkComponent>(ship), Is.False, "The mark ran out.");
            ShootAt(stronghold, "lead", ship);
        });

        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFReaverCampaignSystem>().IsMarked(ship), Is.True, "Hitting a stronghold marks the ship too.");
        });
    }
}
