using Content.Shared._WF.Encounters;
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

    /// <summary>Whether it is kept off the sector markers.</summary>
    [DataField]
    public bool Hidden;

    /// <summary>Started by an admin: it stays, ships and all, with no clock on it, until an admin ends it.</summary>
    [DataField]
    public bool Pinned;

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

    /// <summary>Whether it lies in wait, its IFF label hidden, until the encounter begins.</summary>
    [DataField]
    public bool Lurking;

    [DataField]
    public float WarnRange;

    [DataField]
    public float AttackRange;

    [DataField]
    public string ZoneLines = "wf-encounter-zone";

    [DataField]
    public WFEncounterZoneTargets ZoneTargets;

    /// <summary>Companies its zones and hunt leave alone: never warned, fired on or boarded, though a shot at it is still answered.</summary>
    [DataField]
    public List<string> NeutralCompanies = new();

    /// <summary>Since when no player has been near it, while resolved.</summary>
    public TimeSpan? Quiet;

    /// <summary>Whether it hunts player ships to dock with and board.</summary>
    [DataField]
    public bool Hunt;

    /// <summary>The ship it is trying to dock with.</summary>
    [DataField]
    public EntityUid? Prey;

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

    /// <summary>The ship a hunter's raid went aboard.</summary>
    public EntityUid? Boarded;

    /// <summary>When its crew next top up its batteries.</summary>
    public TimeSpan NextPower;

    /// <summary>Intruders already warned, and when each may be warned again.</summary>
    public Dictionary<EntityUid, TimeSpan> Warned = new();

    /// <summary>Intruders inside the attack zone that have been told so.</summary>
    public HashSet<EntityUid> Engaged = new();
}

/// <summary>Marks a passenger an encounter put aboard one of its ships.</summary>
[RegisterComponent]
public sealed partial class WFEncounterPassengerComponent : Component
{
    public EntityUid Encounter;

    public string Key = string.Empty;
}
