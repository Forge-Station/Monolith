using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Tuning for the heat haze clients draw over tiles with hot air.
/// </summary>
[CVarDefs]
public sealed class HeatHazeCVars
{
    /// <summary>
    /// Whether hot air shimmers the world seen through it. The haze is also off while reduced motion is on.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.heat_haze.enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Multiplier on how far the haze displaces the world. 0 is flat, 1 is the tuned default.
    /// </summary>
    public static readonly CVarDef<float> Strength =
        CVarDef.Create("wf.heat_haze.strength", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
