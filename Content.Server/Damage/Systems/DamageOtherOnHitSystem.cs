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
using Robust.Shared.Utility; // Forge-Change

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
            var damage = GetThrowDamage(uid, component.Damage, consumeModifiers: true);
            DoThrowDamage(uid, args.Target, args.Component.Thrower, damage, component.IgnoreResistances);
        }

        private void OnDamageExamine(EntityUid uid, DamageOtherOnHitComponent component, ref DamageExamineEvent args)
        {
            AddThrowDamageExamine(args.Message, GetThrowDamage(uid, component.Damage));
        }

        /// <summary>
        /// Prevent players with the Pacified status effect from throwing things that deal damage.
        /// </summary>
        private void OnAttemptPacifiedThrow(Entity<DamageOtherOnHitComponent> ent, ref AttemptPacifiedThrowEvent args)
        {
            CancelPacifiedThrow(ref args);
        }

        // Forge-Change-Start: shared throw-damage pipeline with optional one-shot modifier consumption.
        public DamageSpecifier GetThrowDamage(EntityUid uid, DamageSpecifier damage, bool consumeModifiers = false)
        {
            var ev = new GetThrowDamageModifierEvent(1f, consumeModifiers);
            RaiseLocalEvent(uid, ref ev);

            return damage * _damageable.UniversalThrownDamageModifier * ev.Multiplier;
        }

        public void DoThrowDamage(
            EntityUid uid,
            EntityUid target,
            EntityUid? thrower,
            DamageSpecifier damage,
            bool ignoreResistances)
        {
            if (TerminatingOrDeleted(target))
                return;

            var dmg = _damageable.TryChangeDamage(target, damage, ignoreResistances, origin: thrower);

            // Log damage only for mobs. Useful for when people throw spears at each other, but also avoids log-spam when explosions send glass shards flying.
            if (dmg != null && HasComp<MobStateComponent>(target))
                _adminLogger.Add(LogType.ThrowHit, $"{ToPrettyString(target):target} received {dmg.GetTotal():damage} damage from collision");

            if (dmg is { Empty: false })
                _color.RaiseEffect(Color.Red, new List<EntityUid>() { target }, Filter.Pvs(target, entityManager: EntityManager));

            _guns.PlayImpactSound(target, dmg, null, false, null, null);
            if (TryComp<PhysicsComponent>(uid, out var body) && body.LinearVelocity.LengthSquared() > 0f)
            {
                var direction = body.LinearVelocity.Normalized();
                _sharedCameraRecoil.KickCamera(target, direction);
            }
        }

        public void AddThrowDamageExamine(FormattedMessage message, DamageSpecifier damage)
        {
            _damageExamine.AddDamageExamine(
                message,
                _damageable.ApplyUniversalAllModifiers(damage),
                Loc.GetString("damage-throw"));
        }

        public static void CancelPacifiedThrow(ref AttemptPacifiedThrowEvent args)
        {
            args.Cancel("pacified-cannot-throw");
        }
        // Forge-Change-End
    }

    // Forge-Change: throw modifier event with consume flag for ricochet-safe multiplier usage.
    [ByRefEvent]
    public struct GetThrowDamageModifierEvent
    {
        public GetThrowDamageModifierEvent(float multiplier, bool consume = false)
        {
            Multiplier = multiplier;
            Consume = consume;
        }

        public float Multiplier;
        public bool Consume;
    }
}
