#nullable enable
using System.Collections.Generic;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared._WF.Vehicles.Grid.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Streets;

/// <summary>Street furniture in a vehicle's way is smashed, and the heavy pieces only by the vehicles heavy enough.</summary>
[TestFixture]
public sealed class StreetFurnitureSmashTest
{
    /// <summary>Prototype, the lightest vehicle that breaks it, and the lightest that drives on through it.</summary>
    private static readonly (string Id, VehicleWeightClass? Destroy, VehicleWeightClass? Continue)[] Cases =
    {
        ("RMCStreetlight", null, null),
        ("RMCStreetlightBroken", null, null),
        ("RMCTrafficLight", null, null),
        ("RMCTrafficLightBrokenAlt", null, null),
        ("AU14Streetlight", null, null),
        ("RMCBenchLeft", null, null),
        ("WFStreetsTrashBin", null, null),
        ("AU14PropTrafficDirector1", null, null),
        ("RMCPhoneboxOff", null, null),
        ("RMCPhoneboxBroken", null, null),
        ("AU14PropTent1", null, null),
        ("RMCPropSupermartShelf1", null, null),
        // Owner, 2026-10-17: small things never stop a vehicle.
        ("AU14DumpsterBlue", null, null),
        ("RMCPlanter", null, null),
        ("RMCFuelPump", VehicleWeightClass.Medium, null),
        ("RMCConcreteStatue", VehicleWeightClass.Medium, null),
    };

    [Test]
    public async Task StreetFurnitureIsSmashed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var spawned = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            for (var i = 0; i < Cases.Length; i++)
            {
                spawned.Add(entMan.SpawnEntity(Cases[i].Id, new EntityCoordinates(map.Grid.Owner, i * 4 + 0.5f, 0.5f)));
            }
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var mover = server.System<GridVehicleMoverSystem>();
            using (Assert.EnterMultipleScope())
            {
                for (var i = 0; i < Cases.Length; i++)
                {
                    var (id, destroy, through) = Cases[i];
                    if (!mover.TryGetSmashable(spawned[i], out var settings))
                    {
                        Assert.Fail($"{id} stops a vehicle dead instead of being smashed.");
                        continue;
                    }

                    Assert.That(settings.MinDestroyWeightClass, Is.EqualTo(destroy), $"{id}: the lightest vehicle that breaks it.");
                    Assert.That(settings.MinContinueWeightClass, Is.EqualTo(through), $"{id}: the lightest vehicle that drives on through it.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
