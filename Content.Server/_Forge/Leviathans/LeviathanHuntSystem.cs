using System.Numerics;
using Content.Server.NPC.HTN;
using Content.Server.NPC;
using Content.Server.Shuttles.Components;
using Content.Shared._Forge.Leviathans.Components;
using Content.Shared.CombatMode;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Leviathans;

/// <summary>
/// Steers AI leviathans at player shuttles and lets them shoot.
/// </summary>
public sealed partial class LeviathanHuntSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var wormQuery = EntityQueryEnumerator<VoidWormComponent, TransformComponent, PhysicsComponent>();
        while (wormQuery.MoveNext(out var uid, out var worm, out var xform, out var physics))
        {
            if (!CanHunt(uid))
                continue;

            if (!TryGetHuntTarget(uid, worm.HuntRange, out var target, out var dest))
            {
                worm.PassWaypoint = null;
                continue;
            }

            TickWorm(uid, worm, xform, physics, target, dest, frameTime);
            RememberTarget(uid, target, dest);

            var wormOrigin = _transform.GetMapCoordinates(uid, xform);
            var wormDist = wormOrigin.MapId == dest.MapId
                ? (dest.Position - wormOrigin.Position).Length()
                : float.MaxValue;
            if (wormDist <= worm.ShootRange && _timing.CurTime >= worm.NextShot && TryShoot(uid, target, dest))
                worm.NextShot = _timing.CurTime + worm.ShootInterval;
        }

        var drakeQuery = EntityQueryEnumerator<VoidDrakeComponent, TransformComponent, PhysicsComponent>();
        while (drakeQuery.MoveNext(out var uid, out var drake, out var xform, out var physics))
        {
            if (!CanHunt(uid))
                continue;

            if (!TryGetHuntTarget(uid, drake.HuntRange, out var target, out var dest))
                continue;

            var origin = _transform.GetMapCoordinates(uid, xform);
            var dist = origin.MapId == dest.MapId
                ? (dest.Position - origin.Position).Length()
                : float.MaxValue;

            TickDrake(uid, drake, xform, physics, dest, dist, frameTime);
            RememberTarget(uid, target, dest);

            if (origin.MapId != dest.MapId)
                continue;

            if (dist > drake.ShootRange || _timing.CurTime < drake.NextShot)
                continue;

            if (TryShoot(uid, target, dest))
                drake.NextShot = _timing.CurTime + drake.ShootInterval;
        }

        var medusaQuery = EntityQueryEnumerator<VoidMedusaComponent, TransformComponent, PhysicsComponent>();
        while (medusaQuery.MoveNext(out var uid, out var medusa, out var xform, out var physics))
        {
            if (!CanHunt(uid))
                continue;

            if (!TryGetHuntTarget(uid, medusa.HuntRange, out var target, out var dest))
                continue;

            var origin = _transform.GetMapCoordinates(uid, xform);
            var dist = origin.MapId == dest.MapId
                ? (dest.Position - origin.Position).Length()
                : float.MaxValue;

            TickMedusa(uid, medusa, xform, physics, dest, dist, frameTime);
            RememberTarget(uid, target, dest);

            if (origin.MapId != dest.MapId)
                continue;

            if (dist > medusa.ShootRange || _timing.CurTime < medusa.NextShot)
                continue;

            if (TryShoot(uid, target, dest))
                medusa.NextShot = _timing.CurTime + medusa.ShootInterval;
        }
    }

    public bool TryGetHuntTarget(EntityUid hunter, float range, out EntityUid target, out MapCoordinates dest)
    {
        target = default;
        dest = default;

        var origin = _transform.GetMapCoordinates(hunter);
        var best = float.MaxValue;
        var found = false;

        var players = EntityQueryEnumerator<ActorComponent, TransformComponent, MobStateComponent>();
        while (players.MoveNext(out var player, out _, out var pxform, out var mob))
        {
            if (player == hunter ||
                HasComp<GhostComponent>(player) ||
                mob.CurrentState == MobState.Dead)
                continue;

            if (pxform.MapID != origin.MapId)
                continue;

            EntityUid? grid = pxform.GridUid;
            Vector2 aim;
            if (grid != null && HasComp<MapGridComponent>(grid.Value))
                aim = _lookup.GetWorldAABB(grid.Value).Center;
            else
                aim = _transform.GetWorldPosition(player);

            var dist = (aim - origin.Position).Length();
            if (dist > range || dist >= best)
                continue;

            best = dist;
            target = player;
            dest = new MapCoordinates(aim, origin.MapId);
            found = true;
        }

        if (found)
            return true;

        var shuttles = EntityQueryEnumerator<ShuttleComponent, TransformComponent, MapGridComponent>();
        while (shuttles.MoveNext(out var shuttle, out _, out var sxform, out _))
        {
            if (sxform.MapID != origin.MapId)
                continue;

            var gridPos = _lookup.GetWorldAABB(shuttle).Center;
            var dist = (gridPos - origin.Position).Length();
            if (dist > range || dist >= best)
                continue;

            best = dist;
            target = shuttle;
            dest = new MapCoordinates(gridPos, origin.MapId);
            found = true;
        }

        return found;
    }

    private void TickWorm(
        EntityUid uid,
        VoidWormComponent worm,
        TransformComponent xform,
        PhysicsComponent physics,
        EntityUid target,
        MapCoordinates dest,
        float frameTime)
    {
        var origin = _transform.GetMapCoordinates(uid, xform);
        if (origin.MapId != dest.MapId)
            return;

        if (worm.PassTarget != target ||
            !worm.PassWaypoint.HasValue ||
            (worm.PassWaypoint.Value - dest.Position).Length() > worm.PassMax * 1.6f)
            BeginWormPass(worm, origin.Position, dest.Position, target);

        var waypoint = worm.PassWaypoint!.Value;
        var wayDist = (waypoint - origin.Position).Length();

        if (wayDist <= 28f)
        {
            BeginWormPass(worm, origin.Position, dest.Position, target);
            waypoint = worm.PassWaypoint!.Value;
        }

        Steer(
            uid,
            xform,
            physics,
            new MapCoordinates(waypoint, dest.MapId),
            worm.HuntSpeed,
            frameTime,
            worm.HuntAcceleration);
    }

    private void BeginWormPass(VoidWormComponent worm, Vector2 origin, Vector2 shuttle, EntityUid target)
    {
        var delta = shuttle - origin;
        var dir = delta.LengthSquared() > 0.01f
            ? delta.Normalized()
            : _random.NextAngle().ToVec();
        var overshoot = _random.NextFloat(worm.PassMin, worm.PassMax);
        worm.PassWaypoint = shuttle + dir * overshoot;
        worm.PassTarget = target;
    }

    private void TickDrake(
        EntityUid uid,
        VoidDrakeComponent drake,
        TransformComponent xform,
        PhysicsComponent physics,
        MapCoordinates dest,
        float dist,
        float frameTime)
    {
        if (drake.Enraged)
        {
            var origin = _transform.GetMapCoordinates(uid, xform);
            if (origin.MapId != dest.MapId)
                return;

            var delta = dest.Position - origin.Position;
            var through = dest;
            if (delta.LengthSquared() > 1f)
                through = new MapCoordinates(dest.Position + delta.Normalized() * 36f, dest.MapId);

            Steer(uid, xform, physics, through, drake.EnrageSpeed, frameTime, weave: true);
            return;
        }

        var origin = _transform.GetMapCoordinates(uid, xform);
        if (origin.MapId != dest.MapId)
            return;

        var standoff = drake.StandoffRange;
        if (dist < standoff - 12f)
        {
            var away = origin.Position - dest.Position;
            if (away.LengthSquared() < 0.01f)
                away = _transform.GetWorldRotation(uid).ToVec();
            var kite = new MapCoordinates(origin.Position + away.Normalized() * 48f, dest.MapId);
            Steer(uid, xform, physics, kite, drake.HuntSpeed, frameTime, weave: false);
            return;
        }

        Steer(uid, xform, physics, dest, drake.HuntSpeed, frameTime, weave: false, orbit: dist < standoff + 18f);
    }

    private void TickMedusa(
        EntityUid uid,
        VoidMedusaComponent medusa,
        TransformComponent xform,
        PhysicsComponent physics,
        MapCoordinates dest,
        float dist,
        float frameTime)
    {
        var latched = medusa.LatchedGrid != null;
        var resting = _timing.CurTime < medusa.LatchRestUntil;

        if (latched)
        {
            Steer(uid, xform, physics, dest, medusa.HuntSpeed, frameTime, weave: false, orbit: true, rotate: false);
            return;
        }

        if (resting || dist < medusa.HoverRange)
        {
            if (dist < medusa.HoverRange * 0.55f)
            {
                var origin = _transform.GetMapCoordinates(uid, xform);
                var away = origin.Position - dest.Position;
                if (away.LengthSquared() < 0.01f)
                    away = Vector2.UnitX;
                dest = new MapCoordinates(origin.Position + away.Normalized() * 40f, dest.MapId);
                Steer(uid, xform, physics, dest, medusa.HuntSpeed, frameTime, weave: false, rotate: false);
                return;
            }

            Steer(uid, xform, physics, dest, medusa.HuntSpeed, frameTime, weave: false, orbit: true, rotate: false);
            return;
        }

        Steer(
            uid,
            xform,
            physics,
            dest,
            medusa.HuntSpeed,
            frameTime,
            weave: false,
            orbit: dist < medusa.ShootRange && dist > medusa.HoverRange,
            rotate: false);
    }

    private void Steer(
        EntityUid uid,
        TransformComponent xform,
        PhysicsComponent physics,
        MapCoordinates dest,
        float speed,
        float frameTime,
        float acceleration = 0f,
        bool weave = false,
        bool orbit = false,
        bool rotate = true)
    {
        var origin = _transform.GetMapCoordinates(uid, xform);
        if (origin.MapId != dest.MapId)
            return;

        var delta = dest.Position - origin.Position;
        var dist = delta.Length();
        if (dist < 0.2f)
            return;

        var dir = delta / dist;
        if (orbit)
        {
            var tangent = new Vector2(-dir.Y, dir.X);
            dir = (dir * 0.18f + tangent * 0.98f).Normalized();
        }
        else if (weave)
        {
            var tangent = new Vector2(-dir.Y, dir.X);
            var wave = MathF.Sin((float)_timing.CurTime.TotalSeconds * 0.7f + uid.Id * 0.13f) * 0.72f;
            dir = (dir + tangent * wave).Normalized();
        }

        var desired = dir * speed;
        Vector2 vel;
        if (acceleration <= 0f)
        {
            vel = desired;
        }
        else
        {
            var current = physics.LinearVelocity;
            var change = desired - current;
            var maxStep = acceleration * MathF.Max(frameTime, 0.001f);
            vel = change.Length() <= maxStep
                ? desired
                : current + change.Normalized() * maxStep;
        }

        _physics.SetLinearVelocity(uid, vel, body: physics);
        if (!rotate)
            return;

        var face = vel.LengthSquared() > 1f ? vel : dir;
        _transform.SetWorldRotation(uid, face.ToWorldAngle());
    }

    private bool TryShoot(EntityUid uid, EntityUid target, MapCoordinates dest)
    {
        if (!TryComp<ActionGunComponent>(uid, out var actionGun) ||
            actionGun.Gun is not { } gunUid ||
            !TryComp<GunComponent>(gunUid, out var gun))
            return false;

        if (TryComp<CombatModeComponent>(uid, out var combat))
            _combat.SetInCombatMode(uid, true, combat);

        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(gunUid, ref ammo);
        if (ammo.Count <= 0)
            return false;

        var to = _transform.ToCoordinates(dest);
        _gun.AttemptShoot(uid, gunUid, gun, to, target);
        return true;
    }

    private void RememberTarget(EntityUid uid, EntityUid target, MapCoordinates dest)
    {
        if (!TryComp<HTNComponent>(uid, out var htn) || htn.Blackboard.ReadOnly)
            return;

        htn.Blackboard.SetValue("Target", target);
        htn.Blackboard.SetValue(NPCBlackboard.MovementTarget, _transform.ToCoordinates(dest));
    }

    private bool CanHunt(EntityUid uid)
    {
        if (_mobState.IsDead(uid) || HasComp<ActorComponent>(uid))
            return false;

        return !HasComp<MindContainerComponent>(uid) || !Comp<MindContainerComponent>(uid).HasMind;
    }
}
