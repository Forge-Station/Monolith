#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcVehicleCrew;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.NpcVehicleCrew;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.CCVar;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.NpcVehicleCrew;

/// <summary>
/// An NPC crew in an armed test vehicle: seated and driving, shooting a hostile but not a friend, bailing out of a broken
/// frame, and stopping when the driver dies.
/// </summary>
[TestFixture]
public sealed class WFVehicleCrewTest
{
    private const string Vehicle = "WFVehicleCrewTestVehicle";
    private const string WeaponsSeat = "WFVehicleCrewTestWeaponsSeat";
    private const string Crew = "WFVehicleCrewTestCrew";
    private const string CrewWithTrooper = "WFVehicleCrewTestCrewTrooper";

    private static readonly Vector2 VehicleSpot = new(5.5f, 5.5f);

    /// <summary>Where the weapons seat goes on <c>wf_test_interior.yml</c>, which has only a driver seat.</summary>
    private static readonly Vector2 WeaponsSeatSpot = new(1.5f, 2.5f);

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFVehicleCrewTestBullet
  parent: BaseBullet
  categories: [ HideSpawnMenu ]

- type: entity
  id: WFVehicleCrewTestCartridge
  categories: [ HideSpawnMenu ]
  components:
  - type: CartridgeAmmo
    proto: WFVehicleCrewTestBullet
    deleteOnSpawn: true

- type: entity
  id: WFVehicleCrewTestGun
  parent: BaseItem
  categories: [ HideSpawnMenu ]
  components:
  - type: HardpointItem
    hardpointType: HardpointTypeCannon
    vehicleFamily: HardpointVehicleFamilyHumvee
    slotType: HardpointSlotTypeCannon
  - type: HardpointIntegrity
    maxIntegrity: 100
  - type: VehicleTurret
    rotateToCursor: true
  - type: GunUseGunOrigin
  - type: GunIgnoreContainerOwnerCollision
  - type: Gun
    fireRate: 1
    recoil: 0
    minAngle: 0
    maxAngle: 0
    angleIncrease: 0
    projectileSpeed: 30
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BallisticAmmoProvider
    proto: WFVehicleCrewTestCartridge
    capacity: 5
    cycleable: false
  - type: VehicleHardpointAmmo
    magazineSize: 5
    maxStoredMagazines: 3

- type: entity
  parent: WFVehicleTestChassis
  id: WFVehicleCrewTestVehicle
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
    - offset: -1,0
      radius: 0.6
      interiorCoords: 3.5,1.5
  - type: ItemSlots
    slots:
      gun:
        startingItem: WFVehicleCrewTestGun
        disableEject: true
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

- type: entity
  parent: SeatBase
  id: WFVehicleCrewTestWeaponsSeat
  categories: [ HideSpawnMenu ]
  components:
  - type: Physics
    bodyType: Static
  - type: VehicleWeaponsSeat
  - type: UserInterface
    interfaces:
      enum.VehicleWeaponsUiKey.Key:
        type: VehicleWeaponsBoundUserInterface

- type: wfVehicleCrew
  id: WFVehicleCrewTestCrew
  vehicle: WFVehicleCrewTestVehicle
  driver: WFVehicleDriver
  gunner: WFVehicleGunner
  faction: WFCrew
  behaviour: Hold
  engagement: OnSight
  engageRange: 16
  standoff: 8
  fireOnTheMove: true
  pursues: false
  skill: Elite

- type: wfVehicleCrew
  id: WFVehicleCrewTestCrewTrooper
  vehicle: WFVehicleCrewTestVehicle
  driver: WFVehicleDriver
  gunner: WFVehicleGunner
  troopers: [ WFVehicleTrooper ]
  faction: WFCrew
  behaviour: Hold
  engagement: OnSight
  engageRange: 16
  standoff: 8
  fireOnTheMove: true
  pursues: false
  skill: Elite
";

    private sealed class Rig
    {
        public TestMapData Map = default!;
        public EntityUid Vehicle;
        public EntityUid Gun;
    }

    /// <summary>
    /// A block of plating with the vehicle on it; its interior is loaded and given a weapons seat before the crew is
    /// put in, since the test interior has only a driver seat.
    /// </summary>
    private static async Task<Rig> BuildRig(TestPair pair, string crew)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var rig = new Rig { Map = await pair.CreateTestMap() };

