using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    /// <summary>True when this map is a planet orbit layer, where parked grids never fall.</summary>
    private bool WfIsOrbitLayer(EntityUid mapUid) => HasComp<WFOrbitLayerComponent>(mapUid);

    /// <summary>Disables z-gravity on an orbit layer and restores it when the grid leaves.</summary>
    // Orbit has no terrain, so CE z-physics would otherwise walk a parked grid down every layer to the ground.
    private void WfRefreshOrbitParking(EntityUid grid, EntityUid? mapUid)
    {
        if (!ZPhysicsQuery.TryComp(grid, out var zPhys))
            return;

        var parked = mapUid is { } map && WfIsOrbitLayer(map);

        zPhys.VelocityGravity = !parked;

        if (!parked)
            return;

        // A hull in orbit rests at its layer's height.
        if (zPhys.Velocity != 0f)
            SetZVelocity((grid, zPhys), 0f);

        if (zPhys.LocalPosition != 0f)
            SetZPosition((grid, zPhys), 0f);
    }

    /// <summary>True for a planet layer below its ground; hulls never enter one.</summary>
    private bool WfClosedToHulls(Entity<CEZMapComponent> map) => map.Comp.Depth < 0 && HasComp<WFPlanetLayerComponent>(map);

    /// <summary>True on an orbit layer (left through transit) or when the hop would take the hull below ground.</summary>
    private bool WfRefusesLevelHop(EntityUid grid, Entity<CEZMapComponent> target)
        => WfIsOrbitLayer(Transform(grid).MapUid ?? EntityUid.Invalid) || WfClosedToHulls(target);


    /// <summary>Test seam: whether this grid is currently parked by the orbit hold.</summary>
    public bool WfIsParkedInOrbit(Entity<MapGridComponent> grid)
    {
        return WfIsOrbitLayer(Transform(grid).MapUid ?? EntityUid.Invalid)
               && ZPhysicsQuery.TryComp(grid, out var zPhys)
               && !zPhys.VelocityGravity;
    }

    /// <summary>Destroys contacts held by the grid's children before a map change.</summary>
    // Children keep old-map contacts across a grid move, and the broadphase then asserts "already in contact".
    public void WfDestroyRiderContacts(EntityUid grid)
    {
        var children = Transform(grid).ChildEnumerator;

        while (children.MoveNext(out var child))
        {
            if (TryComp<PhysicsComponent>(child, out var body))
                _physics.DestroyContacts(body);
        }
    }
}
