#nullable enable
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Turret;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

// The rig stands in for a seated gunner by writing the turret and seat state directly.
#pragma warning disable RA0002

/// <summary>The dashboard's turret lock flips the gunner's selected turret between holding its bearing and turning with the hull.</summary>
[TestFixture]
public sealed class VehicleTurretLockTest
{
    [Test]
    public async Task TheGunnerTogglesTheTurretLock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid vehicle = default, turret = default, gunner = default;
        await server.WaitPost(() =>
        {
            vehicle = entMan.SpawnEntity(null, map.GridCoords);
            entMan.EnsureComponent<HardpointSlotsComponent>(vehicle);
            entMan.EnsureComponent<ItemSlotsComponent>(vehicle);
            turret = entMan.SpawnEntity(null, map.GridCoords);
            var turretComp = entMan.EnsureComponent<VehicleTurretComponent>(turret);
            turretComp.RotateToCursor = true;
            turretComp.StabilizedRotation = true;
            gunner = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var weapons = entMan.EnsureComponent<VehicleWeaponsComponent>(vehicle);
            weapons.Operator = gunner;
            weapons.OperatorSelections[gunner] = turret;
            server.PlayerMan.SetAttachedEntity(pair.Player!, gunner);
        });
        await pair.RunTicksSync(5);

        async Task Press(bool stabilized)
        {
            var net = entMan.GetNetEntity(vehicle);
            await client.WaitPost(() => client.EntMan.EntityNetManager!.SendSystemNetworkMessage(new WFTurretStabilizeRequestEvent(net, stabilized)));
            await pair.RunTicksSync(5);
        }

        await Press(false);
        await server.WaitAssertion(() => Assert.That(entMan.GetComponent<VehicleTurretComponent>(turret).StabilizedRotation, Is.False,
            "Unlocking should let the turret turn with the hull."));

        await Press(true);
        await server.WaitAssertion(() => Assert.That(entMan.GetComponent<VehicleTurretComponent>(turret).StabilizedRotation, Is.True,
            "Locking should hold the turret's bearing."));

        // Someone who isn't working the guns can't flip it.
        await server.WaitPost(() => entMan.GetComponent<VehicleWeaponsComponent>(vehicle).Operator = null);
        await Press(false);
        await server.WaitAssertion(() => Assert.That(entMan.GetComponent<VehicleTurretComponent>(turret).StabilizedRotation, Is.True));

        await pair.CleanReturnAsync();
    }
}