        await server.WaitPost(() =>
        {
            // Nobody is connected; without this the hostile mobs sleep.
            server.CfgMan.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false);

            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -8; x < 24; x++)
            for (var y = -8; y < 20; y++)
                maps.SetTile(rig.Map.Grid.Owner, rig.Map.Grid.Comp, new Vector2i(x, y), new Tile(plating));

            rig.Vehicle = entMan.SpawnEntity(Vehicle, new EntityCoordinates(rig.Map.Grid.Owner, VehicleSpot));
            server.System<SharedTransformSystem>().SetLocalRotation(rig.Vehicle, Angle.Zero);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<VehicleInteriorSystem>().TryLoadInterior(rig.Vehicle, out var interior), Is.True,
                "Test setup: the interior did not load.");
            entMan.SpawnEntity(WeaponsSeat, new EntityCoordinates(interior!.Grid, WeaponsSeatSpot));
            Assert.That(server.System<WFVehicleCrewSystem>().TryCrew(rig.Vehicle, crew), Is.True, "The crew was not seated.");

            Assert.That(server.System<ItemSlotsSystem>().TryGetSlot(rig.Vehicle, "gun", out var slot), Is.True);
            rig.Gun = slot!.Item ?? EntityUid.Invalid;
            Assert.That(rig.Gun, Is.Not.EqualTo(EntityUid.Invalid), "Test setup: the gun was not fitted.");
        });
        await server.WaitRunTicks(5);

        return rig;
    }

    private static async Task Finish(TestPair pair)
    {
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange,
            CCVars.NPCPauseWhenNoPlayersInRange.DefaultValue));
        await pair.CleanReturnAsync();
    }

    private static Vector2 WorldPosition(IEntityManager entMan, EntityUid uid)
    {
        return entMan.System<SharedTransformSystem>().GetWorldPosition(uid);
    }

    [Test]
    public async Task SpawnSeatsStartsAndDrives()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var rig = await BuildRig(pair, Crew);
        var destination = default(MapCoordinates);

        await server.WaitAssertion(() =>
        {
            var crew = entMan.GetComponent<WFVehicleCrewComponent>(rig.Vehicle);
            Assert.That(crew.Driver, Is.Not.Null, "No driver was spawned.");
            Assert.That(crew.Gunner, Is.Not.Null, "No gunner was spawned.");
            Assert.That(entMan.GetComponent<RMCVehicleComponent>(rig.Vehicle).Operator, Is.EqualTo(crew.Driver),
                "The driver is not the vehicle's operator.");
            Assert.That(entMan.HasComponent<VehicleWeaponsOperatorComponent>(crew.Gunner!.Value), Is.True,
                "The gunner is not working the guns.");
            Assert.That(entMan.GetComponent<WFVehicleEngineComponent>(rig.Vehicle).Running, Is.True,
                "The engine is not running.");

            var start = entMan.System<SharedTransformSystem>().GetMapCoordinates(rig.Vehicle);
            destination = new MapCoordinates(start.Position + new Vector2(8f, 0f), start.MapId);
            server.System<WFVehicleCrewSystem>().SetWaypoints(rig.Vehicle, new List<MapCoordinates> { destination });
        });

        await pair.RunTicksSync(pair.SecondsToTicks(10));

        await server.WaitAssertion(() =>
        {
            var distance = (WorldPosition(entMan, rig.Vehicle) - destination.Position).Length();
            Assert.That(distance, Is.LessThan(2f), $"The vehicle stopped {distance:0.0} tiles from its waypoint.");
        });

        await Finish(pair);
    }

    [Test]
    public async Task GunnerShootsAHostileAndHoldsFireOnFriends()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var rig = await BuildRig(pair, Crew);
        var rounds = 0;
        EntityUid friend = default;

        await server.WaitPost(() =>
        {
            rounds = entMan.GetComponent<BallisticAmmoProviderComponent>(rig.Gun).Count;
            friend = entMan.SpawnEntity("MobHuman", new EntityCoordinates(rig.Map.Grid.Owner, VehicleSpot + new Vector2(8f, 0f)));
            var factions = server.System<NpcFactionSystem>();
            entMan.EnsureComponent<NpcFactionMemberComponent>(friend);
            factions.ClearFactions(friend, false);
            factions.AddFaction(friend, "WFCrew");
        });

        await pair.RunTicksSync(pair.SecondsToTicks(6));

        await server.WaitAssertion(() =>
        {
            Assert.That(rounds, Is.GreaterThan(0), "Test setup: the gun has no rounds.");
            Assert.That(entMan.GetComponent<BallisticAmmoProviderComponent>(rig.Gun).Count, Is.EqualTo(rounds),
                "The gunner fired with only a friend in sight.");
            entMan.DeleteEntity(friend);
            entMan.SpawnEntity("MobCarp", new EntityCoordinates(rig.Map.Grid.Owner, VehicleSpot + new Vector2(8f, 0f)));
        });

        await pair.RunTicksSync(pair.SecondsToTicks(6));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<BallisticAmmoProviderComponent>(rig.Gun).Count, Is.LessThan(rounds),
                "The gunner never fired at the hostile carp.");
        });

        await Finish(pair);
    }

    [Test]
    public async Task BailsWhenTheFrameBreaks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var rig = await BuildRig(pair, CrewWithTrooper);
        var members = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            var crew = entMan.GetComponent<WFVehicleCrewComponent>(rig.Vehicle);
            Assert.That(crew.Driver, Is.Not.Null);
            Assert.That(crew.Gunner, Is.Not.Null);
            Assert.That(crew.Troopers, Has.Count.EqualTo(1), "The trooper was not spawned.");
            members.Add(crew.Driver!.Value);
            members.Add(crew.Gunner!.Value);
            members.AddRange(crew.Troopers);

            var frame = entMan.GetComponent<HardpointIntegrityComponent>(rig.Vehicle);
            server.System<HardpointSystem>().DamageHardpoint(rig.Vehicle, rig.Vehicle, frame.MaxIntegrity * 2f, frame);
            Assert.That(frame.Integrity, Is.LessThanOrEqualTo(0f), "Test setup: the frame did not break.");
        });

        await pair.RunTicksSync(pair.SecondsToTicks(3));

        await server.WaitAssertion(() =>
        {
            var outside = entMan.GetComponent<TransformComponent>(rig.Vehicle).MapID;
            foreach (var member in members)
            {
                Assert.That(entMan.GetComponent<TransformComponent>(member).MapID, Is.EqualTo(outside),
                    $"{entMan.ToPrettyString(member)} is still inside the vehicle.");
                Assert.That(entMan.TryGetComponent<WFCrewComponent>(member, out var crewman), Is.True,
                    $"{entMan.ToPrettyString(member)} lost his crew component.");
                Assert.That(crewman!.Duty, Is.EqualTo(WFCrewDuties.Guard));
                Assert.That(entMan.HasComponent<WFVehicleCrewMemberComponent>(member), Is.False,
                    $"{entMan.ToPrettyString(member)} still counts as mounted.");
            }

            Assert.That(entMan.HasComponent<WFVehicleAutopilotComponent>(rig.Vehicle), Is.False,
                "The bailed vehicle still has its autopilot.");
            Assert.That(entMan.GetComponent<WFVehicleCrewComponent>(rig.Vehicle).Bailed, Is.True);
        });

        await Finish(pair);
    }

    [Test]
    public async Task ADeadDriverStopsTheVehicle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var rig = await BuildRig(pair, Crew);
        EntityUid driver = default;

        await server.WaitPost(() =>
        {
            driver = entMan.GetComponent<WFVehicleCrewComponent>(rig.Vehicle).Driver!.Value;
            var start = entMan.System<SharedTransformSystem>().GetMapCoordinates(rig.Vehicle);
            var far = new MapCoordinates(start.Position + new Vector2(14f, 0f), start.MapId);
            server.System<WFVehicleCrewSystem>().SetWaypoints(rig.Vehicle, new List<MapCoordinates> { far });
        });

        await pair.RunTicksSync(pair.SecondsToTicks(2));

        await server.WaitAssertion(() =>
        {
            Assert.That(MathF.Abs(entMan.GetComponent<GridVehicleMoverComponent>(rig.Vehicle).CurrentSpeed), Is.GreaterThan(0.1f),
                "Test setup: the vehicle was not driving.");
            server.System<MobStateSystem>().ChangeMobState(driver, MobState.Dead);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(3));

        var stopped = Vector2.Zero;
        await server.WaitAssertion(() =>
        {
            Assert.That(MathF.Abs(entMan.GetComponent<GridVehicleMoverComponent>(rig.Vehicle).CurrentSpeed), Is.LessThan(0.01f),
                "The vehicle kept moving with a dead driver.");
            stopped = WorldPosition(entMan, rig.Vehicle);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(3));

        await server.WaitAssertion(() =>
        {
            Assert.That((WorldPosition(entMan, rig.Vehicle) - stopped).Length(), Is.LessThan(0.1f),
                "The vehicle moved again after its driver died.");
        });

        await Finish(pair);
    }
}
