namespace Content.Server._WF.Remade.Vents;

/// <summary>A Monolith's vent habits: when it next sends a runner through the ducts, and what has come of it so far.</summary>
[RegisterComponent]
public sealed partial class WFVentStateComponent : Component
{
    [ViewVariables]
    public TimeSpan NextRun;

    [ViewVariables]
    public TimeSpan NextHaul;

    /// <summary>Corpses a Remade has dragged into the vents and set down elsewhere. Tests read it.</summary>
    [ViewVariables]
    public int Hauls;

    /// <summary>Runners sent through the vents. Tests read it.</summary>
    [ViewVariables]
    public int Runs;

    /// <summary>Runners sent to lie in wait. Tests read it.</summary>
    [ViewVariables]
    public int Ambushes;

    /// <summary>Remade that have burst out of a vent at a crewman. Tests read it.</summary>
    [ViewVariables]
    public int Bursts;
}

/// <summary>Where a Remade has got to in dragging a corpse to a vent.</summary>
public enum WFHaulPhase : byte
{
    /// <summary>Walking to the corpse.</summary>
    Fetch,

    /// <summary>Pulling it to the vent.</summary>
    Drag,

    /// <summary>In the ducts with it. The exit vent sets it down.</summary>
    Travel,
}

/// <summary>A Remade dragging a crew corpse into the vents, to set it down where it will turn: a morgue, a nest or the corruption by the stone.</summary>
[RegisterComponent]
public sealed partial class WFRemadeHaulComponent : Component
{
    [ViewVariables]
    public EntityUid Corpse;

    /// <summary>The vent it takes the body in by.</summary>
    [ViewVariables]
    public EntityUid Vent;

    /// <summary>The vent it comes out of.</summary>
    [ViewVariables]
    public EntityUid Exit;

    /// <summary>Where the body is put down.</summary>
    [ViewVariables]
    public Robust.Shared.Map.EntityCoordinates Drop;

    /// <summary>The morgue tray it goes into, if that is where it is to go.</summary>
    [ViewVariables]
    public EntityUid? Tray;

    [ViewVariables]
    public WFHaulPhase Phase;

    [ViewVariables]
    public TimeSpan GiveUpAt;
}
