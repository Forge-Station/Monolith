using Robust.Shared.Configuration;

namespace Content.Server._WF.Remade.Garrison;

/// <summary>Remade settings for what the stone keeps round itself.</summary>
[CVarDefs]
public sealed class WFGarrisonCVars
{
    /// <summary>Whether a Monolith keeps a standing guard. Off, it raises none and the existing ones stay where they are.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("remade.garrison_enabled", true, CVar.SERVERONLY);
}
