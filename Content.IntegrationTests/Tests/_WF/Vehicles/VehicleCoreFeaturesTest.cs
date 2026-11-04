#nullable enable
using System;
using System.Numerics;
using Content.Client._WF.Vehicles.Ui;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Actions;
using Content.Shared.Input;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Containers;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>Horn, spotlight, overcharge and the hardpoint window, driven through the client the way a player would.</summary>
public sealed class VehicleCoreFeaturesTest : InteractionTest
{
    #pragma warning disable RA0002 // Set deliberately mismatched test keys before exercising authenticated UI requests.
    [Test]
    public async Task IgnitionStoresKeyAndRequiresTurnBeforeStarting()
    {
        var vehicle = await SpawnAndSeat(Vehicle);
        var key = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            SEntMan.AddComponent<WFVehicleEngineComponent>(vehicle);
            SEntMan.EnsureComponent<VehicleLockComponent>(vehicle).KeyId = "ignition-test";
            key = SEntMan.SpawnAtPosition("RMCVehicleKey", SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates);
            SEntMan.GetComponent<VehicleKeyComponent>(key).KeyId = "wrong-key";
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(SPlayer, key), Is.True);
        });
        var request = SEntMan.GetNetEntity(vehicle);
        await SendRaw(new WFVehicleControlRequest(request, false, accessories: true));
        Assert.That(SEntMan.GetComponent<WFVehicleEngineComponent>(vehicle).IgnitionKey, Is.Null);
        await Server.WaitPost(() => SEntMan.GetComponent<VehicleKeyComponent>(key).KeyId = "ignition-test");
        await SendRaw(new WFVehicleControlRequest(request, false, accessories: true));
        var engine = SEntMan.GetComponent<WFVehicleEngineComponent>(vehicle);
        Assert.That(engine.IgnitionKey, Is.EqualTo(key));
        Assert.That(engine.Electrics, Is.False, "Insertion must not turn the ignition.");
        Assert.That(SEntMan.System<SharedContainerSystem>().IsEntityInContainer(key), Is.True);
        await SendRaw(new WFVehicleControlRequest(request, false));
        Assert.That(engine.Running, Is.False, "The starter must be disabled until the key turns.");
        await SendRaw(new WFVehicleControlRequest(request, false, accessories: true));
        Assert.That(engine.Electrics, Is.True);
        await SendRaw(new WFVehicleControlRequest(request, false));
        Assert.That(engine.Running, Is.True);
        await SendRaw(new WFVehicleControlRequest(request, false, ejectKey: true));
        Assert.That(engine.IgnitionKey, Is.EqualTo(key), "A live ignition must retain its key.");
        await SendRaw(new WFVehicleControlRequest(request, false, accessories: true));
        Assert.That(engine.Running, Is.False);
        Assert.That(engine.Electrics, Is.False);
        await SendRaw(new WFVehicleControlRequest(request, false, ejectKey: true));
        Assert.That(engine.IgnitionKey, Is.Null);
        Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(SPlayer, key), Is.True);
    }

    #pragma warning restore RA0002

    private const string Vehicle = "WFCoreTestVehicle";
    private const string Hardpointed = "WFCoreTestVehicleHardpointed";
    private const string Lit = "WFCoreTestVehicleLit";
    private const string OverchargeAction = "WFActionVehicleOvercharge";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: WFVehicleTestChassis
  id: WFCoreTestVehicle
  components:
  - type: VehicleSound
    hornSound: /Audio/Effects/Vehicle/carhorn.ogg
    hornCooldown: 5
  - type: VehicleSpotlight
    radius: 6
    energy: 2.5
    softness: 2
  - type: PointLight
    lightMask: ConeSingle
    autoRot: true
    enabled: false
    radius: 6
    energy: 2.5
    softness: 2
  - type: VehicleOvercharge
    speedMultiplier: 1.6
    duration: 3
    cooldown: 16

