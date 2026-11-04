#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._WF.Vehicles.Ui;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared._WF.Vehicles.Weapons.BulletBox;
using Content.Shared.Buckle;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Projectiles;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>
/// Ammo on a vehicle: the loader moving rounds between ammo boxes and a hardpoint gun's reserve, the gun chambering the
/// next magazine when it runs dry, the weapons window showing the reserve, and port guns worked from a seat.
/// </summary>
public sealed class VehicleAmmoTest : InteractionTest
{
    private const string Vehicle = "WFAmmoTestVehicle";
    private const string Box = "WFAmmoTestBox";
    private const string OtherBox = "WFAmmoTestBoxOther";
    private const string Loader = "WFAmmoTestLoader";
    private const string PortSeat = "WFAmmoTestPortSeat";
    private const string ComboSeat = "WFAmmoTestComboSeat";
    private const string WeaponsSeat = "WFAmmoTestWeaponsSeat";
    private const string PortController = "WFAmmoTestPortController";
    private const string PortMagazine = "WFAmmoTestPortMagazine";

    private const int MagazineSize = 5;
    private const int MaxStoredMagazines = 3;

    // The chassis sits at this spot of the grid; its doorways are a metre to its left and right.
    private static readonly Vector2 VehicleSpot = new(5.5f, 5.5f);
    private static readonly Vector2[] DoorwayOffsets = { new(1f, 0f), new(-1f, 0f) };

    private static readonly VehicleSlotPath GunPath = new("gun");

    protected override string PlayerPrototype => "MobHuman";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFAmmoTestBullet
  parent: BaseBullet
  categories: [ HideSpawnMenu ]

- type: entity
  id: WFAmmoTestCartridge
  categories: [ HideSpawnMenu ]
  components:
  - type: CartridgeAmmo
    proto: WFAmmoTestBullet
    deleteOnSpawn: true

- type: entity
  id: WFAmmoTestBox
  parent: BaseItem
  categories: [ HideSpawnMenu ]
  components:
  - type: Appearance
  - type: BulletBox
    amount: 20
    max: 20
    bulletType: WFAmmoTestBox
    delay: 0.1

- type: entity
  id: WFAmmoTestBoxOther
  parent: BaseItem
  categories: [ HideSpawnMenu ]
  components:
  - type: Appearance
  - type: BulletBox
    amount: 20
    max: 20
    bulletType: WFAmmoTestBoxOther
    delay: 0.1

# A mounted gun the loader reloads: five rounds in the gun, two more magazines in reserve.
- type: entity
  id: WFAmmoTestGun
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
  - type: GunUseGunOrigin
  - type: GunIgnoreContainerOwnerCollision
  - type: Gun
    fireRate: 20
    recoil: 0
    minAngle: 0
    maxAngle: 0
    angleIncrease: 0
    projectileSpeed: 30
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BallisticAmmoProvider
    proto: WFAmmoTestCartridge
    capacity: 5
    cycleable: false
  - type: VehicleHardpointAmmo
    magazineSize: 5
    maxStoredMagazines: 3
  - type: RefillableByBulletBox
    bulletType: WFAmmoTestBox

# The L6 SAW's box magazine, cut down to four rounds so the test can empty it, with a cartridge that leaves no casing.
- type: entity
  id: WFAmmoTestPortMagazine
  parent: Magazine762x39mmBox
  categories: [ HideSpawnMenu ]
  components:
  - type: BallisticAmmoProvider
    capacity: 4
    proto: WFAmmoTestCartridge

- type: entity
  id: WFAmmoTestPortGun
  parent: BaseItem
  categories: [ HideSpawnMenu ]
  components:
  - type: VehiclePortGun
  - type: GunUseGunOrigin
  - type: GunIgnoreContainerOwnerCollision
  - type: Gun
    fireRate: 20
    recoil: 0
    minAngle: 0
    maxAngle: 0
    angleIncrease: 0
    projectileSpeed: 30
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: MagazineAmmoProvider
  - type: ContainerContainer
    containers:
      gun_magazine: !type:ContainerSlot
  - type: ItemSlots
    slots:
      gun_magazine:
        name: Magazine
        startingItem: WFAmmoTestPortMagazine
        whitelist:
          tags:
          - Magazine762x39mmBox

- type: entity
  parent: WFVehicleTestChassis
  id: WFAmmoTestVehicle
  components:
  - type: Damageable
    damageContainer: StructuralInorganic
  - type: Appearance
  - type: HardpointIntegrity
    maxIntegrity: 1000
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
        startingItem: WFAmmoTestGun
        disableEject: true
        whitelist:
          components:
          - HardpointItem
      port-gun:
        startingItem: WFAmmoTestPortGun
        disableEject: true
        whitelist:
          components:
          - VehiclePortGun
  - type: HardpointSlots
    vehicleFamily: HardpointVehicleFamilyHumvee
    slots:
    - id: gun
      hardpointType: HardpointTypeCannon
      slotType: HardpointSlotTypeCannon
      required: false

# The real loader prop, with a shorter do-after.
- type: entity
  parent: VehicleAmmoLoader
  id: WFAmmoTestLoader
  categories: [ HideSpawnMenu ]
  components:
  - type: VehicleAmmoLoader
    loadDelay: 0.1

- type: entity
  id: WFAmmoTestPortController
  categories: [ HideSpawnMenu ]
  components:
  - type: Transform
  - type: VehiclePortGunController
    gunSlotId: port-gun

- type: entity
  parent: SeatBase
  id: WFAmmoTestPortSeat
  categories: [ HideSpawnMenu ]
  components:
  - type: Physics
    bodyType: Static
  - type: VehiclePortGunSeat

- type: entity
  parent: SeatBase
  id: WFAmmoTestWeaponsSeat
  categories: [ HideSpawnMenu ]
  components:
  - type: Physics
    bodyType: Static
  - type: VehicleWeaponsSeat
  - type: UserInterface
    interfaces:
      enum.VehicleWeaponsUiKey.Key:
        type: VehicleWeaponsBoundUserInterface

- type: entity
  parent: WFAmmoTestWeaponsSeat
  id: WFAmmoTestComboSeat
  categories: [ HideSpawnMenu ]
  components:
  - type: VehiclePortGunSeat
";

