using Robust.Shared.Configuration;

namespace Content.Shared._WF.Defcon;

/// <summary>Settings for the sector DEFCON.</summary>
[CVarDefs]
public sealed class DefconCVars
{
    /// <summary>Whether the sector DEFCON follows the threat.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.defcon.enabled", true, CVar.SERVERONLY);

    /// <summary>The <see cref="WFDefconProfilePrototype"/> in force.</summary>
    public static readonly CVarDef<string> Profile =
        CVarDef.Create("wf.defcon.profile", "WFDefault", CVar.SERVERONLY);
}
