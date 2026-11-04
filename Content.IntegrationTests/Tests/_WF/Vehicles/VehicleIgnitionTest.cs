#nullable enable
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>The dashboard's one key button: the key comes out of a pocket, then each press is the next step.</summary>
[TestFixture]
public sealed class VehicleIgnitionTest
{
    [Test]
    public async Task OneKeyButtonTakesTheKeyFromAPocketAndCycles()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var rig = await VehicleRigs.Build(pair);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var key = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            entMan.AddComponent<WFVehicleEngineComponent>(rig.Vehicle);
            var keyId = server.System<VehicleLockSystem>().EnsureVehicleKeyId(rig.Vehicle);
            var coords = entMan.GetComponent<TransformComponent>(rig.Driver).Coordinates;
            var inventory = server.System<InventorySystem>();
            if (!inventory.TryGetSlotEntity(rig.Driver, "jumpsuit", out _))
                Assert.That(inventory.TryEquip(rig.Driver, entMan.SpawnAtPosition("ClothingUniformJumpsuitColorGrey", coords), "jumpsuit", force: true), Is.True);
            key = entMan.SpawnAtPosition("RMCVehicleKey", coords);
            server.System<VehicleLockSystem>().BindKey((key, entMan.GetComponent<VehicleKeyComponent>(key)), keyId, rig.Vehicle);
            Assert.That(inventory.TryEquip(rig.Driver, key, "pocket1", force: true), Is.True);
            server.PlayerMan.SetAttachedEntity(pair.Player!, rig.Driver);
        });
        await VehicleRigs.Seat(rig);
        await pair.RunTicksSync(10);

        var engine = entMan.GetComponent<WFVehicleEngineComponent>(rig.Vehicle);
        var request = entMan.GetNetEntity(rig.Vehicle);
        async Task Press(WFVehicleControlRequest message)
        {
            await client.WaitPost(() => client.EntMan.EntityNetManager!.SendSystemNetworkMessage(message));
            await pair.RunTicksSync(5);
        }

        await Press(new WFVehicleControlRequest(request, false) { Ignition = true });
        Assert.That(engine.IgnitionKey, Is.EqualTo(key), "The key in the pocket did not go in.");
        Assert.That(engine.Electrics, Is.True, "Putting the key in should switch the accessories on.");
        Assert.That(engine.Running, Is.False);

        await Press(new WFVehicleControlRequest(request, false) { StartStop = true });
        Assert.That(engine.Running, Is.True, "Start/stop should start the engine.");

        await Press(new WFVehicleControlRequest(request, false) { StartStop = true });
        Assert.That(engine.Running, Is.False, "Start/stop should stop it again.");
        Assert.That(engine.Electrics, Is.True, "Stopping the engine leaves the accessories on.");

        await Press(new WFVehicleControlRequest(request, false) { StartStop = true });
        Assert.That(engine.Running, Is.True);

        // Taking the key out of a running engine stops it and hands the key back.
        await Press(new WFVehicleControlRequest(request, false) { Ignition = true });
        Assert.That(engine.IgnitionKey, Is.Null);
        Assert.That(engine.Running, Is.False, "The engine ran on without its key.");
        Assert.That(engine.Electrics, Is.False);
        Assert.That(server.System<SharedHandsSystem>().IsHolding(rig.Driver, key), Is.True, "The key should come out to hand.");

        await Press(new WFVehicleControlRequest(request, false) { StartStop = true });
        Assert.That(engine.Running, Is.False, "It started with no key in.");

        await pair.CleanReturnAsync();
    }
}
