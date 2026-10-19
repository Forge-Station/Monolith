using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._WF.Xenonids;

/// <summary>RMC cvars the xeno port reads that Wolfgate's RMCCVars copy lacks; names and defaults are RMC's.</summary>
[CVarDefs]
public sealed class WFXenoCVars : CVars
{
    public static readonly CVarDef<float> CMXenoDamageDealtMultiplier =
        CVarDef.Create("rmc.xeno_damage_dealt_multiplier", 1f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<float> CMXenoDamageReceivedMultiplier =
        CVarDef.Create("rmc.xeno_damage_received_multiplier", 1f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<float> CMXenoSpeedMultiplier =
        CVarDef.Create("rmc.xeno_speed_multiplier", 1f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<bool> RMCHiveLeaderTackleXenos =
        CVarDef.Create("rmc.hive_leader_tackle_xenos", true, CVar.CLIENT | CVar.ARCHIVE | CVar.REPLICATED);

    public static readonly CVarDef<float> RMCLateJoinsPerBurrowedLarva =
        CVarDef.Create("rmc.late_joins_per_burrowed_larva", 7f, CVar.SERVER | CVar.SERVERONLY);

    public static readonly CVarDef<float> RMCLateJoinsPerBurrowedLarvaEarly =
        CVarDef.Create("rmc.late_joins_per_burrowed_larva_early", 7.5f, CVar.SERVER | CVar.SERVERONLY);

    public static readonly CVarDef<float> RMCLateJoinsPerBurrowedLarvaEarlyThresholdMinutes =
        CVarDef.Create("rmc.late_joins_per_burrowed_larva_early_threshold_minutes", 15f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCPlaytimeXenoPostfixTimeHours =
        CVarDef.Create("rmc.playtime_xeno_postfix_time_hours", 24, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCPlaytimeXenoPostfixTwoTimeHours =
        CVarDef.Create("rmc.playtime_xeno_postfix_two_time_hours", 300, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCPlaytimeXenoPrefixThreeTimeHours =
        CVarDef.Create("rmc.playtime_xeno_prefix_three_time_hours", 124, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<float> RMCWeedSpreadMaxProcessTimeMilliseconds =
        CVarDef.Create("rmc.weed_spread_max_process_time_milliseconds", 1f, CVar.REPLICATED | CVar.SERVER);

    // RMC mutes a fresh lesser drone or parasite for 180 seconds; players here wait for nothing, so it defaults to off.
    public static readonly CVarDef<float> RMCXenoSpawnInitialMuteDurationSeconds =
        CVarDef.Create("rmc.xeno_spawn_initial_mute_duration_seconds", 0f, CVar.REPLICATED | CVar.SERVER);
}
