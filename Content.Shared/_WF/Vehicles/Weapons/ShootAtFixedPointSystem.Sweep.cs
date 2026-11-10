using System.Numerics;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>
/// Stops a fixed-distance round at the first wall, door, window or hull on its way. Each tick, ahead of the physics step, the
/// round's travel for that step is cast as rays in the map's frame, and from the muzzle a ray is cast from the gun to where the round
/// was spawned. Nothing here reads a contact event, a grid's rotation or the direction a body's velocity is stored in, so a
/// grid turned any way, a second grid over the first, a thin barrier or a fast round makes no difference.
/// </summary>
public sealed partial class ShootAtFixedPointSystem
{
    /// <summary>
    /// How far in front of the obstacle's surface a round that hit one comes to rest, in tiles: the half-length of the longest
    /// round and a little more, so none of it is inside the obstacle.
    /// </summary>
    private const float ObstacleStandOff = 0.55f;

    /// <summary>The same for a round that goes off when it hits, which is put just against the surface for the physics to meet.</summary>
    private const float ImpactStandOff = 0.1f;

    /// <summary>The rays at each side of a round's middle one, as far from it as the round's body reaches, in tiles.</summary>
    private const float SweepHalfWidth = 0.1f;

    /// <summary>A step longer than this is a jump (a ship that was moved to another place, a teleport), not travel to sweep, in tiles.</summary>
    private const float MaxSweepLength = 12f;

    /// <summary>How far round the step's travel to look for grids that move under the round, in tiles.</summary>
    private const float GridReach = 3f;

    /// <summary>The square of the speed, in tiles a second, from which a grid is taken to move under the round.</summary>
    private const float MovingGridSpeedSquared = 0.01f;

    private static readonly float[] SweepSides = { 0f, 1f, -1f };

    private List<Entity<MapGridComponent>> _grids = new();
    private readonly Dictionary<EntityUid, Vector2> _moving = new();

    /// <summary>Where a round comes to rest, what stopped it, where on it the round met it and how far along the step that was (0 to 1).</summary>
    private readonly record struct Stop(Vector2 Rest, EntityUid Entity, Vector2 Surface, float Fraction);

    /// <summary>
    /// The gun may have put the muzzle through a wall, so the round is stopped on the gun's side of it. Then the round's first step is swept
    /// as a later one is.
    /// </summary>
    private void SweepSpawn(Entity<ProjectileFixedDistanceComponent> ent, MapCoordinates origin, float tick)
    {
        var spawn = _transform.GetMapCoordinates(ent);
        if (spawn.MapId == origin.MapId && TryFindStop(ent, spawn.MapId, origin.Position, spawn.Position, 0f, out var stop))
        {
            Halt(ent, stop);
            return;
        }

        SweepAhead(ent, tick);
    }

    /// <summary>Sweeps what the round will travel in the coming step, with the grids it passes moving as they move.</summary>
    private void SweepAhead(Entity<ProjectileFixedDistanceComponent> ent, float tick)
    {
        if (!_physicsQuery.TryComp(ent, out var body) || !_xformQuery.TryComp(ent, out var xform))
            return;

        var velocity = _physics.GetMapLinearVelocity(ent, body, xform);
        if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || velocity.LengthSquared() < 0.0001f)
            return;

