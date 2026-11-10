using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Drydock;

/// <summary>
/// Holographic parking frame and approach lights for a ship's booked berth, in the station grid's local space.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFDrydockGuideComponent : Component
{
    /// <summary>Station grid the coordinates are relative to.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Station;

    /// <summary>Center of the parking frame.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 Center;

    /// <summary>Frame width and length along the station's axes, in tiles.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 Size;

    /// <summary>Start of the approach lights, outside the bay mouth.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 PathStart;

    /// <summary>End of the approach lights, at the frame's edge.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 PathEnd;
}
