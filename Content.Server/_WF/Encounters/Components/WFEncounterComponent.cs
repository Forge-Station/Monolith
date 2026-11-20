using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Encounters.Components;

/// <summary>A running encounter. Sits on its own entity at the encounter's origin and owns the ships.</summary>
[RegisterComponent]
public sealed partial class WFEncounterComponent : Component
{
    [DataField]
    public ProtoId<WFEncounterPrototype> Prototype;

    /// <summary>The encounter's name with its designation filled in.</summary>
    [DataField]
    public string Name = string.Empty;

    [DataField]
    public MapCoordinates Origin;

    /// <summary>The ships by their prototype key. A deleted ship keeps its entry.</summary>
    [DataField]
    public Dictionary<string, WFEncounterShipState> Ships = new();

    [DataField]
    public TimeSpan Started;

    /// <summary>When it expires, if it does.</summary>
    [DataField]
    public TimeSpan? Expires;

    /// <summary>Set once resolved; the ships are then removed as players leave them.</summary>
    [DataField]
    public WFEncounterResolution? Resolution;

    /// <summary>When it resolved.</summary>
    [DataField]
    public TimeSpan? ResolvedAt;

    [DataField]
    public WFEncounterCategory Category;

    [DataField]
    public WFEncounterIcon Icon;

    [DataField]
    public int Cost;

    [DataField]
    public WFEncounterLifetime Lifetime;

    /// <summary>A round-start or round-long encounter: it takes no slot under the cap and nothing from the budget.</summary>
    [DataField]
    public bool OffBudget;

    /// <summary>A freight run, on its own schedule.</summary>
    public bool Freight;

    /// <summary>While it runs, its station counts as blockaded.</summary>
    public bool Blockades;

    /// <summary>Whether it is kept off the sector markers.</summary>
    [DataField]
    public bool Hidden;

    /// <summary>Started by an admin: it stays, ships and all, with no clock on it, until an admin ends it.</summary>
    [DataField]
    public bool Pinned;

    /// <summary>Whether its ships are simulated while nobody is near. A warrant on a captain names him, so its encounter is always real.</summary>
    [DataField]
    public bool Simulated;

    /// <summary>The cargo manifest picked for the run, loaded when a ship is spawned.</summary>
    [DataField]
    public ProtoId<WFEncounterManifestPrototype>? Manifest;

    /// <summary>The threat tier it spawned at, if its ships fly by tier.</summary>
    [DataField]
    public int? Tier;

    /// <summary>The threat rank shown on the map; none for an unranked encounter.</summary>
    [DataField]
    public int? Threat;

    /// <summary>The stations its placement chose, in the order a route calls at them.</summary>
    [DataField]
    public List<EntityUid> Stops = new();

    [DataField]
    public bool AnnounceOnRadio;

    /// <summary>Key of the ship whose crew make a radio announcement.</summary>
    [DataField]
    public string Announcer = string.Empty;

    /// <summary>The distance a player must come within before the ships get their orders; zero for at once.</summary>
    [DataField]
    public float StartRadius;

    /// <summary>How far from the origin a ship may stray before it is called back; zero for no limit.</summary>
    [DataField]
    public float Leash;

    /// <summary>Whether the ships have their orders.</summary>
    [DataField]
    public bool Begun;

    /// <summary>The announcement still to be made, if any.</summary>
    [DataField]
    public string? Announcement;

    [DataField]
    public string? AnnouncementSender;

    [DataField]
    public Robust.Shared.Audio.SoundSpecifier? AnnouncementSound;

    [DataField]
    public Color? AnnouncementColor;

    /// <summary>Ship weapon hits by each player's ship on the ships of each side.</summary>
    public Dictionary<Robust.Shared.Network.NetUserId, Dictionary<string, int>> Hits = new();

    /// <summary>Crewmen of each side each player downed in person, kills and assists.</summary>
    public Dictionary<Robust.Shared.Network.NetUserId, Dictionary<string, int>> Kills = new();

    /// <summary>The same count per attacking ship.</summary>
    public Dictionary<(EntityUid Attacker, string Side), int> ShipHits = new();

    /// <summary>Player ships a side has taken as allies for the encounter.</summary>
    public List<(EntityUid Ship, EntityUid Ally)> Allies = new();

    /// <summary>When each other vessel's ship weapon hits landed on each side over the last half minute, excused or not: whom it is really fighting.</summary>
    public Dictionary<(EntityUid Attacker, string Side), List<TimeSpan>> Fire = new();

