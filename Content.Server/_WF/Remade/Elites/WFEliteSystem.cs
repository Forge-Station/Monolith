using System.Linq;
using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Remade.Elites;

/// <summary>What the elite Remade share: smashing structures, throwing people, blinking, healing and facing.</summary>
public sealed partial class WFEliteSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private ThrowingSystem _throwing = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly ProtoId<DamageTypePrototype> Structural = "Structural";

    private readonly HashSet<Entity<DamageableComponent>> _structures = new();

    /// <summary>
    /// Damages every solid, anchored thing in a sector in front of the mob that isn't a wall: doors, windows, grilles,
    /// machines. Returns how many it hit.
    /// </summary>
    public int Smash(EntityUid owner, Vector2 aim, float reach, float halfArc, float damage)
    {
        if (damage <= 0f || aim.LengthSquared() < 0.001f)
            return 0;

        _structures.Clear();
        _lookup.GetEntitiesInRange(Transform(owner).Coordinates, reach, _structures);
        var origin = _transform.GetWorldPosition(owner);
        var half = Angle.FromDegrees(halfArc);
        var facing = new Angle(aim);
        var spec = new DamageSpecifier(_proto.Index(Structural), FixedPoint2.New(damage));
        var hit = 0;
        foreach (var structure in _structures.ToList())
        {
            if (structure.Owner == owner || !IsBreakable(structure))
                continue;

            var offset = _transform.GetWorldPosition(structure) - origin;
            if (offset.LengthSquared() > 0.25f && Math.Abs(Angle.ShortestDistance(facing, new Angle(offset)).Theta) > half.Theta)
                continue;

            _damageable.TryChangeDamage(structure, new DamageSpecifier(spec), ignoreResistances: true, origin: owner);
            hit++;
        }

        return hit;
    }

    /// <summary>Whether a charge can break it: solid, bolted down, not alive and not a wall.</summary>
    public bool IsBreakable(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) || HasComp<MobStateComponent>(uid) || !Transform(uid).Anchored || _tag.HasTag(uid, WallTag))
            return false;

        return TryComp<PhysicsComponent>(uid, out var body) && body.CanCollide && body.Hard;
    }

    /// <summary>Throws someone along a direction without hurting them.</summary>
    public void Throw(EntityUid target, Vector2 direction, float distance, float speed = 10f)
    {
        if (direction.LengthSquared() < 0.001f || distance <= 0.1f || TerminatingOrDeleted(target))
            return;

        _throwing.TryThrow(target, direction.Normalized() * distance, speed, pushbackRatio: 0f, recoil: false, animated: false, playSound: false, doSpin: false);
    }

    /// <summary>Where the mob is going: its velocity if it's moving, otherwise the fallback.</summary>
    public Vector2 Heading(EntityUid uid, Vector2 fallback)
    {
        if (TryComp<PhysicsComponent>(uid, out var body) && body.LinearVelocity.LengthSquared() > 0.25f)
            return body.LinearVelocity.Normalized();

        return fallback.LengthSquared() > 0.001f ? fallback.Normalized() : Vector2.UnitY;
    }

    /// <summary>The way the mob is facing, as a unit vector.</summary>
    public Vector2 Facing(EntityUid uid)
    {
        return _transform.GetWorldRotation(uid).ToWorldVec();
    }

    /// <summary>Moves the mob along a direction if nothing stands in the way. Returns whether it moved.</summary>
    public bool Blink(EntityUid uid, Vector2 direction, float distance)
    {
        if (direction.LengthSquared() < 0.001f)
            return false;

        var dir = direction.Normalized();
        var position = _transform.GetWorldPosition(uid);
        var ray = new CollisionRay(position, dir, (int) CollisionGroup.MobMask);
        foreach (var _ in _physics.IntersectRay(_transform.GetMapId(uid), ray, distance + 0.4f, uid, true))
        {
            return false;
        }

        _transform.SetWorldPosition(uid, position + dir * distance);
        return true;
    }

    /// <summary>Sets a mob on top of another and stills it, so it rides along.</summary>
    public void Cling(EntityUid rider, EntityUid host)
    {
        if (Transform(rider).Coordinates != Transform(host).Coordinates)
            _transform.SetCoordinates(rider, Transform(host).Coordinates);

        if (TryComp<PhysicsComponent>(rider, out var body))
            _physics.SetLinearVelocity(rider, Vector2.Zero, body: body);
    }

    /// <summary>Lets a mob pass through others, or not.</summary>
    public void SetSolid(EntityUid uid, bool solid)
    {
        if (!TerminatingOrDeleted(uid) && TryComp<PhysicsComponent>(uid, out var body))
            _physics.SetCanCollide(uid, solid, body: body);
    }

    /// <summary>Takes this much damage off the mob, heat last and at a fraction, so fire stays dangerous.</summary>
    public void Heal(EntityUid uid, float amount, float heatFactor = 1f)
    {
        if (amount <= 0f || !TryComp<DamageableComponent>(uid, out var damageable))
            return;

        var damage = new DamageSpecifier(damageable.Damage);
        var budget = FixedPoint2.New(amount);
        foreach (var type in damage.DamageDict.Keys.ToList())
        {
            if (type == "Heat" || budget <= FixedPoint2.Zero)
                continue;

            var current = damage.DamageDict[type];
            if (current <= FixedPoint2.Zero)
                continue;

            var take = FixedPoint2.Min(current, budget);
            damage.DamageDict[type] = current - take;
            budget -= take;
        }

        if (budget > FixedPoint2.Zero && damage.DamageDict.TryGetValue("Heat", out var heat) && heat > FixedPoint2.Zero)
            damage.DamageDict["Heat"] = heat - FixedPoint2.Min(heat, budget * heatFactor);

        _damageable.SetDamage(uid, damageable, damage);
    }

    /// <summary>Heals the mob until it has this share of its health left, if it has less.</summary>
    public void RestoreTo(EntityUid uid, float health)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable) || !_thresholds.TryGetDeadThreshold(uid, out var dead))
            return;

        var allowed = dead.Value.Float() * (1f - Math.Clamp(health, 0f, 1f));
        var total = damageable.TotalDamage.Float();
        if (total <= allowed || total <= 0f)
            return;

        var factor = allowed / total;
        var damage = new DamageSpecifier(damageable.Damage);
        foreach (var type in damage.DamageDict.Keys.ToList())
        {
            damage.DamageDict[type] = damage.DamageDict[type] * factor;
        }

        _damageable.SetDamage(uid, damageable, damage);
    }

    /// <summary>Brings the mob down to its death threshold with plain damage, so its state follows from its damage and stays.</summary>
    public void Fell(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable) || !_thresholds.TryGetDeadThreshold(uid, out var dead))
            return;

        var missing = dead.Value - damageable.TotalDamage;
        if (missing <= FixedPoint2.Zero)
            return;

        _damageable.TryChangeDamage(uid, new DamageSpecifier(_proto.Index<DamageTypePrototype>("Blunt"), missing + FixedPoint2.New(1)),
            ignoreResistances: true, interruptsDoAfters: false, canSever: false);
    }

    /// <summary>The damage the mob has taken of one type.</summary>
    public float Taken(EntityUid uid, string type)
    {
        return TryComp<DamageableComponent>(uid, out var damageable)
            ? damageable.Damage.DamageDict.GetValueOrDefault(type).Float()
            : 0f;
    }
}
