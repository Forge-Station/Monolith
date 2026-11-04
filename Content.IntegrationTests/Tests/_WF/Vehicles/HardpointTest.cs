#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WF.Vehicles.Core;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.Buckle;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Explosion;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>Hardpoint slots, integrity, damage routing, armour, wheels and repair on a bare driveable chassis.</summary>
public sealed class HardpointTest : InteractionTest
{
    private const string Bare = "WFHardpointTestVehicleBare";
    private const string Fitted = "WFHardpointTestVehicleFitted";
    private const string Worn = "WFHardpointTestVehicleWorn";
    private const string Armoured = "WFHardpointTestVehicleArmoured";
    private const string Gunned = "WFHardpointTestVehicleGunned";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: WFVehicleTestChassis
  id: WFHardpointTestVehicleBare
  components:
  - type: Damageable
    damageContainer: StructuralInorganic
    damageModifierSet: VehicleFrameTank
  - type: Appearance
  - type: ItemSlots
  - type: HardpointSlots
    vehicleFamily: HardpointVehicleFamilyTank
    slots:
    - id: armor
      hardpointType: HardpointTypeArmor
      slotType: HardpointSlotTypeArmor
      insertDelay: 0.5
      required: false
    - id: support
      hardpointType: HardpointTypeSupport
      slotType: HardpointSlotTypeSupportAttachment
      insertDelay: 0.5
      required: false
    - id: wheel-1
      hardpointType: HardpointTypeWheel
      slotType: HardpointSlotTypeTreads
      insertDelay: 0.5
      whitelist:
        components:
        - VehicleWheelItem
  - type: VehicleWheelSlots
    slotCount: 1
    slots:
    - wheel-1
    collisionDamagePerSpeed: 10
  - type: HardpointIntegrity
    maxIntegrity: 1000

- type: entity
  parent: WFHardpointTestVehicleBare
  id: WFHardpointTestVehicleFitted
  components:
  - type: ItemSlots
    slots:
      support:
        startingItem: VehicleTankOverdriveEnhancer
        disableEject: true
        whitelist:
          components:
          - HardpointItem
      wheel-1:
        startingItem: VehicleTankTreads
        disableEject: true
        whitelist:
          components:
          - VehicleWheelItem

- type: entity
  parent: WFHardpointTestVehicleFitted
  id: WFHardpointTestVehicleWorn
  components:
  - type: VehicleWheelSlots
    collisionDamagePerSpeed: 0

- type: entity
  parent: WFHardpointTestVehicleFitted
  id: WFHardpointTestVehicleArmoured
  components:
  - type: ItemSlots
    slots:
      armor:
        startingItem: VehicleTankArmorConcussive
        disableEject: true
        whitelist:
          components:
          - HardpointItem

- type: entity
  parent: BaseItem
  id: WFHardpointTestGun
  components:
  - type: Gun
    fireRate: 2
  - type: HardpointItem
    hardpointType: HardpointTypeCannon
    vehicleFamily: HardpointVehicleFamilyTank
    slotType: HardpointSlotTypeCannon
  - type: HardpointIntegrity
    maxIntegrity: 100

- type: entity
  parent: WFHardpointTestVehicleBare
  id: WFHardpointTestVehicleGunned
  components:
  - type: ItemSlots
    slots:
      cannon:
        startingItem: WFHardpointTestGun
        disableEject: true
        whitelist:
          components:
          - HardpointItem
      support:
        startingItem: VehicleTankWarningArray
        disableEject: true
        whitelist:
          components:
          - HardpointItem
      wheel-1:
        startingItem: VehicleTankTreads
        disableEject: true
        whitelist:
          components:
          - VehicleWheelItem
  - type: HardpointSlots
    vehicleFamily: HardpointVehicleFamilyTank
    slots:
    - id: cannon
      hardpointType: HardpointTypeCannon
      slotType: HardpointSlotTypeCannon
      required: false
    - id: support
      hardpointType: HardpointTypeSupport
      slotType: HardpointSlotTypeSupportAttachment
      insertDelay: 0.5
      required: false
    - id: wheel-1
      hardpointType: HardpointTypeWheel
      slotType: HardpointSlotTypeTreads
      whitelist:
        components:
        - VehicleWheelItem

- type: entity
  parent: SeatBase
  id: WFHardpointTestSeat
  categories: [ HideSpawnMenu ]
  components:
  - type: Physics
    bodyType: Static
  - type: VehicleWeaponsSeat
  - type: UserInterface
    interfaces:
      enum.VehicleWeaponsUiKey.Key:
        type: VehicleWeaponsBoundUserInterface
";