    /// <summary>Refuses every port gun seat while <see cref="RefuseSeats"/> is set.</summary>
    public sealed class PortSeatProbeSystem : EntitySystem
    {
        public bool RefuseSeats;

        public override void Initialize()
        {
            SubscribeLocalEvent<VehiclePortGunSeatComponent, VehicleSeatAttemptEvent>(OnSeatAttempt);
        }

        private void OnSeatAttempt(Entity<VehiclePortGunSeatComponent> ent, ref VehicleSeatAttemptEvent args)
        {
            if (RefuseSeats)
                args.Cancelled = true;
        }
    }

    private sealed class Rig
    {
        public EntityUid Vehicle;
        public EntityUid Gun;
        public EntityUid PortGun;
    }

    /// <summary>A block of plating with the vehicle in the middle of it.</summary>
    private async Task<Rig> BuildRig()
    {
        var rig = new Rig();
        await Server.WaitPost(() =>
        {
            var plating = TileMan["Plating"].TileId;
            for (var x = -4; x < 16; x++)
            {
                for (var y = -4; y < 16; y++)
                {
                    MapSystem.SetTile(MapData.Grid.Owner, MapData.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }

            rig.Vehicle = SEntMan.SpawnEntity(Vehicle, new EntityCoordinates(MapData.Grid.Owner, VehicleSpot));
            Transform.SetLocalRotation(rig.Vehicle, Angle.Zero);
        });
        await RunTicks(5);

        await Server.WaitPost(() =>
        {
            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryGetSlot(rig.Vehicle, "gun", out var gunSlot), Is.True, "The vehicle has no gun slot.");
            rig.Gun = gunSlot!.Item ?? EntityUid.Invalid;
            Assert.That(rig.Gun, Is.Not.EqualTo(EntityUid.Invalid), "The gun was not fitted.");

            Assert.That(slots.TryGetSlot(rig.Vehicle, "port-gun", out var portSlot), Is.True, "The vehicle has no port gun slot.");
            rig.PortGun = portSlot!.Item ?? EntityUid.Invalid;
            Assert.That(rig.PortGun, Is.Not.EqualTo(EntityUid.Invalid), "The port gun was not fitted.");
        });

        return rig;
    }

    private static Vector2 Doorway(int index) => VehicleSpot + DoorwayOffsets[index];

    /// <summary>Puts <paramref name="user"/> at a doorway, activates the vehicle and waits out the do-after.</summary>
    private async Task Enter(Rig rig, EntityUid user, int doorway)
    {
        await Server.WaitPost(() => Transform.SetCoordinates(user, new EntityCoordinates(MapData.Grid.Owner, Doorway(doorway))));
        await RunTicks(2);

        await Server.WaitPost(() => SEntMan.System<SharedInteractionSystem>().InteractionActivate(user, rig.Vehicle));
        await RunTicks(40);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<VehicleInteriorOccupantComponent>(user), Is.True, "The user did not get inside the vehicle.");
        });
    }

    /// <summary>Spawns a prop on the vehicle's interior grid.</summary>
    private async Task<EntityUid> SpawnInside(Rig rig, string prototype, Vector2 spot)
    {
        var spawned = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var grid = SEntMan.GetComponent<VehicleInteriorComponent>(rig.Vehicle).Grid;
            spawned = SEntMan.SpawnEntity(prototype, new EntityCoordinates(grid, spot));
        });
        await RunTicks(3);
        return spawned;
    }

    /// <summary>Puts a new seat on the interior grid and straps <paramref name="user"/> into it.</summary>
    private async Task<EntityUid> SeatUp(Rig rig, EntityUid user, string seatPrototype, Vector2 seatSpot)
    {
        var seat = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var grid = SEntMan.GetComponent<VehicleInteriorComponent>(rig.Vehicle).Grid;
            seat = SEntMan.SpawnEntity(seatPrototype, new EntityCoordinates(grid, seatSpot));
            Transform.SetCoordinates(user, new EntityCoordinates(grid, seatSpot));
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(user, user, seat), Is.True, "The user could not strap into the seat.");
        });
        await RunTicks(10);

        return seat;
    }

    private async Task<EntityUid> BoxInHands(EntityUid user, string prototype, int? amount = null)
    {
        var box = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            box = SEntMan.SpawnEntity(prototype, Transform.GetMoverCoordinates(user));
            if (amount != null)
                Assert.That(SEntMan.System<BulletBoxSystem>().TrySetAmount((box, SEntMan.GetComponent<BulletBoxComponent>(box)), amount.Value), Is.True);

            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, box), Is.True, "The user could not hold the box.");
        });
        return box;
    }

    private BulletBoxComponent BoxOf(EntityUid box) => SEntMan.GetComponent<BulletBoxComponent>(box);

    private VehicleHardpointAmmoComponent AmmoOf(Rig rig) => SEntMan.GetComponent<VehicleHardpointAmmoComponent>(rig.Gun);

    private int ReadyRounds(Rig rig) => SEntMan.GetComponent<BallisticAmmoProviderComponent>(rig.Gun).Count;

    private List<EntityUid> Projectiles()
    {
        var found = new List<EntityUid>();
        var query = SEntMan.EntityQueryEnumerator<ProjectileComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            found.Add(uid);
        }

        return found;
    }

    private void Fire(EntityUid user, EntityUid gun)
    {
        var guns = SEntMan.System<SharedGunSystem>();
        guns.AttemptShoot(user, gun, SEntMan.GetComponent<GunComponent>(gun), new EntityCoordinates(gun, new Vector2(6f, 0f)));
    }

    private new int ActiveDoAfters(EntityUid user)
    {
        return SEntMan.TryGetComponent(user, out DoAfterComponent? doAfters) ? doAfters.DoAfters.Count : 0;
    }

    private VehicleAmmoLoaderUiEntry LoaderEntry(EntityUid loader)
    {
        return SEntMan.GetComponent<VehicleAmmoLoaderComponent>(loader).Ui.Hardpoints.Single(e => e.SlotPath == GunPath);
    }

    private async Task SendSelect(EntityUid loader, int ammoSlot, VehicleAmmoLoaderSlotAction action)
    {
        Target = SEntMan.GetNetEntity(loader);
        await SendBui(VehicleAmmoLoaderUiKey.Key, new VehicleAmmoLoaderSelectMessage(GunPath, ammoSlot, action));
    }

    /// <summary>Opens the loader's window for <paramref name="user"/> by using the held box on it.</summary>
    private async Task OpenLoader(EntityUid user, EntityUid loader, EntityUid? box)
    {
        await Server.WaitPost(() =>
        {
            var interaction = SEntMan.System<SharedInteractionSystem>();
            if (box is { } held)
                interaction.InteractUsing(user, held, loader, Transform.GetMoverCoordinates(loader));
            else
                interaction.InteractHand(user, loader);
        });
        await RunTicks(10);
    }

    private static void CollectLabels(Control control, List<string> into)
    {
        if (control is Label { Text: { } text })
            into.Add(text);

        foreach (var child in control.Children)
        {
            CollectLabels(child, into);
        }
    }

    /// <summary>The text of every label in the loader's open window on the client.</summary>
    private async Task<List<string>> ClientMenuLabels(EntityUid loader)
    {
        var labels = new List<string>();
        var netLoader = SEntMan.GetNetEntity(loader);
        await Client.WaitAssertion(() =>
        {
            var cLoader = CEntMan.GetEntity(netLoader);
            var ui = CEntMan.GetComponent<UserInterfaceComponent>(cLoader);
            Assert.That(ui.ClientOpenInterfaces.TryGetValue(VehicleAmmoLoaderUiKey.Key, out var bui), Is.True, "The loader's window is not open on the client.");

            var field = typeof(VehicleAmmoLoaderBoundUserInterface).GetField("_menu", BindingFlags.NonPublic | BindingFlags.Instance);
            var menu = field?.GetValue(bui) as VehicleAmmoLoaderMenu;
            Assert.That(menu, Is.Not.Null, "The loader's window was never built.");
            CollectLabels(menu!.HardpointList, labels);
        });
        return labels;
    }

    /// <summary>
    /// A box held to the loader fills the gun's reserve slot by slot through the window, an emptied box is dropped, and
    /// the same window takes the rounds back out, even the gun's ready magazine, into new boxes in the user's hands.
    /// </summary>
    [Test]
    public async Task TheLoaderMovesRoundsBetweenABoxAndTheReserve()
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);
        var loader = await SpawnInside(rig, Loader, new Vector2(1.5f, 2.5f));
        var box = await BoxInHands(SPlayer, Box, 10);
        await OpenLoader(SPlayer, loader, box);

        await Server.WaitAssertion(() =>
        {
            var entry = LoaderEntry(loader);
            var state = SEntMan.GetComponent<VehicleAmmoLoaderComponent>(loader).Ui;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<VehicleAmmoLoaderUserComponent>(SPlayer), Is.True, "An open window should mark its user.");
                Assert.That(state.AmmoAmount, Is.EqualTo(10), "The window does not know what the user holds.");
                Assert.That(entry.MagazineSize, Is.EqualTo(MagazineSize));
                Assert.That(entry.AmmoSlots.Select(s => s.Rounds), Is.EqualTo(new[] { 5, 0, 0 }));
                Assert.That(entry.AmmoSlots.Select(s => s.CanLoad), Is.EqualTo(new[] { false, true, true }), "Only the reserve has room.");
                Assert.That(entry.AmmoSlots.Select(s => s.CanUnload), Is.EqualTo(new[] { true, false, false }));
            }
        });

        var before = await ClientMenuLabels(loader);
        Assert.That(before.Count(l => l == "5/5"), Is.EqualTo(1), "The client window should show the full gun and an empty reserve.");

        // Reserve slot 1, then slot 2.
        await SendSelect(loader, 1, VehicleAmmoLoaderSlotAction.Load);
        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(AmmoOf(rig).StoredRoundSlots, Is.EqualTo(new[] { 5, 0 }), "The first load did not reach the reserve.");
                Assert.That(BoxOf(box).Amount, Is.EqualTo(5), "The box did not give up the rounds.");
                Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(SPlayer, box), Is.True);
                Assert.That(LoaderEntry(loader).AmmoSlots[1].Rounds, Is.EqualTo(5), "The window state is stale.");
            }
        });

        var after = await ClientMenuLabels(loader);
        Assert.That(after.Count(l => l == "5/5"), Is.EqualTo(2), "The client window did not redraw after the load.");

        await SendSelect(loader, 2, VehicleAmmoLoaderSlotAction.Load);
        await Server.WaitAssertion(() =>
        {
            var ammo = AmmoOf(rig);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(ammo.StoredRoundSlots, Is.EqualTo(new[] { 5, 5 }));
                Assert.That(ammo.StoredRounds, Is.EqualTo(10));
                Assert.That(BoxOf(box).Amount, Is.EqualTo(0), "The box should be empty.");
                Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(SPlayer, box), Is.False, "An emptied box stays in the hand.");

                var grid = SEntMan.GetComponent<VehicleInteriorComponent>(rig.Vehicle).Grid;
                Assert.That(Transform.GetParentUid(box), Is.EqualTo(grid), "The emptied box was not dropped inside the vehicle.");
                Assert.That(Vector2.Distance(Transform.GetWorldPosition(box), Transform.GetWorldPosition(loader)), Is.LessThan(0.1f),
                    "The emptied box was not dropped at the loader.");
            }
        });

        // Unload the gun's ready magazine: the next one comes up from the reserve to take its place.
        await SendSelect(loader, 0, VehicleAmmoLoaderSlotAction.Unload);
        EntityUid firstUnloaded = default;
        await Server.WaitAssertion(() =>
        {
            var held = SEntMan.System<SharedHandsSystem>().EnumerateHeld(SPlayer).ToList();
            Assert.That(held, Has.Count.EqualTo(1), "The unloaded rounds did not end up in a box in the user's hands.");
            firstUnloaded = held[0];

            var ammo = AmmoOf(rig);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(firstUnloaded).EntityPrototype?.ID, Is.EqualTo(Box));
                Assert.That(BoxOf(firstUnloaded).Amount, Is.EqualTo(5));
                Assert.That(ReadyRounds(rig), Is.EqualTo(5), "The next magazine was not chambered from the reserve.");
                Assert.That(ammo.StoredRoundSlots, Is.EqualTo(new[] { 5, 0 }));
            }
        });

        // The user has one hand, so the second box lands at their feet.
        await SendSelect(loader, 1, VehicleAmmoLoaderSlotAction.Unload);
        await Server.WaitAssertion(() =>
        {
            var ammo = AmmoOf(rig);
            var grid = SEntMan.GetComponent<VehicleInteriorComponent>(rig.Vehicle).Grid;
            var full = new List<EntityUid>();
            var boxes = SEntMan.EntityQueryEnumerator<BulletBoxComponent>();
            while (boxes.MoveNext(out var boxUid, out var boxComp))
            {
                if (boxComp.Amount > 0)
                    full.Add(boxUid);
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(full, Has.Count.EqualTo(2), "The unloaded rounds should be in two boxes.");
                Assert.That(full.Sum(b => BoxOf(b).Amount), Is.EqualTo(10), "The unloaded rounds were not all returned in boxes.");
                Assert.That(full.Count(b => SEntMan.System<SharedHandsSystem>().IsHolding(SPlayer, b)), Is.EqualTo(1));
                Assert.That(full.Count(b => Transform.GetParentUid(b) == grid), Is.EqualTo(1), "The box that did not fit in the hand was not dropped inside the vehicle.");
                Assert.That(ammo.StoredRoundSlots, Is.EqualTo(new[] { 0, 0 }));
                Assert.That(ammo.StoredRounds, Is.EqualTo(0));
                Assert.That(ReadyRounds(rig), Is.EqualTo(5), "Only the reserve should have been emptied.");
            }
        });
    }

    /// <summary>A box of another type is not offered a slot and is refused if sent anyway; a full slot is refused too.</summary>
    [Test]
    public async Task TheLoaderRefusesTheWrongBoxAndAFullSlot()
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);
        var loader = await SpawnInside(rig, Loader, new Vector2(1.5f, 2.5f));
        var wrong = await BoxInHands(SPlayer, OtherBox);
        await OpenLoader(SPlayer, loader, wrong);

        await Server.WaitAssertion(() =>
        {
            var entry = LoaderEntry(loader);
            Assert.That(entry.AmmoSlots.Any(s => s.CanLoad), Is.False, "A box of the wrong type is offered a slot.");
        });

        await SendSelect(loader, 1, VehicleAmmoLoaderSlotAction.Load);
        await RunSeconds(0.5f);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(ActiveDoAfters(SPlayer), Is.EqualTo(0), "A load of the wrong type started.");
                Assert.That(AmmoOf(rig).StoredRounds, Is.EqualTo(0), "The wrong rounds got into the reserve.");
                Assert.That(BoxOf(wrong).Amount, Is.EqualTo(20), "The wrong box lost rounds.");
            }

            // Swap it for the right box.
            var hands = SEntMan.System<SharedHandsSystem>();
            hands.TryDrop(SPlayer, wrong, checkActionBlocker: false);
        });

        var box = await BoxInHands(SPlayer, Box);
        await OpenLoader(SPlayer, loader, box);

        await SendSelect(loader, 1, VehicleAmmoLoaderSlotAction.Load);
        await SendSelect(loader, 2, VehicleAmmoLoaderSlotAction.Load);
        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(AmmoOf(rig).StoredRoundSlots, Is.EqualTo(new[] { 5, 5 }));
                Assert.That(BoxOf(box).Amount, Is.EqualTo(10));
                Assert.That(LoaderEntry(loader).AmmoSlots.Any(s => s.CanLoad), Is.False, "A full gun and reserve still offer a slot.");
            }
        });

        // Slot 1 is full, and so is the gun's ready magazine.
        await SendSelect(loader, 1, VehicleAmmoLoaderSlotAction.Load);
        await SendSelect(loader, 0, VehicleAmmoLoaderSlotAction.Load);
        await RunSeconds(0.5f);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(ActiveDoAfters(SPlayer), Is.EqualTo(0), "A load into a full slot started.");
                Assert.That(AmmoOf(rig).StoredRoundSlots, Is.EqualTo(new[] { 5, 5 }), "A full slot took more rounds.");
                Assert.That(ReadyRounds(rig), Is.EqualTo(MagazineSize));
                Assert.That(BoxOf(box).Amount, Is.EqualTo(10), "A refused load cost the box rounds.");
            }
        });
    }

    /// <summary>
    /// The loader hears about the user's hands through a marker that is on them only while a window is open: what they hold
    /// follows into the window, and closing it takes the marker off.
    /// </summary>
    [Test]
    public async Task TheLoaderFollowsTheUsersHandsWhileItsWindowIsOpen()
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);
        var loader = await SpawnInside(rig, Loader, new Vector2(1.5f, 2.5f));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<VehicleAmmoLoaderUserComponent>(SPlayer), Is.False, "The marker is on a user who has opened nothing.");
        });

        await OpenLoader(SPlayer, loader, null);
        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<VehicleAmmoLoaderUserComponent>(SPlayer), Is.True);
                Assert.That(SEntMan.GetComponent<VehicleAmmoLoaderComponent>(loader).Ui.AmmoAmount, Is.EqualTo(0));
                Assert.That(LoaderEntry(loader).AmmoSlots.Any(s => s.CanLoad), Is.False);
            }
        });

        var box = await BoxInHands(SPlayer, Box, 12);
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<VehicleAmmoLoaderComponent>(loader).Ui;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(state.AmmoAmount, Is.EqualTo(12), "Picking the box up did not reach the window.");
                Assert.That(state.AmmoMax, Is.EqualTo(20));
                Assert.That(LoaderEntry(loader).AmmoSlots.Select(s => s.CanLoad), Is.EqualTo(new[] { false, true, true }));
            }

            Assert.That(SEntMan.System<SharedHandsSystem>().TryDrop(SPlayer, box, checkActionBlocker: false), Is.True);
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.GetComponent<VehicleAmmoLoaderComponent>(loader).Ui.AmmoAmount, Is.EqualTo(0), "Dropping the box did not reach the window.");
                Assert.That(LoaderEntry(loader).AmmoSlots.Any(s => s.CanLoad), Is.False);
            }
        });

        Target = SEntMan.GetNetEntity(loader);
        await CloseBui(VehicleAmmoLoaderUiKey.Key);
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<VehicleAmmoLoaderUserComponent>(SPlayer), Is.False, "Closing the window left the marker on the user.");
        });
    }

    /// <summary>
    /// A gun fires its ready magazine, takes the next one up from the reserve the moment it runs dry and keeps going until the
    /// reserve is gone, and then it clicks.
    /// </summary>
    [Test]
    public async Task FiringConsumesTheReadyMagazineAndChambersTheNextFromTheReserve()
    {
        var rig = await BuildRig();
        await Server.WaitPost(() =>
        {
            SEntMan.System<VehicleHardpointAmmoSystem>().SetStoredRounds((rig.Gun, AmmoOf(rig)), 10, MagazineSize);
        });

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(AmmoOf(rig).StoredRoundSlots, Is.EqualTo(new[] { 5, 5 }));
                Assert.That(ReadyRounds(rig), Is.EqualTo(MagazineSize));
            }
        });

        var expectedReady = new[] { 4, 3, 2, 1, 5, 4, 3, 2, 1, 5, 4, 3, 2, 1, 0 };
        var expectedStored = new[] { 10, 10, 10, 10, 5, 5, 5, 5, 5, 0, 0, 0, 0, 0, 0 };
        for (var shot = 0; shot < expectedReady.Length; shot++)
        {
            var index = shot;
            await Server.WaitAssertion(() =>
            {
                Fire(SPlayer, rig.Gun);

                var shots = Projectiles();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(shots, Has.Count.EqualTo(1), $"Shot {index + 1} should have fired.");
                    Assert.That(ReadyRounds(rig), Is.EqualTo(expectedReady[index]), $"Wrong rounds in the gun after shot {index + 1}.");
                    Assert.That(AmmoOf(rig).StoredRounds, Is.EqualTo(expectedStored[index]), $"Wrong reserve after shot {index + 1}.");
                }

                foreach (var shot in shots)
                {
                    SEntMan.DeleteEntity(shot);
                }
            });
            await RunSeconds(0.2f);
        }

        // Everything is spent: the next pull of the trigger fires nothing.
        await Server.WaitAssertion(() =>
        {
            Fire(SPlayer, rig.Gun);
            Assert.That(Projectiles(), Is.Empty, "An empty gun fired.");
            Assert.That(ReadyRounds(rig), Is.EqualTo(0));
            Assert.That(AmmoOf(rig).StoredRoundSlots, Is.EqualTo(new[] { 0, 0 }));
        });
    }

    /// <summary>The gunner's window shows the gun's magazine size and the reserve, and follows shots and loading.</summary>
    [Test]
    public async Task TheWeaponsWindowShowsTheReserve()
    {
        var rig = await BuildRig();
        await Server.WaitPost(() =>
        {
            SEntMan.System<VehicleHardpointAmmoSystem>().SetStoredRounds((rig.Gun, AmmoOf(rig)), 7, MagazineSize);
        });

        await Enter(rig, SPlayer, 0);
        var seat = await SeatUp(rig, SPlayer, WeaponsSeat, new Vector2(1.5f, 2.5f));
        var netGun = SEntMan.GetNetEntity(rig.Gun);

        await Server.WaitAssertion(() =>
        {
            var entry = SEntMan.GetComponent<VehicleWeaponsSeatComponent>(seat).Ui.Hardpoints.Single(e => e.InstalledEntity == netGun);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entry.HasMagazineData, Is.True, "The window has no magazine data for a gun with a reserve.");
                Assert.That(entry.MagazineSize, Is.EqualTo(MagazineSize));
                Assert.That(entry.StoredMagazines, Is.EqualTo(7), "The window does not show the reserve rounds.");
                Assert.That(entry.MaxStoredMagazines, Is.EqualTo((MaxStoredMagazines - 1) * MagazineSize));
                Assert.That(entry.AmmoCount, Is.EqualTo(MagazineSize));
                Assert.That(entry.AmmoCapacity, Is.EqualTo(MagazineSize));
            }
        });

        // Pick the gun and fire one round: the window follows the shot.
        Target = SEntMan.GetNetEntity(seat);
        await SendBui(VehicleWeaponsUiKey.Key, new VehicleWeaponsSelectMessage(netGun));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<VehicleWeaponsOperatorComponent>(SPlayer).SelectedWeapon, Is.EqualTo(rig.Gun), "The gunner could not pick the gun.");
            Fire(SPlayer, rig.Gun);
            foreach (var shot in Projectiles())
            {
                SEntMan.DeleteEntity(shot);
            }

            var entry = SEntMan.GetComponent<VehicleWeaponsSeatComponent>(seat).Ui.Hardpoints.Single(e => e.InstalledEntity == netGun);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entry.AmmoCount, Is.EqualTo(MagazineSize - 1), "The window does not show the round fired.");
                Assert.That(entry.StoredMagazines, Is.EqualTo(7));
            }
        });

        // Rounds loaded into the reserve show straight away, with no shot or other refresh.
        await Server.WaitAssertion(() =>
        {
            SEntMan.System<VehicleHardpointAmmoSystem>().SetStoredRounds((rig.Gun, AmmoOf(rig)), 10, MagazineSize);

            var entry = SEntMan.GetComponent<VehicleWeaponsSeatComponent>(seat).Ui.Hardpoints.Single(e => e.InstalledEntity == netGun);
            Assert.That(entry.StoredMagazines, Is.EqualTo(10), "The window did not follow the reserve.");
        });
    }

    /// <summary>A handheld scanner looks out from the vehicle it is carried in, and stops once carried out.</summary>
    [Test]
    public async Task AHandheldScannerInsideLooksOutFromTheVehicle()
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);

        var scanner = EntityUid.Invalid;
        var ui = SEntMan.System<SharedUserInterfaceSystem>();
        await Server.WaitPost(() =>
        {
            scanner = SEntMan.SpawnEntity("HandHeldMassScanner", new EntityCoordinates(SPlayer, Vector2.Zero));
        });
        await RunTicks(3);
        await Server.WaitPost(() => ui.OpenUi(scanner, RadarConsoleUiKey.Key, SPlayer));
        await RunTicks(3);

        EntityUid? Origin()
        {
            Assert.That(ui.TryGetUiState<NavBoundUserInterfaceState>(scanner, RadarConsoleUiKey.Key, out var state), Is.True, "The scanner has no state.");
            return state!.State.Coordinates is { } coordinates ? SEntMan.GetCoordinates(coordinates).EntityId : null;
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(ui.IsUiOpen(scanner, RadarConsoleUiKey.Key, SPlayer), Is.True, "The scanner did not open.");
            Assert.That(Origin(), Is.EqualTo(rig.Vehicle), "A scanner inside the vehicle shows the interior.");
        });

        await Server.WaitPost(() =>
        {
            var outside = Transform.GetMapCoordinates(rig.Vehicle);
            Transform.SetMapCoordinates(SPlayer, outside.Offset(new Vector2(5f, 0f)));
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(ui.IsUiOpen(scanner, RadarConsoleUiKey.Key, SPlayer), Is.True, "Leaving the vehicle closed the scanner.");
            Assert.That(Origin(), Is.Not.EqualTo(rig.Vehicle), "A scanner carried out still follows the vehicle.");
        });
    }

    /// <summary>
    /// A user has to be strapped into the port gun seat to man the gun at its firing port. Once they have, the gun lookup
    /// answers for them, the shot leaves the vehicle on the outer map, and leaving the seat gives the gun up.
    /// </summary>
    [Test]
    public async Task APortGunIsMannedFromItsSeatAndFiresFromTheVehicle()
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);
        var port = await SpawnInside(rig, PortController, new Vector2(1.5f, 3.5f));

        // Standing next to the firing port is not enough.
        await Server.WaitPost(() => SEntMan.System<SharedInteractionSystem>().InteractHand(SPlayer, port));
        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<VehiclePortGunOperatorComponent>(SPlayer), Is.False, "An unseated user manned the port gun.");
                Assert.That(SEntMan.GetComponent<VehiclePortGunComponent>(rig.PortGun).Operator, Is.Null);
            }
        });

        await SeatUp(rig, SPlayer, PortSeat, new Vector2(1.5f, 2.5f));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedGunSystem>().TryGetGun(SPlayer, out _, out _), Is.False, "A seated user has a gun before using the port.");
            Assert.That(SEntMan.System<SharedInteractionSystem>().InteractHand(SPlayer, port), Is.True);
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            var gunOperator = SEntMan.GetComponent<VehiclePortGunOperatorComponent>(SPlayer);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.GetComponent<VehiclePortGunComponent>(rig.PortGun).Operator, Is.EqualTo(SPlayer));
                Assert.That(gunOperator.Gun, Is.EqualTo(rig.PortGun));
                Assert.That(gunOperator.Vehicle, Is.EqualTo(rig.Vehicle));
                Assert.That(gunOperator.Controller, Is.EqualTo(port));
                Assert.That(SEntMan.GetComponent<VehicleWatchingComponent>(SPlayer).Watching, Is.EqualTo(rig.Vehicle));
                Assert.That(SEntMan.GetComponent<EyeComponent>(SPlayer).Target, Is.EqualTo(rig.Vehicle), "The gunner's eye is not on the vehicle.");
                Assert.That(SEntMan.System<SharedGunSystem>().TryGetGun(SPlayer, out var gun, out _), Is.True, "The gun lookup does not answer for the port gunner.");
                Assert.That(gun, Is.EqualTo(rig.PortGun));
            }
        });

        // The shot leaves the vehicle, on the map it drives on, though the gunner stands on the interior's map.
        await Server.WaitAssertion(() =>
        {
            Assert.That(Transform.GetMapId(SPlayer), Is.Not.EqualTo(MapId), "The test needs the gunner on the interior's map.");

            Fire(SPlayer, rig.PortGun);

            var shots = Projectiles();
            Assert.That(shots, Has.Count.EqualTo(1), "The port gun should fire exactly one projectile.");
            var shot = shots[0];
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Transform.GetMapId(shot), Is.EqualTo(MapId), "The projectile is not on the vehicle's map.");
                Assert.That(Vector2.Distance(Transform.GetWorldPosition(shot), Transform.GetWorldPosition(rig.Vehicle)), Is.LessThan(0.5f),
                    "The projectile did not leave from the vehicle.");
                Assert.That(SEntMan.GetComponent<ProjectileComponent>(shot).Shooter, Is.EqualTo(SPlayer));
                Assert.That(SEntMan.GetComponent<ProjectileComponent>(shot).Weapon, Is.EqualTo(rig.PortGun));
            }

            SEntMan.DeleteEntity(shot);
        });

        // Using the port again lets go of the gun.
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<SharedInteractionSystem>().InteractHand(SPlayer, port), Is.True));
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<VehiclePortGunOperatorComponent>(SPlayer), Is.False);
                Assert.That(SEntMan.GetComponent<VehiclePortGunComponent>(rig.PortGun).Operator, Is.Null);
                Assert.That(SEntMan.System<SharedGunSystem>().TryGetGun(SPlayer, out _, out _), Is.False);
            }
        });

        // So does leaving the seat.
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<SharedInteractionSystem>().InteractHand(SPlayer, port), Is.True));
        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<VehiclePortGunOperatorComponent>(SPlayer), Is.True);
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryUnbuckle(SPlayer, SPlayer, true), Is.True, "The user could not leave the seat.");
        });
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<VehiclePortGunOperatorComponent>(SPlayer), Is.False, "Leaving the seat left the user on the gun.");
                Assert.That(SEntMan.GetComponent<VehiclePortGunComponent>(rig.PortGun).Operator, Is.Null);
                Assert.That(SEntMan.HasComponent<VehicleWatchingComponent>(SPlayer), Is.False);
                Assert.That(SEntMan.GetComponent<EyeComponent>(SPlayer).Target, Is.Null, "The gunner's eye is still on the vehicle.");
            }
        });
    }

    /// <summary>The seat attempt event gates a port gun seat as it does the others.</summary>
    [Test]
    public async Task ARefusedPortGunSeatMakesNoOperator()
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);

        var seat = EntityUid.Invalid;
        var probe = SEntMan.System<PortSeatProbeSystem>();
        try
        {
            await Server.WaitPost(() =>
            {
                var grid = SEntMan.GetComponent<VehicleInteriorComponent>(rig.Vehicle).Grid;
                seat = SEntMan.SpawnEntity(PortSeat, new EntityCoordinates(grid, 1.5f, 2.5f));
                Transform.SetCoordinates(SPlayer, new EntityCoordinates(grid, 1.5f, 2.5f));
            });
            await RunTicks(3);

            await Server.WaitAssertion(() =>
            {
                probe.RefuseSeats = true;
                Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, seat), Is.False, "A refused seat still took the user.");
                probe.RefuseSeats = false;
                Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, seat), Is.True, "The seat stayed closed once the refusal stopped.");
            });
        }
        finally
        {
            probe.RefuseSeats = false;
        }
    }

    /// <summary>
    /// One gunner at a time holds a port gun: a second is refused while the first has it and takes it once it is let go.
    /// </summary>
    [Test]
    public async Task APortGunHasOneOperatorAtATime()
    {
        var rig = await BuildRig();
        var first = EntityUid.Invalid;
        await Server.WaitPost(() => first = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(MapData.Grid.Owner, Doorway(0))));
        await RunTicks(3);

        await Enter(rig, first, 0);
        await SeatUp(rig, first, PortSeat, new Vector2(1.5f, 2.5f));
        await Enter(rig, SPlayer, 1);
        await SeatUp(rig, SPlayer, PortSeat, new Vector2(3.5f, 2.5f));
        var port = await SpawnInside(rig, PortController, new Vector2(2.5f, 3.5f));

        await Server.WaitAssertion(() => Assert.That(SEntMan.System<SharedInteractionSystem>().InteractHand(first, port), Is.True));
        await RunTicks(3);

        // The second gunner is told it is taken and gets nothing.
        await Server.WaitAssertion(() =>
        {
            SEntMan.System<SharedInteractionSystem>().InteractHand(SPlayer, port);
        });
        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.GetComponent<VehiclePortGunComponent>(rig.PortGun).Operator, Is.EqualTo(first), "The first gunner lost the port gun.");
                Assert.That(SEntMan.HasComponent<VehiclePortGunOperatorComponent>(SPlayer), Is.False, "A second gunner took a held port gun.");
                Assert.That(SEntMan.System<SharedGunSystem>().TryGetGun(SPlayer, out _, out _), Is.False, "The second gunner can fire the held port gun.");
                Assert.That(SEntMan.System<SharedGunSystem>().TryGetGun(first, out var gun, out _), Is.True);
                Assert.That(gun, Is.EqualTo(rig.PortGun));
            }
        });

        // Once the first lets go, the second can have it.
        await Server.WaitAssertion(() => SEntMan.System<SharedInteractionSystem>().InteractHand(first, port));
        await RunTicks(3);
        await Server.WaitAssertion(() => SEntMan.System<SharedInteractionSystem>().InteractHand(SPlayer, port));
        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.GetComponent<VehiclePortGunComponent>(rig.PortGun).Operator, Is.EqualTo(SPlayer), "The second gunner could not take the released port gun.");
                Assert.That(SEntMan.HasComponent<VehiclePortGunOperatorComponent>(first), Is.False);
            }
        });
    }

    /// <summary>A gunner in a seat that is both a weapons seat and a port gun seat cannot pick a hardpoint while on the port gun.</summary>
    [Test]
    public async Task AGunnerOnAPortGunCannotPickAHardpoint()
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);
        var seat = await SeatUp(rig, SPlayer, ComboSeat, new Vector2(1.5f, 2.5f));
        var port = await SpawnInside(rig, PortController, new Vector2(1.5f, 3.5f));
        var netGun = SEntMan.GetNetEntity(rig.Gun);
        Target = SEntMan.GetNetEntity(seat);

        // Pick the hardpoint first: that is the gun the gunner fires.
        await SendBui(VehicleWeaponsUiKey.Key, new VehicleWeaponsSelectMessage(netGun));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<VehicleWeaponsOperatorComponent>(SPlayer).SelectedWeapon, Is.EqualTo(rig.Gun), "The gunner could not pick the gun.");
            Assert.That(SEntMan.System<SharedGunSystem>().TryGetGun(SPlayer, out var gun, out _), Is.True);
            Assert.That(gun, Is.EqualTo(rig.Gun));
        });

        // Manning the port gun takes over the trigger, and the pick is kept for later.
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<SharedInteractionSystem>().InteractHand(SPlayer, port), Is.True));
        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedGunSystem>().TryGetGun(SPlayer, out var gun, out _), Is.True);
            Assert.That(gun, Is.EqualTo(rig.PortGun), "A picked hardpoint beat the port gun.");
        });

        // Dropping the pick while on the port gun is refused.
        await SendBui(VehicleWeaponsUiKey.Key, new VehicleWeaponsSelectMessage(netGun));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<VehicleWeaponsOperatorComponent>(SPlayer).SelectedWeapon, Is.EqualTo(rig.Gun), "The pick changed while on the port gun.");
        });

        // Letting go of the port gun gives the pick back its trigger.
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<SharedInteractionSystem>().InteractHand(SPlayer, port), Is.True));
        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedGunSystem>().TryGetGun(SPlayer, out var gun, out _), Is.True);
            Assert.That(gun, Is.EqualTo(rig.Gun));
        });
    }

    /// <summary>The port gun fires its magazine dry, gives the spent one up when a new one is put to the port, and fires again.</summary>
    [Test]
    public async Task APortGunReloadsFromItsMagazine()
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);
        await SeatUp(rig, SPlayer, PortSeat, new Vector2(1.5f, 2.5f));
        var port = await SpawnInside(rig, PortController, new Vector2(1.5f, 3.5f));
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<SharedInteractionSystem>().InteractHand(SPlayer, port), Is.True));
        await RunTicks(3);

        EntityUid oldMagazine = default;
        await Server.WaitAssertion(() =>
        {
            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryGetSlot(rig.PortGun, "gun_magazine", out var slot), Is.True);
            oldMagazine = slot!.Item ?? EntityUid.Invalid;
            Assert.That(oldMagazine, Is.Not.EqualTo(EntityUid.Invalid), "The port gun has no magazine.");

            var ammo = new GetAmmoCountEvent();
            SEntMan.EventBus.RaiseLocalEvent(rig.PortGun, ref ammo);
            Assert.That((ammo.Count, ammo.Capacity), Is.EqualTo((4, 4)));
        });

        for (var shot = 1; shot <= 4; shot++)
        {
            var number = shot;
            await Server.WaitAssertion(() =>
            {
                Fire(SPlayer, rig.PortGun);
                var shots = Projectiles();
                Assert.That(shots, Has.Count.EqualTo(1), $"Shot {number} did not fire.");
                SEntMan.DeleteEntity(shots[0]);
            });
            await RunSeconds(0.2f);
        }

        await Server.WaitAssertion(() =>
        {
            var ammo = new GetAmmoCountEvent();
            SEntMan.EventBus.RaiseLocalEvent(rig.PortGun, ref ammo);
            Assert.That(ammo.Count, Is.EqualTo(0), "The magazine should be empty.");

            Fire(SPlayer, rig.PortGun);
            Assert.That(Projectiles(), Is.Empty, "An empty port gun fired.");
        });

        // Put a fresh magazine to the firing port.
        EntityUid fresh = default;
        await Server.WaitPost(() =>
        {
            fresh = SEntMan.SpawnEntity(PortMagazine, Transform.GetMoverCoordinates(SPlayer));
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(SPlayer, fresh), Is.True);
            SEntMan.System<SharedInteractionSystem>().InteractUsing(SPlayer, fresh, port, Transform.GetMoverCoordinates(port));
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryGetSlot(rig.PortGun, "gun_magazine", out var slot), Is.True);

            var ammo = new GetAmmoCountEvent();
            SEntMan.EventBus.RaiseLocalEvent(rig.PortGun, ref ammo);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(slot!.Item, Is.EqualTo(fresh), "The new magazine is not in the port gun.");
                Assert.That(SEntMan.EntityExists(oldMagazine), Is.True, "The spent magazine was lost.");
                Assert.That(SEntMan.System<ItemSlotsSystem>().GetItemOrNull(rig.PortGun, "gun_magazine"), Is.Not.EqualTo(oldMagazine), "The spent magazine is still in the port gun.");
                Assert.That(ammo.Count, Is.EqualTo(4), "The port gun is not reloaded.");
            }

        });

        // The gunner is still on the port gun: a magazine leaving the gun is not the gun leaving its port.
        await Server.WaitAssertion(() =>
        {
            var guns = SEntMan.System<SharedGunSystem>();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<VehiclePortGunOperatorComponent>(SPlayer), Is.True, "Reloading dropped the gunner off the port gun.");
                Assert.That(SEntMan.GetComponent<VehiclePortGunComponent>(rig.PortGun).Operator, Is.EqualTo(SPlayer));
                Assert.That(guns.TryGetGun(SPlayer, out var gun, out _), Is.True, "The gun lookup stopped answering for the port gunner after the reload.");
                Assert.That(gun, Is.EqualTo(rig.PortGun));
            }
        });

        // The empty click left a short cooldown behind.
        await RunSeconds(0.5f);
        await Server.WaitAssertion(() =>
        {
            Fire(SPlayer, rig.PortGun);
            var shots = Projectiles();
            Assert.That(shots, Has.Count.EqualTo(1), "The reloaded port gun did not fire.");
            SEntMan.DeleteEntity(shots[0]);
        });
    }

    /// <summary>Taking the port gun itself out of its port lets its gunner go: the gun leaving counts, a magazine leaving it does not.</summary>
    [Test]
    public async Task TakingThePortGunOutOfItsPortReleasesTheGunner()
    {
        var rig = await BuildRig();
        await Enter(rig, SPlayer, 0);
        await SeatUp(rig, SPlayer, PortSeat, new Vector2(1.5f, 2.5f));
        var port = await SpawnInside(rig, PortController, new Vector2(1.5f, 3.5f));
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<SharedInteractionSystem>().InteractHand(SPlayer, port), Is.True));
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<VehiclePortGunOperatorComponent>(SPlayer), Is.True, "Test setup: the gunner should be on the port gun.");
        });

        // Out of the vehicle's port by force, as an admin or a wreck would.
        await Server.WaitPost(() =>
        {
            var containers = SEntMan.System<SharedContainerSystem>();
            Assert.That(containers.TryGetContainingContainer((rig.PortGun, null), out var holder), Is.True, "The port gun is not in a container.");
            Assert.That(containers.Remove(rig.PortGun, holder!), Is.True, "The port gun would not come out.");
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<VehiclePortGunOperatorComponent>(SPlayer), Is.False, "The gunner kept a port gun that left its port.");
                Assert.That(SEntMan.GetComponent<VehiclePortGunComponent>(rig.PortGun).Operator, Is.Null);
                Assert.That(SEntMan.System<SharedGunSystem>().TryGetGun(SPlayer, out _, out _), Is.False, "The gunner can still fire a port gun that left its port.");
            }
        });
    }
}
