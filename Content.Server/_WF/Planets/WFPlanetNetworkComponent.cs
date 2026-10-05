using System.Numerics;
using Content.Shared._WF.Planets;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Planets;

/// <summary>Describes a planet's z-map network, stored on the network entity.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPlanetNetworkComponent : Component
{
    /// <summary>The sector body this network belongs to, or null for a standalone network built by hand.</summary>
    [DataField]
    public EntityUid? Planet;

    /// <summary>The depth 0 biome map.</summary>
    [DataField]
    public EntityUid GroundMap;

    /// <summary>The top map, the only FTL door in or out.</summary>
    [DataField]
    public EntityUid OrbitMap;

    /// <summary>Every member map, ordered by depth ascending from 0.</summary>
    [DataField]
    public List<EntityUid> Layers = new();

    /// <summary>Member maps below ground, nearest first; never in <see cref="Layers"/>.</summary>
    [DataField]
    public List<EntityUid> LowerLayers = new();

    /// <summary>The surface definition this network was built from.</summary>
    [DataField]
    public ProtoId<WFPlanetSurfacePrototype> Surface;

    /// <summary>The planet centre in the world frame, matching what the shared FTL range gate measures.</summary>
    [DataField]
    public Vector2 Centre;
}
