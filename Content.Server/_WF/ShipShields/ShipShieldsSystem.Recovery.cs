using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    /// <summary>Estimates automatic startup with unchanged power and no further damage.</summary>
    public static (WFShipShieldRecoveryStatus Status, int Seconds) EstimateWolfgateShieldRecovery(
        ShipShieldEmitterComponent emitter, bool powered, bool enabled = true, bool disabled = false)
    {
        if (!enabled)
            return (WFShipShieldRecoveryStatus.Lowered, -1);
        if (disabled)
            return (WFShipShieldRecoveryStatus.Disabled, -1);
        if (!powered)
            return (WFShipShieldRecoveryStatus.NoPower, -1);

        var damage = emitter.Damage;
        var overload = emitter.OverloadAccumulator;
        var recharging = emitter.Recharging;
        var step = emitter.EmitterUpdateInterval > 0f ? emitter.EmitterUpdateInterval : 1.5f;
        var elapsed = Math.Max(0f, step - emitter.Accumulator);
        var status = WFShipShieldRecoveryStatus.Ready;
        if (!float.IsFinite(damage) || !float.IsFinite(overload) || !float.IsFinite(elapsed) ||
            !float.IsFinite(emitter.MaxDraw) || emitter.MaxDraw < 0f)
            return (WFShipShieldRecoveryStatus.Ready, -1);

        // Bound malformed or impractically long configurations without blocking UI updates.
        for (var tick = 0; tick < 2400; tick++)
        {
            var load = (float) Math.Clamp(Math.Pow(damage, emitter.DamageExp) * emitter.PowerModifier, 0d, emitter.MaxDraw);
            if (load >= emitter.MaxDraw)
                recharging = true;
            var rechargeThisTick = recharging;
            if (overload > 0f)
                overload -= step;
            var healed = emitter.HealPerSecond * step;
            if (recharging)
                healed *= emitter.UnpoweredBonus;
            damage -= healed;
            if (damage <= 0f)
            {
                damage = 0f;
                recharging = false;
            }
            if (damage > emitter.DamageLimit)
                overload = emitter.DamageOverloadTimePunishment;
            if (tick == 0)
            {
                var overloaded = overload >= 1f;
                status = rechargeThisTick
                    ? overloaded ? WFShipShieldRecoveryStatus.RechargingAndOverloaded : WFShipShieldRecoveryStatus.Recharging
                    : overloaded ? WFShipShieldRecoveryStatus.Overloaded : WFShipShieldRecoveryStatus.Ready;
            }
            if (!float.IsFinite(damage) || !float.IsFinite(overload) || !float.IsFinite(healed))
                return (status, -1);
            if (!recharging && overload < 1f)
                return (status, (int) Math.Ceiling(elapsed));
            if (damage > 0f && healed <= 0f && (recharging || damage > emitter.DamageLimit))
                return (status, -1);
            elapsed += step;
        }
        return (status, -1);
    }
}
