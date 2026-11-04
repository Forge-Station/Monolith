using System.Numerics;
using Content.Shared.Interaction;
using Robust.Shared.Map;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>What an NPC needs to climb into a vehicle the way a player does: where the doors are and whether they open.</summary>
public sealed partial class VehicleInteriorSystem
{
    /// <summary>Whether the vehicle's doors are locked against this user.</summary>
    public bool IsLockedAgainst(EntityUid vehicle, EntityUid user)
    {
        return IsEntryBlockedByLock(vehicle, user);
    }

    /// <summary>The doorway of a vehicle nearest a would-be boarder, and how near it he has to stand to climb in.</summary>
    public bool TryGetNearestEntry(EntityUid vehicle, EntityUid user, out MapCoordinates entry, out float radius)
    {
        entry = MapCoordinates.Nullspace;
        radius = 1f;
        if (!TryComp(vehicle, out VehicleEnterComponent? enter))
            return false;

        var vehicleXform = Transform(vehicle);
        var matrix = _transform.GetWorldMatrix(vehicleXform);
        if (enter.EntryPoints.Count == 0)
        {
            entry = _transform.GetMapCoordinates(vehicle, vehicleXform);
            return true;
        }

        var userPos = _transform.GetWorldPosition(user);
        var best = float.MaxValue;
        foreach (var point in enter.EntryPoints)
        {
            var world = Vector2.Transform(point.Offset, matrix);
            var distance = Vector2.DistanceSquared(world, userPos);
            if (distance >= best)
                continue;
            best = distance;
            entry = new MapCoordinates(world, vehicleXform.MapID);
            radius = point.Radius;
        }
        return best < float.MaxValue;
    }

    /// <summary>Tries the vehicle's door as a player would; true once he has started climbing in.</summary>
    public bool TryNpcEnter(EntityUid vehicle, EntityUid user)
    {
        var ev = new ActivateInWorldEvent(user, vehicle, true);
        RaiseLocalEvent(vehicle, ev);
        return ev.Handled;
    }
}
