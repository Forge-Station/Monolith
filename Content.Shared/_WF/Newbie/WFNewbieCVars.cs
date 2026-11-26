using Robust.Shared.Configuration;

namespace Content.Shared._WF.Newbie;

/// <summary>
/// Tuning for the newbie tag.
/// </summary>
[CVarDefs]
public sealed class WFNewbieCVars
{
    /// <summary>
    /// Total playtime in hours under which a player is tagged as new. 0 turns the tag off for everyone.
    /// </summary>
    public static readonly CVarDef<float> Hours =
        CVarDef.Create("wf.newbie.hours", 8f, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// Whether this player shows their own newbie tag. Turning it off also drops the newbie rules' protection.
    /// </summary>
    public static readonly CVarDef<bool> ShowTag =
        CVarDef.Create("wf.newbie.show_tag", true, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// Total playtime in hours under which a joining player is also told to find a crew first.
    /// </summary>
    public static readonly CVarDef<float> FreshHours =
        CVarDef.Create("wf.newbie.fresh_hours", 1f, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Whether new players get the welcome popups when they join.
    /// </summary>
    public static readonly CVarDef<bool> Welcome =
        CVarDef.Create("wf.newbie.welcome", true, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Whether this player still wants the welcome popup. The crew popup ignores it.
    /// </summary>
    public static readonly CVarDef<bool> ShowWelcome =
        CVarDef.Create("wf.newbie.show_welcome", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