- type: entity
  parent: WFCoreTestVehicle
  id: WFCoreTestVehicleHardpointed
  components:
  - type: Damageable
    damageContainer: StructuralInorganic
    damageModifierSet: VehicleFrameTank
  - type: Appearance
  - type: UserInterface
    interfaces:
      enum.HardpointUiKey.Key:
        type: HardpointBoundUserInterface
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
  - type: HardpointSlots
    vehicleFamily: HardpointVehicleFamilyTank
    slots:
    - id: armor
      hardpointType: HardpointTypeArmor
      slotType: HardpointSlotTypeArmor
      required: false
    - id: support
      hardpointType: HardpointTypeSupport
      slotType: HardpointSlotTypeSupportAttachment
      required: false
    - id: wheel-1
      hardpointType: HardpointTypeWheel
      slotType: HardpointSlotTypeTreads
      whitelist:
        components:
        - VehicleWheelItem
  - type: VehicleWheelSlots
    slotCount: 1
    slots:
    - wheel-1
  - type: HardpointIntegrity
    maxIntegrity: 1000

- type: entity
  parent: WFCoreTestVehicle
  id: WFCoreTestVehicleLit
  components:
  - type: Damageable
    damageContainer: StructuralInorganic
    damageModifierSet: VehicleFrameTank
  - type: Appearance
  - type: ItemSlots
    slots:
      roof:
        startingItem: VehicleHumveeOverlight
        disableEject: true
        whitelist:
          components:
          - HardpointItem
  - type: HardpointSlots
    vehicleFamily: HardpointVehicleFamilyHumvee
    slots:
    - id: roof
      hardpointType: HardpointTypeRoofAttachment
      slotType: HardpointSlotTypeRoofAttachment
      required: false
  - type: HardpointIntegrity
    maxIntegrity: 1000
