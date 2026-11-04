using System.Linq;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Turret;
using Content.Shared.Buckle.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>The hotbar actions that pick a hardpoint.</summary>
public sealed partial class VehicleWeaponsSystem
{
    private void OnHardpointActionSelect(Entity<VehicleWeaponsOperatorComponent> ent, ref VehicleHardpointSelectActionEvent args)
    {
        if (_net.IsClient || args.Handled)
            return;

        if (args.Performer == default || !Exists(args.Performer) || args.Performer != ent.Owner)
            return;

        if (!CanUseHardpointActions(args.Performer))
            return;

        if (!TryComp(args.Performer, out BuckleComponent? buckle) ||
            buckle.BuckledTo is not { } seat ||
            !HasComp<VehicleWeaponsSeatComponent>(seat))
        {
            return;
        }

        if (!TryComp(args.Action.Owner, out VehicleHardpointActionComponent? hardpointAction))
            return;

        if (TrySelectHardpoint(seat, args.Performer, hardpointAction.MountedWeapon, fromUi: false))
            args.Handled = true;
    }

    private void OnViewToggled(Entity<VehicleWeaponsOperatorComponent> ent, ref VehicleViewToggledEvent args)
    {
        if (_net.IsClient)
            return;

        if (ent.Comp.Vehicle is not { } vehicle ||
            !TryComp(vehicle, out VehicleWeaponsComponent? weapons))
        {
            return;
        }

        RefreshHardpointActions(ent.Owner, vehicle, weapons, ent.Comp);

        if (TryGetUserWeaponsSeat(ent.Owner, out var seat, out _))
            UpdateWeaponsUi(seat, vehicle, weapons, operatorUid: ent.Owner);
    }

    /// <summary>A weapon with the icon and name of its pick action.</summary>
    private readonly record struct HardpointActionSlot(EntityUid MountedWeapon, EntityUid IconEntity, string DisplayName);

