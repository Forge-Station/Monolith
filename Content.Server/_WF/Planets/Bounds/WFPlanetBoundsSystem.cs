using System.Numerics;
using Content.Server._NF.Shuttles.Components;
using Content.Server.Salvage;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.Planets.Bounds;

/// <summary>
/// Confines a world to a circle: marks every layer with its bounds when the network is built, so the loader never
/// generates terrain outside it, and rings the ground and the layers below it with an impassable boundary.
/// </summary>
public sealed partial class WFPlanetBoundsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private RestrictedRangeSystem _restricted = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

    private readonly HashSet<EntityUid> _docked = new();

    /// <summary>Tiles a hull must keep between its edge and the circle to descend, or be pushed back to.</summary>
    public const float HullMargin = 4f;

    /// <summary>How far inside the edge a clamped hull is put: past rounding, and past the step the physics engine takes
    /// shoving it off whatever it landed on.</summary>
    private const float ClampInset = 0.5f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPlanetNetworkBuiltEvent>(OnNetworkBuilt);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFtlCompleted);
    }

    /// <summary>The radius a surface gets this round, or zero for unbounded.</summary>
    public int RadiusFor(WFPlanetSurfacePrototype surface)
    {
        if (!_cfg.GetCVar(PlanetCVars.Bounds))
            return 0;

        var cvar = _cfg.GetCVar(PlanetCVars.Radius);
        return Math.Max(cvar > 0 ? cvar : surface.Radius, 0);
    }

    private void OnNetworkBuilt(ref WFPlanetNetworkBuiltEvent args)
    {
        var radius = RadiusFor(args.Surface);
        if (radius <= 0 || !TryComp<WFPlanetNetworkComponent>(args.Network, out var network))
            return;

        var centre = network.Centre;

        foreach (var layer in network.Layers)
        {
            if (layer == network.OrbitMap)
                continue;

            Mark(layer, centre, radius);
        }

        foreach (var layer in network.LowerLayers)
        {
            Mark(layer, centre, radius);
        }

        if (TryComp<WFOrbitLayerComponent>(network.OrbitMap, out var orbit))
        {
            orbit.BoundsCentre = centre;
            orbit.BoundsRadius = radius;
            Dirty(network.OrbitMap, orbit);
        }

        // Only where someone can walk: a hull in the air is turned back by the drag sweep instead.
        _restricted.CreateBoundary(new EntityCoordinates(args.Ground, centre), radius);

        foreach (var layer in network.LowerLayers)
        {
            _restricted.CreateBoundary(new EntityCoordinates(layer, centre), radius);
        }
    }

    /// <summary>A hull that jumps onto a bounded layer comes out inside the circle.</summary>
    private void OnFtlCompleted(ref FTLCompletedEvent args)
    {
        ClampHull(args.Entity);
    }

    private void Mark(EntityUid layer, Vector2 centre, int radius)
    {
        var bounds = EnsureComp<WFPlanetBoundsComponent>(layer);
        bounds.Centre = centre;
        bounds.Radius = radius;
        Dirty(layer, bounds);
    }

    /// <summary>
    /// The circle of a map, from a layer's own bounds, an orbit layer's radar copy or, for the gap between two layers,
    /// the layers either side; false where unbounded.
    /// </summary>
    public bool TryGetBounds(EntityUid map, out Vector2 centre, out float radius)
    {
        if (TryComp<CEZTransitMapComponent>(map, out var transit))
        {
            if (transit.LowerMap is { } lower && TryGetBounds(lower, out centre, out radius))
                return true;
            if (transit.UpperMap is { } upper && TryGetBounds(upper, out centre, out radius))
                return true;
        }

        if (TryComp<WFPlanetBoundsComponent>(map, out var bounds))
        {
            centre = bounds.Centre;
            radius = bounds.Radius;
            return radius > 0f;
        }

        if (TryComp<WFOrbitLayerComponent>(map, out var orbit) && orbit.BoundsRadius > 0f)
        {
            centre = orbit.BoundsCentre;
            radius = orbit.BoundsRadius;
            return true;
        }

        centre = Vector2.Zero;
        radius = 0f;
        return false;
    }

    /// <summary>How far a hull reaches from the middle of its bounds: the half-diagonal.</summary>
    public float HullReach(EntityUid grid)
    {
        return TryComp<MapGridComponent>(grid, out var comp) ? comp.LocalAABB.Size.Length() / 2f : 0f;
    }

    /// <summary>
    /// The middle of a hull's bounds in the world. A hull's origin is wherever its mapper put it, often a corner, so
    /// the circle of <see cref="HullReach"/> is round this point and not round the origin.
    /// </summary>
    public Vector2 HullCentre(EntityUid grid)
    {
        var xform = Transform(grid);

        return TryComp<MapGridComponent>(grid, out var comp)
            ? Vector2.Transform(comp.LocalAABB.Center, _transform.GetWorldMatrix(xform))
            : _transform.GetWorldPosition(xform);
    }

    /// <summary>Whether a hull, with its margin, lies wholly inside its map's circle; true on an unbounded map.</summary>
    public bool IsHullInside(EntityUid grid)
    {
        var xform = Transform(grid);

        if (xform.MapUid is not { } map || !TryGetBounds(map, out var centre, out var radius))
            return true;

        var reach = HullReach(grid) + HullMargin;
        return Vector2.Distance(HullCentre(grid), centre) + reach <= radius;
    }

    /// <summary>Moves a hull and whatever is docked to it to the nearest spot where the hull lies wholly inside its map's circle, if it is outside.</summary>
    public void ClampHull(EntityUid grid)
    {
        if (Transform(grid).MapUid is not { } map)
            return;

        var position = _transform.GetWorldPosition(grid);
        var inside = InsideCircle(grid, map, position);

        if (inside == position)
            return;

        _docked.Clear();
        _shuttle.GetAllDockedShuttles(grid, _docked);
        _docked.Add(grid);

        foreach (var hull in _docked)
        {
            // A dock to something fixed doesn't drag it along.
            if (hull != grid && (HasComp<ForceAnchorComponent>(hull)
                || !TryComp<PhysicsComponent>(hull, out var body) || body.BodyType != BodyType.Dynamic))
                continue;

            _transform.SetWorldPosition(hull, _transform.GetWorldPosition(hull) + inside - position);
        }
    }

    /// <summary>The nearest spot to <paramref name="position"/> for a hull's origin where it lies wholly inside a map's circle.</summary>
    public Vector2 InsideCircle(EntityUid grid, EntityUid map, Vector2 position)
    {
        if (!TryGetBounds(map, out var centre, out var radius))
            return position;

        var reach = HullReach(grid) + HullMargin;
        var middle = position + HullCentre(grid) - _transform.GetWorldPosition(grid);
        var offset = middle - centre;
        var distance = offset.Length();
        var allowed = Math.Max(radius - reach, 0f);

        if (distance <= allowed || distance <= 0f)
            return position;

        allowed = Math.Max(allowed - ClampInset, 0f);

        return position + centre + offset * (allowed / distance) - middle;
    }
}
