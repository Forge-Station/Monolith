namespace Content.Server._WF.LivingWorld;

/// <summary>On a mob held in place by a Grab plant.</summary>
[RegisterComponent]
public sealed partial class WFFloraGrabbedComponent : Component
{
    [ViewVariables]
    public EntityUid Plant;

    /// <summary>Movement speed multiplier while held.</summary>
    [ViewVariables]
    public float Speed;

    [ViewVariables]
    public TimeSpan Since;

    /// <summary>When the plant lets go; each struggle brings it closer.</summary>
    [ViewVariables]
    public TimeSpan Until;

    [ViewVariables]
    public TimeSpan NextStruggle;
}
