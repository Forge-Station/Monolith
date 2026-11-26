namespace Content.Server._WF.Contracts;

/// <summary>A contract board; cargo for its contracts is left beside it.</summary>
[RegisterComponent]
public sealed partial class WFContractBoardComponent : Component
{
    /// <summary>Which contractors post here, by their <see cref="Shared._WF.Contracts.WFContractorPrototype.Board"/>.</summary>
    [DataField]
    public string Board = Shared._WF.Contracts.WFContractBoards.Central;
}

/// <summary>Where a station hands out contract cargo; else its warp point, else its centre.</summary>
[RegisterComponent]
public sealed partial class WFContractDropComponent : Component;

/// <summary>A package, crate or recovered item that belongs to a live contract.</summary>
[RegisterComponent]
public sealed partial class WFContractItemComponent : Component
{
    [ViewVariables]
    public int Contract;

    /// <summary>Set once it has counted towards its contract, so its removal does not fail it.</summary>
    [ViewVariables]
    public bool Delivered;
}

/// <summary>Cargo an inspection contract asks to be searched; its holder's crew get a verb to do it.</summary>
[RegisterComponent]
public sealed partial class WFContractInspectableComponent : Component
{
    [ViewVariables]
    public int Contract;
}

/// <summary>Marks the Contracts PDA app.</summary>
[RegisterComponent]
public sealed partial class WFContractCartridgeComponent : Component;
