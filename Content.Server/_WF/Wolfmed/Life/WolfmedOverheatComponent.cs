namespace Content.Server._WF.Wolfmed.Life;

/// <summary>
/// Bookkeeping for a wound host cooking past its overheat threshold. Added by
/// <see cref="WolfmedOverheatSystem"/> the first time it burns a body, and tuned from a prototype when a
/// chassis should cook faster or slower than the default.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedOverheatComponent : Component
{
    /// <summary>Heat damage poured into the body on each burn pulse.</summary>
    [DataField]
    public float HeatPerPulse = 20f;

    /// <summary>When the next burn pulse is due.</summary>
    [ViewVariables]
    public TimeSpan NextPulse;

    /// <summary>When the overheating popup may show again within the same episode.</summary>
    [ViewVariables]
    public TimeSpan NextWarning;
}
