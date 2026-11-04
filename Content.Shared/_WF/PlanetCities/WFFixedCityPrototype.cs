using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.EntityTable.EntitySelectors;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCities;

/// <summary>
/// A hand-made multi-level city stamped onto a world every round at a random spot, from a game map's levels: the
/// main map on the ground, its maps above on the flight layers and its maps below underground. The game map's station
/// becomes joinable there.
/// </summary>
[Prototype("wfFixedCity")]
public sealed partial class WFFixedCityPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The world it is built on.</summary>
    [DataField(required: true)]
    public ProtoId<WFPlanetSurfacePrototype> Surface;

    /// <summary>The game map whose levels and station config are used. A plain id: the client ignores game maps.</summary>
    [DataField(required: true)]
    public string GameMap = string.Empty;

    /// <summary>Which of the game map's stations to create.</summary>
    [DataField(required: true)]
    public string Station = string.Empty;

    /// <summary>
    /// Where the levels' tiles lie, in the ground level's own coordinates: held as soon as the world starts loading, so
    /// other claims keep out while the city loads. Anything outside it is left out.
    /// </summary>
    [DataField(required: true)]
    public Box2i Footprint;

    /// <summary>The site's name on signals and radars.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Entities swapped on load; a null replacement deletes the entity.</summary>
    [DataField]
    public Dictionary<EntProtoId, EntProtoId?> Replace = new();

    /// <summary>Tiles swapped on load, such as a city's own road surface for the world's.</summary>
    [DataField]
    public Dictionary<string, string> ReplaceTiles = new();

    /// <summary>
    /// Bare rock on the levels below the street. Where the layer grows its own rock it is dropped with its tiles before
    /// loading, so the world's underground fills in around the city; elsewhere it is swapped as <see cref="Replace"/> says.
    /// </summary>
    [DataField]
    public HashSet<EntProtoId> Terrain = new();

    /// <summary>Whether godmode is taken off everything in the city, so all of it can be destroyed.</summary>
    [DataField]
    public bool StripGodmode = true;

    /// <summary>Roads cut from the city's own streets out to the edge of its footprint, where the world's roads join.</summary>
    [DataField]
    public List<WFFixedCityEntrance> Entrances = new();

    /// <summary>What has become of the city this round, one picked by weight: overrun by something, or left alone.</summary>
    [DataField]
    public List<WFCityFate> Fates = new();

    /// <summary>The people living and working indoors on every level, whatever the fate; none if unset.</summary>
    [DataField]
    public WFCityResidents? Residents;
}

/// <summary>Civilians living in a city's buildings, spread over its levels in proportion to their roofed floor.</summary>
[DataDefinition]
public sealed partial class WFCityResidents
{
    /// <summary>One pick per resident.</summary>
    [DataField(required: true)]
    public EntityTableSelector Spawns = default!;

    /// <summary>Residents in the whole city.</summary>
    [DataField]
    public int Min = 24;

    [DataField]
    public int Max = 40;

    /// <summary>Whether the street level has residents.</summary>
    [DataField]
    public bool Ground = true;

    /// <summary>Whether the levels above the street have residents.</summary>
    [DataField]
    public bool Upper = true;

    /// <summary>Whether the levels below the street have residents.</summary>
    [DataField]
    public bool Lower = true;

    /// <summary>Tiles two residents on a level keep apart where there is room.</summary>
    [DataField]
    public int Spacing = 3;
}

/// <summary>One way a city can be found: groups of something spread through its buildings and streets, or nothing at all.</summary>
[DataDefinition]
public sealed partial class WFCityFate
{
    /// <summary>For the log.</summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public float Weight = 1f;

    /// <summary>One pick per member of a group; none for a city left alone.</summary>
    [DataField]
    public EntityTableSelector? Spawns;

    [DataField]
    public int MinGroups;

    [DataField]
    public int MaxGroups;

    [DataField]
    public int MinGroupSize = 1;

    [DataField]
    public int MaxGroupSize = 1;

    /// <summary>The share of groups, 0 to 1, that settle indoors on roofed floor instead of in the open streets.</summary>
    [DataField]
    public float IndoorShare;

    /// <summary>Whether indoor groups may also settle on the levels above the street, not only the ground floor.</summary>
    [DataField]
    public bool UpperLevels;

    /// <summary>Spawned where each group gathers, under the open sky: their home and a fire, say.</summary>
    [DataField]
    public List<EntProtoId> Gathering = new();
}

/// <summary>A straight road cut from a street tile in the ground level out to the edge of the city's footprint.</summary>
[DataDefinition]
public sealed partial class WFFixedCityEntrance
{
    /// <summary>The street tile the cut starts from, in the ground level's own coordinates.</summary>
    [DataField(required: true)]
    public Vector2i Start;

    [DataField(required: true)]
    public WFPlanetPoiDirection Direction;

    /// <summary>Width of the cut, centred on the start tile; odd.</summary>
    [DataField]
    public int Width = 5;

    [DataField]
    public string Tile = "FloorAsphalt";
}
