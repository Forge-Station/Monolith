using Robust.Shared.Configuration;

namespace Content.Shared._WF.Camera;

/// <summary>Camera settings.</summary>
[CVarDefs]
public sealed class WFCameraCVars
{
    /// <summary>
    /// Turns the view to a grid's north on stepping onto it, instead of the nearest cardinal to the old view.
    /// Replicated so the server's copy of the eye rotation agrees with the client's.
    /// </summary>
    public static readonly CVarDef<bool> DefaultNorth =
        CVarDef.Create("wf.eye_default_north", false, CVar.ARCHIVE | CVar.CLIENT | CVar.REPLICATED);
}
