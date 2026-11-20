namespace Content.Server._WF.Remade.Garrison;

/// <summary>A Monolith's standing guard: the Remade that hold near the stone while the rest of the horde hunts.</summary>
[RegisterComponent]
public sealed partial class WFGarrisonComponent : Component
{
    [ViewVariables]
    public HashSet<EntityUid> Guards = new();

    [ViewVariables]
    public TimeSpan NextReplace;

    /// <summary>Flesh the stone has put by for guards. Each guard costs a share of what a hunter of its kind costs.</summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float Credit;

    [ViewVariables]
    public TimeSpan LastCredit;

    /// <summary>The stone calls everything near it to an attacker at most this often.</summary>
    [ViewVariables]
    public TimeSpan NextConverge;

    /// <summary>It cries out for the attacker at most this often.</summary>
    [ViewVariables]
    public TimeSpan NextCry;

    /// <summary>Guards the stone has raised so far. Tests read it.</summary>
    [ViewVariables]
    public int Raised;

    /// <summary>Times it has called everything near it to an attacker. Tests read it.</summary>
    [ViewVariables]
    public int Calls;
}

/// <summary>One of a Monolith's guards.</summary>
[RegisterComponent]
public sealed partial class WFRemadeGuardComponent : Component
{
    [ViewVariables]
    public EntityUid Stone;

    [ViewVariables]
    public TimeSpan NextPatrol;

    /// <summary>It may chase past its leash until then: the stone was attacked.</summary>
    [ViewVariables]
    public TimeSpan ConvergeUntil;
}
