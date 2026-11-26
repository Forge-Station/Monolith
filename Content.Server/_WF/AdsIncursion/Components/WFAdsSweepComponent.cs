using System.Numerics;

namespace Content.Server._WF.AdsIncursion.Components;

/// <summary>An ADS sweep, on its encounter entity: the node that sent it, the route it flies and the ship it interdicts.</summary>
[RegisterComponent]
public sealed partial class WFAdsSweepComponent : Component
{
    [ViewVariables]
    public EntityUid Node;

    /// <summary>The key of its lead ship.</summary>
    [ViewVariables]
    public string Lead = string.Empty;

    /// <summary>The cell centres it calls at, then home.</summary>
    [ViewVariables]
    public List<Vector2> Route = new();

    [ViewVariables]
    public Vector2 Home;

    /// <summary>Where its lead was last seen, for the cell it died in.</summary>
    [ViewVariables]
    public Vector2 LastSeen;

    /// <summary>The player ship it was sent to interdict, if any.</summary>
    [ViewVariables]
    public EntityUid? Interdicting;

    /// <summary>When it was sent to interdict.</summary>
    [ViewVariables]
    public TimeSpan InterdictSince;

    /// <summary>The route points it had still to call at when it was sent, flown after the interdiction.</summary>
    [ViewVariables]
    public List<Vector2> Resume = new();

    /// <summary>Since when the interdicted ship has been out of held space.</summary>
    [ViewVariables]
    public TimeSpan? TargetOutsideSince;

    /// <summary>Since when the interdicted ship has been more than three cells off.</summary>
    [ViewVariables]
    public TimeSpan? TargetFarSince;

    /// <summary>Since when its lead has been outside held space and the ring round it.</summary>
    [ViewVariables]
    public TimeSpan? OutsideSince;
}
