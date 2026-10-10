using System.Numerics;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.NpcCrew;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Encounters;

/// <summary>
/// An encounter: one or more NPC-crewed ships spawned together in open space, each with its side, its rules and
/// its orders.
/// </summary>
[Prototype("wfEncounter")]
public sealed partial class WFEncounterPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Name of the encounter; takes $designation.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Sector announcement when it starts; takes $name, $origin and $destination. None announces nothing.</summary>
    [DataField]
    public LocId? Announcement;

    /// <summary>Who the announcement is from.</summary>
    [DataField]
    public LocId? AnnouncementSender;

    [DataField(required: true)]
    public List<WFEncounterShip> Ships = new();

    /// <summary>
    /// If above zero, the ships wait where they arrive, announcement made, and only get their orders once a
    /// player is within this distance of one of them. For encounters a player is meant to witness.
    /// </summary>
    [DataField]
    public float StartRadius;

    /// <summary>
    /// How far from where the encounter was placed its ships may stray before they break off and fly back. Zero for
    /// no limit. Keeps a fight where players were told it is.
    /// </summary>
    [DataField]
    public float Leash;

    /// <summary>What a side pays the players who helped it, when it is the one side left.</summary>
    [DataField]
    public List<WFEncounterReward> Rewards = new();

    /// <summary>Has the ship's radio officer make the announcement on the common channel, not sector control.</summary>
    [DataField]
    public bool AnnounceOnRadio;

    /// <summary>Played with the announcement in place of the ordinary chime; for an alarm such as a fleet action.</summary>
    [DataField]
    public Robust.Shared.Audio.SoundSpecifier? AnnouncementSound;

    /// <summary>The announcement's colour in chat, when it is not the ordinary one.</summary>
    [DataField]
    public Color? AnnouncementColor;

    /// <summary>
    /// Key of the ship whose crew make a radio announcement; left out, the first ship listed. Sector control makes it
    /// when nobody aboard that ship can.
    /// </summary>
    [DataField]
    public string? Announcer;

    /// <summary>Circuit placement: how many stations the haul calls at and how it flies between them.</summary>
    [DataField]
    public WFEncounterRoute? Route;

    /// <summary>What the cargo ships may be carrying; one is picked per run. The announcement takes it as $cargo.</summary>
    [DataField]
    public List<ProtoId<WFEncounterManifestPrototype>> Manifests = new();

    /// <summary>Who starts it: the storyteller at round start, the storyteller during the round, or only admins and code.</summary>
    [DataField]
    public WFEncounterStart Start = WFEncounterStart.Scheduled;

    /// <summary>Transient encounters jump out once their orders are flown; persistent ones stay for the round.</summary>
    [DataField]
    public WFEncounterLifetime Lifetime = WFEncounterLifetime.Transient;

    [DataField]
    public WFEncounterCategory Category = WFEncounterCategory.Traffic;

    /// <summary>The glyph the radar draws for it off the scope; left as Category, the category's own.</summary>
    [DataField]
    public WFEncounterIcon Icon = WFEncounterIcon.Category;

    /// <summary>How much of the storyteller's budget it takes while it runs.</summary>
    [DataField]
    public int Cost = 1;

    /// <summary>No sector marker until its crew raise an alarm or an admin reveals it.</summary>
    [DataField]
    public bool Hidden;

    /// <summary>Whether the storyteller may bring another after this one was started at round start and is gone.</summary>
    [DataField]
    public bool Replaceable;

    /// <summary>Where the storyteller puts its origin.</summary>
    [DataField]
    public WFEncounterPlacement Placement = WFEncounterPlacement.OpenSpace;

    /// <summary>Station and Route: how far beyond the station's hull the origin sits.</summary>
    [DataField]
    public float Standoff = 600f;

    [DataField]
    public float Weight = 1f;

    [DataField]
    public int MinPlayers;

    /// <summary>Only scheduled once war has been declared: a clash between the navies has no place under the ceasefire.</summary>
    [DataField]
    public bool RequiresWar;

    /// <summary>Seconds before the scheduler may pick the same encounter again.</summary>
    [DataField]
    public float Cooldown = 1800f;

    /// <summary>Seconds until it ends as expired. Zero never expires.</summary>
    [DataField]
    public float Duration;

    /// <summary>How far from the chosen player ship the scheduler places it.</summary>
    [DataField]
    public float MinDistance = 2500f;

    [DataField]
    public float MaxDistance = 4000f;

    /// <summary>Open space: no station may be within this distance of the origin. Keeps raiders away from ports.</summary>
    [DataField]
    public float StationClearance;

    /// <summary>
    /// Stations it is never placed by or routed to, such as an enemy's home ports: any whose name or station id
    /// contains one of these, whatever the case.
    /// </summary>
    [DataField]
    public List<string> AvoidStations = new();
}

