using System.Numerics;
using Content.Shared._WF.SectorControl;

namespace Content.Server._WF.ReaverCampaign.Components;

/// <summary>A Reaver response pack, on its encounter entity: sent to drive off a faction challenging a Reaver cell, then home.</summary>
[RegisterComponent]
public sealed partial class WFReaverResponseComponent : Component
{
    /// <summary>The challenged cell it defends.</summary>
    [ViewVariables]
    public WFSectorCell Cell;

    /// <summary>The challenger's encounter it was sent against, if the challenge had one.</summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>The stronghold that sent it.</summary>
    [ViewVariables]
    public EntityUid Stronghold;

    /// <summary>The key of its lead ship.</summary>
    [ViewVariables]
    public string Lead = string.Empty;

    /// <summary>Where it set out from, which it flies back to.</summary>
    [ViewVariables]
    public Vector2 Home;

    /// <summary>Whether it has been sent home, its fight over.</summary>
    [ViewVariables]
    public bool Returning;
}
