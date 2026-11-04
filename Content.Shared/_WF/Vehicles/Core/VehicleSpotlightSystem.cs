using System.Numerics;
using Content.Shared._WF.Vehicles.Grid;
using Robust.Shared.Physics;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Input.Binding;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Drives the vehicle's headlight: the operator toggles it from the flip key and hardpoint parts reshape it.</summary>
public sealed partial class VehicleSpotlightSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<VehicleSpotlightComponent, ComponentStartup>(OnSpotlightStartup);
        SubscribeLocalEvent<WFVehicleEngineComponent, AfterAutoHandleStateEvent>(OnEngineState);
        SubscribeLocalEvent<VehicleSpotlightComponent, HardpointSlotsChangedEvent>(OnHardpointSlotsChanged);
        SubscribeLocalEvent<VehicleSpotlightComponent, VehicleHardpointsRefreshEvent>(OnHardpointsRefresh);

        if (_net.IsClient)
        {
            // Flip is also the key that flips objects on the server, so the bind only swallows it for an operator
            // of a vehicle that has a spotlight; everyone else's press goes through untouched.
            CommandBinds.Builder
                .Bind(ContentKeyFunctions.FlipObject, new PointerInputCmdHandler((session, _, _) =>
                {
                    if (session?.AttachedEntity is not { } user)
                        return false;

                    if (!TryGetOperatedVehicle(user, out var vehicleUid) || !HasComp<VehicleSpotlightComponent>(vehicleUid))
                        return false;

                    RaiseNetworkEvent(new VehicleSpotlightToggleRequestEvent(GetNetEntity(vehicleUid)));
                    return true;
                }, outsidePrediction: true))
                .Register<VehicleSpotlightSystem>();
        }

        SubscribeNetworkEvent<VehicleSpotlightToggleRequestEvent>(OnSpotlightToggleRequest);
    }

    public override void Shutdown()
    {
        if (_net.IsClient)
            CommandBinds.Unregister<VehicleSpotlightSystem>();
    }

    private bool TryGetOperatedVehicle(EntityUid user, out EntityUid vehicle)
    {
        vehicle = EntityUid.Invalid;

        if (!TryComp(user, out RMCVehicleOperatorComponent? op) ||
            op.Vehicle is not { } operated ||
            !Exists(operated))
        {
            return false;
        }

        vehicle = operated;
        return true;
    }

    private void OnSpotlightStartup(Entity<VehicleSpotlightComponent> ent, ref ComponentStartup args)
    {
        EnsureBase(ent.Comp);
        if (_net.IsServer)
            RecalculateFromHardpoints(ent.Owner, ent.Comp);

        ApplySpotlight(ent.Owner, ent.Comp);
    }

    private void OnHardpointSlotsChanged(Entity<VehicleSpotlightComponent> ent, ref HardpointSlotsChangedEvent args)
    {
        RefreshVehicleSpotlight(ent.Owner, ent.Comp);
    }

    private void OnHardpointsRefresh(Entity<VehicleSpotlightComponent> ent, ref VehicleHardpointsRefreshEvent args)
    {
        RefreshVehicleSpotlight(ent.Owner, ent.Comp);
    }

    private void RefreshVehicleSpotlight(EntityUid vehicle, VehicleSpotlightComponent spotlight)
    {
        if (_net.IsClient)
            return;

        RecalculateFromHardpoints(vehicle, spotlight);
        ApplySpotlight(vehicle, spotlight);
        Dirty(vehicle, spotlight);
    }

    private void OnSpotlightToggleRequest(VehicleSpotlightToggleRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!_net.IsServer)
            return;

        if (args.SenderSession.AttachedEntity is not { } user)
            return;

        var vehicle = GetEntity(ev.Vehicle);
        if (!TryComp(vehicle, out RMCVehicleComponent? vehicleComp) || vehicleComp.Operator != user)
            return;

        if (!TryComp(vehicle, out VehicleSpotlightComponent? spotlight))
            return;

        spotlight.Enabled = !spotlight.Enabled;
        ApplySpotlight(vehicle, spotlight);
        Dirty(vehicle, spotlight);
    }

    private void OnEngineState(Entity<WFVehicleEngineComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshPower(ent.Owner);
    }

    /// <summary>Reapplies the driver's light switch after battery power changes.</summary>
    public void RefreshPower(EntityUid uid)
    {
        if (TryComp(uid, out VehicleSpotlightComponent? spotlight))
            ApplySpotlight(uid, spotlight);
    }

    private void ApplySpotlight(EntityUid uid, VehicleSpotlightComponent spotlight)
    {
        SharedPointLightComponent? light = null;
        if (!_lights.ResolveLight(uid, ref light))
            return;

        // Mount the lamp just beyond the front bumper, rotating its offset with the chassis. At rotation 0 a vehicle
        // faces south, the way the cone points, so its front is the hull's bottom edge.
        if (TryComp(uid, out FixturesComponent? fixtures) &&
            GridVehicleMoverSystem.TryGetFixtureAabb(fixtures, Robust.Shared.Physics.Transform.Empty, out var hull))
        {
            var offset = new Vector2(hull.Center.X, hull.Bottom - 0.05f);
            if (light.Offset != offset)
            {
                light.Offset = offset;
                Dirty(uid, light);
            }
        }
        _lights.SetRadius(uid, spotlight.Radius, light);
        _lights.SetEnergy(uid, spotlight.Energy, light);
        _lights.SetSoftness(uid, spotlight.Softness, light);
        var powered = !TryComp(uid, out WFVehicleEngineComponent? engine) || engine.Powered;
        _lights.SetEnabled(uid, spotlight.Enabled && powered, light);
    }

    private static void EnsureBase(VehicleSpotlightComponent spotlight)
    {
        if (spotlight.BaseInitialized)
            return;

        spotlight.BaseInitialized = true;
        spotlight.BaseRadius = spotlight.Radius;
        spotlight.BaseEnergy = spotlight.Energy;
        spotlight.BaseSoftness = spotlight.Softness;
    }

    private void RecalculateFromHardpoints(
        EntityUid vehicle,
        VehicleSpotlightComponent spotlight,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        EnsureBase(spotlight);

        var radius = spotlight.BaseRadius;
        var energy = spotlight.BaseEnergy;
        var softness = spotlight.BaseSoftness;

        if (Resolve(vehicle, ref hardpoints, ref itemSlots, logMissing: false))
        {
            foreach (var slot in hardpoints.Slots)
            {
                if (string.IsNullOrWhiteSpace(slot.Id))
                    continue;

                if (!_itemSlots.TryGetSlot(vehicle, slot.Id, out var itemSlot, itemSlots) || !itemSlot.HasItem)
                    continue;

                var item = itemSlot.Item!.Value;
                if (TryComp(item, out HardpointIntegrityComponent? integrity) &&
                    integrity.Integrity <= 0f)
                {
                    continue;
                }

                if (!TryComp(item, out VehicleSpotlightModifierComponent? modifier))
                    continue;

                radius = radius * modifier.RadiusMultiplier + modifier.RadiusAdd;
                energy = energy * modifier.EnergyMultiplier + modifier.EnergyAdd;
                softness = softness * modifier.SoftnessMultiplier + modifier.SoftnessAdd;
            }
        }

        spotlight.Radius = radius;
        spotlight.Energy = energy;
        spotlight.Softness = softness;
    }
}

/// <summary>A client's request to flip the spotlight of the vehicle it drives.</summary>
[Serializable, NetSerializable]
public sealed partial class VehicleSpotlightToggleRequestEvent : EntityEventArgs
{
    public NetEntity Vehicle;

    public VehicleSpotlightToggleRequestEvent()
    {
    }

    public VehicleSpotlightToggleRequestEvent(NetEntity vehicle)
    {
        Vehicle = vehicle;
    }
}
