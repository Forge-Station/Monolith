using Robust.Shared.Configuration;

namespace Content.Shared._WF.Headshot;

/// <summary>Server settings for character headshots.</summary>
[CVarDefs]
public sealed class HeadshotCVars
{
    /// <summary>Whether headshots are fetched and shown on examine. Replicated so the editor can hide its button.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.headshot.enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Comma-separated hosts headshots may be fetched from. A host also allows its subdomains. Empty allows any
    /// public host. Replicated so the editor can list them.
    /// </summary>
    public static readonly CVarDef<string> AllowedHosts =
        CVarDef.Create("wf.headshot.allowed_hosts", string.Empty, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Largest image download accepted, in kilobytes.</summary>
    public static readonly CVarDef<int> MaxDownloadKb =
        CVarDef.Create("wf.headshot.max_download_kb", 4096, CVar.SERVERONLY);
}
