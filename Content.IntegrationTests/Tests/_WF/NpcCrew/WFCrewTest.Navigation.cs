#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    private async Task<(EntityUid Grid, EntityUid Pilot, EntityUid Helm)> PoweredNavigationShip(Vector2 origin)
    {
        var grid = await CreateDeck(origin, 7, true);
        EntityUid pilot = default, helm = default;
        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<ShuttleComponent>(grid);
            helm = SEntMan.SpawnAtPosition(TestHelm, new EntityCoordinates(grid, new Vector2(3.5f)));
            for (var i = 0; i < 4; i++)
            {
                var thruster = SEntMan.SpawnAtPosition("WFTestCrewThruster", new EntityCoordinates(grid, new Vector2(1.5f + i, 5.5f)));
                Server.System<SharedTransformSystem>().SetLocalRotation(thruster, Angle.FromDegrees(i * 90));
                Server.System<ThrusterSystem>().EnableThruster(thruster, SEntMan.GetComponent<ThrusterComponent>(thruster));
            }
            var gyro = SEntMan.SpawnAtPosition("WFTestCrewGyroscope", new EntityCoordinates(grid, new Vector2(5.5f, 4.5f)));
            Server.System<ThrusterSystem>().EnableThruster(gyro, SEntMan.GetComponent<ThrusterComponent>(gyro));
            pilot = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Pilot,
                new EntityCoordinates(grid, new Vector2(2.5f, 3.5f)), "navigation")!.Value;
            SEntMan.GetComponent<HTNComponent>(pilot).Enabled = false;
        });
        await RunTicks(5);
        await WaitUntil(() => Server.System<WFPilotDutySystem>().TryTakeHelm(pilot, helm), 180,
            () => DescribePilot(pilot, helm));
        return (grid, pilot, helm);
    }

    /// <summary>Powered orbit orders maintain distance and make real progress at their requested slow speed.</summary>
    [TestCase(WFCrewObjectiveKind.Circle, 150f, 4f, false)]
    [TestCase(WFCrewObjectiveKind.Attack, 350f, 6f, false)]
    [TestCase(WFCrewObjectiveKind.Circle, 150f, 2f, true)]
    public async Task CrewNavigationOrbitPhysicallyRespectsRangeAndSpeed(WFCrewObjectiveKind kind, float radius, float speed, bool customProfile)
    {
        var target = await CreateDeck(new Vector2(1000, 1000), 31, true);
        var ship = await PoweredNavigationShip(new Vector2(1015.5f + radius, 1015.5f));
        var initial = Vector2.Zero;
        var minimum = float.MaxValue;
        var maximum = 0f;
        var peakSpeed = 0f;
        var peakTurn = 0f;
        var turnLimit = customProfile ? .05f : .15f;
        var measurements = string.Empty;
        await Server.WaitAssertion(() =>
        {
            initial = Server.System<SharedTransformSystem>().GetWorldPosition(ship.Grid);
            if (customProfile)
                Assert.That(Server.System<WFPilotDutySystem>().SetNavigation(ship.Pilot,
                    new WFCrewNavigationSettings { CircleSpeed = speed, MaximumTurnRate = turnLimit }), Is.True);
            Assert.That(Server.System<WFCrewObjectiveSystem>().SetQueue(ship.Grid, "navigation", new List<WFCrewObjective>
            {
                new() { Kind = kind, Target = SEntMan.GetNetEntity(target), Range = kind == WFCrewObjectiveKind.Attack ? 1 : radius },
            }), Is.True);
        });
        for (var i = 0; i < 180; i++)
        {
            await RunTicks(30);
            await Server.WaitAssertion(() =>
            {
                var transforms = Server.System<SharedTransformSystem>();
                var center = transforms.ToMapCoordinates(new EntityCoordinates(target, SEntMan.GetComponent<MapGridComponent>(target).LocalAABB.Center)).Position;
                var distance = Vector2.Distance(transforms.GetWorldPosition(ship.Grid), center);
                minimum = MathF.Min(minimum, distance);
                maximum = MathF.Max(maximum, distance);
                peakSpeed = MathF.Max(peakSpeed, SEntMan.GetComponent<PhysicsComponent>(ship.Grid).LinearVelocity.Length());
                peakTurn = MathF.Max(peakTurn, MathF.Abs(SEntMan.GetComponent<PhysicsComponent>(ship.Grid).AngularVelocity));
            });
        }
        await Server.WaitAssertion(() =>
        {
            var progress = Vector2.Distance(initial, Server.System<SharedTransformSystem>().GetWorldPosition(ship.Grid));
            var diagnostics = $"range={minimum:0.0}..{maximum:0.0} peakSpeed={peakSpeed:0.00} peakTurn={peakTurn:0.000} progress={progress:0.0}; {DescribePilot(ship.Pilot, ship.Helm)}";
            measurements = diagnostics;
            Assert.That(minimum, Is.GreaterThan(radius - 25), diagnostics);
            Assert.That(maximum, Is.LessThan(radius + 40), diagnostics);
            Assert.That(peakSpeed, Is.LessThan(speed + .5f), diagnostics);
            Assert.That(peakTurn, Is.LessThanOrEqualTo(turnLimit + .01f), diagnostics);
            Assert.That(progress, Is.GreaterThan(50), diagnostics);
        });
        TestContext.Out.WriteLine(measurements);
    }

    /// <summary>Holding arrests drift and keeps the same world anchor when the pilot retakes the helm.</summary>
    [Test]
    public async Task CrewNavigationHoldSettlesAndRetainsAnchor()
    {
        var ship = await PoweredNavigationShip(new Vector2(500, 500));
        EntityCoordinates anchor = default;
        await Server.WaitPost(() =>
        {
            Server.System<WFPilotDutySystem>().Hold(ship.Pilot);
            anchor = SEntMan.GetComponent<WFPilotDutyComponent>(ship.Pilot).HoldPosition!.Value;
            Server.System<SharedPhysicsSystem>().SetLinearVelocity(ship.Grid, new Vector2(2, 1));
            Server.System<SharedPhysicsSystem>().SetAngularVelocity(ship.Grid, .1f);
        });
        await RunTicks(900);
        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.GetComponent<PhysicsComponent>(ship.Grid);
            Assert.That(body.LinearVelocity.Length(), Is.LessThan(.1));
            Assert.That(MathF.Abs(body.AngularVelocity), Is.LessThan(.01));
            Server.System<WFPilotDutySystem>().ReleaseHelm(ship.Pilot);
            Server.System<SharedTransformSystem>().SetCoordinates(ship.Grid, new EntityCoordinates(MapData.MapUid, anchor.Position + new Vector2(10, 0)));
            Assert.That(Server.System<WFPilotDutySystem>().TryTakeHelm(ship.Pilot, ship.Helm), Is.True);
            Assert.That(SEntMan.GetComponent<WFPilotDutyComponent>(ship.Pilot).HoldPosition, Is.EqualTo(anchor));
        });
        await RunTicks(3600);
        var settled = Vector2.Zero;
        await Server.WaitAssertion(() =>
        {
            settled = Server.System<SharedTransformSystem>().GetWorldPosition(ship.Grid);
            Assert.That(Vector2.Distance(settled, anchor.Position), Is.LessThan(5), DescribePilot(ship.Pilot, ship.Helm));
        });
        await RunTicks(600);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Vector2.Distance(settled, Server.System<SharedTransformSystem>().GetWorldPosition(ship.Grid)), Is.LessThan(.2));
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(ship.Grid).LinearVelocity.Length(), Is.LessThan(.1));
            Assert.That(MathF.Abs(SEntMan.GetComponent<PhysicsComponent>(ship.Grid).AngularVelocity), Is.LessThan(.01));
        });
    }

    /// <summary>An escort crosses to the far formation slot without entering its leader's hull.</summary>
    [Test]
    public async Task CrewNavigationEscortAvoidsLeaderDuringTurn()
    {
        var leader = await CreateDeck(new Vector2(1000, 1000), 41, true);
        var ship = await PoweredNavigationShip(new Vector2(1000, 1200));
        var start = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            Server.System<WFPilotDutySystem>().Escort(ship.Pilot, leader, 1);
            var offset = SEntMan.GetComponent<WFPilotDutyComponent>(ship.Pilot).EscortOffset!.Value;
            start = new Vector2(1020.5f) - Vector2.Normalize(offset - new Vector2(20.5f)) * 180;
            Server.System<SharedTransformSystem>().SetCoordinates(ship.Grid, new EntityCoordinates(MapData.MapUid, start));
        });
        var minimum = float.MaxValue;
        var measurements = string.Empty;
        for (var i = 0; i < 240; i++)
        {
            await RunTicks(30);
            await Server.WaitPost(() =>
            {
                var transforms = Server.System<SharedTransformSystem>();
                if (i == 120)
                    transforms.SetWorldRotation(leader, Angle.FromDegrees(90));
                var center = transforms.ToMapCoordinates(new EntityCoordinates(leader, new Vector2(20.5f))).Position;
                minimum = MathF.Min(minimum, Vector2.Distance(transforms.GetWorldPosition(ship.Grid), center));
            });
        }
        await Server.WaitAssertion(() =>
        {
            var position = Server.System<SharedTransformSystem>().GetWorldPosition(ship.Grid);
            var offset = SEntMan.GetComponent<WFPilotDutyComponent>(ship.Pilot).EscortOffset!.Value;
            var slot = Server.System<SharedTransformSystem>().ToMapCoordinates(new EntityCoordinates(leader, offset)).Position;
            measurements = $"escort minimumCenterDistance={minimum:0.0} progress={Vector2.Distance(start, position):0.0} slotDistance={Vector2.Distance(slot, position):0.0}";
            Assert.That(minimum, Is.GreaterThan(70), $"minimum center clearance={minimum}; {DescribePilot(ship.Pilot, ship.Helm)}");
            Assert.That(Vector2.Distance(start, position), Is.GreaterThan(50), DescribePilot(ship.Pilot, ship.Helm));
            Assert.That(Vector2.Distance(slot, position), Is.LessThan(15), measurements);
        });
        TestContext.Out.WriteLine(measurements);
    }
}