/// <summary>One ship of an encounter with its crew, its rules and its orders.</summary>
[DataDefinition]
public sealed partial class WFEncounterShip
{
    /// <summary>What the other ships' orders call this one.</summary>
    [DataField(required: true)]
    public string Key = string.Empty;

    /// <summary>The hull, or leave it out and list several in <see cref="Vessels"/>.</summary>
    [DataField]
    public ProtoId<VesselPrototype>? Vessel;

    /// <summary>Hulls to pick one from each run.</summary>
    [DataField]
    public List<ProtoId<VesselPrototype>> Vessels = new();

    /// <summary>Who the crew are and what they wear and carry. None spawns each role's own mob.</summary>
    [DataField]
    public ProtoId<WFCrewProfilePrototype>? Profile;

    /// <summary>Armed guards aboard, on top of the deckhands.</summary>
    [DataField]
    public int Guards;

    /// <summary>Crates of the run's manifest put in the hold.</summary>
    [DataField]
    public int Cargo;

    /// <summary>Whether this ship flies the encounter's route: docks at each stop in turn, then leaves.</summary>
    [DataField]
    public bool FlyRoute;

    /// <summary>
    /// Runs down the nearest ship with a player aboard and docks with it, again and again until it manages it;
    /// then its hands go aboard, fight whoever they see, carry a few things off, and the ship leaves.
    /// </summary>
    [DataField]
    public bool Hunt;

    /// <summary>Only these roles of the planned crew are spawned. Empty spawns the whole plan.</summary>
    [DataField]
    public List<ProtoId<WFCrewRolePrototype>> Roles = new();

    /// <summary>Others aboard who are not crew, such as a trader, spawned in the hold.</summary>
    [DataField]
    public List<EntProtoId> Passengers = new();

    /// <summary>Seconds held at the end of each wander leg.</summary>
    [DataField]
    public float WanderPause;

    /// <summary>Legs of aimless flying: that many random points within <see cref="WanderRadius"/> of the origin, in turn.</summary>
    [DataField]
    public int Wander;

    [DataField]
    public float WanderRadius = 2500f;

    /// <summary>Fluent prefix of what the ship says to intruders: "-warn-1" to "-warn-3" and "-attack" are appended.</summary>
    [DataField]
    public string ZoneLines = "wf-encounter-zone";

    /// <summary>Whose ships the zones answer to: everyone not of this ship's company, or only its enemies.</summary>
    [DataField]
    public WFEncounterZoneTargets ZoneTargets = WFEncounterZoneTargets.Everyone;

    /// <summary>Player ships of another company inside this range are told over the radio to turn away.</summary>
    [DataField]
    public float WarnRange;

    /// <summary>Player ships of another company inside this range are fired on.</summary>
    [DataField]
    public float AttackRange;

    /// <summary>Position relative to the encounter's origin.</summary>
    [DataField]
    public Vector2 Offset;

    /// <summary>The ship's own radar glyph, where its side is not what the encounter is about; Category for the encounter's.</summary>
    [DataField]
    public WFEncounterIcon Icon = WFEncounterIcon.Category;

