#nullable enable
using System.Numerics;
using Content.Server.Cargo.Systems;
using Content.Server._WF.MilitaryVehicles;
using Content.Server._WF.NpcVehicleCrew;
using Content.Shared._WF.MilitaryVehicles;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.MilitaryVehicles;

/// <summary>
/// A military repair kit won't claim a vehicle its crew still holds, claims it once they are dead: repaired, re-keyed to
/// the user and marked salvaged. Crew vehicles, claimed ones and parts stripped from either appraise at the salvage
/// rate; a part fitted after the claim keeps its value. On anyone else's vehicle the kit only repairs.
/// </summary>
[TestFixture]
[TestOf(typeof(WFVehicleCaptureSystem))]
public sealed class VehicleCaptureTest
{
    private const string Vehicle = "WFCaptureTestVehicle";
    private const string Part = "WFCaptureTestPart";
    private const string Crew = "WFCaptureTestCrew";
    private const string Kit = "WFMilitaryRepairKit";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFCaptureTestPart
  parent: BaseItem
  categories: [ HideSpawnMenu ]
  components:
  - type: HardpointItem
    hardpointType: HardpointTypeCannon
    vehicleFamily: HardpointVehicleFamilyHumvee
    slotType: HardpointSlotTypeCannon
  - type: HardpointIntegrity
    maxIntegrity: 100
  - type: StaticPrice
    price: 2000

- type: entity
  parent: WFVehicleTestChassis
  id: WFCaptureTestVehicle
  components:
  - type: Damageable
    damageContainer: StructuralInorganic
  - type: Appearance
  - type: HardpointIntegrity
    maxIntegrity: 1000
  - type: WFVehicleEngine
  - type: VehicleEnter
    interiorPath: /Maps/_WF/Vehicles/Interiors/wf_test_interior.yml
    maxPassengers: 3
    enterDoAfter: 0.1
    exitDoAfter: 0.1
    entryPoints:
    - offset: 1,0
      radius: 0.6
      interiorCoords: 1.5,1.5
  - type: ItemSlots
    slots:
      gun:
        startingItem: WFCaptureTestPart
        whitelist:
          components:
          - HardpointItem
  - type: HardpointSlots
    vehicleFamily: HardpointVehicleFamilyHumvee
    slots:
    - id: gun
      hardpointType: HardpointTypeCannon
      slotType: HardpointSlotTypeCannon
      required: false

- type: wfVehicleCrew
  id: WFCaptureTestCrew
  vehicle: WFCaptureTestVehicle
  driver: WFVehicleDriver
  faction: WFCrew
  behaviour: Hold
  engagement: OnSight
  engageRange: 16
  standoff: 8
  pursues: false
";

