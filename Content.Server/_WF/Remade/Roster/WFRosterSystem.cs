using System.Numerics;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Stunnable;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Remade.Roster;

/// <summary>Marks a projectile a Remade fired, so it flies through the rest of the Remade.</summary>
[RegisterComponent]
public sealed partial class WFRemadeProjectileComponent : Component;

/// <summary>What the common Remade abilities share: hitting, leaping, firing, spilling and reading limbs.</summary>
public sealed partial class WFRosterSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedWFRemadeLimbsSystem _limbs = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private ThrownItemSystem _thrown = default!;

    private static readonly ProtoId<TagPrototype> RemadeTag = "WFRemade";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRemadeProjectileComponent, PreventCollideEvent>(OnProjectileCollide);
    }

    private void OnProjectileCollide(Entity<WFRemadeProjectileComponent> ent, ref PreventCollideEvent args)
    {
        if (_tag.HasTag(args.OtherEntity, RemadeTag))
            args.Cancelled = true;
    }

    /// <summary>Damages a target as if the owner hit it.</summary>
    public void Strike(EntityUid owner, EntityUid target, DamageSpecifier damage)
    {
        _damageable.TryChangeDamage(target, new DamageSpecifier(damage), origin: owner);
    }

    /// <summary>Throws the owner along a displacement at this speed, so it flies over whatever the throw clears.</summary>
    public void Leap(EntityUid owner, Vector2 displacement, float speed)
    {
        _throwing.TryThrow(owner, displacement, speed, pushbackRatio: 0f, recoil: false, animated: false, playSound: false, doSpin: false);
    }

    /// <summary>Whether the owner is still in the air from a leap.</summary>
    public bool Airborne(EntityUid owner)
    {
        return HasComp<ThrownItemComponent>(owner);
    }

    /// <summary>Ends a leap where the owner is and stops it dead.</summary>
    public void Land(EntityUid owner)
    {
        if (TerminatingOrDeleted(owner))
            return;

        if (TryComp<ThrownItemComponent>(owner, out var thrown))
            _thrown.StopThrow(owner, thrown);

        if (TryComp<PhysicsComponent>(owner, out var body))
            _physics.SetLinearVelocity(owner, Vector2.Zero, body: body);
    }

    /// <summary>Fires a projectile from the owner along a direction. Returns it.</summary>
    public EntityUid Fire(EntityUid owner, EntProtoId proto, Vector2 direction, float speed)
    {
        var dir = direction.LengthSquared() > 0.001f ? direction.Normalized() : Vector2.UnitY;
        var origin = _transform.GetMapCoordinates(owner);
        var at = _transform.ToCoordinates(new MapCoordinates(origin.Position + dir * 0.7f, origin.MapId));
        var bullet = Spawn(proto, at);
        _gun.ShootProjectile(bullet, dir, Vector2.Zero, null, owner, speed);
        return bullet;
    }

    /// <summary>Slows a mob that can be slowed.</summary>
    public void Slow(EntityUid target, float seconds, float multiplier)
    {
        _stun.TrySlowdown(target, TimeSpan.FromSeconds(seconds), true, multiplier, multiplier);
    }

    /// <summary>Spills a little of a reagent at a spot.</summary>
    public void Spill(Vector2 world, MapId map, string reagent, float units)
    {
        var at = _transform.ToCoordinates(new MapCoordinates(world, map));
        _puddle.TrySpillAt(at, new Solution(reagent, FixedPoint2.New(units)), out _, sound: false);
    }

    /// <summary>Where the entity is in the world, and on which map.</summary>
    public Vector2 WorldPosition(EntityUid uid, out MapId map)
    {
        var coordinates = _transform.GetMapCoordinates(uid);
        map = coordinates.MapId;
        return coordinates.Position;
    }

    /// <summary>How many of these limbs the mob still has.</summary>
    public int Remaining(EntityUid owner, IEnumerable<string> ids)
    {
        if (!TryComp<WFRemadeLimbsComponent>(owner, out var limbs))
            return 0;

        var count = 0;
        foreach (var id in ids)
        {
            if (limbs.Limbs.Exists(limb => limb.Id == id) && !limbs.Severed.Contains(id))
                count++;
        }

        return count;
    }

    /// <summary>Whether the mob can still do what the stat is for.</summary>
    public bool Can(EntityUid owner, WFRemadeLimbStat stat)
    {
        return _limbs.Can(owner, stat);
    }

    /// <summary>Everything the mob has taken, to tell when something hurt it.</summary>
    public float TotalDamage(EntityUid owner)
    {
        return TryComp<DamageableComponent>(owner, out var damageable) ? damageable.TotalDamage.Float() : 0f;
    }

    /// <summary>Swaps the damage resistances the mob has.</summary>
    public void SetArmour(EntityUid owner, string modifierSet)
    {
        if (TryComp<DamageableComponent>(owner, out var damageable))
            _damageable.SetDamageModifierSetId(owner, modifierSet, damageable);
    }
}
