using System.Numerics;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Planets.Hop;

public sealed partial class WFPlanetHopSystem
{
    /// <summary>How far past a grid's bounds another grid may not stand at the landing, in tiles.</summary>
    private const float ClearMargin = 20f;

    /// <summary>Rings and samples per ring of the search for a clear spot when walking inward finds none.</summary>
    private const int SearchRings = 8;
    private const int SearchSamples = 12;

    private List<Entity<MapGridComponent>> _found = new();

    /// <summary>
    /// The nearest spot to <paramref name="desired"/> where the hull and its riders lie inside the map's ring and touch no
    /// other grid: first inward past whatever is parked at the edge, then outward in rings.
    /// </summary>
    private Vector2 FindSpot(EntityUid hull, List<WFHopRider> riders, EntityUid map, Vector2 desired, Angle angle)
    {
        var spot = _bounds.InsideCircle(hull, map, desired);

        if (IsClear(hull, riders, map, spot, angle))
            return spot;

        var reach = ConvoyReach(hull, riders);

        if (_bounds.TryGetBounds(map, out var centre, out _))
        {
            var inward = centre - (spot + _bounds.HullCentre(hull) - _transform.GetWorldPosition(hull));
            var distance = inward.Length();

            for (var walked = 0f; walked < distance; walked += reach)
            {
                var candidate = spot + inward * (walked / distance);

                if (IsClear(hull, riders, map, candidate, angle))
                    return candidate;
            }
        }

        for (var ring = 1; ring <= SearchRings; ring++)
        {
            for (var sample = 0; sample < SearchSamples; sample++)
            {
                var direction = new Angle(Math.Tau * sample / SearchSamples).ToVec();
                var candidate = _bounds.InsideCircle(hull, map, spot + direction * reach * ring);

                if (IsClear(hull, riders, map, candidate, angle))
                    return candidate;
            }
        }

        return spot;
    }

    /// <summary>How far the convoy reaches from the hull: the hull's half-diagonal, or a rider's offset plus its own.</summary>
    private float ConvoyReach(EntityUid hull, List<WFHopRider> riders)
    {
        var reach = _bounds.HullReach(hull);

        foreach (var rider in riders)
        {
            reach = MathF.Max(reach, rider.Offset.Length() + _bounds.HullReach(rider.Grid));
        }

        return MathF.Max(reach, 1f);
    }

    /// <summary>True when the hull and its riders at this pose touch no grid that is not one of them.</summary>
    private bool IsClear(EntityUid hull, List<WFHopRider> riders, EntityUid map, Vector2 position, Angle angle)
    {
        if (!TryComp<MapComponent>(map, out var mapComp))
            return false;

        if (!IsClearOf(hull, riders, mapComp.MapId, hull, position, angle))
            return false;

        foreach (var rider in riders)
        {
            if (!IsClearOf(hull, riders, mapComp.MapId, rider.Grid, position + angle.RotateVec(rider.Offset), angle + rider.Rotation))
                return false;
        }

        return true;
    }

    private bool IsClearOf(EntityUid hull, List<WFHopRider> riders, Robust.Shared.Map.MapId mapId, EntityUid grid, Vector2 position, Angle angle)
    {
        if (!TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var box = Matrix3Helpers.CreateTransform(position, angle).TransformBox(gridComp.LocalAABB).Enlarged(ClearMargin);

        _found.Clear();
        _maps.FindGridsIntersecting(mapId, box, ref _found);

        foreach (var other in _found)
        {
            if (other.Owner == hull)
                continue;

            var rides = false;

            foreach (var rider in riders)
            {
                if (rider.Grid == other.Owner)
                {
                    rides = true;
                    break;
                }
            }

            if (!rides)
                return false;
        }

        return true;
    }
}
