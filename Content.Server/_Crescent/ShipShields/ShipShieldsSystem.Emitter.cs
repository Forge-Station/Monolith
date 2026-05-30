using Content.Shared._Crescent.ShipShields;
using Content.Server.Power.Components;
using Content.Shared.Damage;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Content.Server.Emp;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Examine;
using Content.Server.Explosion.Components;
using Content.Shared.Explosion.Components;
using Robust.Shared.Prototypes;
using Content.Server.Station.Systems;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Crescent.ShipShields;

public partial class ShipShieldsSystem
{
    private const float MAX_EMP_DAMAGE = 10000f;
    private const float ShieldAbsorbEpsilon = 0.001f;
    [Dependency] private TriggerSystem _trigger = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    public void InitializeEmitters()
    {
        SubscribeLocalEvent<ShipShieldEmitterComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ShipShieldEmitterComponent, ComponentRemove>(OnRemoved);
    }

    private void OnRemoved(Entity<ShipShieldEmitterComponent> owner, ref ComponentRemove remove)
    {
        var parent = Transform(owner.Owner).GridUid;
        if (parent is null)
            return;
        if (TryComp<ShipShieldedComponent>(parent.Value, out var shielded) && shielded.Source == owner.Owner)
            UnshieldEntity(parent.Value, null);
    }

    /// <summary>
    /// Ship-weapon projectile hitting the shield bubble: absorb part into emitter pool; optionally let the projectile skip the shell.
    /// </summary>
    private void TryHandleShipWeaponShieldHit(
        EntityUid shieldBubbleUid,
        EntityUid emitterUid,
        ShipShieldEmitterComponent emitter,
        EntityUid projUid,
        ProjectileComponent projectile,
        ref PreventCollideEvent args)
    {
        if (TryComp<ShieldBubblePassImmunityComponent>(projUid, out var immune)
            && immune.ShieldBubble == shieldBubbleUid
            && immune.TicksRemaining > 0)
        {
            immune.TicksRemaining--;
            if (immune.TicksRemaining <= 0)
                RemComp<ShieldBubblePassImmunityComponent>(projUid);
            else
                Dirty(projUid, immune);
            args.Cancelled = true;
            return;
        }

        var rawProj = GetProjectileShieldDamage(emitter, projectile.Damage);
        float rawEmp = 0f;
        if (TryComp<EmpOnTriggerComponent>(projUid, out var emp))
            rawEmp = Math.Clamp(emp.EnergyConsumption, 0f, MAX_EMP_DAMAGE) * emitter.ShieldEmpDamageMultiplier;

        float rawExp = 0f;
        if (TryComp<ExplosiveComponent>(projUid, out var exp) && _prototypeManager.TryIndex(exp.ExplosionType, out var type))
            rawExp = exp.TotalIntensity * (float)type.DamagePerIntensity.GetTotal() * emitter.ShieldExplosionDamageMultiplier;

        var totalRaw = rawProj + rawEmp + rawExp;
        if (totalRaw <= 0f)
            return;

        var stress = Math.Clamp(emitter.Damage / Math.Max(emitter.DamageLimit, 1f), 0f, 1f);
        var absorb = Math.Clamp(emitter.ShieldProjectileAbsorptionFraction * (1f - emitter.ShieldPassthroughFromStress * stress), 0f, 1f);
        var toPool = totalRaw * absorb;
        if (emitter.ShieldHitDamageCap > 0f)
            toPool = Math.Min(toPool, emitter.ShieldHitDamageCap);

        emitter.Damage += toPool;
        AdjustEmitterLoad(emitterUid, emitter);

        if (TryComp<EmpOnTriggerComponent>(projUid, out _))
            _trigger.Trigger(projUid);

        var frac = toPool / totalRaw;

        if (frac >= 1f - ShieldAbsorbEpsilon || !projectile.Damage.AnyPositive())
        {
            projectile.ProjectileSpent = true;
            Dirty(projUid, projectile);
            QueueDel(projUid);
            return;
        }

        if (rawProj > 0f)
        {
            var scale = 1f - frac;
            if (scale < ShieldAbsorbEpsilon)
            {
                projectile.ProjectileSpent = true;
                Dirty(projUid, projectile);
                QueueDel(projUid);
                return;
            }
            projectile.Damage *= scale;
            Dirty(projUid, projectile);
        }

        var ticks = Math.Max(0, emitter.ShieldPassthroughImmunityTicks);
        if (ticks > 0)
        {
            var pass = EnsureComp<ShieldBubblePassImmunityComponent>(projUid);
            pass.ShieldBubble = shieldBubbleUid;
            pass.TicksRemaining = ticks;
            Dirty(projUid, pass);
        }

        args.Cancelled = true;
    }

    private static float GetProjectileShieldDamage(ShipShieldEmitterComponent component, DamageSpecifier damage)
    {
        if (damage.DamageDict.Count == 0)
            return (float)damage.GetTotal();

        float sum = 0f;
        foreach (var (typeId, val) in damage.DamageDict)
        {
            var mult = 1f;
            if (component.ProjectileDamageTypeMultipliers.TryGetValue(typeId, out var m))
                mult = m;
            sum += (float)val * mult;
        }

        return sum;
    }

    private void OnExamined(EntityUid uid, ShipShieldEmitterComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("shield-emitter-examine", ("basedraw", component.BaseDraw), ("additional", ShipShieldEmitterMath.CalculateAdditionalLoad(component))));
    }

    private void AdjustEmitterLoad(EntityUid uid, ShipShieldEmitterComponent? emitter = null, ApcPowerReceiverComponent? receiver = null)
    {
        if (!Resolve(uid, ref emitter, ref receiver))
            return;

        receiver.Load = emitter.BaseDraw + ShipShieldEmitterMath.CalculateAdditionalLoad(emitter);
    }
}
