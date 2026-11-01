using Robust.Shared.Configuration;

namespace Content.Shared._WF.RoundSetup;

/// <summary>Explicit opt-in settings for a local Round Setup playtest.</summary>
[CVarDefs]
public sealed class WFRoundSetupCVars
{
    /// <summary>Creates one public crash lobby for the configured real player each round; disabled on normal servers.</summary>
    public static readonly CVarDef<bool> DemoEnabled =
        CVarDef.Create("wf.round_setup.demo_enabled", false, CVar.SERVERONLY);

    /// <summary>The connected player's exact name to use as the local playtest host.</summary>
    public static readonly CVarDef<string> DemoHost =
        CVarDef.Create("wf.round_setup.demo_host", "RoundSetupHost", CVar.SERVERONLY);

    /// <summary>Whether this player receives crew postings in chat and as cards.</summary>
    public static readonly CVarDef<bool> ShowPostings =
        CVarDef.Create("wf.round_setup.show_postings", true, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);
}
