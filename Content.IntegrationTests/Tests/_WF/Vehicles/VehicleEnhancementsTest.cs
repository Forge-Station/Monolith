#nullable enable
using Content.Shared._WF.Vehicles.Core;
using Content.Server.Power.Components;
using Content.Shared._WF.Vehicles.Falling;
using Content.Shared._WF.Planets.Parachute;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using System.Numerics;
using Content.Shared.Movement.Systems;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>Braking must retain travel during deceleration, and an empty engine must stop powering the chassis.</summary>
[TestFixture]
public sealed class VehicleEnhancementsTest
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task OverlappingVehiclesCanReverseApart(bool freeSteering)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, minX: -20,
            proto: freeSteering ? "WFVehicleTestSteeredChassis" : "WFVehicleTestChassis", facingRight: true);
        await pair.Server.WaitPost(() =>
            pair.Server.EntMan.SpawnEntity("WFVehicleTestChassis", new EntityCoordinates(rig.Grid, 4f, 2.5f)));
        await VehicleRigs.Seat(rig);
        await pair.Server.WaitPost(() => VehicleRigs.Hold(rig, freeSteering ? MoveButtons.Down : MoveButtons.Left));
        await pair.Server.WaitRunTicks(120);
        await pair.Server.WaitAssertion(() =>
            Assert.That(VehicleRigs.LocalPosition(rig).X, Is.LessThan(1f),
                "An existing overlap must allow a separating reverse move instead of trapping the driver."));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CabinConsumerUsesBatteryAndRestoresSupplyWhenRemoved()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair);
        var outside = await pair.CreateTestMap();
        var device = EntityUid.Invalid;
        var em = pair.Server.EntMan;
        await pair.Server.WaitPost(() =>
        {
#pragma warning disable RA0002 // Link the isolated test cabin without loading a full vehicle interior.
            em.EnsureComponent<VehicleInteriorLinkComponent>(rig.Map.MapUid).Vehicle = rig.Vehicle;
#pragma warning restore RA0002
            var engine = em.AddComponent<WFVehicleEngineComponent>(rig.Vehicle);
            engine.Electrics = true;
            engine.BatteryCharge = 10000f;
            device = em.SpawnEntity(null, rig.Map.GridCoords);
            em.AddComponent<ApcPowerReceiverComponent>(device).Load = 1000f;
        });
        await pair.Server.WaitRunTicks(60);
        var charge = 0f;
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(device).Powered, Is.True);
            var engine = em.GetComponent<WFVehicleEngineComponent>(rig.Vehicle);
            charge = engine.BatteryCharge;
            Assert.That(charge, Is.LessThan(9500f));
            engine.Electrics = false;
        });
        await pair.Server.WaitRunTicks(60);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(device).Powered, Is.False);
            Assert.That(em.GetComponent<WFVehicleEngineComponent>(rig.Vehicle).BatteryCharge, Is.EqualTo(charge));
            pair.Server.System<SharedTransformSystem>().SetCoordinates(device, outside.GridCoords);
        });
        await pair.Server.WaitRunTicks(30);
        await pair.Server.WaitAssertion(() =>
        {
            var receiver = em.GetComponent<ApcPowerReceiverComponent>(device);
            Assert.That(receiver.NeedsPower, Is.True);
            Assert.That(receiver.PowerDisabled, Is.False);
            Assert.That(receiver.Powered, Is.False, "A portable machine kept free cabin power outside the vehicle.");
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task OffCentreVehicleImpactTransfersTravelAndSpin(bool freeSteering)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair,
            proto: freeSteering ? "WFVehicleTestSteeredChassis" : "WFVehicleTestChassis", facingRight: true);
        var target = EntityUid.Invalid;
        await pair.Server.WaitPost(() => target = pair.Server.EntMan.SpawnEntity("WFVehicleTestSteeredChassis",
            new EntityCoordinates(rig.Grid, 6f, 3f)));
        await VehicleRigs.Seat(rig);
        await pair.Server.WaitPost(() => VehicleRigs.Hold(rig, freeSteering ? MoveButtons.Up : MoveButtons.Right));
        await pair.Server.WaitRunTicks(90);
        await pair.Server.WaitAssertion(() =>
        {
            var xform = pair.Server.EntMan.GetComponent<TransformComponent>(target);
            Assert.That(xform.LocalPosition.X, Is.GreaterThan(6.05f), "The struck chassis received no travel.");
            Assert.That(Math.Abs(xform.LocalRotation.Theta), Is.GreaterThan(0.01), "An off-centre strike produced no rotation.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NormalKeyPairsAndOnlyUnlocksItsOwnVehicle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var locks = pair.Server.System<VehicleLockSystem>();
            var key = em.SpawnEntity("RMCVehicleKey", rig.Map.GridCoords);
            var keyComponent = em.GetComponent<VehicleKeyComponent>(key);
            locks.TryUseKeyOnVehicle((key, keyComponent), rig.Vehicle, rig.Driver);
            var vehicleLock = em.GetComponent<VehicleLockComponent>(rig.Vehicle);
            Assert.That(keyComponent.KeyId, Is.EqualTo(vehicleLock.KeyId).And.Not.Null);
            locks.TryUseKeyOnVehicle((key, keyComponent), rig.Vehicle, rig.Driver);
            Assert.That(vehicleLock.Locked, Is.True);
            var wrongKey = em.SpawnEntity("RMCVehicleKey", rig.Map.GridCoords);
            var wrongComponent = em.GetComponent<VehicleKeyComponent>(wrongKey);
            locks.TryUseKeyOnVehicle((wrongKey, wrongComponent), rig.Vehicle, rig.Driver);
            Assert.That(wrongComponent.KeyId, Is.Null, "A new key claimed a locked vehicle.");
            Assert.That(vehicleLock.Locked, Is.True);
            locks.TryUseKeyOnVehicle((key, keyComponent), rig.Vehicle, rig.Driver);
            Assert.That(vehicleLock.Locked, Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BatteryDrainsOnlyWithElectricsAndChargesOnlyWithEngine()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair);
        await pair.Server.WaitPost(() =>
        {
            var engine = pair.Server.EntMan.AddComponent<WFVehicleEngineComponent>(rig.Vehicle);
            engine.BatteryCharge = 10000f;
        });
        await pair.Server.WaitRunTicks(60);
        await pair.Server.WaitAssertion(() =>
        {
            var engine = pair.Server.EntMan.GetComponent<WFVehicleEngineComponent>(rig.Vehicle);
            Assert.That(engine.BatteryCharge, Is.EqualTo(10000f), "Ignition OFF still drained the battery.");
            engine.Electrics = true;
        });
        await pair.Server.WaitRunTicks(60);
        await pair.Server.WaitAssertion(() =>
        {
            var engine = pair.Server.EntMan.GetComponent<WFVehicleEngineComponent>(rig.Vehicle);
            Assert.That(engine.BatteryCharge, Is.LessThan(10000f));
            engine.Running = true;
        });
        await pair.Server.WaitRunTicks(60);
        await pair.Server.WaitAssertion(() =>
        {
            var engine = pair.Server.EntMan.GetComponent<WFVehicleEngineComponent>(rig.Vehicle);
            Assert.That(engine.BatteryCharge, Is.GreaterThan(10000f));
            engine.Running = false;
            engine.BatteryCharge = engine.StarterEnergy + 1f;
        });
        await pair.Server.WaitRunTicks(60);
        await pair.Server.WaitAssertion(() =>
        {
            var engine = pair.Server.EntMan.GetComponent<WFVehicleEngineComponent>(rig.Vehicle);
            Assert.That(engine.BatteryCharge, Is.EqualTo(engine.StarterEnergy));
            Assert.That(engine.Powered, Is.False, "Accessories consumed the starter reserve.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ParachutedVehicleLandsAndReturnsPack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var levels = await FallLevelRigs.Build(pair, "WFFallTestVehicle");
        var server = pair.Server;
        var em = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var attempt = new WFParachuteAttachAttemptEvent(false);
            em.EventBus.RaiseLocalEvent(levels.Vehicle, ref attempt);
            Assert.That(attempt.Allowed, Is.True);
            em.EnsureComponent<WFParachutedComponent>(levels.Vehicle);
            Assert.That(em.HasComponent<CEZPhysicsComponent>(levels.Vehicle), Is.True);
            server.System<SharedTransformSystem>().SetCoordinates(levels.Vehicle,
                new EntityCoordinates(levels.Rig.Map.MapUid, new Vector2(25.5f, 2.5f)));
            server.System<CESharedZLevelsSystem>().SetZVelocity(levels.Vehicle, -2f);
        });
        var landed = false;
        for (var i = 0; i < 300 && !landed; i++)
        {
            await server.WaitRunTicks(2);
            await server.WaitPost(() => landed = FallLevelRigs.MapOf(levels, levels.Vehicle) == levels.LowerMap &&
                !em.HasComponent<WFParachutedComponent>(levels.Vehicle));
        }
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(landed, Is.True, "Vehicle never completed its canopy descent.");
            Assert.That(em.HasComponent<CEZPhysicsComponent>(levels.Vehicle), Is.False);
            Assert.That(em.GetComponent<DamageableComponent>(levels.Vehicle).TotalDamage.Float(), Is.Zero);
            var packs = 0;
            var query = em.EntityQueryEnumerator<WFParachuteComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var xform))
            {
                if (xform.MapID == levels.LowerMap)
                    packs++;
            }
            Assert.That(packs, Is.EqualTo(1));
        });
        await FallLevelRigs.Teardown(levels);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReverseBrakesOverSeveralSteps()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, proto: "WFVehicleTestSteeredChassis", facingRight: true);
        await VehicleRigs.Seat(rig);
        await pair.Server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Up));
        await pair.Server.WaitRunTicks(45);
        var speed = 0f;
        var position = 0f;
        await pair.Server.WaitPost(() =>
        {
            speed = VehicleRigs.Mover(rig).CurrentSpeed;
            position = VehicleRigs.LocalPosition(rig).X;
            VehicleRigs.Hold(rig, MoveButtons.Down);
        });
        await pair.Server.WaitRunTicks(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(VehicleRigs.Mover(rig).CurrentSpeed, Is.GreaterThan(0f).And.LessThan(speed));
            Assert.That(VehicleRigs.LocalPosition(rig).X, Is.GreaterThan(position));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EmptyFuelStopsEngineAndPreventsThrottle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, proto: "WFVehicleTestSteeredChassis", facingRight: true);
        await VehicleRigs.Seat(rig);
        await pair.Server.WaitPost(() =>
        {
            var engine = pair.Server.EntMan.AddComponent<WFVehicleEngineComponent>(rig.Vehicle);
            engine.Running = true;
            engine.Fuel = 0.001f;
            VehicleRigs.Hold(rig, MoveButtons.Up);
        });
        await pair.Server.WaitRunTicks(120);
        await pair.Server.WaitAssertion(() =>
        {
            var engine = pair.Server.EntMan.GetComponent<WFVehicleEngineComponent>(rig.Vehicle);
            Assert.That(engine.Fuel, Is.Zero);
            Assert.That(engine.Running, Is.False);
            Assert.That(VehicleRigs.Mover(rig).CurrentSpeed, Is.Zero);
        });
        await pair.CleanReturnAsync();
    }
}
