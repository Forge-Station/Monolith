using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    // Forge-Change-start
    /// <summary>
    /// Maximum number of character profile update requests a client may send during the profile update rate limit period.
    /// </summary>
    public static readonly CVarDef<int> ProfileUpdateRateLimitCount =
        CVarDef.Create("preferences.profile_update_rate_limit_count", 4, CVar.SERVERONLY);

    /// <summary>
    /// Time window in seconds used to track character profile update requests.
    /// </summary>
    public static readonly CVarDef<float> ProfileUpdateRateLimitPeriod =
        CVarDef.Create("preferences.profile_update_rate_limit_period", 5f, CVar.SERVERONLY);

    /// <summary>
    /// Minimum delay between admin alerts for profile update spam.
    /// </summary>
    public static readonly CVarDef<int> ProfileUpdateRateLimitAnnounceAdminsDelay =
        CVarDef.Create("preferences.profile_update_rate_limit_announce_admins_delay", 120, CVar.SERVERONLY);

    /// <summary>
    /// Whether repeated profile update spam should disconnect the client.
    /// </summary>
    public static readonly CVarDef<bool> ProfileUpdateRateLimitDisconnect =
        CVarDef.Create("preferences.profile_update_rate_limit_disconnect", true, CVar.SERVERONLY);
    // Forge-Change-end
}