    /// <summary>The vessels each side has hailed, and when it may hail each again: one voice for a side, however many ships.</summary>
    public Dictionary<(string Side, EntityUid Intruder), TimeSpan> Hailed = new();

    /// <summary>The vessels each side has called its guns onto, and when it may call each again.</summary>
    public Dictionary<(string Side, EntityUid Intruder), TimeSpan> Called = new();

    /// <summary>The vessels its interrogating ships have asked, and when each may be asked again.</summary>
    public Dictionary<EntityUid, TimeSpan> Interrogated = new();

    /// <summary>When its ships jump out, whoever is near.</summary>
    [DataField]
    public TimeSpan? JumpAt;
}

/// <summary>One ship of a running encounter.</summary>
[DataDefinition]
public sealed partial class WFEncounterShipState
{
    [DataField]
    public EntityUid Grid;

    [DataField]
    public string Group = string.Empty;

    [DataField]
    public string Side = string.Empty;

    /// <summary>Whether its prototype gave it orders, so an empty queue means they are done.</summary>
    [DataField]
    public bool HasOrders;

    /// <summary>
    /// Whether the queue the encounter last gave it has been flown to its end or skipped through, as its crew reported
    /// it. Reset whenever it is given a new queue.
    /// </summary>
    [DataField]
    public bool Flown;

    /// <summary>Its radar colour, when the encounter gave it one.</summary>
    [DataField]
    public Color? Color;

    /// <summary>Its own radar glyph; Category for the encounter's.</summary>
    [DataField]
    public WFEncounterIcon Icon;

    /// <summary>How it arrived stranded, if it did.</summary>
    [DataField]
    public WFEncounterStranding Stranding = WFEncounterStranding.None;

    /// <summary>Whether players got it under way again after it arrived stranded.</summary>
    [DataField]
    public bool Rescued;

    /// <summary>A ship too small for an engineer: its generators are topped up by themselves while the crew live.</summary>
    [DataField]
    public bool AutoRefuel;

    /// <summary>While stranded: since when it has had thrust without a break.</summary>
    public TimeSpan? UnderwaySince;

    /// <summary>Whether it strayed past the encounter's leash and is flying back.</summary>
    public bool Recalled;

    /// <summary>Whether it is a crewless hulk, in the encounter for as long as it is there.</summary>
    [DataField]
    public bool Derelict;

    /// <summary>Whether its hull got pirate retrofitting that mounts guns: a civilian hull the raiders armed.</summary>
    [DataField]
    public bool Retrofitted;

    /// <summary>Whether it is simulated: <see cref="Grid"/> is then a plain stand-in entity at its position, not a hull.</summary>
    [DataField]
    public bool Virtual;

    /// <summary>The hull it flies, picked when the encounter started.</summary>
    [DataField]
    public ProtoId<VesselPrototype>? Vessel;

    /// <summary>Its crew's skill, or none for each crewman to take one from the profile's pool.</summary>
    [DataField]
    public WFCrewSkill? Skill;

    [DataField]
    public ProtoId<WFPirateRetrofitPrototype>? Retrofit;

    /// <summary>Seeds its retrofit, so it looks the same each time it is spawned.</summary>
    [DataField]
    public int Seed;

    /// <summary>Its name, given to the hull or its stand-in.</summary>
    [DataField]
    public string Name = string.Empty;

    /// <summary>Crew it carried when it was last spawned; a real ship that has lost any is never simulated again.</summary>
    [DataField]
    public int Crew;

    /// <summary>Since when no player has been near this real ship.</summary>
    public TimeSpan? AloneSince;

    /// <summary>Its hull is spawned but not yet refitted, crewed and powered; it is left alone until it is.</summary>
    public bool Building;

    /// <summary>Players have boarded or docked with it, or anyone has shot it: it stays real.</summary>
    [DataField]
    public bool Touched;

    /// <summary>The ship it was docked with while simulated and is docking with again; it counts as docked meanwhile.</summary>
    [DataField]
    public EntityUid? RedockTo;

    /// <summary>Whether it lies in wait, its IFF label hidden, until the encounter begins.</summary>
    [DataField]
    public bool Lurking;

    /// <summary>Whether it flies unlabelled for good and keeps its encounter off the sector markers.</summary>
    [DataField]
    public bool Stealth;

    /// <summary>When its fuel stores are next checked, for a persistent ship.</summary>
    public TimeSpan NextStock;

    [DataField]
    public float WarnRange;

    [DataField]
    public float AttackRange;

    [DataField]
    public string ZoneLines = "wf-encounter-zone";

    [DataField]
    public WFEncounterZoneTargets ZoneTargets;

