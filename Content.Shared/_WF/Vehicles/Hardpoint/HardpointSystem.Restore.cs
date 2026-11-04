using System.Collections.Generic;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Containers.ItemSlots;

namespace Content.Shared._WF.Vehicles.Hardpoint;

/// <summary>A workshop's full repair: the frame and every fitted part, destroyed ones included, made whole at once.</summary>
public sealed partial class HardpointSystem
{
    /// <summary>The frame and every part fitted to it, down through turrets and their own slots.</summary>
    public List<(EntityUid Part, HardpointIntegrityComponent Integrity)> GetIntegrityParts(EntityUid vehicle)
    {
        var parts = new List<(EntityUid, HardpointIntegrityComponent)>();
        var visited = new HashSet<EntityUid>();
        CollectIntegrityParts(vehicle, parts, visited);
        return parts;
    }

    /// <summary>How much integrity the vehicle is missing over all its parts, and the most it can have.</summary>
    public float GetMissingIntegrity(EntityUid vehicle, out float max)
    {
        var missing = 0f;
        max = 0f;
        foreach (var (_, integrity) in GetIntegrityParts(vehicle))
        {
            if (integrity.MaxIntegrity <= 0f)
                continue;

            max += integrity.MaxIntegrity;
            missing += MathF.Max(0f, integrity.MaxIntegrity - integrity.Integrity);
        }

        return missing;
    }

    /// <summary>Puts the frame and every fitted part back to full integrity. False when nothing needed it.</summary>
    public bool RestoreIntegrity(EntityUid vehicle)
    {
        if (_net.IsClient)
            return false;

        var restored = false;
        var frameWasDown = false;
        foreach (var (part, integrity) in GetIntegrityParts(vehicle))
        {
            if (integrity.MaxIntegrity <= 0f || integrity.Integrity >= integrity.MaxIntegrity)
                continue;

            if (part == vehicle && integrity.Integrity <= 0f)
                frameWasDown = true;

            integrity.Integrity = integrity.MaxIntegrity;
            Dirty(part, integrity);
            UpdateFrameDamageAppearance(part, integrity);
            RaiseIntegrityChanged(part);
            RefreshSupportModifiers(part);
            restored = true;
        }

        if (!restored)
            return false;

        if (frameWasDown)
            RaiseFrameIntegrityChanged(vehicle, true);

        if (HasComp<VehicleWheelSlotsComponent>(vehicle))
            _wheels.OnWheelDamaged(vehicle);

        RefreshCanRun(vehicle);
        UpdateHardpointUi(vehicle);
        RaiseHardpointsRefresh(vehicle);
        return true;
    }

    private void CollectIntegrityParts(EntityUid owner,
        List<(EntityUid, HardpointIntegrityComponent)> parts,
        HashSet<EntityUid> visited)
    {
        if (!visited.Add(owner))
            return;

        if (TryComp(owner, out HardpointIntegrityComponent? integrity))
            parts.Add((owner, integrity));

        if (!TryComp(owner, out ItemSlotsComponent? slots))
            return;

        foreach (var slot in slots.Slots.Values)
        {
            if (slot.Item is { } item && HasComp<HardpointIntegrityComponent>(item))
                CollectIntegrityParts(item, parts, visited);
        }
    }
}
