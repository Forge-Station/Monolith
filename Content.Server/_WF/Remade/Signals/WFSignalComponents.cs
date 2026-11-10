namespace Content.Server._WF.Remade.Signals;

/// <summary>A Monolith that takes Signals: the gate ghosts join through and the Signals it holds.</summary>
[RegisterComponent]
public sealed partial class WFSignalStoneComponent : Component
{
    [ViewVariables]
    public EntityUid? Gate;

    [ViewVariables]
    public HashSet<EntityUid> Signals = new();
}

/// <summary>The ghost role a Monolith offers. It is paused while the stone is full or can't take Signals.</summary>
[RegisterComponent]
public sealed partial class WFSignalGateComponent : Component
{
    [ViewVariables]
    public EntityUid? Stone;
}

/// <summary>A Remade a Signal is driving. Its own thinking is switched off until the Signal lets go.</summary>
[RegisterComponent]
public sealed partial class WFSignalPossessedComponent : Component
{
    [ViewVariables]
    public EntityUid Signal;

    /// <summary>The ability actions and the release action given to the player.</summary>
    [ViewVariables]
    public List<EntityUid> Actions = new();

    /// <summary>Who the last ability was aimed at, for abilities that keep working on it.</summary>
    [ViewVariables]
    public EntityUid? Target;
}

/// <summary>A door a Signal has bolted. It lets go by itself.</summary>
[RegisterComponent]
public sealed partial class WFSignalBoltComponent : Component
{
    [ViewVariables]
    public TimeSpan ReleaseAt;
}
