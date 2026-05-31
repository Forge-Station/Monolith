using Content.Shared._Crescent.ShipShields;
using NUnit.Framework;

namespace Content.Tests.Shared.Crescent;

[TestFixture]
public sealed class ShipShieldEmitterMathTest
{
    [Test]
    public void CalculateAdditionalLoad_ClampsToMaxDraw()
    {
        var e = new ShipShieldEmitterComponent
        {
            Damage = 999999f,
            DamageExp = 2f,
            PowerModifier = 1f,
            MaxDraw = 50_000f,
        };

        Assert.That(ShipShieldEmitterMath.CalculateAdditionalLoad(e), Is.EqualTo(50_000f));
    }

    [Test]
    public void CalculateAdditionalLoad_MatchesPowFormula()
    {
        var e = new ShipShieldEmitterComponent
        {
            Damage = 100f,
            DamageExp = 1f,
            PowerModifier = 0.5f,
            MaxDraw = 1_000_000f,
        };

        Assert.That(ShipShieldEmitterMath.CalculateAdditionalLoad(e), Is.EqualTo(50f).Within(0.001f));
    }

    [Test]
    public void CalculateAdditionalLoad_IncludesLinearCoefficient()
    {
        var e = new ShipShieldEmitterComponent
        {
            Damage = 100f,
            DamageLinearLoadCoefficient = 2f,
            DamageExp = 1f,
            PowerModifier = 0.5f,
            MaxDraw = 1_000_000f,
        };

        // 100*2 + 100*0.5 = 250
        Assert.That(ShipShieldEmitterMath.CalculateAdditionalLoad(e), Is.EqualTo(250f).Within(0.001f));
    }

    [Test]
    public void EffectiveCollisionResistance_NoBlendWhenExponentZero()
    {
        var e = new ShipShieldEmitterComponent
        {
            CollisionResistanceMultiplier = 0.1f,
            CollisionResistanceAtFullStress = 0.9f,
            CollisionStressExponent = 0f,
            Damage = 500f,
            DamageLimit = 1000f,
        };

        Assert.That(ShipShieldEmitterMath.EffectiveCollisionResistance(e), Is.EqualTo(0.1f).Within(0.001f));
    }

    [Test]
    public void EffectiveCollisionResistance_LerpsAtFullStress()
    {
        var e = new ShipShieldEmitterComponent
        {
            CollisionResistanceMultiplier = 0.1f,
            CollisionResistanceAtFullStress = 0.5f,
            CollisionStressExponent = 1f,
            Damage = 1000f,
            DamageLimit = 1000f,
        };

        Assert.That(ShipShieldEmitterMath.EffectiveCollisionResistance(e), Is.EqualTo(0.5f).Within(0.001f));
    }

    [Test]
    public void EstimateSeconds_UsesRechargingHealRate()
    {
        var e = new ShipShieldEmitterComponent
        {
            Damage = 100f,
            DamageLimit = 1000f,
            Recharging = true,
            HealPerSecond = 10f,
            UnpoweredBonus = 2f,
            OverloadAccumulator = 0f,
            DamageOverloadTimePunishment = 30f,
        };

        // 100 / (10 * 2) = 5
        Assert.That(ShipShieldEmitterMath.EstimateSecondsUntilShieldCanRaise(e), Is.EqualTo(5f).Within(0.01f));
    }

    [Test]
    public void EstimateSeconds_DamageAboveLimit_AddsPunishmentAfterExcessHeal()
    {
        var e = new ShipShieldEmitterComponent
        {
            Damage = 150f,
            DamageLimit = 100f,
            Recharging = true,
            HealPerSecond = 10f,
            UnpoweredBonus = 1f,
            OverloadAccumulator = 0f,
            DamageOverloadTimePunishment = 20f,
        };

        // excess 50 / 10 + 20 = 25
        Assert.That(ShipShieldEmitterMath.EstimateSecondsUntilShieldCanRaise(e), Is.EqualTo(25f).Within(0.01f));
    }

    [Test]
    public void EstimateSeconds_TakesMaxOfOverloadAccumulator()
    {
        var e = new ShipShieldEmitterComponent
        {
            Damage = 0f,
            DamageLimit = 100f,
            Recharging = false,
            HealPerSecond = 10f,
            UnpoweredBonus = 1f,
            OverloadAccumulator = 40f,
            DamageOverloadTimePunishment = 10f,
        };

        Assert.That(ShipShieldEmitterMath.EstimateSecondsUntilShieldCanRaise(e), Is.EqualTo(40f).Within(0.01f));
    }
}
