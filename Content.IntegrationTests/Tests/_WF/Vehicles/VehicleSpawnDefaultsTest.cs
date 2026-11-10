#nullable enable
using System.Collections.Generic;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>Every concrete vehicle in the game, the Bison included, carries the defaults a Wolfgate vehicle needs.</summary>
[TestFixture]
public sealed class VehicleSpawnDefaultsTest
{
    private const string CarrierId = "CMUCargoCarrier";

    /// <summary>Whether a vehicle id is one the tests declare for themselves, which every pair loads.</summary>
    public static bool IsTestVehicle(string id) => id.StartsWith("WF") && id.Contains("Test");

    /// <summary>
    /// Every concrete vehicle is not saved, survives cleanup and an arriving ship, has a price, and falls through z-levels:
    /// one without <c>VehicleFall</c> would stop dead at an edge with a level below.
    /// </summary>
    [Test]
    public async Task EveryVehicleHasTheWolfgateSpawnDefaults()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ProtoMan;
        var checkedVehicles = new List<string>();

        foreach (var prototype in prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            // The exit markers inside interiors carry RMCVehicle too, but they are not vehicles that spawn and drive.
            if (prototype.Abstract || !prototype.Components.ContainsKey("RMCVehicle") || !prototype.Components.ContainsKey("GridVehicleMover"))
                continue;

            checkedVehicles.Add(prototype.ID);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(prototype.MapSavable, Is.False, $"{prototype.ID} would be saved with a dead interior or controller.");
                Assert.That(prototype.Components.ContainsKey("CleanupImmune"), Is.True, $"{prototype.ID} is not cleanup immune.");
                Assert.That(prototype.Components.ContainsKey("FTLSmashImmune"), Is.True, $"{prototype.ID} is not safe from an arriving ship.");
                Assert.That(prototype.Components.ContainsKey("StaticPrice"), Is.True, $"{prototype.ID} has no price.");

                // The vehicles the tests declare (WF...Test...) are built to drive nowhere near an edge.
                if (!IsTestVehicle(prototype.ID))
                    Assert.That(prototype.Components.ContainsKey("VehicleFall"), Is.True, $"{prototype.ID} cannot fall through z-levels.");
            }
        }

        Assert.That(checkedVehicles, Does.Contain(CarrierId));
        await pair.CleanReturnAsync();
    }
}