    [Test]
    public async Task KitClaimsAnAbandonedCrewVehicleAsSalvage()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var capture = server.System<WFVehicleCaptureSystem>();
        var pricing = server.System<PricingSystem>();
        var slots = server.System<ItemSlotsSystem>();
        var containers = server.System<SharedContainerSystem>();
        EntityUid vehicle = default, user = default, kit = default, driver = default;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -8; x < 16; x++)
            for (var y = -8; y < 16; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));

            vehicle = entMan.SpawnEntity(Vehicle, new EntityCoordinates(map.Grid.Owner, new Vector2(5.5f, 5.5f)));
            server.System<SharedTransformSystem>().SetLocalRotation(vehicle, Angle.Zero);
            user = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, new Vector2(7.5f, 5.5f)));
            kit = entMan.SpawnEntity(Kit, new EntityCoordinates(map.Grid.Owner, new Vector2(7.5f, 5.5f)));
        });
        await server.WaitRunTicks(5);

        var vehicleNew = 0.0;
        var partNew = 0.0;
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<WFVehicleCrewSystem>().TryCrew(vehicle, Crew), Is.True, "The crew was not seated.");
            driver = entMan.GetComponent<WFVehicleCrewComponent>(vehicle).Driver!.Value;
            vehicleNew = pricing.GetEstimatedPrice(server.ProtoMan.Index(Vehicle));
            partNew = pricing.GetEstimatedPrice(server.ProtoMan.Index(Part));
            Assert.That(pricing.GetPrice(vehicle), Is.EqualTo((vehicleNew + partNew) * WFVehicleSalvageSystem.SalvageResale).Within(1),
                "A crew's vehicle appraises above the salvage rate.");

            var kitEnt = (kit, entMan.GetComponent<WFMilitaryRepairKitComponent>(kit));
            Assert.That(capture.Apply(kitEnt, vehicle, user), Is.False, "The kit claimed a vehicle with its driver alive at the wheel.");
            server.System<MobStateSystem>().ChangeMobState(driver, MobState.Dead);
        });
        await server.WaitRunTicks(10);

        EntityUid part = default;
        var oldKey = string.Empty;
        await server.WaitAssertion(() =>
        {
            // Stripped before the claim: salvage.
            Assert.That(slots.TryGetSlot(vehicle, "gun", out var slot), Is.True);
            part = slot!.Item!.Value;
            Assert.That(containers.Remove(part, slot.ContainerSlot!), Is.True, "Test setup: the part would not come off.");
            Assert.That(entMan.HasComponent<WFSalvagedComponent>(part), Is.True, "A part stripped from a crew's vehicle isn't salvage.");
            Assert.That(pricing.GetPrice(part), Is.EqualTo(partNew * WFVehicleSalvageSystem.SalvageResale).Within(1),
                "A stripped part appraises above the salvage rate.");

            oldKey = entMan.GetComponent<VehicleLockComponent>(vehicle).KeyId!;
            var kitEnt = (kit, entMan.GetComponent<WFMilitaryRepairKitComponent>(kit));
            Assert.That(capture.Apply(kitEnt, vehicle, user), Is.True, "The kit didn't claim the vehicle of a dead crew.");
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.Deleted(kit), Is.True, "The kit wasn't used up.");
                Assert.That(entMan.HasComponent<WFVehicleCrewComponent>(vehicle), Is.False, "The vehicle still has its NPC brain.");
                Assert.That(entMan.HasComponent<WFSalvagedComponent>(vehicle), Is.True, "The claimed vehicle isn't marked salvaged.");

                var keyId = entMan.GetComponent<VehicleLockComponent>(vehicle).KeyId;
                Assert.That(keyId, Is.Not.EqualTo(oldKey), "The vehicle wasn't re-keyed.");
                var held = false;
                foreach (var item in server.System<SharedHandsSystem>().EnumerateHeld(user))
                    held |= entMan.TryGetComponent<VehicleKeyComponent>(item, out var key) && key.KeyId == keyId;
                Assert.That(held, Is.True, "The user wasn't handed the new key.");
            }

            // A part the new owner fits keeps its full value.
            var fresh = entMan.SpawnEntity(Part, new EntityCoordinates(map.Grid.Owner, new Vector2(7.5f, 5.5f)));
            Assert.That(slots.TryGetSlot(vehicle, "gun", out var slot), Is.True);
            Assert.That(containers.Insert(fresh, slot!.ContainerSlot!), Is.True, "Test setup: the new part would not fit.");
            Assert.That(pricing.GetPrice(vehicle), Is.EqualTo(vehicleNew * WFVehicleSalvageSystem.SalvageResale + partNew).Within(1),
                "A claimed vehicle with a new part isn't priced as salvage plus the part.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task KitOnlyRepairsAnOrdinaryVehicle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var capture = server.System<WFVehicleCaptureSystem>();
        var hardpoints = server.System<HardpointSystem>();
        EntityUid vehicle = default, kit = default;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            for (var x = -8; x < 8; x++)
            for (var y = -8; y < 8; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);

            vehicle = entMan.SpawnEntity(Vehicle, new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f)));
            kit = entMan.SpawnEntity(Kit, new EntityCoordinates(map.Grid.Owner, new Vector2(4.5f, 4.5f)));
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var kitEnt = (kit, entMan.GetComponent<WFMilitaryRepairKitComponent>(kit));
            var keyId = entMan.GetComponent<VehicleLockComponent>(vehicle).KeyId;
            Assert.That(capture.Apply(kitEnt, vehicle, null), Is.False, "A kit was spent on a vehicle that needed nothing.");

            foreach (var (part, integrity) in hardpoints.GetIntegrityParts(vehicle))
                hardpoints.DamageHardpoint(vehicle, part, integrity.MaxIntegrity * 0.6f, integrity);

            Assert.That(capture.Apply(kitEnt, vehicle, null), Is.True, "The kit didn't fix a damaged vehicle.");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(hardpoints.GetMissingIntegrity(vehicle, out _), Is.Zero, "The vehicle is still damaged.");
                Assert.That(entMan.GetComponent<VehicleLockComponent>(vehicle).KeyId, Is.EqualTo(keyId), "Someone's own vehicle was re-keyed.");
                Assert.That(entMan.HasComponent<WFSalvagedComponent>(vehicle), Is.False, "Someone's own vehicle was marked salvaged.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
