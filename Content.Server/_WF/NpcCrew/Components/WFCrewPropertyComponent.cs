namespace Content.Server._WF.NpcCrew.Components;

/// <summary>An item that belongs to an NPC crew's ship: a stranger seen picking it up by its fighters is a thief.</summary>
[RegisterComponent]
public sealed partial class WFCrewPropertyComponent : Component
{
    /// <summary>The ship it belongs to.</summary>
    [DataField]
    public EntityUid Grid;

    /// <summary>The crew that owns it.</summary>
    [DataField]
    public string Group = string.Empty;
}
