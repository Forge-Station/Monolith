using Content.Shared._WF.Vehicles.Core;

namespace Content.Shared._WF.WolfWeather;

public abstract partial class SharedWolfWeatherSystem
{
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;
    [Dependency] private EntityQuery<VehicleInteriorLinkComponent> _interiorQuery = default!;

    /// <summary>
    /// The hull of the vehicle whose interior a body is in, if any. Such a body is on a private map but lives under the
    /// sky of the map the hull is on.
    /// </summary>
    public bool TryGetHull(EntityUid body, out EntityUid hull)
    {
        hull = default;
        if (!_xformQuery.TryComp(body, out var xform)
            || xform.MapUid is not { } map
            || !_interiorQuery.TryComp(map, out var link)
            || link.Vehicle is not { Valid: true } vehicle
            || TerminatingOrDeleted(vehicle))
            return false;

        hull = vehicle;
        return true;
    }

    /// <summary>The map whose sky a body is under: its own, or for someone inside a vehicle the hull's.</summary>
    public EntityUid? GetSkyMap(EntityUid body)
    {
        if (TryGetHull(body, out var hull))
            body = hull;

        return _xformQuery.TryComp(body, out var xform) ? xform.MapUid : null;
    }
}
