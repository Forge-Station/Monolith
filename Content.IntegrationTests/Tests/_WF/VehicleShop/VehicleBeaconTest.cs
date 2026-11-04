#nullable enable
using System.Linq;
using Content.Server._WF.VehicleShop;
using Content.Server.Cargo.Systems;
using Content.Shared._WF.VehicleShop;
using Content.Shared._WF.Vehicles.Hardpoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.VehicleShop;

/// <summary>
/// Delivery beacons open into their vehicle ten seconds after they are set off, not before, and not onto something in
/// the way; they sell at their catalogue listing and resell for less. A repair box makes a damaged vehicle whole.
/// </summary>
[TestFixture]
[TestOf(typeof(WFVehicleBeaconSystem))]
public sealed class VehicleBeaconTest
{
    private const string Beacon = "WFVehicleBeaconCivTruck";
    private const string Vehicle = "AU14VehicleCivTruck";
    private const string Catalog = "WFVehicleCatalogMechanic";

    private static int CountVehicles(IEntityManager entMan, MapId map)
    {
        return entMan.EntityQuery<MetaDataComponent>()
            .Count(meta => meta.EntityPrototype?.ID == Vehicle && entMan.GetComponent<TransformComponent>(meta.Owner).MapID == map);
    }

    [Test]
    public async Task BeaconOpensAfterItsCountdown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var beacons = server.System<WFVehicleBeaconSystem>();
        var ticksPerSecond = (int) Math.Round(1 / server.Timing.TickPeriod.TotalSeconds);
        EntityUid beacon = default, blocked = default;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            for (var x = -10; x < 30; x++)
            for (var y = -10; y < 30; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);

            beacon = entMan.SpawnEntity(Beacon, new EntityCoordinates(map.Grid.Owner, 5.5f, 5.5f));
            beacons.Arm((beacon, entMan.GetComponent<WFVehicleBeaconComponent>(beacon)), null);

            // A crate where the second would open keeps it shut.
            blocked = entMan.SpawnEntity(Beacon, new EntityCoordinates(map.Grid.Owner, 20.5f, 20.5f));
            entMan.SpawnEntity("CrateGenericSteel", new EntityCoordinates(map.Grid.Owner, 20.5f, 21.5f));
            beacons.Arm((blocked, entMan.GetComponent<WFVehicleBeaconComponent>(blocked)), null);
        });

        await server.WaitRunTicks(9 * ticksPerSecond);
        await server.WaitAssertion(() =>
        {
            Assert.That(CountVehicles(entMan, map.MapId), Is.Zero, "The beacon opened before its ten seconds were up.");
            Assert.That(entMan.EntityExists(beacon), Is.True, "The beacon went early.");
        });

        await server.WaitRunTicks(2 * ticksPerSecond);
        await server.WaitAssertion(() =>
        {
            Assert.That(CountVehicles(entMan, map.MapId), Is.EqualTo(1), "The beacon didn't open into its vehicle, or both did.");
            Assert.That(entMan.Deleted(beacon), Is.True, "The opened beacon is still there.");
            Assert.That(entMan.EntityExists(blocked), Is.True, "The blocked beacon opened anyway.");
            Assert.That(entMan.GetComponent<WFVehicleBeaconComponent>(blocked).OpensAt, Is.Null, "The blocked beacon is still counting down.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BeaconsSellAtTheirListing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var pricing = server.System<PricingSystem>();
            var catalog = server.ProtoMan.Index<WFVehicleCatalogPrototype>(Catalog);
            var beacons = server.ProtoMan.EnumeratePrototypes<EntityPrototype>()
                .Where(proto => !proto.Abstract && proto.TryGetComponent<WFVehicleBeaconComponent>(out _, entMan.ComponentFactory))
                .ToList();
            Assert.That(beacons, Has.Count.EqualTo(catalog.Vehicles.Count), "Every mechanic listing should have one beacon.");

            foreach (var proto in beacons)
            {
                proto.TryGetComponent<WFVehicleBeaconComponent>(out var comp, entMan.ComponentFactory);
                var listing = catalog.Vehicles.FirstOrDefault(entry => entry.Vehicle == comp!.Vehicle);
                Assert.That(listing, Is.Not.Null, $"{proto.ID} delivers a vehicle its catalogue doesn't list.");
                Assert.That(pricing.GetEstimatedVendPrice(proto), Is.EqualTo(listing!.Price), $"{proto.ID} isn't sold at its listing.");

                var spawned = entMan.SpawnEntity(proto.ID, new MapCoordinates(0, 0, map.MapId));
                Assert.That(pricing.GetPrice(spawned), Is.InRange(1, listing.Price - 1), $"{proto.ID} resells for nothing, or for what it cost.");
                entMan.DeleteEntity(spawned);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RepairBoxMakesAVehicleWhole()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var hardpoints = server.System<HardpointSystem>();
        EntityUid vehicle = default, kit = default;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            for (var x = -10; x < 10; x++)
            for (var y = -10; y < 10; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);

            vehicle = entMan.SpawnEntity(Vehicle, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            kit = entMan.SpawnEntity("WFVehicleRepairKit", new EntityCoordinates(map.Grid.Owner, 5.5f, 5.5f));
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var kits = server.System<WFVehicleRepairKitSystem>();
            var kitEnt = (kit, entMan.GetComponent<WFVehicleRepairKitComponent>(kit));
            Assert.That(kits.Apply(kitEnt, vehicle, null), Is.False, "A kit was spent on a vehicle that needed nothing.");

            // Every part, the frame and the wheels both, takes a beating.
            foreach (var (part, integrity) in hardpoints.GetIntegrityParts(vehicle))
                hardpoints.DamageHardpoint(vehicle, part, integrity.MaxIntegrity * 0.6f, integrity);

            Assert.That(hardpoints.GetMissingIntegrity(vehicle, out _), Is.GreaterThan(0f), "The vehicle took no damage.");
            Assert.That(kits.Apply(kitEnt, vehicle, null), Is.True, "The kit didn't fix the vehicle.");
            Assert.That(hardpoints.GetMissingIntegrity(vehicle, out _), Is.Zero, "The vehicle is still damaged.");
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() => Assert.That(entMan.Deleted(kit), Is.True, "The kit wasn't used up."));

        await pair.CleanReturnAsync();
    }
}
