using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipShields;

/// <summary>Replicates the hull outline and remaining shield strength.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class WFShipShieldVisualsComponent : Component
{
    /// <summary>Counterclockwise outlines in shield-local coordinates.</summary>
    [DataField, AutoNetworkedField]
    public Vector2[][] Contours = Array.Empty<Vector2[]>();

    /// <summary>Remaining strength from zero to one.</summary>
    [DataField, AutoNetworkedField]
    public float Health = 1f;

    /// <summary>The current field appearance transition.</summary>
    [DataField, AutoNetworkedField]
    public WFShipShieldTransition Transition;

    /// <summary>Server time when the appearance transition began.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan TransitionStarted;

    /// <summary>The grid protected by this shield.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Grid;
}

/// <summary>Starts a ripple at the closest point on the shield boundary.</summary>
[Serializable, NetSerializable]
public sealed class WFShipShieldImpactEvent : EntityEventArgs
{
    public NetEntity Shield;
    public Vector2 Position;
    public float Strength;

    public WFShipShieldImpactEvent(NetEntity shield, Vector2 position, float strength)
    {
        Shield = shield;
        Position = position;
        Strength = strength;
    }
}
