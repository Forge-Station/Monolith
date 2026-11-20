using System.Numerics;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.AdsIncursion.Components;

/// <summary>An interdictor sent to challenge a cell another faction holds, on its encounter entity. Won, it becomes a picket.</summary>
[RegisterComponent]
public sealed partial class WFAdsInterdictorComponent : Component
{
    /// <summary>The cell it challenges.</summary>
    [ViewVariables]
    public WFSectorCell Target;

    /// <summary>The point it flies to: the cell's centre, or a clear point near it.</summary>
    [ViewVariables]
    public Vector2 TargetPosition;

    /// <summary>The faction holding the cell when it set out.</summary>
    [ViewVariables]
    public ProtoId<WFSectorFactionPrototype>? Holder;

    /// <summary>The node it set out from.</summary>
    [ViewVariables]
    public EntityUid Node;

    /// <summary>The key of its lead ship.</summary>
    [ViewVariables]
    public string Lead = string.Empty;

    [ViewVariables]
    public TimeSpan Launched;

    /// <summary>When it has taken too long short of its cell: it is ended and the challenge dropped.</summary>
    [ViewVariables]
    public TimeSpan Overdue;

    /// <summary>Whether it has reached its cell and holds there.</summary>
    [ViewVariables]
    public bool Arrived;
}