    /// <summary>Whether its zones leave player ships with hidden or unlabelled IFF alone until they fire.</summary>
    [DataField]
    public bool KnownThreatsOnly;

    /// <summary>Whether its zones take on only NPC ships, leaving every player ship alone until it fires.</summary>
    [DataField]
    public bool NpcTargetsOnly;

    /// <summary>Companies its zones and hunt leave alone: never warned, fired on or boarded, though a shot at it is still answered.</summary>
    [DataField]
    public List<string> NeutralCompanies = new();

    /// <summary>Since when no player has been near it, while resolved.</summary>
    public TimeSpan? Quiet;

    /// <summary>When players were last aboard it or docked with it, while resolved.</summary>
    public TimeSpan? LastVisited;

    /// <summary>When it last fought players: its crew raised the alarm, a player ship hit it or a player downed one of its crew.</summary>
    public TimeSpan? LastFought;

    /// <summary>Whether it hunts player ships to dock with and board.</summary>
    [DataField]
    public bool Hunt;

    /// <summary>The ship it is trying to dock with.</summary>
    [DataField]
    public EntityUid? Prey;

    /// <summary>Guns mounted at spawn to stop the ship it means to board.</summary>
    [DataField]
    public List<EntProtoId> Disablers = new();

    /// <summary>Set on an extorter: where it is in its round of tolls.</summary>
    public WFExtortState? Extort;

    /// <summary>Set on an abductor: its run at a mark and whoever it holds.</summary>
    public WFAbductState? Abduct;

    /// <summary>Whether it answers an attack by turning on the attacker.</summary>
    [DataField]
    public bool Evades = true;

    /// <summary>How near a player ship may come before it runs; zero for never.</summary>
    [DataField]
    public float Flees;

    /// <summary>Whether it is flying a run from a player ship, not its own route.</summary>
    public bool Fleeing;

    /// <summary>The direction of the run it is on.</summary>
    public System.Numerics.Vector2 FleeHeading;

    /// <summary>Where the run ends.</summary>
    public System.Numerics.Vector2 FleeGoal;

    /// <summary>The ship was taken out of the encounter by cleanup or an admin, not destroyed.</summary>
    public bool Removed;

    /// <summary>Whether it calls for help when adrift.</summary>
    [DataField]
    public bool Distress = true;

    /// <summary>While docked to its prey: when the boarding party is called back.</summary>
    public TimeSpan? RaidEnds;

    /// <summary>The boarding party and the posts they go back to.</summary>
    public Dictionary<EntityUid, Robust.Shared.Map.EntityCoordinates?> BoardingParty = new();

    /// <summary>Whether its raid is over and it is leaving.</summary>
    [DataField]
    public bool Raided;

    /// <summary>Since when the ship has had no thrust while not in a fight.</summary>
    public TimeSpan? AdriftSince;

    /// <summary>Since when its queue has had nobody left to fly it.</summary>
    public TimeSpan? NoPilotSince;

    /// <summary>When it may next call for help.</summary>
    public TimeSpan NextDistress;

    /// <summary>When the ship may next call for help because it or its passengers are attacked.</summary>
    public TimeSpan NextAttackCall;

    /// <summary>Whether it carries passengers, such as a trader, and so speaks for them on the radio.</summary>
    public bool Passengers;

    /// <summary>Whether its orders are paused while players are aboard or docked with it.</summary>
    public bool Serving;

    /// <summary>Until when it holds position for a ship that hailed it to stop, so that ship can come alongside.</summary>
    public TimeSpan? HeldUntil;

    /// <summary>The ship a hunter's raid went aboard.</summary>
    public EntityUid? Boarded;

    /// <summary>When its crew next top up its batteries.</summary>
    public TimeSpan NextPower;

    /// <summary>Intruders already warned, and when each may be warned again.</summary>
    public Dictionary<EntityUid, TimeSpan> Warned = new();

    /// <summary>Intruders inside the attack zone that have been told so.</summary>
    public HashSet<EntityUid> Engaged = new();

    /// <summary>Vessels that answered its interrogation as a threat: an interrogating zone fires on them for the encounter's life.</summary>
    public HashSet<EntityUid> Declared = new();

    /// <summary>Vessels in its warning zone that an interrogating zone has no cause to fire on, as of its last look: those it asks.</summary>
    public HashSet<EntityUid> Strangers = new();
}

/// <summary>Marks a passenger an encounter put aboard one of its ships.</summary>
[RegisterComponent]
public sealed partial class WFEncounterPassengerComponent : Component
{
    public EntityUid Encounter;

    public string Key = string.Empty;
}
