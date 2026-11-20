using Robust.Shared.Configuration;

namespace Content.Shared._WF.Warmth;

/// <summary>Tuning for body warmth: warning signs, wind chill and wet clothes.</summary>
[CVarDefs]
public sealed class WFWarmthCVars
{
    /// <summary>Share of the way from normal body temperature to the line where harm starts at which warnings begin.</summary>
    public static readonly CVarDef<float> WarningShare =
        CVarDef.Create("wf.warmth.warning_share", 0.5f, CVar.SERVERONLY);

    /// <summary>Extra heat lost on an open tile per tile a second of wind speed, as a share of the still-air loss.</summary>
    public static readonly CVarDef<float> WindChillPerSpeed =
        CVarDef.Create("wf.warmth.wind_chill_per_speed", 0.13f, CVar.SERVERONLY);

    /// <summary>The most the wind can multiply the heat a body loses.</summary>
    public static readonly CVarDef<float> WindChillMax =
        CVarDef.Create("wf.warmth.wind_chill_max", 2f, CVar.SERVERONLY);

    /// <summary>Extra heat lost when drenched, as a share of the dry loss; scales with wetness.</summary>
    public static readonly CVarDef<float> WetCooling =
        CVarDef.Create("wf.warmth.wet_cooling", 0.5f, CVar.SERVERONLY);

    /// <summary>Seconds for a drenched body to dry out in the open with no rain.</summary>
    public static readonly CVarDef<float> DrySeconds =
        CVarDef.Create("wf.warmth.dry_seconds", 600f, CVar.SERVERONLY);

    /// <summary>Movement speed multiplier while the cold's warning signs show.</summary>
    public static readonly CVarDef<float> ColdSlowdown =
        CVarDef.Create("wf.warmth.cold_slowdown", 0.9f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Movement speed multiplier while the heat's warning signs show.</summary>
    public static readonly CVarDef<float> HeatSlowdown =
        CVarDef.Create("wf.warmth.heat_slowdown", 0.95f, CVar.SERVER | CVar.REPLICATED);
}
