using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipShields;

/// <summary>Publishes the selected emitter's runtime operating specifications.</summary>
[Serializable, NetSerializable]
public sealed class WFShipShieldGeneratorStats
{
    /// <summary>The selected generator's display name.</summary>
    public string Name;
    /// <summary>Usable damage capacity before power or overload forces recharge.</summary>
    public float Capacity;
    /// <summary>Damage threshold that triggers overload punishment.</summary>
    public float DamageLimit;
    /// <summary>Damage restored per second while online.</summary>
    public float HealPerSecond;
    /// <summary>Damage restored per second while recharging.</summary>
    public float RechargePerSecond;
    /// <summary>Idle power demand in watts.</summary>
    public float BasePowerWatts;
    /// <summary>Capped total power demand in watts.</summary>
    public float MaximumPowerWatts;
    /// <summary>Overload cooldown duration in seconds.</summary>
    public float OverloadSeconds;

    /// <summary>Creates one runtime generator specification.</summary>
    public WFShipShieldGeneratorStats(string name, float capacity, float damageLimit, float healPerSecond,
        float rechargePerSecond, float basePowerWatts, float maximumPowerWatts, float overloadSeconds)
    {
        Name = name;
        Capacity = capacity;
        DamageLimit = damageLimit;
        HealPerSecond = healPerSecond;
        RechargePerSecond = rechargePerSecond;
        BasePowerWatts = basePowerWatts;
        MaximumPowerWatts = maximumPowerWatts;
        OverloadSeconds = overloadSeconds;
    }

    /// <summary>Checks whether optional specifications have the same visible values.</summary>
    public static bool Same(WFShipShieldGeneratorStats? left, WFShipShieldGeneratorStats? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        return left != null && right != null && left.Name == right.Name && left.Capacity == right.Capacity &&
            left.DamageLimit == right.DamageLimit && left.HealPerSecond == right.HealPerSecond &&
            left.RechargePerSecond == right.RechargePerSecond && left.BasePowerWatts == right.BasePowerWatts &&
            left.MaximumPowerWatts == right.MaximumPowerWatts && left.OverloadSeconds == right.OverloadSeconds;
    }
}
