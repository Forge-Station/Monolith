#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>The hub is a gun platform: it holds its point, turns its guns on attackers and zone intruders, and never gives chase.</summary>
[TestOf(typeof(WFPilotDutySystem))]
public sealed class WFReaverGunPlatformTest : WFReaverTestBase
{
    private const string Hub = "WFReaverStronghold";

    /// <summary>How far off its station the hub may drift while it turns.</summary>
    private const float StationReach = 100f;

    private const double Tolerance = 0.12;

    private EntityUid _hull;
    private EntityUid _pilot;
    private string _group = string.Empty;
    private Vector2 _station;

    /// <summary>Spawns the real hub and waits for its pilot to take the helm on his own.</summary>
    private async Task SpawnHub()
    {
        await StartCampaign();
        _station = CentreOf(Home);
        await Server.WaitPost(() =>
        {
            var encounters = Server.System<WFEncounterSystem>();
            Assert.That(encounters.TrySpawn(ProtoMan.Index<WFEncounterPrototype>(Hub), new MapCoordinates(_station, MapData.MapId), out var encounter, offBudget: true), Is.True);
            var ship = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["lead"];
            _hull = ship.Grid;
            _group = ship.Group;
        });
        await RunTicks(300);
        await Server.WaitPost(() =>
        {
            _pilot = SEntMan.EntityQuery<WFPilotDutyComponent, WFCrewComponent>()
                .First(pair => pair.Item2.Group == _group && pair.Item2.Role == WFCrewRoles.Pilot.Id).Item1.Owner;
        });
        await WaitUntilServer(() => SEntMan.GetComponent<WFPilotDutyComponent>(_pilot).AtHelm, 3000);
    }

    /// <summary>The real hub's pilot takes a helm square to the hull by himself well inside the two minutes a ship may go unpiloted.</summary>
    [Test]
    public async Task HubPilotTakesTheHelmUnaided()
    {
        await SpawnHub();
        await Server.WaitAssertion(() =>
        {
            var duty = SEntMan.GetComponent<WFPilotDutyComponent>(_pilot);
            Assert.That(duty.AtHelm, Is.True);
            var console = duty.Console!.Value;
            Assert.That(SEntMan.GetComponent<TransformComponent>(console).GridUid, Is.EqualTo(_hull));
            Assert.That(WFPilotDutySystem.IsSquareHelm(SEntMan.GetComponent<TransformComponent>(console).LocalRotation), Is.True,
                "The hub is flown from a console square to the hull.");
            Assert.That(Server.System<WFCrewObjectiveSystem>().QueueStatus(_hull, _group), Is.Not.EqualTo("awaiting-helm"));
        });
    }

    /// <summary>How far, in radians, the hub's nose is from the heading its best armed side would take towards a point.</summary>
    private double TurnLeft(Vector2 offset)
    {
        var heading = Server.System<WFPilotDutySystem>().GunHeading(_hull, offset);
        if (heading == null)
            return double.MaxValue;
        var facing = Server.System<SharedTransformSystem>().GetWorldRotation(_hull) + new Angle(Math.PI);
        return Math.Abs(ShipSteeringSystem.ShortestAngleDistance(facing, heading.Value).Theta);
    }

    /// <summary>Runs ticks until the hub presents its guns to a point, failing if it ever strays from its station or gives chase.</summary>
    private async Task AssertTurnsTo(Vector2 offset, string what)
    {
        var left = double.MaxValue;
        for (var i = 0; i < 40 && left >= Tolerance; i++)
        {
            await RunTicks(30);
            await Server.WaitAssertion(() =>
            {
                left = TurnLeft(offset);
                var distance = Vector2.Distance(Server.System<SharedTransformSystem>().GetWorldPosition(_hull), _station);
                Assert.That(distance, Is.LessThan(StationReach), $"The hub keeps its point while it turns to {what}.");
                Assert.That(SEntMan.GetComponent<WFPilotDutyComponent>(_pilot).Orders, Is.EqualTo(WFPilotOrder.Hold), "The hub never leaves Hold.");
            });
        }

        Assert.That(left, Is.LessThan(Tolerance), $"The hub should turn its guns to {what}.");
    }