    /// <summary>Ships of the same side are one battlegroup. The encounter is decided when one side is left.</summary>
    [DataField]
    public string Side = string.Empty;

    [DataField]
    public int Deckhands = 2;

    [DataField]
    public bool Captain = true;

    /// <summary>
    /// Whether an engineer is posted by the plant to keep it fuelled from the ship's stores. Only where the roles allow
    /// one; a fighter crewed by a pilot and gunner alone tops its own generator up while its crew live.
    /// </summary>
    [DataField]
    public bool Engineer = true;

    [DataField]
    public ProtoId<NpcFactionPrototype> Faction = "WFCrew";

    [DataField]
    public ProtoId<CompanyPrototype>? Company;

    [DataField]
    public WFCrewSecurityResponse Boarding = WFCrewSecurityResponse.Hostile;

    [DataField]
    public WFCrewSecurityResponse Docking = WFCrewSecurityResponse.Hostile;

    /// <summary>When this ship stops attacking another.</summary>
    [DataField]
    public WFCrewDisengage Disengage = WFCrewDisengage.Disable;

    [DataField]
    public float DisengageRange = 500f;

    /// <summary>How well its crew shoot, lay the ship's guns and fly. Unset takes the crew profile's skill pool, else Veteran.</summary>
    [DataField]
    public WFCrewSkill? Skill;

    /// <summary>Whether the ship calls for help on the radio: a mayday when attacked, a distress call when adrift.</summary>
    [DataField]
    public bool Distress = true;

    /// <summary>Whether the ship evades attackers.</summary>
    [DataField]
    public bool Evades = true;

    /// <summary>Flight limits; none uses the cautious defaults.</summary>
    [DataField]
    public ProtoId<WFCrewNavigationProfilePrototype>? Navigation;

    /// <summary>
    /// Its colour on radars. Ships of a side share the first colour set on any of them; with none set, an encounter
    /// of several sides gives each side one of <c>WFEncounterSystem.SideColors</c> in the order the sides appear.
    /// </summary>
    [DataField]
    public Color? IffColor;

    /// <summary>
    /// Arrives stranded and stays so until players get it under way again. Its orders wait until then, and its side's
    /// reward goes to the players who helped.
    /// </summary>
    [DataField]
    public WFEncounterStranding Stranded = WFEncounterStranding.None;

    /// <summary>
    /// Lies in wait until the encounter begins at its start radius: its IFF label is hidden, it holds still, and it
    /// has no zones and no sector marker.
    /// </summary>
    [DataField]
    public bool Lurks;

    /// <summary>Tasks flown in order.</summary>
    [DataField]
    public List<WFEncounterObjective> Objectives = new();

    /// <summary>Set, the hull arrives a hulk: no crew and no fuel, squatters aboard, and it can be claimed.</summary>
    [DataField]
    public WFEncounterDerelict? Derelict;
}

/// <summary>What lives in a hulk and what it takes to own it.</summary>
[DataDefinition]
public sealed partial class WFEncounterDerelict
{
    [DataField]
    public int Dwellers = 6;

    [DataField]
    public List<EntProtoId> DwellerPool = new();

    /// <summary>The squatters' chief, who leaves the claim to the ship when he dies.</summary>
    [DataField]
    public EntProtoId? King;

    [DataField]
    public EntProtoId Claim = "WFSalvageClaim";

    /// <summary>Pieces of litter scattered over the decks.</summary>
    [DataField]
    public int Debris = 30;

    [DataField]
    public List<EntProtoId> DebrisPool = new();

    /// <summary>The share of its worth the ship sells for once claimed.</summary>
    [DataField]
    public float Resale = 0.25f;
}

/// <summary>What a side pays its helpers when it wins.</summary>
[DataDefinition]
public sealed partial class WFEncounterReward
{
    [DataField(required: true)]
    public string Side = string.Empty;

