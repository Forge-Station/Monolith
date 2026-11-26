using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>Settings for how loud jukeboxes play on this client.</summary>
[CVarDefs]
public sealed class JukeboxVolumeCVars
{
    /// <summary>Gain multiplier applied to every jukebox song.</summary>
    public static readonly CVarDef<float> Volume =
        CVarDef.Create("wf.jukebox.volume", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
