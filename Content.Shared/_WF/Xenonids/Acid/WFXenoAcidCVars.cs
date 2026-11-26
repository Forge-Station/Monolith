using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._WF.Xenonids.Acid;

/// <summary>RMC's corrosive acid cvars, which Wolfgate's RMCCVars copy lacks; names and defaults are RMC's.</summary>
[CVarDefs]
public sealed class WFXenoAcidCVars : CVars
{
    public static readonly CVarDef<int> RMCCorrosiveAcidTickDelaySeconds =
        CVarDef.Create("rmc.corrosive_acid_tick_delay_seconds", 10, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<string> RMCCorrosiveAcidDamageType =
        CVarDef.Create("rmc.corrosive_acid_damage_type", "Heat", CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCCorrosiveAcidDamageTimeSeconds =
        CVarDef.Create("rmc.corrosive_acid_damage_time_seconds", 40, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<bool> RMCCorrosiveAcidInstant =
        CVarDef.Create("rmc.corrosive_acid_instant", false, CVar.REPLICATED | CVar.SERVER);
}