    private void RefreshHardpointActions(
        EntityUid user,
        EntityUid vehicle,
        VehicleWeaponsComponent weapons,
        VehicleWeaponsOperatorComponent? operatorComp = null,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        if (_net.IsClient || TerminatingOrDeleted(user))
            return;

        if (!Resolve(user, ref operatorComp, logMissing: false))
            return;

        if (!Resolve(vehicle, ref hardpoints, ref itemSlots, logMissing: false))
        {
            ClearHardpointActions(user, operatorComp);
            return;
        }

        var desired = CanUseHardpointActions(user)
            ? GetSelectableHardpointActionSlots(vehicle, user, weapons, hardpoints, itemSlots)
            : new List<HardpointActionSlot>();

        var desiredSlots = new HashSet<EntityUid>(desired.Select(slot => slot.MountedWeapon));

        foreach (var pair in operatorComp.HardpointActions.ToArray())
        {
            if (!desiredSlots.Contains(pair.Key) || !Exists(pair.Value))
            {
                RemoveAndDeleteHardpointAction(user, pair.Value);
                operatorComp.HardpointActions.Remove(pair.Key);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var desiredSlot = desired[i];
            if (operatorComp.HardpointActions.TryGetValue(desiredSlot.MountedWeapon, out var existingAction) &&
                Exists(existingAction) &&
                _actions.TryGetActionData(existingAction, out var existingActionComp, logError: false) &&
                existingActionComp.Container == desiredSlot.IconEntity)
            {
                if (TryComp(existingAction, out VehicleHardpointActionComponent? existingHardpointAction))
                {
                    existingHardpointAction.MountedWeapon = desiredSlot.MountedWeapon;
                    existingHardpointAction.SortOrder = i;
                    Dirty(existingAction, existingHardpointAction);
                }

                _meta.SetEntityName(existingAction, desiredSlot.DisplayName);
                continue;
            }

            if (operatorComp.HardpointActions.TryGetValue(desiredSlot.MountedWeapon, out var staleAction) &&
                Exists(staleAction))
            {
                RemoveAndDeleteHardpointAction(user, staleAction);
                operatorComp.HardpointActions.Remove(desiredSlot.MountedWeapon);
            }

            // A gun on its way out, as when the vehicle is deleted, gets no action to hold.
            if (TerminatingOrDeleted(desiredSlot.IconEntity))
                continue;

            EntityUid? action = null;
            if (!_actions.AddAction(user, ref action, HardpointSelectActionId, container: desiredSlot.IconEntity) ||
                action == null)
            {
                continue;
            }

            var hardpointAction = EnsureComp<VehicleHardpointActionComponent>(action.Value);
            hardpointAction.MountedWeapon = desiredSlot.MountedWeapon;
            hardpointAction.SortOrder = i;
            Dirty(action.Value, hardpointAction);
            _meta.SetEntityName(action.Value, desiredSlot.DisplayName);
            operatorComp.HardpointActions[desiredSlot.MountedWeapon] = action.Value;
        }

        UpdateHardpointActionStates(user, weapons, operatorComp);
    }

    private List<HardpointActionSlot> GetSelectableHardpointActionSlots(
        EntityUid vehicle,
        EntityUid user,
        VehicleWeaponsComponent weapons,
        HardpointSlotsComponent hardpoints,
        ItemSlotsComponent itemSlots)
    {
        var slots = new List<HardpointActionSlot>();
        if (!TryGetUserWeaponsSeat(user, out _, out var seatComp))
            return slots;

        foreach (var mountedSlot in _topology.GetMountedSlots(vehicle, hardpoints, itemSlots))
        {
            if (!IsHardpointTypeAllowed(seatComp, mountedSlot.HardpointType))
                continue;

            if (mountedSlot.Item is not { } installed ||
                !HasComp<VehicleTurretComponent>(installed) ||
                !HasComp<GunComponent>(installed))
            {
                continue;
            }

            var sharedSelection = IsSharedHardpointType(mountedSlot.HardpointType);
            if (sharedSelection ||
                !weapons.HardpointOperators.TryGetValue(installed, out var slotOperator) ||
                slotOperator == user)
            {
                slots.Add(new HardpointActionSlot(installed, installed, Name(installed)));
            }
        }

        return slots;
    }

    private void UpdateHardpointActionStates(
        EntityUid user,
        VehicleWeaponsComponent weapons,
        VehicleWeaponsOperatorComponent? operatorComp = null)
    {
        if (_net.IsClient || !Resolve(user, ref operatorComp, logMissing: false))
            return;

        var canUseHardpointActions = CanUseHardpointActions(user);
        EntityUid? selectedWeapon = weapons.OperatorSelections.TryGetValue(user, out var weapon)
            ? weapon
            : null;

        foreach (var pair in operatorComp.HardpointActions)
        {
            _actions.SetEnabled(pair.Value, canUseHardpointActions);
            _actions.SetToggled(
                pair.Value,
                canUseHardpointActions &&
                selectedWeapon != null &&
                pair.Key == selectedWeapon.Value);
        }
    }

    private void ClearHardpointActions(EntityUid user, VehicleWeaponsOperatorComponent? operatorComp = null)
    {
        if (_net.IsClient || !Resolve(user, ref operatorComp, logMissing: false))
            return;

        foreach (var action in operatorComp.HardpointActions.Values.ToArray())
        {
            if (Exists(action))
                RemoveAndDeleteHardpointAction(user, action);
        }

        operatorComp.HardpointActions.Clear();
    }

    private void RemoveAndDeleteHardpointAction(EntityUid user, EntityUid action)
    {
        if (!Exists(action))
            return;

        _actions.RemoveAction(user, action);

        if (_net.IsClient)
            return;

        if (Exists(action))
            QueueDel(action);
    }

    private bool CanUseHardpointActions(EntityUid user, bool forUi = false)
    {
        if (!TryGetUserWeaponsSeat(user, out _, out var seatComp))
            return false;

        if (forUi && !seatComp.AllowUiSelection)
            return false;

        if (!forUi && !seatComp.AllowHotbarSelection)
            return false;

        if (TryComp(user, out VehicleViewToggleComponent? viewToggle) && !viewToggle.IsOutside)
            return false;

        return true;
    }
}
