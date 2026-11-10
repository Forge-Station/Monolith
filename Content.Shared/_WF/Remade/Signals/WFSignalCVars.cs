using Robust.Shared.Configuration;

namespace Content.Shared._WF.Remade.Signals;

/// <summary>Settings for the Signals.</summary>
[CVarDefs]
public sealed class WFSignalCVars
{
    /// <summary>Whether ghosts can join a Monolith as a Signal.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.remade.signals.enabled", true, CVar.SERVERONLY);
}
