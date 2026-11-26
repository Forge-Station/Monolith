using Robust.Shared.GameStates;

namespace Content.Shared._WF.Planets;

/// <summary>
/// Marks a map as one layer of a planet network, so the outbound FTL gate only applies to planets and never to a station z-network.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFPlanetLayerComponent : Component
{
    /// <summary>The z-network entity this layer belongs to.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Network;

    /// <summary>Surface gravity of this world in gees, from its wfPlanetSurface; divides a hull's landing-thruster lift.</summary>
    [DataField, AutoNetworkedField]
    public float Gravity = 1f;

    /// <summary>Speed cap (m/s) for a grid flying this layer, so it cannot outrun the terrain streaming in below.</summary>
    [DataField, AutoNetworkedField]
    public float MaxSpeed = 12f;

    /// <summary>Linear damping a grid flying this layer runs under; thrusters still work, they fight it.</summary>
    [DataField, AutoNetworkedField]
    public float LinearDamping = 1.5f;
}
