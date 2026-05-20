namespace Content.Shared._Crescent.ShipShields;

/// <summary>
/// Pure helpers for shield load and UI ETA so server, examine, and tests share one definition.
/// </summary>
public static class ShipShieldEmitterMath
{
    /// <summary>
    /// Additional watts from accumulated shield damage (clamped to <see cref="ShipShieldEmitterComponent.MaxDraw"/>).
    /// </summary>
    public static float CalculateAdditionalLoad(ShipShieldEmitterComponent emitter)
    {
        var linear = emitter.Damage * emitter.DamageLinearLoadCoefficient;
        var power = Math.Pow(emitter.Damage, emitter.DamageExp) * emitter.PowerModifier;
        return (float) Math.Clamp(linear + power, 0d, emitter.MaxDraw);
    }

    /// <summary>
    /// Collision multiplier lerped toward <see cref="ShipShieldEmitterComponent.CollisionResistanceAtFullStress"/> as damage approaches the limit.
    /// When <see cref="ShipShieldEmitterComponent.CollisionResistanceAtFullStress"/> is negative, returns <see cref="ShipShieldEmitterComponent.CollisionResistanceMultiplier"/> only.
    /// </summary>
    public static float EffectiveCollisionResistance(ShipShieldEmitterComponent emitter)
    {
        if (emitter.CollisionStressExponent <= 0f || emitter.CollisionResistanceAtFullStress < 0f)
            return emitter.CollisionResistanceMultiplier;

        var stress = MathF.Pow(
            Math.Clamp(emitter.Damage / Math.Max(emitter.DamageLimit, 1f), 0f, 1f),
            emitter.CollisionStressExponent);
        return emitter.CollisionResistanceMultiplier
               + (emitter.CollisionResistanceAtFullStress - emitter.CollisionResistanceMultiplier) * stress;
    }

    /// <summary>
    /// Rough seconds until the emitter can raise the bubble again (matches discrete tick healing: effective heal/s uses
    /// <see cref="ShipShieldEmitterComponent.HealPerSecond"/> times <see cref="ShipShieldEmitterComponent.UnpoweredBonus"/> only when <see cref="ShipShieldEmitterComponent.Recharging"/>).
    /// Capped to avoid <see cref="TimeSpan"/> overflow in UI.
    /// </summary>
    public static float EstimateSecondsUntilShieldCanRaise(ShipShieldEmitterComponent emitter, float maxSeconds = 8_640_000f)
    {
        var healPerSec = emitter.Recharging
            ? emitter.HealPerSecond * emitter.UnpoweredBonus
            : emitter.HealPerSecond;

        float rechargeSeconds;
        if (emitter.Damage > emitter.DamageLimit)
        {
            if (healPerSec > 0f)
            {
                var excess = emitter.Damage - emitter.DamageLimit;
                rechargeSeconds = excess / healPerSec + emitter.DamageOverloadTimePunishment;
            }
            else
                rechargeSeconds = maxSeconds;
        }
        else
            rechargeSeconds = healPerSec > 0f ? emitter.Damage / healPerSec : 0f;

        var raw = MathF.Max(rechargeSeconds, emitter.OverloadAccumulator);
        return MathF.Min(raw, maxSeconds);
    }
}
