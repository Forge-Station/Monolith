using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._WF.Xenonids.ManageHive;

/// <summary>RMC cvars the queen and king code reads that Wolfgate's RMCCVars copy lacks; names and defaults are RMC's.</summary>
[CVarDefs]
public sealed class WFXenoQueenCVars : CVars
{
    public static readonly CVarDef<int> RMCBurrowedLarvaSacrificeTimeMinutes =
        CVarDef.Create("rmc.burrowed_larva_sacrifice_time_minutes", 15, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCBurrowedLarvaEvolutionPointsPer =
        CVarDef.Create("rmc.burrowed_larva_evolution_points_per", 250, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCRoyalResinEveryMinutes =
        CVarDef.Create("rmc.royal_resin_every_minutes", 5, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCKingVoteCandidateTimeRequirementHours =
        CVarDef.Create("rmc.king_vote_candidate_time_requirement", 50, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCKingHatchingFirstWarningMinutes =
        CVarDef.Create("rmc.king_hatching_first_warning_minutes", 5, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCKingVoteStartTimeSeconds =
        CVarDef.Create("rmc.king_vote_start_time_seconds", 60, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCKingVoteAskCandidatesTimeSeconds =
        CVarDef.Create("rmc.king_vote_ask_candidates_time_seconds", 40, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCKingVoteStartHatchingTimeSeconds =
        CVarDef.Create("rmc.king_vote_start_hatching_time_seconds", 20, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<bool> RMCXenoAbilityPreviews =
        CVarDef.Create("rmc.xeno_ability_previews", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