    /// <summary>Spesos shared equally among everyone who helped, paid into their bank accounts.</summary>
    [DataField]
    public int Spesos;

    /// <summary>Faction credits handed to each helper whose company is one of <see cref="CreditCompanies"/>.</summary>
    [DataField]
    public int Credits;

    /// <summary>The credit to hand out, as a one-credit stack entity.</summary>
    [DataField]
    public EntProtoId? CreditEntity;

    [DataField]
    public List<string> CreditCompanies = new();

    /// <summary>What the winning ship says on the common channel; takes $count, the number of helpers.</summary>
    [DataField]
    public LocId? Thanks;
}

/// <summary>How a haul flies its stops.</summary>
[DataDefinition]
public sealed partial class WFEncounterRoute
{
    [DataField]
    public int MinStops = 2;

    [DataField]
    public int MaxStops = 4;

    /// <summary>Seconds docked at each stop.</summary>
    [DataField]
    public float DwellMin = 120f;

    [DataField]
    public float DwellMax = 240f;

    /// <summary>How far from the first stop it enters the sector.</summary>
    [DataField]
    public float ApproachMin = 3000f;

    [DataField]
    public float ApproachMax = 5000f;

    /// <summary>How far clear of the last stop it flies before jumping out.</summary>
    [DataField]
    public float ExitDistance = 1500f;
}

/// <summary>One task of an encounter ship's queue.</summary>
[DataDefinition]
public sealed partial class WFEncounterObjective
{
    [DataField(required: true)]
    public WFCrewObjectiveKind Kind;

    /// <summary>
    /// Key of the ship the task is aimed at, @origin or @destination for the placement's stations, or @player for the
    /// nearest ship with a player aboard when the orders are given. A task aimed at @player is left out when there is none.
    /// </summary>
    [DataField]
    public string? Target;

    /// <summary>GoTo: the point, relative to the encounter's origin.</summary>
    [DataField]
    public Vector2 Offset;

    [DataField]
    public float Range = 100f;

    /// <summary>Seconds; zero is until done or forever, by kind.</summary>
    [DataField]
    public float Duration;
}

/// <summary>How an encounter ship arrives stranded.</summary>
public enum WFEncounterStranding : byte
{
    None,

    /// <summary>Its generators and antimatter engine are empty and its batteries flat: no power, so no thrust.</summary>
    Fuel,

    /// <summary>Every thruster that drives it is wrecked; its power is left on.</summary>
    Thrusters,

    /// <summary>One of the two, picked when it arrives.</summary>
    Random,
}

public enum WFEncounterStart : byte
{
    Manual,
    RoundStart,
    Scheduled,
}

public enum WFEncounterLifetime : byte
{
    Transient,
    Persistent,
}

public enum WFEncounterCategory : byte
{
    Traffic,
    Patrol,
    Threat,
    Distress,
}

/// <summary>The glyph a radar draws for an encounter at the rim of the scope. Category draws the category's own.</summary>
public enum WFEncounterIcon : byte
{
    Category,
    Skull,
    Shield,
    Crate,
    Cross,
    Coins,
    Flag,
    Swords,
    Ghost,
}

public enum WFEncounterPlacement : byte
{
    /// <summary>Clear space at a distance from a ship with players aboard.</summary>
    OpenSpace,

    /// <summary>Beside a station.</summary>
    Station,

    /// <summary>Beside one station, with another as the destination.</summary>
    Route,

    /// <summary>Out in space at the route's approach distance from the first of several stations to call at.</summary>
    Circuit,
}

/// <summary>How an encounter ended.</summary>
public enum WFEncounterResolution : byte
{
    /// <summary>Its duration ran out.</summary>
    Expired,

    /// <summary>Every ship flew its orders to the end.</summary>
    Completed,

    /// <summary>One side is left in the fight.</summary>
    Decided,

    /// <summary>No ship is left in the fight.</summary>
    Destroyed,

    /// <summary>An admin or the round ended it.</summary>
    Ended,
}
