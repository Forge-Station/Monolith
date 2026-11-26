using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._WF.Xenonids.Evolution;

/// <summary>Growth cvars (evolution points, larva queue) that Wolfgate's RMCCVars copy lacks; names and defaults are RMC's.</summary>
[CVarDefs]
public sealed class WFGrowthCVars : CVars
{
    public static readonly CVarDef<int> RMCEvolutionPointsRequireOvipositorMinutes =
        CVarDef.Create("rmc.evolution_points_require_ovipositor_minutes", 5, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCEvolutionPointsAccumulateBeforeMinutes =
        CVarDef.Create("rmc.evolution_points_accumulate_before_minutes", 15, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCXenoEvolveSameCasteCooldownSeconds =
        CVarDef.Create("rmc.xeno_evolve_same_caste_cooldown_seconds", 300, CVar.ARCHIVE | CVar.CLIENT | CVar.REPLICATED);

    public static readonly CVarDef<int> RMCXenoEarlyEvoPointBoostBeforeMinutes =
        CVarDef.Create("rmc.evolution_early_evo_point_boost_minutes", 15, CVar.REPLICATED | CVar.SERVER);

    // RMC makes a ghost wait 2.5 minutes after dying before it can take a larva; here there is no wait.
    public static readonly CVarDef<float> RMCLateJoinsBurrowedLarvaDeathTime =
        CVarDef.Create("rmc.late_joins_burrowed_larva_death_time", 0f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<float> RMCLateJoinsBurrowedLarvaDeathTimeIgnoreBeforeMinutes =
        CVarDef.Create("rmc.late_joins_burrowed_larva_death_time_ignore_before_minutes", 2.5f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCDisconnectedXenoGhostRoleTimeSeconds =
        CVarDef.Create("rmc.disconnected_xeno_ghost_role_time_seconds", 300, CVar.REPLICATED | CVar.SERVER);

    // WOLFGATE: RMC delays ghost larva joins 15 minutes so marines land first; there is no landing here, so no delay.
    public static readonly CVarDef<int> RMCLarvaQueueRoundstartDelaySeconds =
        CVarDef.Create("rmc.larva_queue_roundstart_delay_seconds", 0, CVar.REPLICATED | CVar.SERVER);

    // RMC keeps a freshly dead ghost out of the larva queue for 300 seconds; here there is no wait.
    public static readonly CVarDef<int> RMCLarvaQueueWaitSeconds =
        CVarDef.Create("rmc.larva_queue_wait_seconds", 0, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCLarvaQueueOfferTimeoutSeconds =
        CVarDef.Create("rmc.larva_queue_offer_timeout_seconds", 30, CVar.REPLICATED | CVar.SERVER);
}
