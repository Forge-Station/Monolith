using System.Numerics;
using Content.Server.Destructible;
using Content.Server.NPC.Components;
using Content.Shared.CombatMode;
using Content.Shared.NPC;
using Content.Shared.Weapons.Melee;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCCombatSystem
{
    private const float TargetMeleeLostRange = 14f;

    /// <summary>
    /// Railings, crates and similar cover. Walls sit well above this and are left alone.
    /// </summary>
    private const float LightObstacleMaxDamage = 100f;

    private void InitializeMelee()
    {
        SubscribeLocalEvent<NPCMeleeCombatComponent, ComponentStartup>(OnMeleeStartup);
        SubscribeLocalEvent<NPCMeleeCombatComponent, ComponentShutdown>(OnMeleeShutdown);
    }

    private void OnMeleeShutdown(EntityUid uid, NPCMeleeCombatComponent component, ComponentShutdown args)
    {
        if (TryComp<CombatModeComponent>(uid, out var combatMode))
        {
            _combat.SetInCombatMode(uid, false, combatMode);
        }

        _steering.Unregister(uid);
    }

    private void OnMeleeStartup(EntityUid uid, NPCMeleeCombatComponent component, ComponentStartup args)
    {
        if (TryComp<CombatModeComponent>(uid, out var combatMode))
        {
            _combat.SetInCombatMode(uid, true, combatMode);
        }
    }

    private void UpdateMelee(float frameTime)
    {
        var combatQuery = GetEntityQuery<CombatModeComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();
        var physicsQuery = GetEntityQuery<PhysicsComponent>();
        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<NPCMeleeCombatComponent, ActiveNPCComponent>();

        while (query.MoveNext(out var uid, out var comp, out _))
        {
            if (!combatQuery.TryGetComponent(uid, out var combat) || !combat.IsInCombatMode)
            {
                RemComp<NPCMeleeCombatComponent>(uid);
                continue;
            }

            Attack(uid, comp, curTime, physicsQuery, xformQuery);
        }
    }

    private void Attack(EntityUid uid, NPCMeleeCombatComponent component, TimeSpan curTime, EntityQuery<PhysicsComponent> physicsQuery, EntityQuery<TransformComponent> xformQuery)
    {
        component.Status = CombatStatus.Normal;

        if (!_melee.TryGetWeapon(uid, out var weaponUid, out var weapon))
        {
            component.Status = CombatStatus.NoWeapon;
            return;
        }

        if (!xformQuery.TryGetComponent(uid, out var xform) ||
            !xformQuery.TryGetComponent(component.Target, out var targetXform))
        {
            component.Status = CombatStatus.TargetUnreachable;
            return;
        }

        var attackTarget = GetAttackTarget(uid, component.Target);
        if (attackTarget != component.Target &&
            xformQuery.TryGetComponent(attackTarget, out var storageXform))
            targetXform = storageXform;

        if (!xform.Coordinates.TryDistance(EntityManager, targetXform.Coordinates, out var distance))
        {
            component.Status = CombatStatus.TargetUnreachable;
            return;
        }

        if (distance > TargetMeleeLostRange)
        {
            component.Status = CombatStatus.TargetUnreachable;
            return;
        }

        TryComp<NPCSteeringComponent>(uid, out var steering);

        // No way around: walk into the railing or crate and break it instead of standing there.
        if (TryBreach(uid, attackTarget, weaponUid, weapon, steering, curTime, out var breachStatus))
        {
            component.Status = breachStatus;
            return;
        }

        if (steering != null && steering.Status == SteeringStatus.NoPath)
        {
            component.Status = CombatStatus.TargetUnreachable;
            return;
        }

        // TODO: When I get parallel operators move this as NPC combat shouldn't be handling this.
        _steering.Register(uid, new EntityCoordinates(component.Target, Vector2.Zero), steering);

        if (distance > weapon.Range)
        {
            component.Status = CombatStatus.TargetOutOfRange;
            return;
        }

        if (weapon.NextAttack > curTime || !Enabled)
            return;

        if (_random.Prob(component.MissChance) &&
            physicsQuery.TryGetComponent(component.Target, out var targetPhysics) &&
            targetPhysics.LinearVelocity.LengthSquared() != 0f)
        {
            _melee.AttemptLightAttackMiss(uid, weaponUid, weapon, targetXform.Coordinates.Offset(_random.NextVector2(0.5f)));
        }
        else
        {
            _melee.AttemptLightAttack(uid, weaponUid, weapon, attackTarget);
        }
    }

    /// <summary>
    /// Pathfinding gave up. Break the light destructible standing between us and the target.
    /// </summary>
    private bool TryBreach(
        EntityUid uid,
        EntityUid attackTarget,
        EntityUid weaponUid,
        MeleeWeaponComponent weapon,
        NPCSteeringComponent? steering,
        TimeSpan curTime,
        out CombatStatus status)
    {
        status = CombatStatus.Normal;

        if (steering == null)
            return false;

        var pathBlocked = steering.DirectMove ||
                          steering.Status == SteeringStatus.NoPath ||
                          steering.FailedPathCount > 0 && steering.CurrentPath.Count == 0 && !steering.Pathfind;

        if (!pathBlocked)
            return false;

        var cover = ClassifyCover(uid, attackTarget, out var obstacle);

        // A real wall. Stop pushing and let the chase fail.
        if (cover == CoverHit.Heavy)
        {
            steering.DirectMove = false;
            return false;
        }

        // The railing is already gone. Walk through the gap instead of waiting on a stale path.
        if (cover == CoverHit.Clear)
        {
            if (Transform(uid).Coordinates.TryDistance(EntityManager, Transform(attackTarget).Coordinates, out var targetDistance) &&
                targetDistance <= weapon.Range)
            {
                steering.DirectMove = false;
                steering.Status = SteeringStatus.Moving;
                steering.FailedPathCount = 0;
                return false;
            }

            StartDirectMove(steering, attackTarget);
            status = CombatStatus.TargetOutOfRange;
            return true;
        }

        StartDirectMove(steering, attackTarget);

        if (!Transform(uid).Coordinates.TryDistance(EntityManager, Transform(obstacle).Coordinates, out var distance) ||
            distance > weapon.Range)
        {
            status = CombatStatus.TargetOutOfRange;
            return true;
        }

        if (weapon.NextAttack <= curTime && Enabled)
            _melee.AttemptLightAttack(uid, weaponUid, weapon, obstacle);

        status = CombatStatus.Normal;
        return true;
    }

    private void StartDirectMove(NPCSteeringComponent steering, EntityUid target)
    {
        steering.DirectMove = true;
        steering.Status = SteeringStatus.Moving;
        steering.FailedPathCount = 0;
        steering.Coordinates = new EntityCoordinates(target, Vector2.Zero);
        // Pushing a railing looks like being stuck. Don't give up the chase while breaching.
        steering.LastStuckTime = _timing.CurTime;
    }

    private enum CoverHit
    {
        Clear,
        Light,
        Heavy,
    }

    /// <summary>
    /// What the straight line to the target hits first.
    /// </summary>
    private CoverHit ClassifyCover(EntityUid uid, EntityUid target, out EntityUid obstacle)
    {
        obstacle = default;
        var origin = _transform.GetMapCoordinates(uid);
        var destination = _transform.GetMapCoordinates(target);

        if (origin.MapId != destination.MapId)
            return CoverHit.Heavy;

        var delta = destination.Position - origin.Position;
        var length = delta.Length();
        if (length < 0.2f)
            return CoverHit.Clear;

        var (_, mask) = _physics.GetHardCollision(uid);
        if (mask == 0)
            mask = (int) CollisionGroup.MobMask;

        var ray = new CollisionRay(origin.Position, delta / length, mask);
        foreach (var hit in _physics.IntersectRay(origin.MapId, ray, length, uid, false))
        {
            if (hit.HitEntity == target)
            {
                if (!IsLightDestructible(target))
                    return CoverHit.Clear;

                obstacle = target;
                return CoverHit.Light;
            }

            if (!IsLightDestructible(hit.HitEntity))
                return CoverHit.Heavy;

            obstacle = hit.HitEntity;
            return CoverHit.Light;
        }

        return CoverHit.Clear;
    }

    private bool IsLightDestructible(EntityUid uid)
    {
        return TryComp<DestructibleComponent>(uid, out var destructible) &&
               _destructible.DestroyedAt(uid, destructible).Float() <= LightObstacleMaxDamage;
    }
}
