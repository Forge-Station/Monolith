using System;
using System.Collections.Generic;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Turret;
using Content.Shared._WF.Vehicles.Weapons.BulletBox;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>A hardpoint slot of a vehicle, or a slot nested in a turret, by root and child id.</summary>
[Serializable, NetSerializable]
public readonly record struct VehicleSlotPath(string Root, string? Child = null)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Root);
    public bool IsNested => !string.IsNullOrWhiteSpace(Child);

    /// <summary>The slot as one id: the root, or root and child joined.</summary>
    public string ToCompositeId()
    {
        return IsNested
            ? VehicleTurretSlotIds.Compose(Root, Child!)
            : Root;
    }

    /// <summary>This path with a nested child slot added.</summary>
    public VehicleSlotPath Append(string child)
    {
        return IsNested
            ? new VehicleSlotPath(Root, VehicleTurretSlotIds.Compose(Child!, child))
            : new VehicleSlotPath(Root, child);
    }

    /// <summary>Reads a slot path from its composite id.</summary>
    public static bool TryParse(string? value, out VehicleSlotPath path)
    {
        path = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (VehicleTurretSlotIds.TryParse(value, out var parent, out var child))
        {
            path = new VehicleSlotPath(parent, child);
            return true;
        }

        path = new VehicleSlotPath(value);
        return true;
    }
}

/// <summary>A slot of a vehicle or of a part on it, with what it holds and the part holding it.</summary>
public readonly record struct VehicleMountedSlot(
    EntityUid Vehicle,
    EntityUid SlotOwner,
    string SlotId,
    VehicleSlotPath Path,
    EntProtoId HardpointType,
    EntityUid? Item,
    EntityUid? ParentItem,
    VehicleSlotPath? ParentPath)
{
    public bool HasItem => Item != null;
    public bool IsNested => ParentItem != null;
    public string CompositeId => Path.ToCompositeId();
    public string? ParentSlotId => ParentPath?.ToCompositeId();
}

/// <summary>A mounted gun's ready magazine, reserve and box refill, with its slot.</summary>
public readonly record struct VehicleMountedAmmoProvider(
    VehicleMountedSlot Slot,
    EntityUid AmmoUid,
    BallisticAmmoProviderComponent Ammo,
    VehicleHardpointAmmoComponent HardpointAmmo,
    RefillableByBulletBoxComponent Refill);

