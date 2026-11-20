using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>Settings for NPC crew.</summary>
[CVarDefs]
public sealed class NpcCrewCVars
{
    /// <summary>Logs crew, verb and ghost menu request delivery while reproducing missing menu contents.</summary>
    public static readonly CVarDef<bool> UiDiagnostics =
        CVarDef.Create("wf.crew.ui_diagnostics", false, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Whether a crew pilot that runs out of docking attempts jumps its ship straight onto the port instead of holding
    /// off it.
    /// </summary>
    public static readonly CVarDef<bool> DockFtlFallback =
        CVarDef.Create("wf.crew.dock_ftl_fallback", false, CVar.SERVERONLY);

    /// <summary>
    /// Whether crew with nothing to do (at their station, no alert, no work, safe air) sleep while no player is near.
    /// Off keeps every crewman thinking all the time.
    /// </summary>
    public static readonly CVarDef<bool> SleepIdle =
        CVarDef.Create("wf.crew.sleep_idle", true, CVar.SERVERONLY);
}
