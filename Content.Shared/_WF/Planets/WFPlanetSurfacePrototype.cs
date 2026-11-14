using Content.Shared._DV.Planet;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared._WF.PlanetPois;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Planets;

/// <summary>
/// Describes the whole z-stack of one sector planet: its ground biome, how many air layers sit above it, and its orbit layer.
/// </summary>
// Kind named explicitly: Robust would derive "wFPlanetSurface".
[Prototype("wfPlanetSurface")]
public sealed partial class WFPlanetSurfacePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The sector body type this surface belongs to; the registry matches spawned planets by this id.</summary>
    [DataField(required: true)]
    public ProtoId<PlanetTypePrototype> PlanetType;

    /// <summary>The planet prototype the ground layer is generated from.</summary>
    [DataField(required: true)]
    public ProtoId<PlanetPrototype> Ground;

    /// <summary>Optional hand-made grid loaded onto the ground layer at the planet centre.</summary>
    [DataField]
    public ResPath? GroundGrid;

    /// <summary>
    /// Radius in tiles of the world's circle round its centre; terrain outside it never exists. Zero leaves the world
    /// unbounded. The cvar wf.planet_radius overrides it when set.
    /// </summary>
    [DataField]
    public int Radius = 256;

    /// <summary>Whether the whole ground inside the circle is generated when the network is built, over the ticks after.</summary>
    [DataField]
    public bool Preload = true;

    /// <summary>The wall laid on the outermost ring of ground tiles once the world is preloaded; null lays none.</summary>
    [DataField]
    public EntProtoId? BoundaryWall = "WFPlanetBoundaryWall";

    /// <summary>The points of interest rolled onto the world each round before its preload; null places none.</summary>
    [DataField]
    public ProtoId<WFPlanetPoiTablePrototype>? Pois;

    /// <summary>How many bare fall-through air layers sit between the ground and the cloud layer.</summary>
    [DataField]
    public int AirLayers = 2;

    /// <summary>Whether a cloud layer sits between the topmost air layer and orbit.</summary>
    [DataField]
    public bool CloudLayer = true;

    /// <summary>Surface gravity in gees, stamped onto every layer; divides a hull's landing-thruster lift.</summary>
    [DataField]
    public float Gravity = 1f;

    /// <summary>How close to the sector planet a shuttle must be to enter orbit.</summary>
    [DataField]
    public float OrbitRange = 2000f;

    /// <summary>Speed cap (m/s) on this world's orbit layer, stamped onto <see cref="WFOrbitLayerComponent"/>.</summary>
    [DataField]
    public float OrbitMaxSpeed = 6f;

    /// <summary>Linear damping a grid on this world's orbit layer flies under; thrusters fight it rather than beat it.</summary>
    [DataField]
    public float OrbitDamping = 3f;

    /// <summary>Speed cap (m/s) for a grid on one of this world's air layers, stamped onto every layer marker.</summary>
    [DataField]
    public float AirMaxSpeed = 12f;

    /// <summary>Linear damping a grid flying one of this world's air layers runs under.</summary>
    [DataField]
    public float AirDamping = 1.5f;

    /// <summary>Whether the network is built eagerly when the sector body spawns at round start.</summary>
    [DataField]
    public bool BuildAtRoundStart;

    /// <summary>Whether this world is sanctioned; entering an unsanctioned world's orbit asks for confirmation.</summary>
    [DataField]
    public bool Sanctioned = true;

    /// <summary>Fixed ground biome seed so terrain repeats every round; null rolls a new seed each start.</summary>
    [DataField]
    public int? Seed;

    /// <summary>Components stamped on every member map by the z-network registry, transit maps included.</summary>
    [DataField]
    public ComponentRegistry? NetworkComponents;

    /// <summary>Components added to the ground layer after the network is initialised.</summary>
    [DataField]
    public ComponentRegistry? GroundComponents;

    /// <summary>Components added to each air layer after the network is initialised.</summary>
    [DataField]
    public ComponentRegistry? AirComponents;

    /// <summary>Components added to the cloud layer after the network is initialised.</summary>
    [DataField]
    public ComponentRegistry? CloudComponents;

    /// <summary>Components added to the orbit layer after the network is initialised.</summary>
    [DataField]
    public ComponentRegistry? OrbitComponents;

    /// <summary>Name given to the orbit map entity; the console's orbit button reads it.</summary>
    [DataField]
    public LocId OrbitMapName = "wf-planet-orbit-map-name";

    /// <summary>Name given to the orbit layer's centre marker; a warp point and radar label, not an FTL beacon.</summary>
    [DataField]
    public LocId OrbitMarkerName = "wf-planet-orbit-marker-name";

    /// <summary>The world's climate and ground in a few words, for the radar's planet scan.</summary>
    [DataField]
    public LocId? Climate;

    /// <summary>Known dangers beyond what the air, heat and gravity give away, for the radar's planet scan.</summary>
    [DataField]
    public List<LocId> Hazards = new();
}
