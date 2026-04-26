using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     The time you must spend reading the rules, before the "Request" button is enabled
    /// </summary>
    public static readonly CVarDef<float> GhostRoleTime =
        CVarDef.Create("ghost.role_time", 3f, CVar.REPLICATED | CVar.SERVER);

    /// <summary>
    ///     If ghost role lotteries should be made near-instanteous.
    /// </summary>
    public static readonly CVarDef<bool> GhostQuickLottery =
        CVarDef.Create("ghost.quick_lottery", false, CVar.SERVERONLY);

    /// <summary>
    ///     Whether or not to kill the player's mob on ghosting, when it is in a critical health state.
    /// </summary>
    public static readonly CVarDef<bool> GhostKillCrit =
        CVarDef.Create("ghost.kill_crit", true, CVar.REPLICATED | CVar.SERVER);

    // Forge-Change-start
    /// <summary>
    /// Maximum number of invalid ghost-only requests a client may send during the ghost invalid request period.
    /// </summary>
    public static readonly CVarDef<int> GhostInvalidRequestRateLimitCount =
        CVarDef.Create("ghost.invalid_request_rate_limit_count", 4, CVar.SERVERONLY);

    /// <summary>
    /// Time window in seconds used to track invalid ghost-only requests.
    /// </summary>
    public static readonly CVarDef<float> GhostInvalidRequestRateLimitPeriod =
        CVarDef.Create("ghost.invalid_request_rate_limit_period", 10f, CVar.SERVERONLY);

    /// <summary>
    /// Minimum delay between admin alerts for invalid ghost requests.
    /// </summary>
    public static readonly CVarDef<int> GhostInvalidRequestRateLimitAnnounceAdminsDelay =
        CVarDef.Create("ghost.invalid_request_rate_limit_announce_admins_delay", 120, CVar.SERVERONLY);

    /// <summary>
    /// Whether repeated invalid ghost-only requests should disconnect the player.
    /// </summary>
    public static readonly CVarDef<bool> GhostInvalidRequestRateLimitDisconnect =
        CVarDef.Create("ghost.invalid_request_rate_limit_disconnect", true, CVar.SERVERONLY);
    // Forge-Change-end
}
