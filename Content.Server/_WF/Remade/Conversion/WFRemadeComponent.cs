namespace Content.Server._WF.Remade.Conversion;

/// <summary>Marks one of the Remade and names the Monolith it answers to.</summary>
[RegisterComponent]
public sealed partial class WFRemadeComponent : Component
{
    /// <summary>The Monolith it answers to. Without one it starves, unless it never had one.</summary>
    [ViewVariables]
    public EntityUid? Source;
}

/// <summary>A corpse lying on corruption, waiting to turn.</summary>
[RegisterComponent]
public sealed partial class WFRemadeIncubatingComponent : Component
{
    [ViewVariables]
    public TimeSpan TurnsAt;

    [ViewVariables]
    public EntityUid? Source;
}

/// <summary>A corpse in the throes of turning.</summary>
[RegisterComponent]
public sealed partial class WFRemadeConvertingComponent : Component
{
    [ViewVariables]
    public TimeSpan EndsAt;

    [ViewVariables]
    public TimeSpan NextPulse;

    [ViewVariables]
    public EntityUid? Source;

    /// <summary>The Infector doing this, if one is. It must stay alive and beside the corpse.</summary>
    [ViewVariables]
    public EntityUid? Reanimator;
}
