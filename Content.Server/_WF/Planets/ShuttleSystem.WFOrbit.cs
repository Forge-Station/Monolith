using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    /// <summary>Warm-up of an orbital insertion, in seconds.</summary>
    public const float WfOrbitStartupTime = 5f;

    /// <summary>Transit time of an orbital insertion, in seconds; includes the arrival phase.</summary>
    public const float WfOrbitTravelTime = 5f;

    /// <summary>True when FTL departure is refused: below orbit on a planet, or mid-transit.</summary>
    // Checked from TrySetupFTL and FTLToDock, since the console's beacon and free-FTL paths skip CanFTLTo.
    public bool WfRefusesFtlDeparture(EntityUid shuttleUid)
    {
        if (Transform(shuttleUid).MapUid is not { } mapUid)
            return false;

        if (HasComp<CEZTransitMapComponent>(mapUid))
            return true;

        return HasComp<WFPlanetLayerComponent>(mapUid) && !HasComp<WFOrbitLayerComponent>(mapUid);
    }

    /// <summary>FTLs a hull to another map with no drive, beacon or range check; the caller owns every gate.</summary>
    public bool WfFTLToLayer(Entity<ShuttleComponent> shuttle, EntityUid targetMap, Vector2 worldPos)
    {
        if (!HasComp<MapComponent>(targetMap) || HasComp<FTLComponent>(shuttle.Owner))
            return false;

        // Keep the same pose when the spot is clear instead of the arrival scatter.
        var coordinates = new EntityCoordinates(targetMap, worldPos);
        var angle = _transform.GetWorldRotation(shuttle.Owner);

        if (!WfLayerSpotIsClear(shuttle.Owner, targetMap, worldPos)
            && TryGetFTLProximity(shuttle.Owner, coordinates, out var free, out var freeAngle))
        {
            coordinates = free;
            angle = freeAngle;
        }

        FTLToCoordinates(shuttle.Owner, shuttle.Comp, coordinates, angle, WfOrbitStartupTime, WfOrbitTravelTime);
        return HasComp<FTLComponent>(shuttle.Owner);
    }

    /// <summary>True when the hull's footprint at the same pose on the target map touches no other grid.</summary>
    private bool WfLayerSpotIsClear(EntityUid shuttleUid, EntityUid targetMap, Vector2 worldPos)
    {
        if (!TryComp<MapGridComponent>(shuttleUid, out var grid) || !TryComp<MapComponent>(targetMap, out var map))
            return false;

        var delta = worldPos - _transform.GetWorldPosition(shuttleUid);
        var bounds = _transform.GetWorldMatrix(shuttleUid)
            .TransformBox(grid.LocalAABB)
            .Translated(delta)
            .Enlarged(FTLBufferRange);

        var found = new List<Entity<MapGridComponent>>();
        _mapManager.FindGridsIntersecting(map.MapId, bounds, ref found);

        foreach (var other in found)
        {
            if (other.Owner != shuttleUid)
                return false;
        }

        return true;
    }
}
