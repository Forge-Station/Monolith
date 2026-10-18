namespace Content.Server._WF.AdsIncursion.Components;

/// <summary>Where a sentinel is in its service to a node.</summary>
public enum WFAdsSentinelPhase : byte
{
    /// <summary>Flying in from the sector edge to join its node.</summary>
    Inbound,

    /// <summary>Joined, circling its node.</summary>
    Circling,

    /// <summary>Broken away to attack a ship that hit its node.</summary>
    Attacking,
}

/// <summary>A sentinel gunship, on its encounter entity: it joins a node, circles it and turns on whoever hits it.</summary>
[RegisterComponent]
public sealed partial class WFAdsSentinelComponent : Component
{
    /// <summary>The node it serves.</summary>
    [ViewVariables]
    public EntityUid Node;

    /// <summary>The key of its lead ship.</summary>
    [ViewVariables]
    public string Lead = string.Empty;

    [ViewVariables]
    public WFAdsSentinelPhase Phase;

    /// <summary>The ship it was sent against while attacking.</summary>
    [ViewVariables]
    public EntityUid? Target;

    [ViewVariables]
    public TimeSpan Launched;

    /// <summary>When an inbound sentinel that never reached its node is ended.</summary>
    [ViewVariables]
    public TimeSpan Overdue;

    /// <summary>Since when its lead has been outside held space and the ring round it while attacking.</summary>
    [ViewVariables]
    public TimeSpan? OutsideSince;
}
