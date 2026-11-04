using Content.Shared._WF.NpcVehicleCrew;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.Procedural;
using Content.Shared.Salvage;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetPois;

/// <summary>What a world rolls its points of interest from each round, and how they are spread over it.</summary>
[Prototype("wfPlanetPoiTable")]
public sealed partial class WFPlanetPoiTablePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Optional physical road network connecting the sites.</summary>
    [DataField]
    public WFPlanetRoadNetwork? Roads;

    /// <summary>The kinds of site this world can hold, drawn by weight.</summary>
    [DataField(required: true)]
    public List<WFPlanetPoiEntry> Entries = new();

    /// <summary>How many sites a world gets, inclusive.</summary>
    [DataField]
    public int MinCount = 4;

    [DataField]
    public int MaxCount = 8;

    /// <summary>Tiles between one site's origin and the next.</summary>
    [DataField]
    public float Spacing = 128f;

    /// <summary>Tiles a site's origin keeps from the world's edge, so the whole site fits inside it.</summary>
    [DataField]
    public float EdgeMargin = 96f;

    /// <summary>Tiles a site's origin keeps from the world's centre, where arrivals land and the gate mouth lies.</summary>
    [DataField]
    public float CentreClear = 96f;

    /// <summary>Half the side, in tiles, of the square round a candidate origin that must hold nothing pinned, such as a cavern mouth.</summary>
    [DataField]
    public int Footprint = 40;

    /// <summary>Metres from a site's origin at which someone on the ground identifies its signal.</summary>
    [DataField]
    public float RevealRange = 48f;

    /// <summary>The expedition rating of this world's sites, unless an entry says otherwise.</summary>
    [DataField]
    public DifficultyRating Difficulty = DifficultyRating.Moderate;

    /// <summary>Crashed ships scattered off the roads, reclaimed by the world.</summary>
    [DataField]
    public WFPlanetWrecks? Wrecks;

    /// <summary>Armed camps round ships set down off the roads, such as Reaver encampments.</summary>
    [DataField]
    public List<WFPlanetCampEntry> Camps = new();

    /// <summary>Who may be found living in a dwelling site, such as a house; spawned on its floor once the world exists.</summary>
    [DataField]
    public EntProtoId? Residents;

    /// <summary>The chance each dwelling site has a resident.</summary>
    [DataField]
    public float ResidentChance;

    /// <summary>Marks a lived-in site as its resident's home, which he guards; sized to the site.</summary>
    [DataField]
    public EntProtoId? ResidentHome;

    /// <summary>Big creatures set loose on the open ground each round, away from the landing zone.</summary>
    [DataField]
    public List<WFPlanetRoamer> Roamers = new();

    /// <summary>Working vehicles left about the world, by the roads and off them, some with someone holed up inside.</summary>
    [DataField]
    public WFPlanetVehicles? Vehicles;

    /// <summary>NPC-crewed vehicles that patrol up and down the roads.</summary>
    [DataField]
    public List<WFPlanetRoadPatrol> RoadPatrols = new();
}

/// <summary>Working vehicles parked about a world: the key on the driver's seat, or in a squatter's hand.</summary>
[DataDefinition]
public sealed partial class WFPlanetVehicles
{
    /// <summary>One pick per vehicle.</summary>
    [DataField(required: true)]
    public EntityTableSelector Spawns = default!;

    [DataField]
    public int MinCount = 2;

    [DataField]
    public int MaxCount = 4;

    /// <summary>The share parked by a road, where the world has roads; the rest are off in the open.</summary>
    [DataField]
    public float Roadside = 0.6f;

    /// <summary>The chance someone has holed up inside one, with its key.</summary>
    [DataField]
    public float SquatterChance = 0.35f;

    [DataField]
    public EntProtoId? Squatter;
}

/// <summary>A crewed vehicle driving up and down a stretch of a world's roads.</summary>
[DataDefinition]
public sealed partial class WFPlanetRoadPatrol
{
    [DataField(required: true)]
    public ProtoId<WFVehicleCrewPrototype> Crew;

    [DataField]
    public int MinCount = 1;

    [DataField]
    public int MaxCount = 1;
}

/// <summary>A creature that roams a world's surface, and how many of it.</summary>
[DataDefinition]
public sealed partial class WFPlanetRoamer
{
    [DataField(required: true)]
    public EntProtoId Id;

    [DataField]
    public int MinCount = 1;

    [DataField]
    public int MaxCount = 1;
}

/// <summary>One kind of site: the dungeon generated for it and what its signal is called once identified.</summary>
[DataDefinition]
public sealed partial class WFPlanetPoiEntry
{
    [DataField]
    public ProtoId<DungeonConfigPrototype>? Dungeon;

    /// <summary>Authored map to stamp instead of a procedural dungeon.</summary>
    [DataField]
    public string? MapPath;

    /// <summary>Reserved tile bounds relative to the placement origin, with exclusive upper bounds.</summary>
    [DataField]
    public Box2i? Footprint;

    /// <summary>Guaranteed number of this entry before weighted selection.</summary>
    [DataField]
    public int MinCount;

    /// <summary>Maximum number of this entry, or no limit.</summary>
    [DataField]
    public int? MaxCount;

