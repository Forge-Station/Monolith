using System.Numerics;
using Content.Server._WF.Remade.Conversion;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Maths;

namespace Content.Server._WF.Remade.Elites;

/// <summary>Scales the damage a brute takes by where the attacker stands (DS13's brute facing armour) and flings whoever it hits.</summary>
public sealed partial class WFBruteSystem : EntitySystem
{
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFEliteSystem _elite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFBruteArmourComponent, DamageModifyEvent>(OnModify);
        SubscribeLocalEvent<WFFlingOnHitComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnModify(Entity<WFBruteArmourComponent> ent, ref DamageModifyEvent args)
    {
        args.Damage = args.Damage * Share(ent, args.Origin);
    }

    /// <summary>Everything living that the blow lands on goes flying away from the brute.</summary>
    private void OnMeleeHit(Entity<WFFlingOnHitComponent> ent, ref MeleeHitEvent args)
    {
        var origin = _transform.GetWorldPosition(args.User);
        foreach (var target in args.HitEntities)
        {
            if (target == args.User || TerminatingOrDeleted(target) || !HasComp<MobStateComponent>(target) || HasComp<WFRemadeComponent>(target))
                continue;

            var away = _transform.GetWorldPosition(target) - origin;
            _elite.Throw(target, away.LengthSquared() > 0.01f ? away : _elite.Facing(args.User), ent.Comp.Distance, ent.Comp.Speed);
            if (ent.Comp.Knockdown > 0f)
                _stun.TryKnockdown(target, TimeSpan.FromSeconds(ent.Comp.Knockdown), true);
        }
    }

    /// <summary>The share of a blow from this attacker that gets through.</summary>
    public float Share(Entity<WFBruteArmourComponent> ent, EntityUid? attacker)
    {
        var comp = ent.Comp;
        if (attacker is not { } origin || TerminatingOrDeleted(origin) || origin == ent.Owner)
            return comp.Side;

        var offset = _transform.GetWorldPosition(origin) - _transform.GetWorldPosition(ent);
        if (offset.LengthSquared() < 0.01f)
            return comp.Side;

        var facing = _elite.Facing(ent);
        var cosine = Math.Clamp(Vector2.Dot(facing, offset.Normalized()), -1f, 1f);
        var degrees = MathHelper.RadiansToDegrees(MathF.Acos(cosine));
        if (degrees <= comp.FrontArc)
            return comp.Front;

        return degrees >= comp.BackArc ? comp.Back : comp.Side;
    }
}
