using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>Whether nationalities from the contractor passports are applied on spawn.</summary>
    public static readonly CVarDef<bool> ContractorsEnabled =
        CVarDef.Create("contractors.enabled", true, CVar.SERVER | CVar.REPLICATED);
}
