#nullable enable
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>Walls off the north of a deck and mounts two cannons behind them, so the guns fire past the other three sides.</summary>
    private void ArmSouthSide(EntityUid grid)
    {
        for (var x = 0; x < 7; x++)
        {
            if (x != 5)
                SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(grid, new Vector2(x + 0.5f, 4.5f)));
        }
        SEntMan.SpawnAtPosition("WFTestCrewCannon", new EntityCoordinates(grid, new Vector2(0.5f, 2.5f)));
        SEntMan.SpawnAtPosition("WFTestCrewCannon", new EntityCoordinates(grid, new Vector2(6.5f, 2.5f)));
    }

    /// <summary>
    /// A holding ship told to face threats turns its gun side to the ship that attacked it without leaving its
    /// station; one not told to keeps its heading.
    /// </summary>
    [Test]
    public async Task HoldingShipFacesThreatsOnlyWhenTold()
    {
        var facing = await PoweredNavigationShip(new Vector2(500, 500));
        var plain = await PoweredNavigationShip(new Vector2(900, 500));
        var threat = await CreateDeck(new Vector2(700, 1100), 7, true);
        Angle plainHeading = default;
        var start = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            ArmSouthSide(facing.Grid);
            ArmSouthSide(plain.Grid);
        });
        await RunTicks(5);
        await Server.WaitPost(() =>
        {
            var pilots = Server.System<WFPilotDutySystem>();
            pilots.Hold(facing.Pilot);
            pilots.Hold(plain.Pilot);
            // Neither ship flies at a threat, so the only difference between them is the facing.
            SEntMan.GetComponent<WFPilotDutyComponent>(facing.Pilot).ReactToAttacks = false;
            SEntMan.GetComponent<WFPilotDutyComponent>(plain.Pilot).ReactToAttacks = false;
            SEntMan.GetComponent<WFPilotDutyComponent>(facing.Pilot).FaceThreats = true;
            start = Server.System<SharedTransformSystem>().GetWorldPosition(facing.Grid);
            plainHeading = SEntMan.GetComponent<WFPilotDutyComponent>(plain.Pilot).HoldHeading;
            Assert.That(pilots.GunBearings(facing.Grid), Is.Not.Empty, "The walled-off north leaves a gun side.");
            Server.System<WFCrewAlertSystem>().ReportShipThreat(facing.Grid, "navigation", threat);
            Server.System<WFCrewAlertSystem>().ReportShipThreat(plain.Grid, "navigation", threat);
        });

        var transform = Server.System<SharedTransformSystem>();
        var turned = false;
        for (var i = 0; i < 60 && !turned; i++)
        {
            await RunTicks(30);
            await Server.WaitAssertion(() =>
            {
                var toThreat = transform.GetWorldPosition(threat) - transform.GetWorldPosition(facing.Grid);
                var heading = Server.System<WFPilotDutySystem>().GunHeading(facing.Grid, toThreat)!.Value;
                var nose = transform.GetWorldRotation(facing.Grid) + new Angle(System.Math.PI);
                turned = System.Math.Abs(ShipSteeringSystem.ShortestAngleDistance(nose, heading).Theta) < 0.12;
            });
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(turned, Is.True, "The ship should have turned its gun side to the threat. " + DescribePilot(facing.Pilot, facing.Helm));
            var duty = SEntMan.GetComponent<WFPilotDutyComponent>(facing.Pilot);
            Assert.That(duty.Orders, Is.EqualTo(WFPilotOrder.Hold));
            Assert.That(Vector2.Distance(transform.GetWorldPosition(facing.Grid), start), Is.LessThan(40f), "It turned in place.");

            var other = SEntMan.GetComponent<WFPilotDutyComponent>(plain.Pilot);
            Assert.That(other.HoldHeading, Is.EqualTo(plainHeading), "A ship not told to face threats holds its heading.");
            Assert.That(System.Math.Abs(SEntMan.GetComponent<PhysicsComponent>(plain.Grid).AngularVelocity), Is.LessThan(0.02f));
        });
    }
}
