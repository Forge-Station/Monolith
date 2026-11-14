using Robust.Shared.Configuration;

namespace Content.Shared._WF.AdsIncursion;

/// <summary>Settings for the ADS incursion campaign.</summary>
[CVarDefs]
public sealed class AdsIncursionCVars
{
    /// <summary>Admin override: forces the campaign on whether or not its rule runs. It also needs the storyteller and sector control on.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.ads.enabled", false, CVar.SERVERONLY);
}
