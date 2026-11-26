using System.Linq;
using System.Numerics;
using Content.Shared.Atmos;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._WF.Barricades;

/// <summary>Facing and line-of-movement checks against directional blockers, with RMC-14's names for the RMC ports.</summary>
public sealed partial class SharedDirectionalAttackBlockSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private int GetRelativeDiff(EntityUid blocker, EntityUid target, EntityCoordinates? originCoordinates)
    {
        var targetCoordinates = originCoordinates ?? _transform.GetMoverCoordinates(target);
        var blockerCoordinates = _transform.GetMoverCoordinateRotation(blocker, Transform(blocker));
        var diff = targetCoordinates.Position - blockerCoordinates.Coords.Position;
        var dir = diff.Normalized().GetDir();
        return Math.Abs(dir - blockerCoordinates.worldRot.GetDir());
    }

    /// <summary>Whether the target is in the 135 degree cone the blocker faces.</summary>
    public bool IsFacingTarget(EntityUid blocker, EntityUid target, EntityCoordinates? originCoordinates = null)
    {
        return GetRelativeDiff(blocker, target, originCoordinates) is 0 or 1 or 7;
    }

    /// <summary>Whether the target is in the 135 degree cone behind the blocker.</summary>
    public bool IsBehindTarget(EntityUid blocker, EntityUid target, EntityCoordinates? originCoordinates = null)
    {
        return GetRelativeDiff(blocker, target, originCoordinates) is 3 or 4 or 5;
    }

    // RMC's BarricadeImpassable layer doesn't exist here; Impassable stands in for it.
    /// <summary>Whether an anchored blocker sits within range in a cardinal direction.</summary>
    public bool IsDirectionBlocked(EntityUid origin, AtmosDirection cardinal, float checkRange = 0.6f, CollisionGroup collisionGroup = CollisionGroup.Impassable | CollisionGroup.BulletImpassable)
    {
        return IsDirectionBlocked(origin, (Vector2) cardinal.CardinalToIntVec(), checkRange, collisionGroup);
    }

    /// <summary>Whether an anchored blocker sits within range along the direction.</summary>
    public bool IsDirectionBlocked(EntityUid origin, Vector2 direction, float checkRange = 0.6f, CollisionGroup collisionGroup = CollisionGroup.Impassable | CollisionGroup.BulletImpassable)
    {
        if (direction == Vector2.Zero)
            return false;

        var originPosition = _transform.GetMoverCoordinates(origin).Position;
        var ray = new CollisionRay(originPosition, direction, (int) collisionGroup);
        var intersect = _physics.IntersectRayWithPredicate(Transform(origin).MapID, ray, checkRange, e => !Transform(e).Anchored);
        return intersect.Any();
    }
}
