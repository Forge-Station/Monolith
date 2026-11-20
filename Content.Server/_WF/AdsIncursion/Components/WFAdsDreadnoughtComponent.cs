namespace Content.Server._WF.AdsIncursion.Components;

/// <summary>The tier 5 dreadnought, on its encounter entity: it hunts the Reavers' strongholds one by one and holds by a node with none standing.</summary>
[RegisterComponent]
public sealed partial class WFAdsDreadnoughtComponent : Component
{
    /// <summary>The node it was activated beside.</summary>
    [ViewVariables]
    public EntityUid? Source;

    /// <summary>The key of its lead ship.</summary>
    [ViewVariables]
    public string Lead = string.Empty;

    /// <summary>The Reaver stronghold it is sent against, or null while it holds by a node.</summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>The node it holds by while no stronghold stands.</summary>
    [ViewVariables]
    public EntityUid? Station;

    [ViewVariables]
    public TimeSpan Launched;

    /// <summary>When an idle dreadnought next looks for a stronghold.</summary>
    [ViewVariables]
    public TimeSpan NextLook;
}
