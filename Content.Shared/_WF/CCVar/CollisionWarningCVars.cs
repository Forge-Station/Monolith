using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Tuning for the ship collision warning (TCAS).
/// </summary>
[CVarDefs]
public sealed class CollisionWarningCVars
{
    /// <summary>
    /// Whether ships predict collisions and warn their crew at all.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.tcas.enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// How far ahead contact is predicted, in seconds. Nothing further out than this warns.
    /// </summary>
    public static readonly CVarDef<float> Lookahead =
        CVarDef.Create("wf.tcas.lookahead", 15f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds to contact at which the warning escalates from advisory to imminent.
    /// </summary>
    public static readonly CVarDef<float> ImminentTime =
        CVarDef.Create("wf.tcas.imminent_time", 5f, CVar.SERVERONLY);

    /// <summary>
    /// Floor on closing speed, under which nothing warns however heavy the two ships are. Whether a
    /// faster approach warns is decided by the impact system's own thresholds
    /// (shuttle.impact.minimum_velocity and shuttle.impact.minimum_inertia), so a warning always means
    /// a hit that system would act on. This only stops a heavy hull creeping onto a station from
    /// tripping the inertia half of that test.
    /// </summary>
    public static readonly CVarDef<float> MinimumClosingSpeed =
        CVarDef.Create("wf.tcas.minimum_closing_speed", 20f, CVar.SERVERONLY);

    /// <summary>
    /// How much hull a hit has to be predicted to carve out before it is worth warning about, as the
    /// radius in tiles the impact system would damage. Under 1 is a scrape, 4 is the widest that system
    /// will damage at once, so raising this towards 4 leaves only the serious hits. This is the knob to
    /// turn if the warning is being obtrusive.
    /// </summary>
    public static readonly CVarDef<float> DangerRadius =
        CVarDef.Create("wf.tcas.danger_radius", 3f, CVar.SERVERONLY);

    /// <summary>
    /// Extra metres allowed around the pair of hulls when predicting contact, so the warning arrives
    /// before the paint touches.
    /// </summary>
    public static readonly CVarDef<float> Margin =
        CVarDef.Create("wf.tcas.margin", 4f, CVar.SERVERONLY);

    /// <summary>
    /// Speed a threat is assumed capable of when deciding how far out to look for traffic. Raising it
    /// catches faster incoming ships at the cost of a wider search per ship.
    /// </summary>
    public static readonly CVarDef<float> ThreatSpeedAllowance =
        CVarDef.Create("wf.tcas.threat_speed_allowance", 50f, CVar.SERVERONLY);

    /// <summary>
    /// How long the banner is held after the threat clears, so it cannot strobe while a ship yaws. The
    /// alarms stop as soon as the threat does, hold or no hold.
    /// </summary>
    public static readonly CVarDef<float> Hysteresis =
        CVarDef.Create("wf.tcas.hysteresis", 2f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds between spoken traffic callouts while the warning is only an advisory. The imminent
    /// stage runs its callout continuously instead.
    /// </summary>
    public static readonly CVarDef<float> AdvisoryCalloutInterval =
        CVarDef.Create("wf.tcas.advisory_callout_interval", 1.5f, CVar.SERVERONLY);

    /// <summary>
    /// How often ships are checked, in seconds.
    /// </summary>
    public static readonly CVarDef<float> UpdateInterval =
        CVarDef.Create("wf.tcas.update_interval", 0.25f, CVar.SERVERONLY);
}
