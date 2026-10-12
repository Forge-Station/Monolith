using System.Numerics;

namespace Content.Server._WF.ReaverCampaign.Components;

/// <summary>A Reaver patrol, on its encounter entity: the stronghold that sent it and the route it flies.</summary>
[RegisterComponent]
public sealed partial class WFReaverPatrolComponent : Component
{
    [ViewVariables]
    public EntityUid Stronghold;

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

    /// <summary>Whether it was called back to defend its stronghold.</summary>
    [ViewVariables]
    public bool Recalled;

    /// <summary>The player ship it was sent to intercept, if any.</summary>
    [ViewVariables]
    public EntityUid? Intercepting;

    /// <summary>When it was sent to intercept.</summary>
    [ViewVariables]
    public TimeSpan InterceptSince;

    /// <summary>The route points it had still to call at when it was sent, flown after the intercept.</summary>
    [ViewVariables]
    public List<Vector2> Resume = new();

    /// <summary>Since when the intercepted ship has been out of held space.</summary>
    [ViewVariables]
    public TimeSpan? TargetOutsideSince;

    /// <summary>Since when the intercepted ship has been more than three cells off.</summary>
    [ViewVariables]
    public TimeSpan? TargetFarSince;

    /// <summary>Whether it was sent out from a stronghold under attack, to hunt the ship that hit it and fly home.</summary>
    [ViewVariables]
    public bool Sortie;

    /// <summary>Since when its lead has been outside held space and the ring round it; it is called off its chase after a few seconds.</summary>
    [ViewVariables]
    public TimeSpan? OutsideSince;
}
