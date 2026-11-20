using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Planets;

/// <summary>
/// The round-scoped planet registry entry, added to each spawned sector body that has a Wolfgate surface.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFSectorPlanetComponent : Component
{
    /// <summary>The surface definition for this body, or null when it has no Wolfgate surface.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<WFPlanetSurfacePrototype>? Surface;

    /// <summary>The z-network entity, once the stack has been built.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Network;

    /// <summary>The orbit layer map of this body's network, once built.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? OrbitMap;

    /// <summary>Whether this world is sanctioned, mirrored off the surface prototype; Planet Control can flip it.</summary>
    [DataField, AutoNetworkedField]
    public bool Sanctioned = true;
}