    /// <summary>How the site joins the road network.</summary>
    [DataField]
    public WFPlanetPoiConnection Connection = WFPlanetPoiConnection.Roadside;

    /// <summary>Road entrances relative to the placement origin.</summary>
    [DataField]
    public List<WFPlanetPoiSocket> Sockets = new();

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public float Weight = 1f;

    /// <summary>Whether the expedition console may offer this site; off for an inhabited settlement.</summary>
    [DataField]
    public bool Expeditions = true;

    /// <summary>The expedition rating of this site, over the table's.</summary>
    [DataField]
    public DifficultyRating? Difficulty;

    /// <summary>An authored map that takes a random quarter turn each round, entrances and all.</summary>
    [DataField]
    public bool Rotate;

    /// <summary>An off-road site that a footpath links to the nearest road, from just outside its first socket.</summary>
    [DataField]
    public bool Trail;

    /// <summary>A house or hideout someone may live in: the table's residents move in by its chance.</summary>
    [DataField]
    public bool Dwelling;

    /// <summary>A site that must be on the roads, such as a cargo hub: routed first, retried, never left off them.</summary>
    [DataField]
    public bool Priority;

    /// <summary>
    /// The level under the site, such as a basement, in the site's own frame; it goes on the level under the ground,
    /// where the world has one, and the floor is opened over its stairs.
    /// </summary>
    [DataField]
    public string? Below;
}

/// <summary>Whether a site has one entrance, multiple entrances, or no roads.</summary>
public enum WFPlanetPoiConnection { Roadside, Through, Isolated }

/// <summary>Outward direction of a road entrance.</summary>
public enum WFPlanetPoiDirection { North, East, South, West }

/// <summary>An authored entrance and the direction roads leave it.</summary>
[DataDefinition]
public sealed partial class WFPlanetPoiSocket
{
    /// <summary>Tile position relative to the site origin.</summary>
    [DataField(required: true)]
    public Vector2i Offset;

    /// <summary>Direction away from the building.</summary>
    [DataField(required: true)]
    public WFPlanetPoiDirection Direction;
}

/// <summary>Surface and clearance of the shared road network.</summary>
[DataDefinition]
public sealed partial class WFPlanetRoadNetwork
{
    /// <summary>Road surface tile prototype.</summary>
    [DataField]
    public string Tile = "FloorDirt";

    /// <summary>Odd road width in tiles, from 1 to 15.</summary>
    [DataField]
    public int Width = 3;

    /// <summary>Optional shoulder surface tile prototype.</summary>
    [DataField]
    public string? ShoulderTile;

    /// <summary>Shoulder width on each side of the road.</summary>
    [DataField]
    public int ShoulderWidth;

    /// <summary>Roads out of the landing zone's edge, each toward a site; none on a world too small for them.</summary>
    [DataField]
    public int Gates = 2;

    /// <summary>Roads run out toward the world's edge through its emptiest stretches; none on a world too small for them.</summary>
    [DataField]
    public int Exits = 1;

    /// <summary>Extra links that close the longest detours between sites into loops.</summary>
    [DataField]
    public int Loops = 1;

    /// <summary>
    /// How near, in tiles, the roads come to an off-road site with a footpath; a road runs out to one further off. Zero
    /// leaves them to their footpaths.
    /// </summary>
    [DataField]
    public int Reach = 64;

    /// <summary>Kerbs, lane lines, street lights and scattered traffic laid over the painted roads; none leaves them bare.</summary>
    [DataField]
    public ProtoId<WFRoadDressingPrototype>? Dressing;

    /// <summary>Footpath surface tile prototype, worn from off-road sites to the roads.</summary>
    [DataField]
    public string TrailTile = "FloorDirt";

    /// <summary>The longest footpath, in tiles; a site further from the roads goes without.</summary>
    [DataField]
    public int TrailLength = 240;

    /// <summary>Stretches of road walled in and roofed over, dark, with something living inside; none leaves them open.</summary>
    [DataField]
    public WFRoadTunnels? Tunnels;
}

/// <summary>How a world's roads tunnel through rock: how many, how long, what they're cut through and what lurks there.</summary>
[DataDefinition]
public sealed partial class WFRoadTunnels
{
    [DataField]
    public int MinCount = 1;

    [DataField]
    public int MaxCount = 2;

    /// <summary>Length along the road, in tiles.</summary>
    [DataField]
    public int MinLength = 16;

    [DataField]
    public int MaxLength = 28;

    /// <summary>The rock either side.</summary>
    [DataField]
    public EntProtoId Wall = "WallRock";

    /// <summary>How many tiles thick the rock is each side.</summary>
    [DataField]
    public int Thickness = 2;

    /// <summary>What lives in there; one pick per lurker.</summary>
    [DataField]
    public EntityTableSelector? Lurkers;

    [DataField]
    public int MinLurkers = 2;

    [DataField]
    public int MaxLurkers = 4;

    /// <summary>Webs, bones and the like strewn along the edges.</summary>
    [DataField]
    public List<EntProtoId> Clutter = new();

    [DataField]
    public float ClutterChance = 0.1f;
}
