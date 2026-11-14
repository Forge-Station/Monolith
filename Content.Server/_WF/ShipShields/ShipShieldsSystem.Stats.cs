using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;
using Content.Shared.Examine;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    /// <summary>Reads operating specifications from the selected emitter's runtime component.</summary>
    public WFShipShieldGeneratorStats GetWolfgateGeneratorStats(EntityUid uid, ShipShieldEmitterComponent emitter)
    {
        return new WFShipShieldGeneratorStats(Name(uid),
            WFShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp),
            emitter.DamageLimit, emitter.HealPerSecond, emitter.HealPerSecond * emitter.UnpoweredBonus,
            emitter.BaseDraw, emitter.BaseDraw + emitter.MaxDraw, emitter.DamageOverloadTimePunishment);
    }

    /// <summary>Shows this generator's specifications before or after installation.</summary>
    private void ExamineWolfgateGeneratorStats(EntityUid uid, ShipShieldEmitterComponent emitter, ExaminedEvent args)
    {
        var stats = GetWolfgateGeneratorStats(uid, emitter);
        args.PushMarkup(Loc.GetString("wf-shield-examine-capacity", ("capacity", MathF.Round(stats.Capacity, 0)), ("overload", MathF.Round(stats.DamageLimit, 0))));
        if (stats.Capacity < stats.DamageLimit)
            args.PushMarkup(Loc.GetString("wf-shield-examine-capacity-note"));
        args.PushMarkup(Loc.GetString("wf-shield-examine-recharge", ("online", MathF.Round(stats.HealPerSecond, 1)), ("offline", MathF.Round(stats.RechargePerSecond, 1))));
        args.PushMarkup(Loc.GetString("wf-shield-examine-power", ("idle", MathF.Round(stats.BasePowerWatts / 1000f, 1)), ("maximum", MathF.Round(stats.MaximumPowerWatts / 1000f, 1))));
        args.PushMarkup(Loc.GetString("wf-shield-examine-overload", ("seconds", MathF.Round(stats.OverloadSeconds, 1))));
    }
}
