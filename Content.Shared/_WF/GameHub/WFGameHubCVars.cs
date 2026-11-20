using Robust.Shared.Configuration;

namespace Content.Shared._WF.GameHub;

/// <summary>Limits on game downloads, relayed sounds and prizes.</summary>
[CVarDefs]
public sealed class WFGameHubCVars
{
    /// <summary>Clients receiving game data at once; others queue. Downloads share the game connection on most servers.</summary>
    public static readonly CVarDef<int> MaxDownloads =
        CVarDef.Create("wf.gamehub.max_downloads", 4, CVar.SERVERONLY);

    /// <summary>Download speed per client, in KiB per second.</summary>
    public static readonly CVarDef<int> DownloadRate =
        CVarDef.Create("wf.gamehub.download_rate", 512, CVar.SERVERONLY);

    /// <summary>Sounds per second a game may relay to nearby players.</summary>
    public static readonly CVarDef<int> MaxNoisePerSecond =
        CVarDef.Create("wf.gamehub.max_noise_per_second", 8, CVar.SERVERONLY);

    /// <summary>Shortest time, in seconds, a finished level can count: both reported and since the last report.</summary>
    public static readonly CVarDef<int> MinLevelSeconds =
        CVarDef.Create("wf.gamehub.min_level_seconds", 20, CVar.SERVERONLY);

    /// <summary>Prizes a player can earn per round.</summary>
    public static readonly CVarDef<int> MaxPrizesPerRound =
        CVarDef.Create("wf.gamehub.max_prizes_per_round", 5, CVar.SERVERONLY);
}
