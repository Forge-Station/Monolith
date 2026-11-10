namespace Content.Server._WF.Remade.Gifts;

/// <summary>Someone Flesh raised. When they die again they rise as one of the Remade at once.</summary>
[RegisterComponent]
public sealed partial class WFFleshMarkedComponent : Component
{
    /// <summary>The Scion that raised them. If it is gone, so is the mark.</summary>
    [ViewVariables]
    public EntityUid? Source;
}

/// <summary>A body in reach of Flesh, on its way back.</summary>
[RegisterComponent]
public sealed partial class WFFleshRevivingComponent : Component
{
    /// <summary>From 0 to 1; it comes back at 1.</summary>
    [ViewVariables]
    public float Progress;

    [ViewVariables]
    public TimeSpan LastSeen;

    [ViewVariables]
    public TimeSpan NextWrithe;

    /// <summary>Not tried again before this: Wolfmed refused the body.</summary>
    [ViewVariables]
    public TimeSpan RetryAt;
}