        var start = _transform.GetWorldPosition(xform);
        if (TryFindStop(ent, xform.MapID, start, start + velocity * tick, tick, out var stop))
            Halt(ent, stop);
    }

    /// <summary>
    /// Finds the first obstacle a round meets between two points of the map, where the round comes to rest in front of it. A grid that
    /// moves during the step is met in its own frame: the round's travel is taken as it is relative to that grid.
    /// </summary>
    private bool TryFindStop(Entity<ProjectileFixedDistanceComponent> ent, MapId map, Vector2 start, Vector2 end, float tick, out Stop stop)
    {
        stop = default;

        if (map == MapId.Nullspace ||
            !_physicsQuery.TryComp(ent, out var body) ||
            !_fixturesQuery.TryComp(ent, out var fixtures) ||
            !fixtures.Fixtures.TryGetValue(SharedProjectileSystem.ProjectileFixture, out var fixture))
        {
            return false;
        }

        var length = (end - start).Length();
        if (!float.IsFinite(length) || length < 0.001f || length > MaxSweepLength)
            return false;

        var standOff = IsImpact(ent) ? ImpactStandOff : ObstacleStandOff;

        _grids.Clear();
        _moving.Clear();
        _mapManager.FindGridsIntersecting(map, Box2.FromTwoPoints(start, end).Enlarged(GridReach), ref _grids, approx: true, includeMap: false);
        foreach (var grid in _grids)
        {
            var velocity = GridPointVelocity(grid, end);
            if (velocity.LengthSquared() > MovingGridSpeedSquared)
                _moving[grid.Owner] = velocity;
        }

        Stop? best = null;
        Cast(ent, body, fixture, map, start, end, standOff, null, ref best);
        foreach (var (grid, velocity) in _moving)
        {
            Cast(ent, body, fixture, map, start, end - velocity * tick, standOff, grid, ref best);
        }

        if (best is not { } found)
            return false;

        stop = found;
        return true;
    }

    /// <summary>Casts the round's three rays, its middle and the two sides of its body, and keeps the nearest obstacle that would stop it.</summary>
    private void Cast(
        Entity<ProjectileFixedDistanceComponent> round,
        PhysicsComponent body,
        Fixture fixture,
        MapId map,
        Vector2 start,
        Vector2 end,
        float standOff,
        EntityUid? onlyGrid,
        ref Stop? best)
    {
        var travel = end - start;
        var length = travel.Length();
        if (length < 0.001f)
            return;

        var direction = travel / length;
        var sideways = new Vector2(-direction.Y, direction.X) * SweepHalfWidth;
        var state = (Round: round.Owner, Only: onlyGrid, Moving: _moving, Xforms: _xformQuery);

        foreach (var side in SweepSides)
        {
            var ray = new CollisionRay(start + sideways * side, direction, BlockCollisionGroup);
            foreach (var hit in _physics.IntersectRayWithPredicate(map, ray, state, IgnoreForSweep, length, returnOnFirstHit: false))
            {
                var fraction = hit.Distance / length;

                // The hits come nearest first: nothing further along this ray can beat what is known.
                if (best is { } known && fraction >= known.Fraction)
                    break;

                if (!CanStop(round, body, fixture, hit.HitEntity))
                    continue;

                best = new Stop(start + direction * Math.Max(hit.Distance - standOff, 0f), hit.HitEntity, start + direction * hit.Distance, fraction);
                break;
            }
        }
    }

    /// <summary>The round itself, and the things that belong to another frame than the one the ray is cast in.</summary>
    private static bool IgnoreForSweep(EntityUid uid, (EntityUid Round, EntityUid? Only, Dictionary<EntityUid, Vector2> Moving, EntityQuery<TransformComponent> Xforms) state)
    {
        if (uid == state.Round)
            return true;

        var grid = state.Xforms.TryComp(uid, out var xform) ? xform.GridUid : null;
        return state.Only is { } only
            ? grid != only
            : grid is { } moving && state.Moving.ContainsKey(moving);
    }

    /// <summary>Whether the round would meet this entity: a hard fixture on the stopping layers that the round's body touches and nothing lets it past.</summary>
    private bool CanStop(Entity<ProjectileFixedDistanceComponent> round, PhysicsComponent roundBody, Fixture roundFixture, EntityUid other)
    {
        if (TerminatingOrDeleted(other) ||
            !_physicsQuery.TryComp(other, out var otherBody) ||
            !_fixturesQuery.TryComp(other, out var otherFixtures))
        {
            return false;
        }

        foreach (var fixture in otherFixtures.Fixtures.Values)
        {
            if (!fixture.Hard ||
                (fixture.CollisionLayer & BlockCollisionGroup) == 0 ||
                (fixture.CollisionLayer & roundFixture.CollisionMask) == 0)
            {
                continue;
            }

            var ours = new PreventCollideEvent(round, other, roundBody, otherBody, roundFixture, fixture);
            RaiseLocalEvent(round, ref ours);
            if (ours.Cancelled)
                continue;

            var theirs = new PreventCollideEvent(other, round, otherBody, roundBody, fixture, roundFixture);
            RaiseLocalEvent(other, ref theirs);
            if (!theirs.Cancelled)
                return true;
        }

        return false;
    }

    /// <summary>A round that is deleted on its first hit, the cannon shell, which the projectile system finishes on a contact.</summary>
    private bool IsImpact(EntityUid round)
    {
        return _projectileQuery.TryComp(round, out var projectile) && projectile is { DeleteOnCollide: true, ProjectileSpent: false };
    }

    /// <summary>
    /// Puts a round at its stop. One that goes off on a hit is set against the obstacle and hits it, as the physics contact would have it,
    /// so its damage is the usual one; the end of its flight sets off its blast there. The others come to rest.
    /// </summary>
    private void Halt(Entity<ProjectileFixedDistanceComponent> ent, Stop stop)
    {
        _transform.SetWorldPosition(ent.Owner, stop.Rest);

        if (IsImpact(ent) &&
            _projectileQuery.TryComp(ent, out var projectile) &&
            _physicsQuery.TryComp(ent, out var body))
        {
            _projectiles.ProjectileCollide((ent.Owner, projectile, body), stop.Entity, new MapCoordinates(stop.Surface, _transform.GetMapId(ent.Owner)));
        }

        Ride(ent.Owner, stop.Entity);
        Finish(ent.Owner);
    }

    /// <summary>
    /// A round that rests in open space in front of a grid's hull rides with that grid, so a ship that moves or turns takes the round along
    /// instead of carrying its wall over a round that lies still on the map. Over a grid's floor it is on that grid already.
    /// </summary>
    private void Ride(EntityUid round, EntityUid obstacle)
    {
        var xform = Transform(round);
        if (xform.GridUid != null || !_xformQuery.TryComp(obstacle, out var obstacleXform))
            return;

        if (obstacleXform.GridUid is not { } grid || xform.ParentUid == grid)
            return;

        _transform.SetParent(round, xform, grid);
        xform.GridTraversal = false;
    }

    /// <summary>How fast a point of a grid is going, in the map's frame, from the grid's own linear and angular velocity.</summary>
    private Vector2 GridPointVelocity(Entity<MapGridComponent> grid, Vector2 point)
    {
        if (!_physicsQuery.TryComp(grid, out var body) || body.BodyType == BodyType.Static)
            return Vector2.Zero;

        var (linear, angular) = _physics.GetMapVelocities(grid.Owner, body);
        if (angular == 0f)
            return linear;

        var centre = Vector2.Transform(body.LocalCenter, _transform.GetWorldMatrix(grid.Owner));
        var offset = point - centre;
        return linear + angular * new Vector2(-offset.Y, offset.X);
    }
}