    /// <summary>The hub's guns are laid on the ship that hit it, and as it moves round the hub turns after it.</summary>
    [Test]
    public async Task HubTurnsToAttackerAndHoldsItsPoint()
    {
        await SpawnHub();
        EntityUid attacker = default;
        await Server.WaitPost(() =>
        {
            Assert.That(Server.System<WFPilotDutySystem>().GunBearings(_hull), Is.Not.Empty, "Saintie's guns fire past one side, so it has a side to present.");
            attacker = SpawnGrid(_station + new Vector2(0f, 600f), "Test Gunship");
            Server.System<WFCrewAlertSystem>().ReportShipThreat(_hull, _group, attacker);
        });
        await AssertTurnsTo(new Vector2(0f, 600f), "an attacker to the north");

        await Server.WaitPost(() => Place(attacker, _station + new Vector2(-600f, 0f)));
        await AssertTurnsTo(new Vector2(-600f, 0f), "the same attacker gone round to the west");

        await Server.WaitAssertion(() =>
        {
            var gunners = Server.System<WFGunnerDutySystem>();
            var laid = SEntMan.EntityQuery<WFGunnerDutyComponent, WFCrewComponent>()
                .Where(pair => pair.Item2.Group == _group)
                .SelectMany(pair => gunners.Batteries(pair.Item1.Owner))
                .Any(battery => battery.Target == attacker && battery.Guns.Count > 0);
            Assert.That(laid, Is.True, "A gunner should have the hub's guns laid on the attacker.");
        });
    }

    /// <summary>A reinforcement docked to the hub is its garrison, not a berth: the hub keeps turning on attackers.</summary>
    [Test]
    public async Task HubWithDockedReinforcementStillTurns()
    {
        await SpawnHub();
        EntityUid intruder = default;
        // A joint made on the server in a test is re-made by the client's docking state, which it logs as an error.
        var failureLevel = Pair.ClientLogHandler.FailureLevel;
        Pair.ClientLogHandler.FailureLevel = LogLevel.Fatal;
        await Server.WaitPost(() =>
        {
            var flag = SEntMan.GetComponent<CompanyComponent>(_hull).CompanyName;
            Assert.That(flag.Id, Is.Not.EqualTo("None"), "The hub flies its company.");
            var ally = SpawnGrid(Server.System<SharedTransformSystem>().GetWorldPosition(_hull) + new Vector2(0f, 40f), "Test Reinforcement");
            SEntMan.EnsureComponent<CompanyComponent>(ally).CompanyName = flag;
            var maps = Server.System<SharedMapSystem>();
            var hull = SEntMan.GetComponent<MapGridComponent>(_hull);
            var tile = maps.GetAllTiles(_hull, hull).First(entry => !entry.Tile.IsEmpty).GridIndices;
            var hubDock = SEntMan.SpawnEntity("AirlockShuttle", maps.GridTileToLocal(_hull, hull, tile));
            var allyDock = SEntMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(ally, new Vector2(0.5f)));
            Server.System<DockingSystem>().Dock((hubDock, SEntMan.GetComponent<DockingComponent>(hubDock)),
                (allyDock, SEntMan.GetComponent<DockingComponent>(allyDock)));
            Assert.That(Server.System<DockingSystem>().AreGridsDocked(_hull, ally), Is.True);

            intruder = SpawnGrid(_station + new Vector2(0f, 600f), "Test Gunship");
            Server.System<WFCrewAlertSystem>().ReportZoneEngagement(_hull, _group, intruder);
        });
        await AssertTurnsTo(new Vector2(0f, 600f), "an intruder with a reinforcement docked");
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<WFPilotDutyComponent>(_pilot).Docked, Is.False, "A garrison is not a berth."));
        Pair.ClientLogHandler.FailureLevel = failureLevel;
    }

    /// <summary>A ship that comes inside the attack zone is faced too, and the zone engaging it sends the hub no further than the turn.</summary>
    [Test]
    public async Task HubTurnsToZoneIntruderWithoutPursuing()
    {
        await SpawnHub();
        EntityUid intruder = default;
        await Server.WaitPost(() =>
        {
            intruder = SpawnGrid(_station + new Vector2(0f, -500f), "Test Gunship");
            Server.System<WFCrewAlertSystem>().ReportZoneEngagement(_hull, _group, intruder);
        });
        await AssertTurnsTo(new Vector2(0f, -500f), "an intruder in its attack zone");

        // The intruder leaves; the hub is where it was and still holding.
        await Server.WaitPost(() => Place(intruder, _station + new Vector2(0f, -2500f)));
        await RunTicks(600);
        await Server.WaitAssertion(() =>
        {
            var duty = SEntMan.GetComponent<WFPilotDutyComponent>(_pilot);
            Assert.That(duty.Orders, Is.EqualTo(WFPilotOrder.Hold));
            Assert.That(Vector2.Distance(Server.System<SharedTransformSystem>().GetWorldPosition(_hull), _station), Is.LessThan(StationReach));
        });
    }
}
