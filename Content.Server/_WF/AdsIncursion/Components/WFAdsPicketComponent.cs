using Content.Shared._WF.SectorControl;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.AdsIncursion.Components;

/// <summary>An interdictor that won its challenge, on its encounter entity: it holds the cell it took, which frees when it dies.</summary>
[RegisterComponent]
public sealed partial class WFAdsPicketComponent : Component
{
    /// <summary>The cell it holds.</summary>
    [ViewVariables]
    public WFSectorCell Cell;

    /// <summary>The key of its lead ship.</summary>
    [ViewVariables]
    public string Lead = string.Empty;

    /// <summary>The faction it took the cell from.</summary>
    [ViewVariables]
    public ProtoId<WFSectorFactionPrototype>? Holder;
}