/// <summary>Finds the vehicle that holds a part and walks the vehicle's nested hardpoint slots.</summary>
public sealed partial class VehicleTopologySystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;

    private readonly List<VehicleMountedSlot> _internalSlotsBuffer = new();

    /// <summary>Finds the vehicle that holds an entity, through any containers.</summary>
    public bool TryGetVehicle(EntityUid uid, out EntityUid vehicle, bool includeSelf = true)
    {
        return TryGetContainerAncestor<RMCVehicleComponent>(uid, out vehicle, includeSelf);
    }

    /// <summary>Finds the vehicle a part is mounted on, counting only containers that are hardpoint slots.</summary>
    public bool TryGetMountedVehicle(EntityUid part, out EntityUid vehicle)
    {
        vehicle = default;
        var current = part;

        while (_containers.TryGetContainingContainer((current, null), out var container))
        {
            var owner = container.Owner;
            if (!TryComp(owner, out HardpointSlotsComponent? hardpoints) || !HasHardpointSlot(hardpoints, container.ID))
                return false;

            if (HasComp<RMCVehicleComponent>(owner))
            {
                vehicle = owner;
                return true;
            }

            current = owner;
        }

        return false;
    }

    private static bool HasHardpointSlot(HardpointSlotsComponent hardpoints, string slotId)
    {
        foreach (var slot in hardpoints.Slots)
        {
            if (slot.Id == slotId)
                return true;
        }

        return false;
    }

    /// <summary>Finds the turret that holds an entity, through any containers.</summary>
    public bool TryGetParentTurret(EntityUid uid, out EntityUid turret, bool includeSelf = false)
    {
        return TryGetContainerAncestor<VehicleTurretComponent>(uid, out turret, includeSelf);
    }

    /// <summary>Every slot of the vehicle and of the turrets on it.</summary>
    public List<VehicleMountedSlot> GetMountedSlots(
        EntityUid vehicle,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        var result = new List<VehicleMountedSlot>();
        GetMountedSlots(vehicle, result, hardpoints, itemSlots);
        return result;
    }

    /// <summary>Adds every slot of the vehicle and of the turrets on it to a list.</summary>
    public void GetMountedSlots(
        EntityUid vehicle,
        List<VehicleMountedSlot> result,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        result.Clear();

        if (!Resolve(vehicle, ref hardpoints, ref itemSlots, logMissing: false))
            return;

        EnumerateMountedSlots(
            vehicle,
            vehicle,
            hardpoints,
            itemSlots,
            result,
            parentPath: null,
            parentItem: null);
    }

    /// <summary>The composite ids of every slot of the vehicle and of the turrets on it.</summary>
    public HashSet<string> GetMountedSlotIds(
        EntityUid vehicle,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        var result = new HashSet<string>();

        if (!Resolve(vehicle, ref hardpoints, ref itemSlots, logMissing: false))
            return result;

        PopulateMountedSlotIds(vehicle, hardpoints, itemSlots, result, parentCompositeId: null);
        return result;
    }

    /// <summary>Finds a slot by its composite id.</summary>
    public bool TryGetMountedSlot(
        EntityUid vehicle,
        string slotId,
        out VehicleMountedSlot mountedSlot,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        mountedSlot = default;

        if (!VehicleSlotPath.TryParse(slotId, out var path))
            return false;

        return TryGetMountedSlot(vehicle, path, out mountedSlot, hardpoints, itemSlots);
    }

    /// <summary>Finds a slot by its path.</summary>
    public bool TryGetMountedSlot(
        EntityUid vehicle,
        VehicleSlotPath path,
        out VehicleMountedSlot mountedSlot,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        mountedSlot = default;

        if (!path.IsValid || !Resolve(vehicle, ref hardpoints, ref itemSlots, logMissing: false))
            return false;

        return TryGetMountedSlotRecursive(
            vehicle,
            vehicle,
            path,
            hardpoints,
            itemSlots,
            parentPath: null,
            parentItem: null,
            out mountedSlot);
    }

    /// <summary>Finds the slot that holds a part.</summary>
    public bool TryGetMountedSlotByItem(
        EntityUid vehicle,
        EntityUid item,
        out VehicleMountedSlot mountedSlot,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        mountedSlot = default;

        if (!Resolve(vehicle, ref hardpoints, ref itemSlots, logMissing: false))
            return false;

        return TryGetMountedSlotByItemRecursive(
            vehicle,
            vehicle,
            item,
            hardpoints,
            itemSlots,
            parentPath: null,
            parentItem: null,
            out mountedSlot);
    }

    /// <summary>Gets the part in a slot.</summary>
    public bool TryGetMountedSlotItem(
        EntityUid vehicle,
        string slotId,
        out EntityUid item,
        ItemSlotsComponent? itemSlots = null,
        HardpointSlotsComponent? hardpoints = null)
    {
        item = default;

        if (!TryGetMountedSlot(vehicle, slotId, out var mountedSlot, hardpoints, itemSlots) ||
            mountedSlot.Item is not { } mountedItem)
        {
            return false;
        }

        item = mountedItem;
        return true;
    }

    /// <summary>Gets the hardpoint type a slot takes.</summary>
    public bool TryGetMountedSlotHardpointType(
        EntityUid vehicle,
        string slotId,
        out EntProtoId hardpointType,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        hardpointType = default;

        if (!TryGetMountedSlot(vehicle, slotId, out var mountedSlot, hardpoints, itemSlots))
            return false;

        hardpointType = mountedSlot.HardpointType;
        return true;
    }

    /// <summary>Every mounted gun that has a ready magazine, reserve and box refill.</summary>
    public List<VehicleMountedAmmoProvider> GetMountedAmmoProviders(
        EntityUid vehicle,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        var result = new List<VehicleMountedAmmoProvider>();
        GetMountedAmmoProviders(vehicle, result, hardpoints, itemSlots);
        return result;
    }

    /// <summary>Adds every mounted gun's ammo to a list.</summary>
    public void GetMountedAmmoProviders(
        EntityUid vehicle,
        List<VehicleMountedAmmoProvider> result,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        result.Clear();

        GetMountedSlots(vehicle, _internalSlotsBuffer, hardpoints, itemSlots);
        foreach (var slot in _internalSlotsBuffer)
        {
            if (slot.Item is not { } item)
                continue;

            if (!TryGetAmmoProviderFromItem(slot, item, out var provider))
                continue;

            result.Add(provider);
        }
    }

    /// <summary>Finds a gun's ammo by its slot's composite id.</summary>
    public bool TryGetMountedAmmoProvider(
        EntityUid vehicle,
        string? slotId,
        out VehicleMountedAmmoProvider provider,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        provider = default;

        if (!VehicleSlotPath.TryParse(slotId, out var path))
            return false;

        return TryGetMountedAmmoProvider(vehicle, path, out provider, hardpoints, itemSlots);
    }

    /// <summary>Finds a gun's ammo by its slot's path.</summary>
    public bool TryGetMountedAmmoProvider(
        EntityUid vehicle,
        VehicleSlotPath path,
        out VehicleMountedAmmoProvider provider,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        provider = default;

        if (!path.IsValid ||
            !TryGetMountedSlot(vehicle, path, out var mountedSlot, hardpoints, itemSlots) ||
            mountedSlot.Item is not { } item)
        {
            return false;
        }

        return TryGetAmmoProviderFromItem(mountedSlot, item, out provider);
    }

    /// <summary>Finds the first turret on the vehicle that no other turret steers.</summary>
    public bool TryGetPrimaryTurret(
        EntityUid vehicle,
        out EntityUid turretUid,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        turretUid = default;

        if (!Resolve(vehicle, ref hardpoints, ref itemSlots, logMissing: false))
            return false;

        foreach (var slot in hardpoints.Slots)
        {
            if (string.IsNullOrWhiteSpace(slot.Id) ||
                !_itemSlots.TryGetSlot(vehicle, slot.Id, out var itemSlot, itemSlots) ||
                !itemSlot.HasItem ||
                itemSlot.Item is not { } item)
            {
                continue;
            }

            if (!HasComp<VehicleTurretComponent>(item) || HasComp<VehicleTurretAttachmentComponent>(item))
                continue;

            turretUid = item;
            return true;
        }

        return false;
    }

    private bool TryGetAmmoProviderFromItem(
        VehicleMountedSlot slot,
        EntityUid item,
        out VehicleMountedAmmoProvider provider)
    {
        provider = default;

        if (!TryComp(item, out BallisticAmmoProviderComponent? ammo))
            return false;

        if (!TryComp(item, out VehicleHardpointAmmoComponent? hardpointAmmo))
            return false;

        if (!TryComp(item, out RefillableByBulletBoxComponent? refill))
            return false;

        provider = new VehicleMountedAmmoProvider(slot, item, ammo, hardpointAmmo, refill);
        return true;
    }

    private bool TryGetContainerAncestor<TComponent>(EntityUid uid, out EntityUid ancestor, bool includeSelf = false)
        where TComponent : IComponent
    {
        ancestor = default;
        var query = GetEntityQuery<TComponent>();

        if (includeSelf && query.HasComp(uid))
        {
            ancestor = uid;
            return true;
        }

        var current = uid;
        while (_containers.TryGetContainingContainer((current, null), out var container))
        {
            var owner = container.Owner;
            if (query.HasComp(owner))
            {
                ancestor = owner;
                return true;
            }

            current = owner;
        }

        return false;
    }

    private void EnumerateMountedSlots(
        EntityUid vehicle,
        EntityUid slotOwner,
        HardpointSlotsComponent hardpoints,
        ItemSlotsComponent itemSlots,
        List<VehicleMountedSlot> result,
        VehicleSlotPath? parentPath,
        EntityUid? parentItem)
    {
        foreach (var slot in hardpoints.Slots)
        {
            if (string.IsNullOrWhiteSpace(slot.Id))
                continue;

            EntityUid? item = null;
            if (_itemSlots.TryGetSlot(slotOwner, slot.Id, out var itemSlot, itemSlots) &&
                itemSlot.HasItem)
            {
                item = itemSlot.Item;
            }

            var path = parentPath?.Append(slot.Id) ?? new VehicleSlotPath(slot.Id);

            result.Add(new VehicleMountedSlot(
                vehicle,
                slotOwner,
                slot.Id,
                path,
                slot.HardpointType,
                item,
                parentItem,
                parentPath));

            if (item is not { } nestedItem ||
                !TryComp(nestedItem, out HardpointSlotsComponent? nestedHardpoints) ||
                !TryComp(nestedItem, out ItemSlotsComponent? nestedItemSlots))
            {
                continue;
            }

            EnumerateMountedSlots(
                vehicle,
                nestedItem,
                nestedHardpoints,
                nestedItemSlots,
                result,
                path,
                nestedItem);
        }
    }

    private void PopulateMountedSlotIds(
        EntityUid slotOwner,
        HardpointSlotsComponent hardpoints,
        ItemSlotsComponent itemSlots,
        HashSet<string> result,
        string? parentCompositeId)
    {
        foreach (var slot in hardpoints.Slots)
        {
            if (string.IsNullOrWhiteSpace(slot.Id))
                continue;

            var compositeId = parentCompositeId == null
                ? slot.Id
                : VehicleTurretSlotIds.Compose(parentCompositeId, slot.Id);

            result.Add(compositeId);

            if (!_itemSlots.TryGetSlot(slotOwner, slot.Id, out var itemSlot, itemSlots) ||
                !itemSlot.HasItem ||
                itemSlot.Item is not { } nestedItem ||
                !TryComp(nestedItem, out HardpointSlotsComponent? nestedHardpoints) ||
                !TryComp(nestedItem, out ItemSlotsComponent? nestedItemSlots))
            {
                continue;
            }

            PopulateMountedSlotIds(nestedItem, nestedHardpoints, nestedItemSlots, result, compositeId);
        }
    }

    private bool TryGetMountedSlotRecursive(
        EntityUid vehicle,
        EntityUid slotOwner,
        VehicleSlotPath targetPath,
        HardpointSlotsComponent hardpoints,
        ItemSlotsComponent itemSlots,
        VehicleSlotPath? parentPath,
        EntityUid? parentItem,
        out VehicleMountedSlot mountedSlot)
    {
        foreach (var slot in hardpoints.Slots)
        {
            if (string.IsNullOrWhiteSpace(slot.Id))
                continue;

            EntityUid? item = null;
            if (_itemSlots.TryGetSlot(slotOwner, slot.Id, out var itemSlot, itemSlots) &&
                itemSlot.HasItem)
            {
                item = itemSlot.Item;
            }

            var path = parentPath?.Append(slot.Id) ?? new VehicleSlotPath(slot.Id);

            var current = new VehicleMountedSlot(
                vehicle,
                slotOwner,
                slot.Id,
                path,
                slot.HardpointType,
                item,
                parentItem,
                parentPath);

            if (PathEquals(path, targetPath))
            {
                mountedSlot = current;
                return true;
            }

            if (item is not { } nestedItem ||
                !TryComp(nestedItem, out HardpointSlotsComponent? nestedHardpoints) ||
                !TryComp(nestedItem, out ItemSlotsComponent? nestedItemSlots))
            {
                continue;
            }

            if (TryGetMountedSlotRecursive(
                    vehicle,
                    nestedItem,
                    targetPath,
                    nestedHardpoints,
                    nestedItemSlots,
                    path,
                    nestedItem,
                    out mountedSlot))
            {
                return true;
            }
        }

        mountedSlot = default;
        return false;
    }

    private bool TryGetMountedSlotByItemRecursive(
        EntityUid vehicle,
        EntityUid slotOwner,
        EntityUid targetItem,
        HardpointSlotsComponent hardpoints,
        ItemSlotsComponent itemSlots,
        VehicleSlotPath? parentPath,
        EntityUid? parentItem,
        out VehicleMountedSlot mountedSlot)
    {
        foreach (var slot in hardpoints.Slots)
        {
            if (string.IsNullOrWhiteSpace(slot.Id))
                continue;

            EntityUid? item = null;
            if (_itemSlots.TryGetSlot(slotOwner, slot.Id, out var itemSlot, itemSlots) &&
                itemSlot.HasItem)
            {
                item = itemSlot.Item;
            }

            var path = parentPath?.Append(slot.Id) ?? new VehicleSlotPath(slot.Id);

            var current = new VehicleMountedSlot(
                vehicle,
                slotOwner,
                slot.Id,
                path,
                slot.HardpointType,
                item,
                parentItem,
                parentPath);

            if (item == targetItem)
            {
                mountedSlot = current;
                return true;
            }

            if (item is not { } nestedItem ||
                !TryComp(nestedItem, out HardpointSlotsComponent? nestedHardpoints) ||
                !TryComp(nestedItem, out ItemSlotsComponent? nestedItemSlots))
            {
                continue;
            }

            if (TryGetMountedSlotByItemRecursive(
                    vehicle,
                    nestedItem,
                    targetItem,
                    nestedHardpoints,
                    nestedItemSlots,
                    path,
                    nestedItem,
                    out mountedSlot))
            {
                return true;
            }
        }

        mountedSlot = default;
        return false;
    }

    private static bool PathEquals(VehicleSlotPath left, VehicleSlotPath right)
    {
        return string.Equals(left.Root, right.Root, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.Child, right.Child, StringComparison.OrdinalIgnoreCase);
    }
}
