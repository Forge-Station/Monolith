using System.Numerics;
using Content.Shared._Forge.Hive.Components;
using Content.Shared.Interaction.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Projectiles;

namespace Content.Server._Forge.Hive.Abilities;


public sealed partial class HiveAbilitySystem
{
    private void InitializeTentacle()
    {
        SubscribeLocalEvent<HiveTentacleComponent, ProjectileHitEvent>(OnTentacleHit);
    }

    private void OnTentacleHit(Entity<HiveTentacleComponent> ent, ref ProjectileHitEvent args)
    {
        if (args.Shooter is not { } shooter || TerminatingOrDeleted(shooter))
            return;

        var target = args.Target;
        if (target == shooter || !HasComp<MobStateComponent>(target))
            return;

        var shooterPos = _transform.GetWorldPosition(shooter);


        if (ent.Comp.YankItems
            && _hands.GetActiveItem(target) is { } item
            && !HasComp<UnremoveableComponent>(item)
            && _hands.TryDrop(target, item, checkActionBlocker: false))
        {
            ThrowTowards(item, shooterPos, ent.Comp.PullSpeed, shooter);
            _popup.PopupEntity(Loc.GetString("hive-tentacle-yank", ("target", target), ("item", item)), shooter, shooter);
            _popup.PopupEntity(Loc.GetString("hive-tentacle-yank-victim", ("item", item)), target, target, PopupType.MediumCaution);
            return;
        }


        ThrowTowards(target, shooterPos, ent.Comp.PullSpeed, shooter);
        if (ent.Comp.Knockdown > TimeSpan.Zero)
            _stun.TryKnockdown(target, ent.Comp.Knockdown, true);

        _popup.PopupEntity(Loc.GetString("hive-tentacle-pull-victim"), target, target, PopupType.MediumCaution);
    }

    private void ThrowTowards(EntityUid uid, Vector2 destination, float speed, EntityUid user)
    {
        var direction = destination - _transform.GetWorldPosition(uid);
        var length = direction.Length();
        if (length < 0.01f)
            return;


        direction *= MathF.Max(0f, length - 0.8f) / length;
        _throwing.TryThrow(uid, direction, speed, user, pushbackRatio: 0f);
    }
}
