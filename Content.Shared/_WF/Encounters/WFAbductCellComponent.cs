namespace Content.Shared._WF.Encounters;

/// <summary>The chair an abductor ship holds its prisoner on. A hull without one can't crew an abductor.</summary>
[RegisterComponent]
public sealed partial class WFAbductCellComponent : Component
{
    /// <summary>How far from the chair an airlock counts as the cell's door.</summary>
    [DataField]
    public float DoorRange = 2.5f;

    /// <summary>Who is held here, for as long as a ransom can be paid.</summary>
    [ViewVariables]
    public EntityUid? Prisoner;

    /// <summary>What the prisoner can pay to go free.</summary>
    [ViewVariables]
    public int Ransom;
}
