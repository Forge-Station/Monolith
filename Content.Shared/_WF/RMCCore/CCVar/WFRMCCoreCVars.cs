using Robust.Shared.Configuration;

namespace Content.Shared._WF.RMCCore.CCVar;

/// <summary>RMC-14 cvars that RMCCore reads and Frontier's RMCCVars copy lacks; names match RMC's.</summary>
[CVarDefs]
public sealed class WFRMCCoreCVars
{
    public static readonly CVarDef<bool> RMCDamageYourself =
        CVarDef.Create("rmc.damage_yourself", false, CVar.ARCHIVE | CVar.CLIENT | CVar.REPLICATED);

    public static readonly CVarDef<float> RMCEmoteCooldownSeconds =
        CVarDef.Create("rmc.emote_cooldown_seconds", 20f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float> RMCLagCompensationMarginTiles =
        CVarDef.Create("rmc.lag_compensation_margin_tiles", 0.25f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> RMCLagCompensationMilliseconds =
        CVarDef.Create("rmc.lag_compensation_milliseconds", 750, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<bool> RMCUseAlternateSprites =
        CVarDef.Create("rmc.use_alternate_sprites", false, CVar.REPLICATED | CVar.CLIENT | CVar.ARCHIVE);
}
