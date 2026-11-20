namespace Content.Shared._WF.NpcVehicleCrew;

/// <summary>An NPC driver's controls; while active the grid mover reads them instead of the operator's keys.</summary>
[RegisterComponent]
public sealed partial class WFVehicleAutopilotComponent : Component
{
    /// <summary>Whether the autopilot drives; the operator must still be alive and conscious.</summary>
    [ViewVariables]
    public bool Active;

    /// <summary>Grid cardinal for a four-way vehicle; zero stops it.</summary>
    [ViewVariables]
    public Vector2i Direction;

    /// <summary>Free-steering throttle, -1 (reverse) to 1.</summary>
    [ViewVariables]
    public float Throttle;

    /// <summary>Free-steering wheel, -1 to 1; positive turns anticlockwise.</summary>
    [ViewVariables]
    public float Steering;
}
