using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Tuning for the shockwave explosions throw out: the screen distortion clients draw and the shove the server applies.
/// </summary>
[CVarDefs]
public sealed class ShockwaveCVars
{
    /// <summary>
    /// Whether explosions push a distortion ring out across the screen.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.shockwave.enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Multiplier on how far the ring displaces the world. 0 is flat, 1 is the tuned default.
    /// </summary>
    public static readonly CVarDef<float> Strength =
        CVarDef.Create("wf.shockwave.strength", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// How far past the edge of the blast the wave reaches, in tiles. Sets the reach of both the ring and the shove,
    /// so the two always agree.
    /// </summary>
    public static readonly CVarDef<float> Overshoot =
        CVarDef.Create("wf.shockwave.overshoot", 3f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Whether the wave shoves mobs, items and unanchored objects away from the epicentre.
    /// </summary>
    public static readonly CVarDef<bool> PushEnabled =
        CVarDef.Create("wf.shockwave.push_enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Throw speed at the epicentre per tile of reach, in metres per second. Scales the shove with the blast the same
    /// way the ring's displacement scales.
    /// </summary>
    public static readonly CVarDef<float> PushSpeedPerTile =
        CVarDef.Create("wf.shockwave.push_speed_per_tile", 1.5f, CVar.SERVERONLY);

    /// <summary>
    /// Ceiling on that epicentre speed, so a station-wide bomb doesn't fire everything into orbit.
    /// </summary>
    public static readonly CVarDef<float> PushMaxSpeed =
        CVarDef.Create("wf.shockwave.push_max_speed", 24f, CVar.SERVERONLY);
}
