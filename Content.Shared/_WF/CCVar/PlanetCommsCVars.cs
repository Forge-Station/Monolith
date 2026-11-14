using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Settings for planetary comms, which ride a world's mass transceivers.
/// </summary>
[CVarDefs]
public sealed class PlanetCommsCVars
{
    /// <summary>
    /// Whether a world with no powered, working mass transceiver is cut off: headset radio and announcements stop at
    /// its edge. Off, nothing is gated.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.planet_comms", true, CVar.SERVERONLY);
}
