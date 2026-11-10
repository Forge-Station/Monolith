using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Waypoints;

/// <summary>The waypoints a ship's crew has marked, kept on its grid and shown on every radar aboard.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFWaypointsComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public List<WFWaypoint> Waypoints = new();

    /// <summary>The next waypoint's id, never reused while the grid exists.</summary>
    [ViewVariables]
    public int NextId = 1;
}

/// <summary>A named position on one map.</summary>
[Serializable, NetSerializable]
public readonly record struct WFWaypoint(int Id, string Name, NetEntity Map, Vector2 Position);

/// <summary>Asks to mark a waypoint on the console's grid and current map; an empty name gets a numbered one.</summary>
[Serializable, NetSerializable]
public sealed class WFWaypointAddMessage(string name, Vector2 position) : BoundUserInterfaceMessage
{
    public readonly string Name = name;
    public readonly Vector2 Position = position;
}

/// <summary>Asks to delete a waypoint from the console's grid.</summary>
[Serializable, NetSerializable]
public sealed class WFWaypointRemoveMessage(int id) : BoundUserInterfaceMessage
{
    public readonly int Id = id;
}
