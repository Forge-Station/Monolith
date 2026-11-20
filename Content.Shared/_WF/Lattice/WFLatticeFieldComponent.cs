using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Lattice;

/// <summary>On a sector map: the Lattice holds it, banded by its profile.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFLatticeFieldComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<WFLatticeProfilePrototype> Profile = "WFDefault";

    /// <summary>Inner edge of each band for the intact Lattice, in metres from the centre; the first is 0.</summary>
    [DataField, AutoNetworkedField]
    public List<float> Edges = new();

    /// <summary>Field strength at each band edge after the first, for the intact Lattice.</summary>
    [DataField, AutoNetworkedField]
    public List<float> Thresholds = new();

    /// <summary>Every anchor's world position on the map, so clients can work out the field too.</summary>
    [DataField, AutoNetworkedField]
    public List<Vector2> Anchors = new();

    /// <summary>Share of the primary anchors still standing, 0 to 1. Kyphrus looks worse as it falls.</summary>
    [DataField, AutoNetworkedField]
    public float Integrity = 1f;

    /// <summary>Each anchor's strength, matching <see cref="Anchors"/>.</summary>
    [DataField, AutoNetworkedField]
    public List<float> Strengths = new();
}
