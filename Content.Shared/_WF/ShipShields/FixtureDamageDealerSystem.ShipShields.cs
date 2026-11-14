using System.Numerics;
using Content.Shared._WF.ShipShields;
using Robust.Shared.Physics;

namespace Content.Shared._Mono.Fixtures;

public sealed partial class FixtureDamageDealerSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly HashSet<EntityUid> _wfShieldCandidates = new();

    /// <summary>Returns entities inside a hull shield's outline, or null when the body has no outline.</summary>
    private HashSet<EntityUid>? GetWolfgateShieldTargets(EntityUid shield, int collisionMask)
    {
        if (!TryComp<WFShipShieldVisualsComponent>(shield, out var visuals) || visuals.Contours.Length == 0)
            return null;

        var xform = Transform(shield);
        var world = _transform.GetWorldMatrix(xform);
        var bounds = Box2.Empty;
        var first = true;
        foreach (var contour in visuals.Contours)
        foreach (var point in contour)
        {
            var worldPoint = Vector2.Transform(point, world);
            bounds = first ? new Box2(worldPoint, worldPoint) : bounds.ExtendToContain(worldPoint);
            first = false;
        }

        Matrix3x2.Invert(world, out var invWorld);
        var targets = new HashSet<EntityUid>();
        _wfShieldCandidates.Clear();
        _lookup.GetEntitiesIntersecting(xform.MapID, bounds, _wfShieldCandidates, LookupFlags.Dynamic | LookupFlags.Sundries);
        foreach (var uid in _wfShieldCandidates)
        {
            if (uid == shield || !TryComp<FixturesComponent>(uid, out var fixtures))
                continue;

            var layered = false;
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if ((fixture.CollisionLayer & collisionMask) == 0)
                    continue;
                layered = true;
                break;
            }

            var local = Vector2.Transform(_transform.GetWorldPosition(uid), invWorld);
            if (layered && WFShipShieldMesh.SignedDistance(visuals.Contours, local) > 0f)
                targets.Add(uid);
        }

        return targets;
    }
}
