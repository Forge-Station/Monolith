using Content.Shared._WF.Warmth;
using Robust.Shared.Player;

namespace Content.Server._WF.Warmth;

public sealed partial class WFWarmthSystem
{
    /// <summary>Gauge change per second under which the body counts as steady.</summary>
    private const float SteadyRate = 0.0005f;

    /// <summary>Share of each second's change the smoothed rate takes.</summary>
    private const float RateSmoothing = 0.3f;

    /// <summary>
    /// The HUD gauge: where the body sits between the cold and heat harm lines, which way it is heading, and whether a
    /// fire reaches it. Only a player's client reads these, so other bodies skip them.
    /// </summary>
    private bool UpdateGauge(EntityUid body, WFWarmthComponent comp, TransformComponent xform, float seconds)
    {
        if (!HasComp<ActorComponent>(body))
            return false;

        var raw = Math.Clamp(0.5f + (comp.HeatShare - comp.ColdShare) * 0.5f, 0f, 1f);
        comp.GaugeRate = float.IsNaN(comp.LastGauge)
            ? 0f
            : comp.GaugeRate + ((raw - comp.LastGauge) / seconds - comp.GaugeRate) * RateSmoothing;
        comp.LastGauge = raw;

        var trend = comp.GaugeRate > SteadyRate
            ? WFWarmthTrend.Warming
            : comp.GaugeRate < -SteadyRate ? WFWarmthTrend.Cooling : WFWarmthTrend.Steady;
        var gauge = (byte) (MathF.Round(raw * 20f) * 5f);
        var near = NearFire(xform);

        if (comp.Gauge == gauge && comp.Trend == trend && comp.NearWarmth == near)
            return false;

        comp.Gauge = gauge;
        comp.Trend = trend;
        comp.NearWarmth = near;
        return true;
    }
}
