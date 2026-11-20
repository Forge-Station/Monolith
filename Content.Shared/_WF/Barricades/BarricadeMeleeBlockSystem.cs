using System.Linq;
using Content.Shared._RMC14.Random;
using Content.Shared.Damage;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Barricades;

/// <summary>Turns a melee attack into a hit on a barricade standing between the attacker and the target.</summary>
public sealed partial class BarricadeMeleeBlockSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private EntityQuery<DirectionalAttackBlockerComponent> _blockerQuery;

    public override void Initialize()
    {
        _blockerQuery = GetEntityQuery<DirectionalAttackBlockerComponent>();
    }

    /// <summary>Swaps a light attack or disarm for a light attack on a barricade in the way, if one blocks it.</summary>
    public void TryRedirectAttack(EntityUid attacker, EntityUid target, EntityUid weapon, ref AttackEvent attack)
    {
        if (attack is not (LightAttackEvent or DisarmAttackEvent))
            return;

        var attackerMap = _transform.GetMapCoordinates(attacker);
        var targetMap = _transform.GetMapCoordinates(target);
        if (attackerMap.MapId != targetMap.MapId)
            return;

        var diff = targetMap.Position - attackerMap.Position;
        var length = diff.Length();
        if (length <= 0.01f)
            return;

        var ray = new CollisionRay(attackerMap.Position, diff / length, (int) CollisionGroup.MidImpassable);
        var hits = _physics.IntersectRay(attackerMap.MapId, ray, length, attacker, false)
            .Where(h => h.HitEntity != target && _blockerQuery.HasComp(h.HitEntity))
            .OrderBy(h => h.Distance);

        foreach (var hit in hits)
        {
            if (!IsAttackBlocked(attacker, hit.HitEntity))
                continue;

            var coordinates = GetNetCoordinates(_transform.GetMoverCoordinates(hit.HitEntity));
            attack = new LightAttackEvent(GetNetEntity(hit.HitEntity), GetNetEntity(weapon), coordinates);
            return;
        }
    }

    /// <summary>Rolls whether this barricade stops an attack from the attacker, weaker the more damaged it is.</summary>
    public bool IsAttackBlocked(EntityUid attacker, EntityUid blocker)
    {
        if (!_blockerQuery.TryComp(blocker, out var comp) || !Transform(blocker).Anchored)
            return false;

        if (!IsFacingTarget(blocker, attacker))
            return false;

        if (!TryComp(blocker, out DamageableComponent? damageable) || comp.MaxHealth <= 0)
            return true;

        var seed = ((long) _timing.CurTick.Value << 32) | (uint) GetNetEntity(attacker).Id;
        var blockChance = Math.Max(comp.MinimumBlockChance, (comp.MaxHealth - (float) damageable.TotalDamage) / comp.MaxHealth);
        return blockChance >= new Xoroshiro64S(seed).NextFloat(0, 1);
    }

    /// <summary>Whether the target is in the 135 degree cone the blocker faces.</summary>
    public bool IsFacingTarget(EntityUid blocker, EntityUid target, EntityCoordinates? originCoordinates = null)
    {
        var targetCoordinates = originCoordinates ?? _transform.GetMoverCoordinates(target);
        var blockerCoordinates = _transform.GetMoverCoordinateRotation(blocker, Transform(blocker));
        var diff = targetCoordinates.Position - blockerCoordinates.Coords.Position;
        var dir = diff.Normalized().GetDir();
        var relativeDiff = Math.Abs(dir - blockerCoordinates.worldRot.GetDir());
        return relativeDiff is 0 or 1 or 7;
    }
}
