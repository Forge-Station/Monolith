// WOLFGATE(Wolfmed): ported from Onyx for Wolfmed.
using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<float> SurgeryScarChance =
        CVarDef.Create("surgery.scar_chance", 0.35f, CVar.SERVER | CVar.ARCHIVE);

    public static readonly CVarDef<bool> SurgerySelfEnabled =
        CVarDef.Create("surgery.self_enabled", false, CVar.SERVER | CVar.ARCHIVE);

    public static readonly CVarDef<float> SurgerySelfMultiplier =
        CVarDef.Create("surgery.self_multiplier", 3f, CVar.SERVER | CVar.ARCHIVE | CVar.REPLICATED);
}
