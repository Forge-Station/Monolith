using Robust.Shared.Configuration;

namespace Content.Shared._WF.ExamineCard;

/// <summary>Server settings for the character examine card.</summary>
[CVarDefs]
public sealed class ExamineCardCVars
{
    /// <summary>Whether examining a character shows the ID card instead of the old worn-item lines.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.examine_card.enabled", true, CVar.SERVERONLY);

    /// <summary>Whether a character is told in chat when someone examines them.</summary>
    public static readonly CVarDef<bool> Notice =
        CVarDef.Create("wf.examine_card.notice", true, CVar.SERVERONLY);
}
