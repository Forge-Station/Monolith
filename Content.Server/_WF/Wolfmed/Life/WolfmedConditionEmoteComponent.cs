namespace Content.Server._WF.Wolfmed.Life;

/// <summary>
/// Playtest 4 (SEPSIS): on a body once it has played a condition emote. Holds the vomit clock and what it last did,
/// so the same emote does not play twice running while something else is wrong. Tests and admins read the counts.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedConditionEmoteComponent : Component
{
    /// <summary>The last condition emote played, or null.</summary>
    [ViewVariables]
    public string? LastEmote;

    /// <summary>How many condition emotes the body has played.</summary>
    [ViewVariables]
    public int EmoteCount;

    /// <summary>How many times a retch has brought something up.</summary>
    [ViewVariables]
    public int VomitCount;

    /// <summary>The earliest a retch may bring something up again.</summary>
    [ViewVariables]
    public TimeSpan NextVomit;
}
