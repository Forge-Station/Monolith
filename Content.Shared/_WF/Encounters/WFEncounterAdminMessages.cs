using Robust.Shared.Serialization;

namespace Content.Shared._WF.Encounters;

[Serializable, NetSerializable]
public enum WFEncounterAdminAction : byte
{
    /// <summary>Only asks for the current state.</summary>
    Refresh,

    /// <summary>Starts a prototype north of the admin.</summary>
    Spawn,

    /// <summary>Marks an encounter as over; its ships stay until players leave them.</summary>
    Resolve,

    /// <summary>Ends an encounter and removes its ships now.</summary>
    End,

    /// <summary>Has the scheduler pick and start one now.</summary>
    Schedule,

    /// <summary>Applies the scheduler settings in the request.</summary>
    Scheduler,

    /// <summary>Moves the admin to an encounter or one of its ships.</summary>
    Teleport,

    /// <summary>Makes the preset named in Prototype the storyteller's.</summary>
    Preset,

    /// <summary>Puts a hidden encounter on the sector markers.</summary>
    Reveal,

    /// <summary>Places this round's round-start encounters now.</summary>
    StartRound,

    /// <summary>Has one ship of an encounter carry out an admin's order: see <see cref="WFEncounterAdminOrder"/>.</summary>
    Order,
}

/// <summary>What an admin can have an encounter ship do from the window.</summary>
[Serializable, NetSerializable]
public enum WFEncounterAdminOrder : byte
{
    /// <summary>An extorter goes after a target ship for its toll.</summary>
    Extort,

    /// <summary>A boarding ship docks with a target ship and raids it.</summary>
    Board,

    /// <summary>Engages a target ship.</summary>
    Attack,

    /// <summary>Keeps station on a target ship.</summary>
    Follow,

    /// <summary>Flies to where the admin is.</summary>
    ComeToMe,

    /// <summary>Stops where it is.</summary>
    Hold,

    /// <summary>Takes up its own orders again.</summary>
    Resume,

    /// <summary>A simulated ship has its hull built now.</summary>
    SpawnNow,

    /// <summary>The encounter is wrapped up and its ships jump out.</summary>
    SendAway,
}

/// <summary>An admin's request from the encounter window. Every request is answered with the current state.</summary>
[Serializable, NetSerializable]
public sealed class WFEncounterAdminRequest : EntityEventArgs
{
    public WFEncounterAdminAction Action;
    public string Prototype = string.Empty;

    /// <summary>The encounter, or for a teleport the encounter or ship.</summary>
    public NetEntity? Target;
    public float Distance = 300f;
    /// <summary>Spawn: whether the encounter is kept until an admin ends it, or jumps out and is cleaned up as usual.</summary>
    public bool Pinned = true;
    /// <summary>Spawn: the threat tier for ships that fly by tier; none takes the sector threat's.</summary>
    public int? Tier;
    public bool Enabled;
    public bool Paused;
    public float IntervalMin;
    public float IntervalMax;
    public int MaxActive;

    /// <summary>Order: the ship of the encounter in <see cref="Target"/>, what it is to do, and the ship it is to do it to.</summary>
    public string Key = string.Empty;
    public WFEncounterAdminOrder Order;
    public NetEntity? OrderTarget;
}

/// <summary>Everything the encounter window shows.</summary>
[Serializable, NetSerializable]
public sealed class WFEncounterAdminState : EntityEventArgs
{
    /// <summary>Why the request did nothing, if it did nothing.</summary>
    public string Message = string.Empty;
    public List<WFEncounterAdminEntry> Encounters = new();
    public List<WFEncounterAdminPrototype> Prototypes = new();
    public bool Enabled;
    public bool Paused;
    public float IntervalMin;
    public float IntervalMax;
    public int MaxActive;

    /// <summary>Seconds until the scheduler next tries, or negative while it is not running.</summary>
    public float NextIn = -1f;
    public string Preset = string.Empty;
    public List<WFEncounterAdminPreset> Presets = new();
    public int Budget;
    public int Cost;

    /// <summary>Ships with living players aboard, nearest the admin first: what an order can be aimed at.</summary>
    public List<WFEncounterAdminTarget> Targets = new();
}

/// <summary>A player ship an order can be aimed at.</summary>
[Serializable, NetSerializable]
public sealed class WFEncounterAdminTarget
{
    public NetEntity Grid;
    public string Name = string.Empty;

    /// <summary>The players aboard, by character name.</summary>
    public string Players = string.Empty;

    /// <summary>Metres from the admin, or negative on another map.</summary>
    public float Distance = -1f;
}

[Serializable, NetSerializable]
public sealed class WFEncounterAdminEntry
{
    public NetEntity Uid;
    public string Prototype = string.Empty;
    public string Name = string.Empty;
    public string State = string.Empty;
    public bool Resolved;
    public bool Hidden;
    /// <summary>Started by an admin, so it stays until ended.</summary>
    public bool Pinned;
    public float Age;

    /// <summary>Seconds until it expires, or negative if it never does.</summary>
    public float ExpiresIn = -1f;

    /// <summary>Its threat rank on the map, if it has one.</summary>
    public int? Threat;
    public List<WFEncounterAdminShip> Ships = new();
}

[Serializable, NetSerializable]
public sealed class WFEncounterAdminShip
{
    public string Key = string.Empty;
    public NetEntity Grid;
    public string Name = string.Empty;
    public string Group = string.Empty;
    public string Side = string.Empty;
    public bool Exists;
    public bool Disabled;
    public int Crew;
    public string Activity = string.Empty;

    /// <summary>Simulated: a point on the map with no hull, until a player comes near.</summary>
    public bool Virtual;

    /// <summary>Its hull is being built.</summary>
    public bool Building;

    /// <summary>It can be ordered to extort, or to board.</summary>
    public bool Extorter;
    public bool Hunter;
}

[Serializable, NetSerializable]
public sealed class WFEncounterAdminPreset
{
    public string Id = string.Empty;
    public string Name = string.Empty;
}

[Serializable, NetSerializable]
public sealed class WFEncounterAdminPrototype
{
    public string Id = string.Empty;
    public bool Scheduled;
    public float Weight;
    public bool Running;
}
