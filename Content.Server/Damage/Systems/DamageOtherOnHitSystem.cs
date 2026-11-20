using Content.Server.Administration.Logs;
using Content.Server.Damage.Components;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Camera;
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Effects;
using Content.Shared.Mobs.Components;
using Content.Shared.Throwing;
using Content.Shared.Wires;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Damage.Systems
{
    public sealed partial class DamageOtherOnHitSystem : EntitySystem
    {
        [Dependency] private IAdminLogManager _adminLogger = default!;
        [Dependency] private GunSystem _guns = default!;
        [Dependency] private DamageableSystem _damageable = default!;
        [Dependency] private DamageExamineSystem _damageExamine = default!;
        [Dependency] private SharedCameraRecoilSystem _sharedCameraRecoil = default!;
        [Dependency] private SharedColorFlashEffectSystem _color = default!;

        public override void Initialize()
        {
            SubscribeLocalEvent<DamageOtherOnHitComponent, ThrowDoHitEvent>(OnDoHit);
            SubscribeLocalEvent<DamageOtherOnHitComponent, DamageExamineEvent>(OnDamageExamine);
            SubscribeLocalEvent<DamageOtherOnHitComponent, AttemptPacifiedThrowEvent>(OnAttemptPacifiedThrow);
        }

        private void OnDoHit(EntityUid uid, DamageOtherOnHitComponent component, ThrowDoHitEvent args)
        {
            if (TerminatingOrDeleted(args.Target))
                return;

            // WOLFGATE(Wolfmed): W1: tool = the thrown item, so Wolfmed's wound rules can tell a thrown spear from a swung one.
            var modifier = new GetThrowDamageModifierEvent();
            RaiseLocalEvent(uid, ref modifier);
            var dmg = _damageable.TryChangeDamage(args.Target, component.Damage * _damageable.UniversalThrownDamageModifier * modifier.Multiplier, component.IgnoreResistances, origin: args.Component.Thrower, tool: uid);

            // Log damage only for mobs. Useful for when people throw spears at each other, but also avoids log-spam when explosions send glass shards flying.
            if (dmg != null && HasComp<MobStateComponent>(args.Target))
                _adminLogger.Add(LogType.ThrowHit, $"{ToPrettyString(args.Target):target} received {dmg.GetTotal():damage} damage from collision");

            if (dmg is { Empty: false })
            {
                _color.RaiseEffect(Color.Red, new List<EntityUid>() { args.Target }, Filter.Pvs(args.Target, entityManager: EntityManager));
            }

            _guns.PlayImpactSound(args.Target, dmg, null, false, null, null);
            if (TryComp<PhysicsComponent>(uid, out var body) && body.LinearVelocity.LengthSquared() > 0f)
            {
                var direction = body.LinearVelocity.Normalized();
                _sharedCameraRecoil.KickCamera(args.Target, direction);
            }
        }

        private void OnDamageExamine(EntityUid uid, DamageOtherOnHitComponent component, ref DamageExamineEvent args)
        {
            _damageExamine.AddDamageExamine(args.Message, _damageable.ApplyUniversalAllModifiers(component.Damage * _damageable.UniversalThrownDamageModifier), Loc.GetString("damage-throw"));
        }

        /// <summary>
        /// Prevent players with the Pacified status effect from throwing things that deal damage.
        /// </summary>
        private void OnAttemptPacifiedThrow(Entity<DamageOtherOnHitComponent> ent, ref AttemptPacifiedThrowEvent args)
        {
            CancelPacifiedThrow(ref args);
        }

        public DamageSpecifier GetThrowDamage(EntityUid uid, DamageSpecifier damage, bool consumeModifiers = false)
        {
            var modifier = new GetThrowDamageModifierEvent(Consume: consumeModifiers);
            RaiseLocalEvent(uid, ref modifier);
            return damage * _damageable.UniversalThrownDamageModifier * modifier.Multiplier;
        }

        public void DoThrowDamage(EntityUid uid, EntityUid target, EntityUid? thrower, DamageSpecifier damage, bool ignoreResistances)
        {
            if (TerminatingOrDeleted(target))
                return;

            var dmg = _damageable.TryChangeDamage(target, damage, ignoreResistances, origin: thrower, tool: uid);

            if (dmg != null && HasComp<MobStateComponent>(target))
                _adminLogger.Add(LogType.ThrowHit, $"{ToPrettyString(target):target} received {dmg.GetTotal():damage} damage from collision");

            if (dmg is { Empty: false })
                _color.RaiseEffect(Color.Red, new List<EntityUid>() { target }, Filter.Pvs(target, entityManager: EntityManager));

            _guns.PlayImpactSound(target, dmg, null, false, null, null);
            if (TryComp<PhysicsComponent>(uid, out var body) && body.LinearVelocity.LengthSquared() > 0f)
                _sharedCameraRecoil.KickCamera(target, body.LinearVelocity.Normalized());
        }

        public void AddThrowDamageExamine(FormattedMessage message, DamageSpecifier damage)
        {
            _damageExamine.AddDamageExamine(message, damage, Loc.GetString("damage-throw"));
        }

        public static void CancelPacifiedThrow(ref AttemptPacifiedThrowEvent args)
        {
            args.Cancel("pacified-cannot-throw");
        }
    }
}
