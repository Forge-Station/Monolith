namespace Content.Server._WF.Lattice;

/// <summary>A grounding drug keeps a share of the Drift a person feels off them until it wears off.</summary>
[RegisterComponent]
public sealed partial class WFGroundedComponent : Component
{
    [ViewVariables]
    public TimeSpan Until;

    [ViewVariables]
    public float Share;
}
