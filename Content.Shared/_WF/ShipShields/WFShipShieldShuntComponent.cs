using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.ShipShields;

/// <summary>Allocates shield capacity to a grid-local angular sector.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class WFShipShieldShuntComponent : Component
{
    /// <summary>Whether installed emitters may maintain this ship's field.</summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    /// <summary>Direction measured counterclockwise from grid-local east.</summary>
    [DataField, AutoNetworkedField]
    public float DirectionRadians = MathF.PI / 2f;

    /// <summary>Fraction of the other sectors' power transferred into this sector.</summary>
    [DataField, AutoNetworkedField]
    public float Concentration;

    /// <summary>Angular width of the reinforced sector.</summary>
    [DataField, AutoNetworkedField]
    public float ArcRadians = MathF.PI / 2f;

    /// <summary>Requested allocation direction during redistribution.</summary>
    public float TargetDirectionRadians = MathF.PI / 2f;
    /// <summary>Requested allocation concentration.</summary>
    public float TargetConcentration;
    /// <summary>Requested allocation width.</summary>
    public float TargetArcRadians = MathF.PI / 2f;
    /// <summary>Whether targets were initialized from the persistent allocation.</summary>
    public bool TargetInitialized;

    /// <summary>Hull center in grid-local coordinates.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 Center;
}
