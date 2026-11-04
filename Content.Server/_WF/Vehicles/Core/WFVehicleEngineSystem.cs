using Robust.Shared.Containers;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Vehicles.Core;

/// <summary>Authorizes ignition with the matching key and consumes or replenishes vehicle fuel.</summary>
public sealed class WFVehicleEngineSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;

    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private VehicleLockSystem _lock = default!;

    private static readonly EntProtoId NpcKey = "RMCVehicleKey";

    private float _elapsed;

    public override void Initialize()
    {
        SubscribeNetworkEvent<WFVehicleControlRequest>(OnControl);
        SubscribeLocalEvent<WFVehicleEngineComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<WFVehicleEngineComponent, EntRemovedFromContainerMessage>(OnKeyRemoved);
        SubscribeLocalEvent<WFVehicleEngineComponent, InteractUsingEvent>(OnRefuel);
    }

    private void OnControl(WFVehicleControlRequest ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user ||
            !_blocker.CanConsciouslyPerformAction(user))
            return;

        var uid = GetEntity(ev.Vehicle);
        if (!TryComp(uid, out RMCVehicleComponent? vehicle) || vehicle.Operator != user ||
            !TryComp(uid, out WFVehicleEngineComponent? engine))
            return;

        if (ev.Brake)
        {
            engine.ParkingBrake = !engine.ParkingBrake;
            Dirty(uid, engine);
            return;
        }

        var slot = _containers.EnsureContainer<ContainerSlot>(uid, WFVehicleEngineComponent.IgnitionContainer);
        if (ev.EjectKey)
        {
            if (engine.Electrics || slot.ContainedEntity is not { } inserted)
                return;
            _hands.PickupOrDrop(user, inserted);
            return;
        }

        if (ev.Ignition)
        {
            Ignition((uid, engine), user, slot);
            return;
        }

        if (ev.StartStop)
        {
            StartStop((uid, engine), user, slot);
            return;
        }

        if (ev.Accessories)
        {
            if (slot.ContainedEntity == null)
            {
                if (!_hands.TryGetActiveItem(user, out var held) ||
                    !TryComp(held, out VehicleKeyComponent? key) ||
                    !TryComp(uid, out VehicleLockComponent? vehicleLock) ||
                    key.KeyId == null || key.KeyId != vehicleLock.KeyId)
                {
                    _popup.PopupEntity(Loc.GetString("wf-vehicle-key-required"), user, user);
                    return;
                }
                if (_hands.TryDropIntoContainer(user, held.Value, slot))
                {
                    engine.IgnitionKey = held;
                    Dirty(uid, engine);
                }
                return;
            }
            engine.Electrics = !engine.Electrics;
            if (!engine.Electrics)
                engine.Running = false;
            Dirty(uid, engine);
            return;
        }

        if (!engine.Electrics || slot.ContainedEntity == null)
            return;
        if (!engine.Running)
        {
            if (engine.Fuel <= 0f)
            {
                _popup.PopupEntity(Loc.GetString("wf-vehicle-no-fuel"), user, user);
                return;
            }
            if (engine.BatteryCharge < engine.StarterEnergy)
            {
                _popup.PopupEntity(Loc.GetString("wf-vehicle-battery-flat"), user, user);
                return;
            }
            engine.BatteryCharge -= engine.StarterEnergy;
        }
        engine.Running = !engine.Running;
        Dirty(uid, engine);
    }

    /// <summary>
    /// The key: the matching key goes in from anywhere on the driver and turns the accessories on; with it in, the
    /// engine stops, everything switches off and the key comes out to the driver's hand.
    /// </summary>
    private void Ignition(Entity<WFVehicleEngineComponent> vehicle, EntityUid user, ContainerSlot slot)
    {
        var engine = vehicle.Comp;
        if (slot.ContainedEntity is { } inserted)
        {
            engine.Running = false;
            engine.Electrics = false;
            Dirty(vehicle);
            _hands.PickupOrDrop(user, inserted);
            return;
        }

        if (FindKey(user, vehicle) is not { } key)
        {
            _popup.PopupEntity(Loc.GetString("wf-vehicle-key-required"), user, user);
            return;
        }

        if (!_containers.Insert(key, slot))
            return;

        engine.IgnitionKey = key;
        engine.Electrics = true;
        Dirty(vehicle);
    }

    /// <summary>The start/stop button: with the key in, starts the engine (accessories and all) or stops it.</summary>
    private void StartStop(Entity<WFVehicleEngineComponent> vehicle, EntityUid user, ContainerSlot slot)
    {
        var engine = vehicle.Comp;
        if (slot.ContainedEntity == null)
        {
            _popup.PopupEntity(Loc.GetString("wf-vehicle-key-required"), user, user);
            return;
        }

        if (engine.Running)
        {
            engine.Running = false;
            Dirty(vehicle);
            return;
        }

        if (engine.Fuel <= 0f)
        {
            _popup.PopupEntity(Loc.GetString("wf-vehicle-no-fuel"), user, user);
            return;
        }

        if (engine.BatteryCharge < engine.StarterEnergy)
        {
            _popup.PopupEntity(Loc.GetString("wf-vehicle-battery-flat"), user, user);
            return;
        }

        engine.BatteryCharge -= engine.StarterEnergy;
        engine.Electrics = true;
        engine.Running = true;
        Dirty(vehicle);
    }

    /// <summary>This vehicle's key in the driver's hands, or worn, or in a pocket, belt or anything else they have on.</summary>
    private EntityUid? FindKey(EntityUid user, EntityUid vehicle)
    {
        if (!TryComp(vehicle, out VehicleLockComponent? vehicleLock) || vehicleLock.KeyId == null)
            return null;

        bool Matches(EntityUid item) => TryComp(item, out VehicleKeyComponent? key) && key.KeyId == vehicleLock.KeyId;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (Matches(held))
                return held;
        }

        var slots = _inventory.GetSlotEnumerator(user);
        while (slots.MoveNext(out var inventorySlot))
        {
            if (inventorySlot.ContainedEntity is not { } worn)
                continue;
            if (Matches(worn))
                return worn;
            if (!TryComp(worn, out ContainerManagerComponent? containers))
                continue;
            foreach (var container in containers.Containers.Values)
            {
                foreach (var item in container.ContainedEntities)
                {
                    if (Matches(item))
                        return item;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Starts the engine for an NPC driver: puts the vehicle's key from its interior (or a new one) in the ignition,
    /// switches the electrics on, starts it when fuel and battery allow and releases the parking brake.
    /// </summary>
    public bool TryStartForNpc(Entity<WFVehicleEngineComponent?> vehicle)
    {
        if (!Resolve(vehicle, ref vehicle.Comp, false))
            return false;

        var engine = vehicle.Comp;
        engine.ParkingBrake = false;
        var slot = _containers.EnsureContainer<ContainerSlot>(vehicle, WFVehicleEngineComponent.IgnitionContainer);
        if (slot.ContainedEntity == null)
        {
            if (FindOrSpawnNpcKey(vehicle) is not { } key || !_containers.Insert(key, slot))
            {
                Dirty(vehicle.Owner, engine);
                return false;
            }

            engine.IgnitionKey = key;
        }

        engine.Electrics = true;
        if (!engine.Running)
        {
            if (engine.Fuel <= 0f || engine.BatteryCharge < engine.StarterEnergy)
            {
                Dirty(vehicle.Owner, engine);
                return false;
            }

            engine.BatteryCharge -= engine.StarterEnergy;
            engine.Running = true;
        }

        Dirty(vehicle.Owner, engine);
        return true;
    }

    /// <summary>Switches the engine and electrics off, as an NPC crew leaving the vehicle.</summary>
    public void StopForNpc(Entity<WFVehicleEngineComponent?> vehicle)
    {
        if (!Resolve(vehicle, ref vehicle.Comp, false))
            return;

        vehicle.Comp.Running = false;
        vehicle.Comp.Electrics = false;
        Dirty(vehicle.Owner, vehicle.Comp);
    }

    /// <summary>The vehicle's own loose key on its interior map, else a new key bound to it.</summary>
    private EntityUid? FindOrSpawnNpcKey(EntityUid vehicle)
    {
        var keyId = _lock.EnsureVehicleKeyId(vehicle);
        if (TryComp(vehicle, out VehicleInteriorComponent? interior) && interior.MapId != MapId.Nullspace)
        {
            var query = EntityQueryEnumerator<VehicleKeyComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var key, out var xform))
            {
                if (xform.MapID == interior.MapId && key.KeyId == keyId && !_containers.IsEntityInContainer(uid))
                    return uid;
            }
        }

        var spawned = Spawn(NpcKey, Transform(vehicle).Coordinates);
        if (!TryComp(spawned, out VehicleKeyComponent? spawnedKey))
        {
            Del(spawned);
            return null;
        }

        _lock.BindKey((spawned, spawnedKey), keyId, vehicle);
        return spawned;
    }

    private void OnInit(Entity<WFVehicleEngineComponent> ent, ref ComponentInit args)
    {
        _containers.EnsureContainer<ContainerSlot>(ent, WFVehicleEngineComponent.IgnitionContainer);
    }

    private void OnKeyRemoved(Entity<WFVehicleEngineComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != WFVehicleEngineComponent.IgnitionContainer)
            return;
        ent.Comp.IgnitionKey = null;
        ent.Comp.Electrics = false;
        ent.Comp.Running = false;
        Dirty(ent);
    }

    private void OnRefuel(Entity<WFVehicleEngineComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp(args.Used, out DrainableSolutionComponent? drain) ||
            !_solutions.TryGetSolution(args.Used, drain.Solution, out var solutionEntity, out var solution))
            return;

        var available = solution.GetTotalPrototypeQuantity("WeldingFuel");
        if (available <= FixedPoint2.Zero)
            return;

        args.Handled = true;
        if (ent.Comp.Running)
        {
            _popup.PopupEntity(Loc.GetString("wf-vehicle-refuel-stop"), args.User, args.User);
            return;
        }

        var amount = FixedPoint2.Min(available, FixedPoint2.New(MathF.Max(0f, ent.Comp.Capacity - ent.Comp.Fuel)));
        if (amount <= FixedPoint2.Zero)
            return;

        _solutions.RemoveReagent(solutionEntity.Value, "WeldingFuel", amount);
        ent.Comp.Fuel = MathF.Min(ent.Comp.Capacity, ent.Comp.Fuel + (float) amount);
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("wf-vehicle-refueled", ("amount", (float) amount)), args.User, args.User);
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 0.25f)
            return;

        var elapsed = _elapsed;
        _elapsed = 0f;
        var query = EntityQueryEnumerator<WFVehicleEngineComponent, GridVehicleMoverComponent>();
        while (query.MoveNext(out var uid, out var engine, out var mover))
        {
            if (!engine.Running)
                continue;

            var load = Math.Clamp(MathF.Abs(mover.CurrentSpeed) / MathF.Max(1f, mover.MaxSpeed), 0f, 1f);
            engine.Fuel = MathF.Max(0f, engine.Fuel - elapsed * (engine.IdleConsumption + engine.DrivingConsumption * load));
            if (engine.Fuel <= 0f)
                engine.Running = false;
            Dirty(uid, engine);
        }
    }
}
