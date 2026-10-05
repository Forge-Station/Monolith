using Robust.Shared.GameStates;
using System.Numerics;
using Content.Shared.Parallax.Biomes.Layers;

namespace Content.Shared._WF.Planets;

/// <summary>
/// The top map of a planet network: vacuum, no terrain, the only FTL door in or out, and grids parked here never fall.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFOrbitLayerComponent : Component
{
    /// <summary>Terrain recipe, carried on orbit so radar works outside ground PVS without loading chunks.</summary>
    [DataField, AutoNetworkedField]
    public List<IBiomeLayer> RadarLayers = new();

    [DataField, AutoNetworkedField]
    public int RadarSeed;

    [DataField, AutoNetworkedField]
    public NetEntity? RadarGround;

    /// <summary>Holes cut into the ground (centre xy, radius z) that the radar draws as empty; other modules add them.</summary>
    [DataField, AutoNetworkedField]
    public List<Vector3> RadarScars = new();

    /// <summary>The sector body this layer orbits, used by the inbound FTL range gate.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Planet;

    /// <summary>How close to the sector body a shuttle must be to FTL into this layer.</summary>
    [DataField, AutoNetworkedField]
    public float Range = 2000f;

    /// <summary>The z-network entity this layer belongs to.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Network;

    /// <summary>Speed cap (m/s) for a grid parked here; tighter than an air layer's.</summary>
    [DataField, AutoNetworkedField]
    public float MaxSpeed = 6f;

    /// <summary>Linear damping a grid on the orbit layer runs under.</summary>
    [DataField, AutoNetworkedField]
    public float LinearDamping = 3f;
}
