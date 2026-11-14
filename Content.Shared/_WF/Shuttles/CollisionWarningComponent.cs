using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Shuttles;

/// <summary>
/// Sits on a grid that is predicted to hit something at speed. The server adds and removes it; any UI
/// on that ship reads it to draw a warning. The pilot is standing on the grid, so it is always in PVS
/// for them.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CollisionWarningComponent : Component
{
    /// <summary>How close the ship is to contact.</summary>
    [DataField, AutoNetworkedField]
    public CollisionWarningLevel Level = CollisionWarningLevel.Advisory;

    /// <summary>Server time contact is predicted for; the UI counts down to it locally.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan ImpactTime;

    /// <summary>IFF label of what is going to be hit, or null if it has none.</summary>
    [DataField, AutoNetworkedField]
    public string? ThreatName;

    /// <summary>Bearing to the threat in degrees clockwise from the ship's facing.</summary>
    [DataField, AutoNetworkedField]
    public float Bearing;

    /// <summary>Closing speed in metres per second.</summary>
    [DataField, AutoNetworkedField]
    public float ClosingSpeed;

    /// <summary>
    /// Server only: when the warning is dropped if nothing renews it. Holding it briefly stops the
    /// banner strobing while a ship yaws on the edge of the cone.
    /// </summary>
    [ViewVariables]
    public TimeSpan ClearTime;

    /// <summary>Server only: when the advisory callout next comes round.</summary>
    [ViewVariables]
    public TimeSpan NextCallout;

    /// <summary>Server only: the grid the current warning is about.</summary>
    [ViewVariables]
    public EntityUid? Threat;
}

/// <summary>
/// Two stages, as in the real thing: an advisory that traffic is closing, then a resolution advisory
/// that contact is about to happen.
/// </summary>
[Serializable, NetSerializable]
public enum CollisionWarningLevel : byte
{
    /// <summary>Traffic is closing and will hit if nothing changes.</summary>
    Advisory,

    /// <summary>Contact is seconds away.</summary>
    Imminent,
}
