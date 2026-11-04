#nullable enable
using Content.Server._WF.Vehicles.Audio;
using Content.Shared._WF.Vehicles.Core;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>Someone in a vehicle's cabin keeps the world round the hull in view, to hear it through the hull.</summary>
[TestFixture]
[TestOf(typeof(WFVehicleCabinHearingSystem))]
public sealed class VehicleCabinHearingTest
{
    [Test]
    public async Task TheCabinHearsAroundTheHull()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var outside = await pair.CreateTestMap();
        EntityUid vehicle = default, body = default;
        await server.WaitPost(() =>
        {
            vehicle = entMan.SpawnEntity(null, outside.GridCoords);
            body = entMan.SpawnEntity("MobHuman", outside.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, body);
#pragma warning disable RA0002 // A test seats the body in the cabin directly.
            entMan.EnsureComponent<VehicleInteriorOccupantComponent>(body).Vehicle = vehicle;
#pragma warning restore RA0002
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(pair.Player!.ViewSubscriptions, Does.Contain(vehicle), "The cabin isn't kept in view of the hull.");
            entMan.RemoveComponent<VehicleInteriorOccupantComponent>(body);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(pair.Player!.ViewSubscriptions, Does.Not.Contain(vehicle), "Leaving the cabin should drop the view.");
            entMan.DeleteEntity(body);
            entMan.DeleteEntity(vehicle);
        });
        await pair.CleanReturnAsync();
    }
}
