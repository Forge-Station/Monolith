namespace Content.Server._WF.Remade.Sector;

/// <summary>
/// Game rule: while it runs, now and then a dormant Scion is placed somewhere in the sector for a crew to find. Times are
/// seconds.
/// </summary>
[RegisterComponent]
public sealed partial class WFRemadeDiscoveryRuleComponent : Component
{
    /// <summary>How long after the rule starts before the first roll.</summary>
    [DataField]
    public float EarliestStart = 3000f;

    /// <summary>The wait between rolls.</summary>
    [DataField]
    public float CheckMin = 600f;

    [DataField]
    public float CheckMax = 1200f;

    /// <summary>Chance a roll places a Scion.</summary>
    [DataField]
    public float Chance = 0.3f;

    /// <summary>Shortest gap between one wild Scion being placed and the next.</summary>
    [DataField]
    public float MinGap = 2700f;

    /// <summary>How long after a Scion is deleted or sold before another is placed.</summary>
    [DataField]
    public float GoneCooldown = 1800f;

    [ViewVariables]
    public TimeSpan Next;
}
