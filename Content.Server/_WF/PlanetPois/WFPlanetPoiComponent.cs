using System.Numerics;
using Content.Shared.Procedural;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Salvage;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetPois;

/// <summary>On a ground layer: every point of interest placed on it this round.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPlanetPoiComponent : Component
{
    [ViewVariables]
    public List<WFPlanetPoiSite> Sites = new();

    [ViewVariables]
    public bool NetworkReady;

    /// <summary>Creatures to let loose once the whole ground exists, so none starts on terrain that isn't there yet.</summary>
    [ViewVariables]
    public List<WFPlanetRoamer> PendingRoamers = new();

    /// <summary>Camps whose outside guards and cover wait for the ground round them to exist.</summary>
    [ViewVariables]
    public List<WFPendingCamp> PendingCamps = new();

    /// <summary>Who may live in the authored sites, posted once the world exists; null once done.</summary>
    [ViewVariables]
    public EntProtoId? PendingResidents;

    [ViewVariables]
    public float ResidentChance;

    [ViewVariables]
    public EntProtoId? ResidentHome;

    /// <summary>Vehicles to park and road patrols to send out once the world exists; cleared once done.</summary>
    [ViewVariables]
    public WFPlanetVehicles? PendingVehicles;

    [ViewVariables]
    public List<WFPlanetRoadPatrol> PendingRoadPatrols = new();

    [ViewVariables]
    public Vector2 RoamerCentre;

    [ViewVariables]
    public float RoamerRadius;

    [ViewVariables]
    public float RoamerInner;

    [ViewVariables]
    public string? GenerationError;

    [ViewVariables]
    public HashSet<Vector2i> RoadTiles = new();

    [ViewVariables]
    public HashSet<Vector2i> ShoulderTiles = new();

    /// <summary>The footpaths to off-road sites.</summary>
    [ViewVariables]
    public HashSet<Vector2i> TrailTiles = new();

    /// <summary>The roofed road through each tunnel, one set a tunnel.</summary>
    [ViewVariables]
    public List<HashSet<Vector2i>> Tunnels = new();

    /// <summary>The tiles down the middle of the roads, and everything the roads were furnished with.</summary>
    [ViewVariables]
    public HashSet<Vector2i> Centreline = new();

    [ViewVariables]
    public List<EntityUid> Furniture = new();
}

/// <summary>A camp set down before the ground round it existed, and what still goes outside it.</summary>
public sealed record WFPendingCamp(ProtoId<WFPlanetCampPrototype> Camp, Vector2 Centre, float Reach, string Group);

/// <summary>One placed site: what was generated, where, how big it came out and the signal that marks it.</summary>
public sealed class WFPlanetPoiSite
{
    public ProtoId<DungeonConfigPrototype>? Dungeon;
    public string? MapPath;
    public WFPlanetPoiConnection Connection;
    public List<WFPlanetRoadSocket> Sockets = new();
    public Dictionary<WFPlanetRoadSocket, List<Vector2i>> AccessPaths = new();
    public LocId Name;
    /// <summary>Where the generator was pointed; the site spreads round it.</summary>
    public Vector2i Origin;

    /// <summary>The generated tile count and reserved bounds with exclusive upper edges.</summary>
    public int TileCount;
    public Box2i Bounds;

    /// <summary>Where a footpath to the roads starts, for an off-road site that has one.</summary>
    public WFPlanetRoadSocket? TrailStart;

    /// <summary>Whether its footpath reached a road; one too far from any goes without.</summary>
    public bool TrailLaid;

    /// <summary>Whether someone may live here (see <see cref="WFPlanetPoiEntry.Dwelling"/>).</summary>
    public bool Dwelling;

    /// <summary>Whether this is a fixed city, whose levels span the world's layers.</summary>
    public bool City;

    /// <summary>Whether the roads must reach it (see <see cref="WFPlanetPoiEntry.Priority"/>).</summary>
    public bool Priority;

    /// <summary>Openings in the site's floor down to the level under it, over its stairs.</summary>
    public HashSet<Vector2i> Shafts = new();

    /// <summary>The site's own tiles, which a footpath crosses without repainting.</summary>
    public HashSet<Vector2i> Tiles = new();
    public EntityUid Signal;

    /// <summary>Whether an expedition console may offer the site, and at what rating.</summary>
    public bool Expeditions = true;
    public DifficultyRating Difficulty = DifficultyRating.Moderate;

    /// <summary>The station running an expedition on the site, and whether one has already cleared it this round.</summary>
    public EntityUid? ExpeditionClaim;
    public bool Cleared;
}

/// <summary>
/// On an orbit-layer marker over a site: an unidentified signal until someone on the ground gets close, then the
/// site's name.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPoiSignalComponent : Component
{
    [ViewVariables]
    public EntityUid Ground;

    [ViewVariables]
    public Vector2i Origin;

    [ViewVariables]
    public LocId Name;

    [ViewVariables]
    public float RevealRange = 48f;

    [ViewVariables]
    public bool Revealed;
}