    /// <summary>Spawns a vehicle a few tiles to the side of the target, so two of them can sit in one test.</summary>
    private async Task<EntityUid> SpawnAside(string prototype)
    {
        var uid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords).Offset(new Vector2(0f, 4f));
            uid = SEntMan.SpawnAtPosition(prototype, coords);
        });
        return uid;
    }

    private EntityUid? SlotItem(EntityUid vehicle, string slotId)
    {
        return SEntMan.System<ItemSlotsSystem>().GetItemOrNull(vehicle, slotId);
    }

    private bool CanRun(EntityUid vehicle)
    {
        var ev = new VehicleCanRunEvent((vehicle, SEntMan.GetComponent<RMCVehicleComponent>(vehicle)));
        SEntMan.EventBus.RaiseLocalEvent(vehicle, ref ev);
        return ev.CanRun;
    }

    private float Integrity(EntityUid uid)
    {
        return SEntMan.GetComponent<HardpointIntegrityComponent>(uid).Integrity;
    }

    private FixedPoint2 DamageTaken(EntityUid vehicle)
    {
        return SEntMan.GetComponent<DamageableComponent>(vehicle).TotalDamage;
    }

    private void Hit(EntityUid vehicle, float amount)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict["Blunt"] = FixedPoint2.New(amount);
        SEntMan.System<DamageableSystem>().TryChangeDamage(vehicle, damage);
    }

    /// <summary>A hardpoint goes in through the install do-after and comes out through the removal message.</summary>
    [Test]
    public async Task InstallAndRemoveAHardpoint()
    {
        var vehicle = ToServer(await SpawnTarget(Bare));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SlotItem(vehicle, "wheel-1"), Is.Null, "Test setup: the wheel slot should start empty.");
            Assert.That(SEntMan.HasComponent<HardpointStateComponent>(vehicle), Is.True);
        });

        // Wrong family: a van wheel does not fit a tank.
        await InteractUsing("RMCVanWheel");
        await Server.WaitAssertion(() =>
            Assert.That(SlotItem(vehicle, "wheel-1"), Is.Null, "A hardpoint of another vehicle family should not install."));

        await InteractUsing("VehicleTankTreads");

        EntityUid treads = default;
        await Server.WaitAssertion(() =>
        {
            var installed = SlotItem(vehicle, "wheel-1");
            Assert.That(installed, Is.Not.Null, "The treads did not install.");
            treads = installed!.Value;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.GetComponent<HardpointSlotsComponent>(vehicle).Ui.Hardpoints, Has.Some.Matches<HardpointUiEntry>(e => e.SlotId == "wheel-1" && e.HasItem),
                    "The window state should list the installed treads.");
                Assert.That(Integrity(treads), Is.EqualTo(SEntMan.GetComponent<HardpointIntegrityComponent>(treads).MaxIntegrity));
            }
        });

        // Removal needs a prying tool in hand, then the removal message the window sends.
        await PlaceInHands("Crowbar");
        await Server.WaitPost(() =>
        {
            var message = new HardpointRemoveMessage("wheel-1")
            {
                UiKey = HardpointUiKey.Key,
                Actor = SPlayer,
            };
            SEntMan.EventBus.RaiseLocalEvent(vehicle, message, false);
        });
        await RunTicks(5);
        await AwaitDoAfters();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SlotItem(vehicle, "wheel-1"), Is.Null, "The treads did not come out.");
            Assert.That(SEntMan.Deleted(treads), Is.False, "An intact hardpoint should survive its removal.");
        });
    }

    /// <summary>A hardpoint worn down to nothing is scrap when it comes out.</summary>
    [Test]
    public async Task ADestroyedHardpointDisintegratesOnRemoval()
    {
        var vehicle = ToServer(await SpawnTarget(Fitted));
        var treads = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            treads = SlotItem(vehicle, "wheel-1")!.Value;
            SEntMan.GetComponent<HardpointIntegrityComponent>(treads).Integrity = 0f;
        });

        await PlaceInHands("Crowbar");
        await Server.WaitPost(() =>
        {
            var message = new HardpointRemoveMessage("wheel-1")
            {
                UiKey = HardpointUiKey.Key,
                Actor = SPlayer,
            };
            SEntMan.EventBus.RaiseLocalEvent(vehicle, message, false);
        });
        await RunTicks(5);
        await AwaitDoAfters();
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SlotItem(vehicle, "wheel-1"), Is.Null, "The wrecked treads should have come off.");
            Assert.That(SEntMan.Deleted(treads), Is.True, "Wrecked treads should crumble to scrap.");
        });
    }

    /// <summary>Removal asks for a prying tool.</summary>
    [Test]
    public async Task RemovalNeedsAPryingTool()
    {
        var vehicle = ToServer(await SpawnTarget(Fitted));

        await Server.WaitPost(() =>
        {
            var message = new HardpointRemoveMessage("wheel-1")
            {
                UiKey = HardpointUiKey.Key,
                Actor = SPlayer,
            };
            SEntMan.EventBus.RaiseLocalEvent(vehicle, message, false);
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SlotItem(vehicle, "wheel-1"), Is.Not.Null, "Empty hands must not pull a hardpoint.");
            Assert.That(SEntMan.GetComponent<HardpointSlotsComponent>(vehicle).Ui.Error, Is.Not.Null.And.Not.Empty);
        });
    }

    /// <summary>An empty required slot, or a destroyed hardpoint in it, keeps the vehicle from running.</summary>
    [Test]
    public async Task AnEmptyRequiredSlotStopsTheVehicle()
    {
        var bare = ToServer(await SpawnTarget(Bare));
        var fitted = await SpawnAside(Fitted);
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(CanRun(bare), Is.False, "No wheels, no run.");
                Assert.That(CanRun(fitted), Is.True, "A fitted vehicle should run.");
            }

            var treads = SlotItem(fitted, "wheel-1")!.Value;
            var integrity = SEntMan.GetComponent<HardpointIntegrityComponent>(treads);
            integrity.Integrity = 0f;
            SEntMan.System<RMCVehicleSystem>().RefreshCanRun((fitted, null));
            Assert.That(CanRun(fitted), Is.False, "Destroyed treads should stop the vehicle too.");
        });
    }

    /// <summary>Hardpoints soak up most of a hit, the frame takes a fraction, and the hardpoints wear in step.</summary>
    [Test]
    public async Task DamageIsSplitBetweenHardpointsAndFrame()
    {
        var bare = ToServer(await SpawnTarget(Bare));
        var fitted = await SpawnAside(Fitted);
        await RunTicks(5);

        FixedPoint2 bareDamage = default;
        FixedPoint2 fittedDamage = default;
        float bareFrame = 0f;
        float fittedFrame = 0f;

        await Server.WaitAssertion(() =>
        {
            Hit(bare, 100);
            Hit(fitted, 100);

            bareDamage = DamageTaken(bare);
            fittedDamage = DamageTaken(fitted);
            bareFrame = 1000f - Integrity(bare);
            fittedFrame = 1000f - Integrity(fitted);

            var treads = SlotItem(fitted, "wheel-1")!.Value;
            var support = SlotItem(fitted, "support")!.Value;
            var slots = SEntMan.GetComponent<HardpointSlotsComponent>(fitted);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(bareFrame, Is.GreaterThan(0f), "A bare frame takes the hit itself.");
                Assert.That(fittedFrame, Is.EqualTo(bareFrame * slots.FrameDamageFractionWhileIntact).Within(0.01f),
                    "With a hardpoint standing the frame takes only its protected fraction.");
                Assert.That((float) fittedDamage, Is.EqualTo((float) bareDamage * slots.FrameDamageFractionWhileIntact).Within(0.01f),
                    "The hull's own damage is scaled the same way.");
                Assert.That(Integrity(treads), Is.LessThan(SEntMan.GetComponent<HardpointIntegrityComponent>(treads).MaxIntegrity),
                    "The treads should have worn.");
                Assert.That(Integrity(support), Is.LessThan(SEntMan.GetComponent<HardpointIntegrityComponent>(support).MaxIntegrity),
                    "So should the support module.");
            }
        });
    }

    /// <summary>Plating over a block of the test grid, so a chassis longer than a tile stands on deck and not over space.</summary>
    private async Task LayDeck(int minX, int minY, int maxX, int maxY)
    {
        await Server.WaitPost(() =>
        {
            var plating = new Tile(TileMan[Plating].TileId);
            for (var x = minX; x < maxX; x++)
            {
                for (var y = minY; y < maxY; y++)
                {
                    MapSystem.SetTile(MapData.Grid, new Vector2i(x, y), plating);
                }
            }
        });
    }

    /// <summary>A real blast beside a parked vehicle wears its frame and hardpoints and leaves it where it stood; explosions skip the damage modifiers, so this runs through BeforeExplodeEvent.</summary>
    [Test]
    public async Task AnExplosionReducesIntegrity()
    {
        await LayDeck(-3, -3, 6, 4);

        var vehicle = ToServer(await SpawnTarget(Fitted));
        await RunTicks(5);

        var parent = EntityUid.Invalid;
        var start = Vector2.Zero;
        var startRotation = Angle.Zero;

        await Server.WaitPost(() =>
        {
            var xform = SEntMan.GetComponent<TransformComponent>(vehicle);
            parent = xform.ParentUid;
            start = xform.LocalPosition;
            startRotation = xform.LocalRotation;

            // Next to the vehicle's tile, so the blast covers it; a quarter turn from the shockwave once carried the chassis into one two tiles off.
            var pos = Transform.GetMapCoordinates(vehicle);
            var epicenter = new MapCoordinates(pos.Position - new Vector2(1f, 0f), pos.MapId);
            SEntMan.System<ExplosionSystem>().QueueExplosion(epicenter, "Default", 40f, 5f, 10f, null);
        });
        await RunTicks(30);

        await Server.WaitAssertion(() =>
        {
            var treads = SlotItem(vehicle, "wheel-1")!.Value;
            var xform = SEntMan.GetComponent<TransformComponent>(vehicle);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Integrity(vehicle), Is.LessThan(1000f), "The frame did not feel the blast.");
                Assert.That(Integrity(treads), Is.LessThan(SEntMan.GetComponent<HardpointIntegrityComponent>(treads).MaxIntegrity),
                    "Neither did the treads.");
                Assert.That(xform.ParentUid, Is.EqualTo(parent), "The blast took the vehicle off its grid.");
                Assert.That((xform.LocalPosition - start).Length(), Is.LessThan(0.001f), "The blast moved the vehicle.");
                Assert.That(xform.LocalRotation.Theta, Is.EqualTo(startRotation.Theta).Within(0.001f), "The blast turned the vehicle.");
            }
        });
    }

    /// <summary>Armour adds its modifier sets and explosion coefficient when installed and takes them away again.</summary>
    [Test]
    public async Task ArmourAppliesAndLifts()
    {
        var armoured = ToServer(await SpawnTarget(Armoured));
        var plain = await SpawnAside(Fitted);
        await RunTicks(5);

        float Coefficient(EntityUid uid)
        {
            var ev = new GetExplosionResistanceEvent("Default");
            SEntMan.EventBus.RaiseLocalEvent(uid, ref ev);
            return ev.DamageCoefficient;
        }

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Coefficient(plain), Is.EqualTo(1f), "Bare plating takes the whole blast.");
                Assert.That(Coefficient(armoured), Is.EqualTo(0.5f).Within(0.0001f), "Concussive plating halves it.");
                Assert.That(SEntMan.HasComponent<DamageProtectionBuffComponent>(plain), Is.False);
                Assert.That(SEntMan.GetComponent<DamageProtectionBuffComponent>(armoured).Modifiers, Contains.Key("VehicleArmorConcussive"));
            }

            Hit(plain, 100);
            Hit(armoured, 100);
            Assert.That(1000f - Integrity(armoured), Is.LessThan(1000f - Integrity(plain)),
                "Armour should cut the frame's share of the hit.");
        });

        await PlaceInHands("Crowbar");
        await Server.WaitPost(() =>
        {
            var message = new HardpointRemoveMessage("armor")
            {
                UiKey = HardpointUiKey.Key,
                Actor = SPlayer,
            };
            SEntMan.EventBus.RaiseLocalEvent(armoured, message, false);
        });
        await RunTicks(5);
        await AwaitDoAfters();

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SlotItem(armoured, "armor"), Is.Null, "The plating did not come off.");
                Assert.That(Coefficient(armoured), Is.EqualTo(1f), "Its explosion coefficient should go with it.");
                Assert.That(SEntMan.HasComponent<DamageProtectionBuffComponent>(armoured), Is.False, "So should its modifier sets.");
            }
        });
    }

    /// <summary>A speed module is summed onto the vehicle while installed.</summary>
    [Test]
    public async Task SupportModulesAreAggregated()
    {
        var vehicle = ToServer(await SpawnTarget(Fitted));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<VehicleSpeedModifierComponent>(vehicle).SpeedMultiplier, Is.EqualTo(1.2f).Within(0.0001f),
                "The overdrive enhancer should speed the vehicle up.");
        });
    }

    /// <summary>A sensor array's fire rate bonus reaches the guns mounted on the vehicle, through the hardpoint item.</summary>
    [Test]
    public async Task SupportModulesSpeedUpMountedGuns()
    {
        var vehicle = ToServer(await SpawnTarget(Gunned));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var gun = SlotItem(vehicle, "cannon")!.Value;
            Assert.That(SEntMan.GetComponent<GunComponent>(gun).FireRateModified, Is.EqualTo(2f * 1.15f).Within(0.001f),
                "The warning array should raise the cannon's fire rate by 15%.");
        });
    }

    /// <summary>The mover's impact event wears the wheels; a crash alone does not.</summary>
    [Test]
    public async Task ImpactsWearTheWheels()
    {
        var vehicle = ToServer(await SpawnTarget(Fitted));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var treads = SlotItem(vehicle, "wheel-1")!.Value;
            var start = Integrity(treads);

            var crash = new VehicleCollisionEvent(VehicleCollisionKind.Crash, null, 4f, 6f);
            SEntMan.EventBus.RaiseLocalEvent(vehicle, ref crash);
            Assert.That(Integrity(treads), Is.EqualTo(start), "A crash on its own is the cabin's business, not the wheels'.");

            var impact = new VehicleCollisionEvent(VehicleCollisionKind.Impact, null, 4f, 6f);
            SEntMan.EventBus.RaiseLocalEvent(vehicle, ref impact);
            Assert.That(Integrity(treads), Is.LessThan(start), "Hitting something hard should damage the wheels.");
        });
    }

    /// <summary>A hit costs the frame a share of its integrity that grows with speed; a bump costs nothing, and the weight class scales it.</summary>
    [Test]
    public async Task ImpactsHurtTheFrameBySpeed()
    {
        var vehicle = ToServer(await SpawnTarget(Fitted));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var frame = SEntMan.GetComponent<HardpointIntegrityComponent>(vehicle);
            var weight = WFVehicleImpactDamageSystem.WeightScale(SEntMan.GetComponent<GridVehicleMoverComponent>(vehicle).WeightClass);
            var fraction = Server.CfgMan.GetCVar(VehicleImpactCVars.FrameFraction);
            frame.Integrity = frame.MaxIntegrity;

            var bump = new VehicleCollisionEvent(VehicleCollisionKind.Impact, null, 0.5f, 6f);
            SEntMan.EventBus.RaiseLocalEvent(vehicle, ref bump);
            Assert.That(frame.Integrity, Is.EqualTo(frame.MaxIntegrity), "A bump at a crawl should cost nothing.");

            var half = new VehicleCollisionEvent(VehicleCollisionKind.Impact, null, -3f, 6f);
            SEntMan.EventBus.RaiseLocalEvent(vehicle, ref half);
            var afterHalf = frame.Integrity;
            Assert.That(frame.MaxIntegrity - afterHalf, Is.EqualTo(frame.MaxIntegrity * fraction * 0.5f * weight).Within(0.5f),
                "Half speed (reversing counts) should cost half the full share.");

            var crash = new VehicleCollisionEvent(VehicleCollisionKind.Crash, null, 6f, 6f);
            SEntMan.EventBus.RaiseLocalEvent(vehicle, ref crash);
            Assert.That(frame.Integrity, Is.EqualTo(afterHalf), "A crash on its own is the cabin's business, not the frame's.");

            frame.Integrity = frame.MaxIntegrity;
            var full = new VehicleCollisionEvent(VehicleCollisionKind.Impact, null, 6f, 6f);
            SEntMan.EventBus.RaiseLocalEvent(vehicle, ref full);
            Assert.That(frame.MaxIntegrity - frame.Integrity, Is.EqualTo(frame.MaxIntegrity * fraction * weight).Within(0.5f),
                "Top speed should cost the full share.");
        });
    }

    /// <summary>Wheels with no collision wear of their own wear by speed; the vehicle that was hit is hurt too.</summary>
    [Test]
    public async Task WheelsAndTheVehicleHitAreHurtToo()
    {
        var vehicle = ToServer(await SpawnTarget(Worn));
        var struck = await SpawnAside(Fitted);
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var treads = SlotItem(vehicle, "wheel-1")!.Value;
            var start = Integrity(treads);
            var struckFrame = Integrity(struck);

            var impact = new VehicleCollisionEvent(VehicleCollisionKind.Impact, struck, 6f, 6f);
            SEntMan.EventBus.RaiseLocalEvent(vehicle, ref impact);

            Assert.That(Integrity(treads), Is.LessThan(start), "A hit at top speed should wear wheels that carry no wear of their own.");
            Assert.That(Integrity(struck), Is.LessThan(struckFrame), "The vehicle that was run into should take damage too.");
        });
    }

    /// <summary>A ship's hull hitting a vehicle deals it a multiple of what other things take.</summary>
    [Test]
    public async Task HullImpactsHurtVehiclesMore()
    {
        var vehicle = ToServer(await SpawnTarget(Fitted));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var shuttles = SEntMan.System<ShuttleSystem>();
            var multiplier = Server.CfgMan.GetCVar(VehicleImpactCVars.HullMultiplier);
            Assert.That(multiplier, Is.GreaterThan(1f));
            Assert.That(shuttles.VehicleHullMultiplier(vehicle), Is.EqualTo(multiplier));
            Assert.That(shuttles.VehicleHullMultiplier(SPlayer), Is.EqualTo(1f), "Only vehicles take the extra.");
        });
    }

    /// <summary>Welding mends the frame in chunks and stops at the weld cap; the wrench finishes it.</summary>
    [Test]
    public async Task RepairStopsAtTheWeldCap()
    {
        var vehicle = ToServer(await SpawnTarget(Fitted));

        HardpointIntegrityComponent frame = default!;
        await Server.WaitPost(() =>
        {
            frame = SEntMan.GetComponent<HardpointIntegrityComponent>(vehicle);
            frame.Integrity = 600f;
            SEntMan.Dirty(vehicle, frame);
        });
        var cap = frame.MaxIntegrity * frame.FrameWeldCapFraction;

        await InteractUsing(Wrench);
        Assert.That(frame.Integrity, Is.EqualTo(600f), "The wrench cannot start before the weld is done.");

        await InteractUsing(Weld);
        Assert.That(frame.Integrity, Is.EqualTo(cap).Within(0.01f), "Welding should stop at the weld cap.");

        await InteractUsing(Weld);
        Assert.That(frame.Integrity, Is.EqualTo(cap).Within(0.01f), "Another weld past the cap must change nothing.");

        await InteractUsing(Wrench);
        Assert.That(frame.Integrity, Is.EqualTo(frame.MaxIntegrity).Within(0.01f), "The wrench finishes the frame.");
    }

    /// <summary>Takes the part out of a slot the way a crew does, with a prying tool in hand and the removal message the window sends.</summary>
    private async Task PryOut(EntityUid vehicle, string slotId)
    {
        await PlaceInHands("Crowbar");
        await Server.WaitPost(() =>
        {
            var message = new HardpointRemoveMessage(slotId)
            {
                UiKey = HardpointUiKey.Key,
                Actor = SPlayer,
            };
            SEntMan.EventBus.RaiseLocalEvent(vehicle, message, false);
        });
        await RunTicks(5);
        await AwaitDoAfters();
        await RunTicks(5);
    }

    /// <summary>The fire rate bonus of a sensor array goes from the guns when the module is pried out.</summary>
    [Test]
    public async Task RemovingASensorArrayTakesItsFireRateBonusAway()
    {
        var gunned = ToServer(await SpawnTarget(Gunned));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var gun = SlotItem(gunned, "cannon")!.Value;
            Assert.That(SEntMan.GetComponent<GunComponent>(gun).FireRateModified, Is.EqualTo(2f * 1.15f).Within(0.001f), "Test setup: the array should be speeding the cannon up.");
        });

        await PryOut(gunned, "support");

        await Server.WaitAssertion(() =>
        {
            var gun = SlotItem(gunned, "cannon")!.Value;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SlotItem(gunned, "support"), Is.Null, "The array did not come out.");
                Assert.That(SEntMan.HasComponent<VehicleWeaponSupportModifierComponent>(gunned), Is.False, "The vehicle still sums the array's bonus.");
                Assert.That(SEntMan.GetComponent<GunComponent>(gun).FireRateModified, Is.EqualTo(2f).Within(0.001f), "The cannon kept the array's fire rate bonus.");
            }
        });

    }

    /// <summary>The speed bonus of an enhancer sits on a vehicle component the mover reads, so the component has to go with the module.</summary>
    [Test]
    public async Task RemovingAnEnhancerTakesItsSpeedBonusAway()
    {
        var fitted = ToServer(await SpawnTarget(Fitted));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<VehicleSpeedModifierComponent>(fitted), Is.True, "Test setup: the enhancer should be speeding the vehicle up.");
        });

        await PryOut(fitted, "support");

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SlotItem(fitted, "support"), Is.Null, "The enhancer did not come out.");
                Assert.That(SEntMan.HasComponent<VehicleSpeedModifierComponent>(fitted), Is.False, "The vehicle kept the enhancer's speed bonus.");
            }
        });
    }

    /// <summary>
    /// A gunner strapped into a weapons seat gets the wider view a view module gives; prying the module out takes it back
    /// to the PVS scale they had, with no refresh needed from anything else.
    /// </summary>
    [Test]
    public async Task RemovingTheViewModuleNarrowsASeatedGunnersView()
    {
        var vehicle = ToServer(await SpawnTarget(Bare));
        var gunner = EntityUid.Invalid;
        var seat = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            var spot = SEntMan.GetCoordinates(TargetCoords).Offset(new Vector2(0f, 4f));
            seat = SEntMan.SpawnEntity("WFHardpointTestSeat", spot);
            gunner = SEntMan.SpawnEntity("MobHuman", spot);
        });
        await RunTicks(3);

        float Pvs() => SEntMan.GetComponent<EyeComponent>(gunner).PvsScale;
        var before = 0f;

        // The seat is not in an interior, so the gunner is stood up as an operator by hand, as the component's doc allows.
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(gunner, gunner, seat), Is.True, "The gunner could not strap in.");
            SEntMan.EnsureComponent<VehicleWeaponsComponent>(vehicle);
            SEntMan.EnsureComponent<VehicleWeaponsOperatorComponent>(gunner).Vehicle = vehicle;
            before = Pvs();
        });

        await InteractUsing("VehicleTankArtilleryModule");

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SlotItem(vehicle, "support"), Is.Not.Null, "The view module did not install.");
                Assert.That(SEntMan.HasComponent<VehicleGunnerViewUserComponent>(gunner), Is.True, "The seated gunner got no wider view.");
                Assert.That(Pvs(), Is.EqualTo(before + 0.85f).Within(0.001f), "The seated gunner's PVS box was not widened.");
            }
        });

        await PryOut(vehicle, "support");

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SlotItem(vehicle, "support"), Is.Null, "The view module did not come out.");
                Assert.That(SEntMan.HasComponent<VehicleGunnerViewComponent>(vehicle), Is.False, "The vehicle still sums the module's view.");
                Assert.That(SEntMan.HasComponent<VehicleGunnerViewUserComponent>(gunner), Is.False, "The gunner kept the wider view.");
                Assert.That(Pvs(), Is.EqualTo(before).Within(0.001f), "The gunner's PVS box stayed wide.");
            }
        });
    }

    /// <summary>An install whose installer is deleted mid do-after never reports back; the slot must not stay claimed by it.</summary>
    [Test]
    public async Task AnInstallAbandonedByADeletedInstallerDoesNotLockTheSlot()
    {
        var vehicle = ToServer(await SpawnTarget(Bare));
        var installer = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            var spot = SEntMan.GetCoordinates(PlayerCoords);
            installer = SEntMan.SpawnEntity("MobHuman", spot);
            var treads = SEntMan.SpawnEntity("VehicleTankTreads", spot);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(installer, treads), Is.True, "The installer could not hold the treads.");
            SEntMan.System<SharedInteractionSystem>().InteractUsing(installer, treads, vehicle, SEntMan.GetComponent<TransformComponent>(vehicle).Coordinates);
        });
        await RunTicks(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(DoAftersOf(installer), Is.EqualTo(1), "Test setup: the install should be under way.");
        });

        await Server.WaitPost(() => SEntMan.DeleteEntity(installer));
        await RunSeconds(1f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SlotItem(vehicle, "wheel-1"), Is.Null, "Test setup: nothing should have been fitted by a deleted installer.");
        });

        await InteractUsing("VehicleTankTreads");

        await Server.WaitAssertion(() =>
        {
            Assert.That(SlotItem(vehicle, "wheel-1"), Is.Not.Null, "A deleted installer left the slot unable to take a part.");
        });
    }

    private int DoAftersOf(EntityUid user)
    {
        return SEntMan.TryGetComponent(user, out DoAfterComponent? doAfters) ? doAfters.DoAfters.Count : 0;
    }

    /// <summary>A part put in a slot it does not fit without a user (nobody to ask) is taken back out, whatever the slot's eject lock says.</summary>
    [Test]
    public async Task APartThatDoesNotFitItsSlotIsEjectedWhenNoUserPutItThere()
    {
        var vehicle = ToServer(await SpawnTarget(Bare));
        var wheel = EntityUid.Invalid;
        var treads = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            var spot = SEntMan.GetCoordinates(TargetCoords).Offset(new Vector2(0f, 4f));
            wheel = SEntMan.SpawnEntity("RMCVanWheel", spot);
            treads = SEntMan.SpawnEntity("VehicleTankTreads", spot);

            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(vehicle, "wheel-1", wheel, null), Is.True, "Test setup: a user-less insert should skip the install check.");
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SlotItem(vehicle, "wheel-1"), Is.Null, "A van wheel stayed in a tank's wheel slot.");
                Assert.That(SEntMan.Deleted(wheel), Is.False, "The ejected part was lost.");
                Assert.That(SEntMan.System<SharedContainerSystem>().IsEntityInContainer(wheel), Is.False, "The ejected part is still inside something.");
                Assert.That(CanRun(vehicle), Is.False, "A part that does not fit still counted as the vehicle's wheel.");
            }

            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(vehicle, "wheel-1", treads, null), Is.True);
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SlotItem(vehicle, "wheel-1"), Is.EqualTo(treads), "A part that fits was ejected.");
        });
    }
}
