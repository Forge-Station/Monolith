using System.Numerics;

namespace Content.Server._WF.NpcCrew.Components;

/// <summary>
/// A simulated ship: a plain entity standing in for a hull nobody is near. Its objective queue is flown as a point
/// moving at cruise speed, with no crew, physics or grid.
/// </summary>
[RegisterComponent]
public sealed partial class WFCrewVirtualShipComponent : Component
{
    /// <summary>Metres per second it travels.</summary>
    [DataField]
    public float Speed = 25f;

    /// <summary>Its velocity at the last step, for the sector markers.</summary>
    [ViewVariables]
    public Vector2 Velocity;

    /// <summary>The ship it reached on a docking order and counts as docked with, real or simulated.</summary>
    [ViewVariables]
    public EntityUid? DockedTo;

    /// <summary>How many crew it would carry, for the crew lists.</summary>
    [DataField]
    public int Crew;
}
