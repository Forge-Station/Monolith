using Robust.Shared.GameStates;

namespace Content.Shared._WF.Remade;

/// <summary>A console that draws on the nearest Monolith and takes corpses to quiet it.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFMonolithTapComponent : Component
{
    /// <summary>How far the Tap reaches for a Monolith, in metres.</summary>
    [DataField]
    public float Range = 4f;

    /// <summary>Seconds it takes to feed a body to the Tap.</summary>
    [DataField]
    public float FeedTime = 5f;
}
