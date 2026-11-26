using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._WF.Xenonids.Construction;

/// <summary>RMC's resin construction cvars that Wolfgate's RMCCVars copy lacks; names and defaults are RMC's.</summary>
[CVarDefs]
public sealed class WFXenoConstructionCVars : CVars
{
    public static readonly CVarDef<int> RMCNewResinPreventCollideTimeSeconds =
        CVarDef.Create("rmc.new_resin_prevent_collide_time_seconds", 5, CVar.REPLICATED | CVar.SERVER);
}
