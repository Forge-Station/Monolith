// WOLFGATE(Wolfmed): ported from Onyx for Wolfmed.
using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<bool> TargetingEnabled =
        CVarDef.Create("targeting.enabled", true, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<bool> TargetingUseAnatomicalOdds =
        CVarDef.Create("targeting.use_anatomical_odds", true, CVar.SERVER | CVar.ARCHIVE);

    public static readonly CVarDef<bool> TargetingDownedTargetsAreExact =
        CVarDef.Create("targeting.downed_targets_are_exact", true, CVar.SERVER | CVar.ARCHIVE);
}
