using Content.Shared._Forge.Weapons.ChainsawShield;
using Content.Server.Damage.Systems;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Throwing;

namespace Content.Server._Forge.Weapons.ChainsawShield;

public sealed partial class ToggleableThrowDamageSystem : EntitySystem
{
    [Dependency] private DamageOtherOnHitSystem _damageOtherOnHit = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ToggleableThrowDamageComponent, ThrowDoHitEvent>(OnThrowDoHit);
        SubscribeLocalEvent<ToggleableThrowDamageComponent, DamageExamineEvent>(OnDamageExamine);
        SubscribeLocalEvent<ToggleableThrowDamageComponent, AttemptPacifiedThrowEvent>(OnAttemptPacifiedThrow);
    }

    private void OnThrowDoHit(EntityUid uid, ToggleableThrowDamageComponent component, ref ThrowDoHitEvent args)
    {
        var damage = _damageOtherOnHit.GetThrowDamage(uid, GetDamage(uid, component), consumeModifiers: true);
        _damageOtherOnHit.DoThrowDamage(uid, args.Target, args.Component.Thrower, damage, component.IgnoreResistances);
    }

    private void OnDamageExamine(EntityUid uid, ToggleableThrowDamageComponent component, ref DamageExamineEvent args)
    {
        var damage = _damageOtherOnHit.GetThrowDamage(uid, GetDamage(uid, component));
        _damageOtherOnHit.AddThrowDamageExamine(args.Message, damage);
    }

    private void OnAttemptPacifiedThrow(Entity<ToggleableThrowDamageComponent> ent, ref AttemptPacifiedThrowEvent args)
    {
        DamageOtherOnHitSystem.CancelPacifiedThrow(ref args);
    }

    private DamageSpecifier GetDamage(EntityUid uid, ToggleableThrowDamageComponent component)
    {
        return TryComp<ItemToggleComponent>(uid, out var toggle) && toggle.Activated
            ? component.ActiveDamage
            : component.InactiveDamage;
    }
}