";

    private EntityUid SpawnVehicle(string prototype)
    {
        return SEntMan.SpawnAtPosition(prototype, SEntMan.GetCoordinates(TargetCoords));
    }

    private async Task<EntityUid> SpawnAndSeat(string prototype, EntityUid? operatorUid = null)
    {
        var vehicle = EntityUid.Invalid;
        await Server.WaitPost(() => vehicle = SpawnVehicle(prototype));
        await RunTicks(5);
        await Seat(vehicle, operatorUid ?? SPlayer);
        return vehicle;
    }

    private async Task Seat(EntityUid vehicle, EntityUid operatorUid)
    {
        await Server.WaitAssertion(() =>
        {
            var system = SEntMan.System<RMCVehicleSystem>();
            var comp = SEntMan.GetComponent<RMCVehicleComponent>(vehicle);
            Assert.That(system.TrySetOperator((vehicle, comp), operatorUid), Is.True, "The mob could not take the wheel.");
        });

        // Let the operator component reach the client, which the key binds read.
        await RunTicks(5);
    }

    /// <summary>Presses a key on the client and reports whether a client bind swallowed it.</summary>
    private async Task<bool> PressAndCheckSwallowed(BoundKeyFunction key)
    {
        var swallowed = false;

        var funcId = InputManager.NetworkBindMap.KeyFunctionID(key);
        var down = new ClientFullInputCmdMessage(CTiming.CurTick, CTiming.TickFraction, funcId)
        {
            State = BoundKeyState.Down,
            Coordinates = CEntMan.GetCoordinates(TargetCoords),
            Uid = CEntMan.GetEntity(Target ?? default),
        };
        await Client.WaitPost(() => swallowed = InputSystem.HandleInputCommand(ClientSession, key, down));
        await RunTicks(5);

        var up = new ClientFullInputCmdMessage(CTiming.CurTick, CTiming.TickFraction, funcId)
        {
            State = BoundKeyState.Up,
            Coordinates = CEntMan.GetCoordinates(TargetCoords),
            Uid = CEntMan.GetEntity(Target ?? default),
        };
        await Client.WaitPost(() => InputSystem.HandleInputCommand(ClientSession, key, up));
        await RunTicks(2);

        return swallowed;
    }

    /// <summary>Sends a request the way a client that skipped its own checks would.</summary>
    private async Task SendRaw(EntityEventArgs message)
    {
        await Client.WaitPost(() => CEntMan.EntityNetManager!.SendSystemNetworkMessage(message));
        await RunTicks(5);
    }

    private EntityUid? FindOvercharge(IEntityManager entMan, EntityUid holder)
    {
        if (!entMan.TryGetComponent(holder, out ActionsComponent? actions))
            return null;

        foreach (var action in actions.Actions)
        {
            if (entMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == OverchargeAction)
                return action;
        }

        return null;
    }

    private TimeSpan NextHorn(EntityUid vehicle)
    {
        return SEntMan.GetComponent<VehicleSoundComponent>(vehicle).NextHornSound;
    }

    /// <summary>The operator's use-item key sounds the horn once, and it then waits out its cooldown.</summary>
    [Test]
    public async Task OperatorHornIsHonoured()
    {
        var vehicle = await SpawnAndSeat(Vehicle);

        Assert.That(NextHorn(vehicle), Is.EqualTo(TimeSpan.Zero));

        await PressKey(ContentKeyFunctions.UseItemInHand, ticks: 3);
        await RunTicks(5);

        var first = NextHorn(vehicle);
        Assert.That(first, Is.GreaterThan(TimeSpan.Zero), "The operator's horn key did nothing.");

        // A second press inside the cooldown does not restart it.
        await PressKey(ContentKeyFunctions.UseItemInHand, ticks: 3);
        await RunTicks(5);
        Assert.That(NextHorn(vehicle), Is.EqualTo(first), "The horn sounded again inside its cooldown.");
    }

    /// <summary>Someone who is not at the wheel cannot honk it, even with a forged request.</summary>
    [Test]
    public async Task NonOperatorHornIsRefused()
    {
        var driver = EntityUid.Invalid;
        await Server.WaitPost(() => driver = SEntMan.SpawnAtPosition("InteractionTestMob", SEntMan.GetCoordinates(PlayerCoords)));
        var vehicle = await SpawnAndSeat(Vehicle, driver);

        await SendRaw(new VehicleHornRequestEvent(SEntMan.GetNetEntity(vehicle)));
        Assert.That(NextHorn(vehicle), Is.EqualTo(TimeSpan.Zero), "A forged request from the player honked a vehicle someone else drives.");

        // And a vehicle nobody drives refuses it as well.
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<RMCVehicleComponent>(vehicle);
            Assert.That(SEntMan.System<RMCVehicleSystem>().TryRemoveOperator((vehicle, comp)), Is.True);
        });
        await SendRaw(new VehicleHornRequestEvent(SEntMan.GetNetEntity(vehicle)));
        Assert.That(NextHorn(vehicle), Is.EqualTo(TimeSpan.Zero), "A forged request honked a vehicle with no operator.");

        // Control: the same raw request works once the sender is the operator, so the refusals above were the check.
        await Seat(vehicle, SPlayer);
        await SendRaw(new VehicleHornRequestEvent(SEntMan.GetNetEntity(vehicle)));
        Assert.That(NextHorn(vehicle), Is.GreaterThan(TimeSpan.Zero), "The raw request never reached the horn handler.");
    }

    /// <summary>The operator's flip key switches the spotlight and its light on and off.</summary>
    [Test]
    public async Task OperatorSpotlightToggles()
    {
        var vehicle = await SpawnAndSeat(Vehicle);

        var swallowed = await PressAndCheckSwallowed(ContentKeyFunctions.FlipObject);
        Assert.That(swallowed, Is.True, "The operator's flip key should be consumed by the spotlight.");

        Assert.That(SEntMan.GetComponent<VehicleSpotlightComponent>(vehicle).Enabled, Is.True);
        Assert.That(SEntMan.GetComponent<Robust.Server.GameObjects.PointLightComponent>(vehicle).Enabled, Is.True);
        Assert.That(CEntMan.GetComponent<VehicleSpotlightComponent>(ToClient(vehicle)).Enabled, Is.True, "The client never saw the spotlight come on.");
        var beam = CEntMan.GetComponent<Robust.Client.GameObjects.PointLightComponent>(ToClient(vehicle));
        Assert.That(beam.LightMask?.Id, Is.EqualTo("ConeSingle"));
        Assert.That(beam.MaskAutoRotate, Is.True, "The beam must follow the chassis heading.");
        var fixtures = CEntMan.GetComponent<FixturesComponent>(ToClient(vehicle));
        // Vehicles face south, so the front bumper is the bottom of the hull.
        var front = float.MaxValue;
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;
            for (var child = 0; child < fixture.Shape.ChildCount; child++)
                front = MathF.Min(front, fixture.Shape.ComputeAABB(Robust.Shared.Physics.Transform.Empty, child).Bottom);
        }
        Assert.That(beam.Offset.Y, Is.EqualTo(front - 0.05f).Within(0.001f),
            "The light must originate at the front bumper, not the hull centre.");

        await PressAndCheckSwallowed(ContentKeyFunctions.FlipObject);

        Assert.That(SEntMan.GetComponent<VehicleSpotlightComponent>(vehicle).Enabled, Is.False);
        Assert.That(SEntMan.GetComponent<Robust.Server.GameObjects.PointLightComponent>(vehicle).Enabled, Is.False);
    }

    /// <summary>A forged toggle from a non-operator does nothing, and the flip key still reaches the server for them.</summary>
    [Test]
    public async Task NonOperatorSpotlightIsRefused()
    {
        var vehicle = EntityUid.Invalid;
        await Server.WaitPost(() => vehicle = SpawnVehicle(Vehicle));
        await RunTicks(5);

        await SendRaw(new VehicleSpotlightToggleRequestEvent(SEntMan.GetNetEntity(vehicle)));
        Assert.That(SEntMan.GetComponent<VehicleSpotlightComponent>(vehicle).Enabled, Is.False, "A forged request toggled a vehicle with no operator.");

        var driver = EntityUid.Invalid;
        await Server.WaitPost(() => driver = SEntMan.SpawnAtPosition("InteractionTestMob", SEntMan.GetCoordinates(PlayerCoords)));
        await Seat(vehicle, driver);

        await SendRaw(new VehicleSpotlightToggleRequestEvent(SEntMan.GetNetEntity(vehicle)));
        Assert.That(SEntMan.GetComponent<VehicleSpotlightComponent>(vehicle).Enabled, Is.False, "A forged request from the player toggled a vehicle someone else drives.");

        // The player is not operating anything, so the key must go through to the server (it flips objects there).
        var swallowed = await PressAndCheckSwallowed(ContentKeyFunctions.FlipObject);
        Assert.That(swallowed, Is.False, "The flip key was swallowed for someone who is not driving.");

        // Control: the same raw request works once the sender is the operator, so the refusals above were the check.
        await Seat(vehicle, SPlayer);
        await SendRaw(new VehicleSpotlightToggleRequestEvent(SEntMan.GetNetEntity(vehicle)));
        Assert.That(SEntMan.GetComponent<VehicleSpotlightComponent>(vehicle).Enabled, Is.True, "The raw request never reached the spotlight handler.");
    }

    /// <summary>The overlight multiplies the spotlight radius while it is intact and stops once it is destroyed.</summary>
    [Test]
    public async Task OverlightReshapesTheSpotlight()
    {
        var vehicle = EntityUid.Invalid;
        await Server.WaitPost(() => vehicle = SpawnVehicle(Lit));
        await RunTicks(5);

        var spotlight = SEntMan.GetComponent<VehicleSpotlightComponent>(vehicle);
        var light = SEntMan.GetComponent<Robust.Server.GameObjects.PointLightComponent>(vehicle);
        Assert.That(spotlight.Radius, Is.EqualTo(12f).Within(0.01f), "The overlight did not double the spotlight radius.");
        Assert.That(light.Radius, Is.EqualTo(12f).Within(0.01f), "The point light did not follow the spotlight.");

        await Server.WaitAssertion(() =>
        {
            var overlight = SEntMan.System<Content.Shared.Containers.ItemSlots.ItemSlotsSystem>().GetItemOrNull(vehicle, "roof");
            Assert.That(overlight, Is.Not.Null, "The overlight was not installed.");

            var integrity = SEntMan.GetComponent<HardpointIntegrityComponent>(overlight!.Value);
            Assert.That(SEntMan.System<HardpointSystem>().DamageHardpoint(vehicle, overlight.Value, integrity.MaxIntegrity), Is.True);
        });
        await RunTicks(5);

        Assert.That(spotlight.Radius, Is.EqualTo(6f).Within(0.01f), "A destroyed overlight still widened the spotlight.");
        Assert.That(light.Radius, Is.EqualTo(6f).Within(0.01f));
    }

    /// <summary>The overcharge action belongs to whoever is operating the vehicle and nobody else.</summary>
    [Test]
    public async Task OverchargeActionGoesToTheOperatorOnly()
    {
        var bystander = EntityUid.Invalid;
        await Server.WaitPost(() => bystander = SEntMan.SpawnAtPosition("InteractionTestMob", SEntMan.GetCoordinates(PlayerCoords)));

        var vehicle = EntityUid.Invalid;
        await Server.WaitPost(() => vehicle = SpawnVehicle(Vehicle));
        await RunTicks(5);

        Assert.That(FindOvercharge(SEntMan, SPlayer), Is.Null, "The action was handed out before anyone drove.");

        await Seat(vehicle, SPlayer);
        Assert.That(FindOvercharge(SEntMan, SPlayer), Is.Not.Null, "The operator did not get the overcharge action.");
        Assert.That(FindOvercharge(SEntMan, bystander), Is.Null, "A bystander got the overcharge action.");
        Assert.That(FindOvercharge(CEntMan, CPlayer), Is.Not.Null, "The action never reached the operator's client.");

        // Stepping out takes it back.
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<RMCVehicleComponent>(vehicle);
            Assert.That(SEntMan.System<RMCVehicleSystem>().TryRemoveOperator((vehicle, comp)), Is.True);
        });
        await RunTicks(5);
        Assert.That(FindOvercharge(SEntMan, SPlayer), Is.Null, "The action stayed behind after the operator left.");
        Assert.That(FindOvercharge(CEntMan, CPlayer), Is.Null, "The client kept the action after the operator left.");

        // A different operator gets their own.
        await Seat(vehicle, bystander);
        Assert.That(FindOvercharge(SEntMan, bystander), Is.Not.Null);
        Assert.That(FindOvercharge(SEntMan, SPlayer), Is.Null);
    }

    /// <summary>Using the action lifts the top speed for its duration, then the vehicle falls back to normal.</summary>
    [Test]
    public async Task OverchargeRaisesTopSpeedForItsDuration()
    {
        var plating = TileMan["Plating"].TileId;
        var vehicle = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            for (var x = -3; x < 100; x++)
            {
                for (var y = 0; y < 6; y++)
                {
                    MapSystem.SetTile(MapData.Grid.Owner, MapData.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }

            vehicle = SEntMan.SpawnEntity(Vehicle, new EntityCoordinates(MapData.Grid.Owner, 2.5f, 2.5f));
            SEntMan.EnsureComponent<InputMoverComponent>(SPlayer);
        });
        await RunTicks(5);
        await Seat(vehicle, SPlayer);

        var mover = SEntMan.GetComponent<GridVehicleMoverComponent>(vehicle);
        var overcharge = SEntMan.GetComponent<VehicleOverchargeComponent>(vehicle);
        var maxSpeed = mover.MaxSpeed;
        var boosted = maxSpeed * overcharge.SpeedMultiplier;

        await Server.WaitPost(() => SEntMan.GetComponent<InputMoverComponent>(SPlayer).HeldMoveButtons = MoveButtons.Right);
        await RunSeconds(2.5f);

        Assert.That(mover.CurrentSpeed, Is.GreaterThan(maxSpeed - 0.1f).And.LessThanOrEqualTo(maxSpeed + 0.01f),
            "The vehicle should be cruising at its normal top speed before the overcharge.");

        // Use the action from the client, as the button does.
        var clientAction = FindOvercharge(CEntMan, CPlayer);
        Assert.That(clientAction, Is.Not.Null, "The client has no overcharge action to press.");
        await Client.WaitPost(() =>
        {
            var system = CEntMan.System<Content.Client.Actions.ActionsSystem>();
            Assert.That(system.TryGetActionData(clientAction, out var data), Is.True);
            system.TriggerAction(clientAction!.Value, data!);
        });
        await RunTicks(5);

        Assert.That(overcharge.ActiveUntil, Is.GreaterThan(STiming.CurTime), "The action did not start the overcharge.");
        Assert.That(overcharge.CooldownUntil, Is.GreaterThan(overcharge.ActiveUntil));

        await RunSeconds(1.5f);
        Assert.That(mover.CurrentSpeed, Is.GreaterThan(maxSpeed + 1f).And.LessThanOrEqualTo(boosted + 0.01f),
            "The overcharge did not lift the top speed.");

        // The action shows the cooldown, and pressing it again changes nothing.
        var serverAction = FindOvercharge(SEntMan, SPlayer)!.Value;
        Assert.That(SEntMan.GetComponent<InstantActionComponent>(serverAction).Cooldown, Is.Not.Null, "The action button shows no cooldown.");
        var until = overcharge.ActiveUntil;
        await Client.WaitPost(() =>
        {
            var system = CEntMan.System<Content.Client.Actions.ActionsSystem>();
            Assert.That(system.TryGetActionData(clientAction, out var data), Is.True);
            system.TriggerAction(clientAction!.Value, data!);
        });
        await RunTicks(5);
        Assert.That(overcharge.ActiveUntil, Is.EqualTo(until), "Pressing the action again restarted the overcharge.");

        // Past the duration the top speed is back to normal.
        await RunSeconds(overcharge.Duration);
        Assert.That(mover.CurrentSpeed, Is.LessThanOrEqualTo(maxSpeed + 0.01f), "The vehicle kept its boosted speed after the overcharge ended.");
        Assert.That(mover.CurrentSpeed, Is.GreaterThan(maxSpeed - 0.1f), "The vehicle should still be driving at its normal top speed.");
    }

    /// <summary>Opening the hardpoint window lists every slot, with the installed parts, in a window the client has open.</summary>
    [Test]
    public async Task HardpointWindowListsTheHardpoints()
    {
        var target = await SpawnTarget(Hardpointed);
        var vehicle = ToServer(target);

        await Server.WaitPost(() =>
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(vehicle, HardpointUiKey.Key, SPlayer));
        await RunTicks(10);

        Assert.That(TryGetBui(HardpointUiKey.Key, out var bui, target), Is.True);
        var hardpointBui = bui as HardpointBoundUserInterface;
        Assert.That(hardpointBui, Is.Not.Null, "The vehicle opened the wrong user interface class.");
        Assert.That(hardpointBui!.Menu, Is.Not.Null, "The hardpoint window did not open on the client.");
        Assert.That(hardpointBui.Menu!.IsOpen, Is.True);

        var state = CEntMan.GetComponent<HardpointSlotsComponent>(ToClient(vehicle)).Ui;
        var slots = new[] { "armor", "support", "wheel-1" };
        Assert.That(state.Hardpoints, Has.Count.EqualTo(slots.Length), "The state does not list every slot.");

        using (Assert.EnterMultipleScope())
        {
            for (var i = 0; i < slots.Length; i++)
            {
                Assert.That(state.Hardpoints[i].SlotId, Is.EqualTo(slots[i]));
            }

            Assert.That(state.Hardpoints[0].HasItem, Is.False, "The armour slot should be empty.");
            Assert.That(state.Hardpoints[1].HasItem, Is.True, "The support module is missing from the state.");
            Assert.That(state.Hardpoints[1].InstalledName, Is.Not.Null.And.Not.Empty);
            Assert.That(state.Hardpoints[2].HasItem, Is.True, "The treads are missing from the state.");
            Assert.That(state.HasFrameIntegrity, Is.True);
            Assert.That(state.FrameMaxIntegrity, Is.EqualTo(1000f));

            var menu = hardpointBui.Menu;
            Assert.That(menu.HardpointList.ChildCount, Is.EqualTo(slots.Length), "The window does not show one row per slot.");
            Assert.That(menu.FrameHealthRow.Visible, Is.True, "The window hides the frame integrity.");
            Assert.That(menu.VehicleEntity, Is.EqualTo(ToClient(vehicle)));
        }

        // Closing the window from the server side tears it down again.
        await Server.WaitPost(() =>
            SEntMan.System<SharedUserInterfaceSystem>().CloseUi(vehicle, HardpointUiKey.Key, SPlayer));
        await RunTicks(10);
        Assert.That(TryGetBui(HardpointUiKey.Key, out _, target, shouldSucceed: false), Is.False, "The window stayed open after the server closed it.");
    }
}
