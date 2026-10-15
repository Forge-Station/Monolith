using System.Numerics;
using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Drydock;

/// <summary>
/// UI key for the drydock console.
/// </summary>
[Serializable, NetSerializable]
public enum WFDrydockConsoleUiKey : byte
{
    Key
}

/// <summary>
/// State of the drydock window: the caller's berthed ship, damage, repair price, job progress, refits and booking.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFDrydockConsoleState : BoundUserInterfaceState
{
    /// <summary>Name of the berthed ship, or null when none is docked.</summary>
    public string? ShipName;

    /// <summary>Hull tiles missing from the repair snapshot.</summary>
    public int MissingTiles;

    /// <summary>Parts missing from the repair snapshot.</summary>
    public int MissingParts;

    /// <summary>Anchored structures that need healing.</summary>
    public int DamagedParts;

    /// <summary>Total price of the next repair job, including the base fee.</summary>
    public int RepairPrice;

    /// <summary>Whether a job is running.</summary>
    public bool Working;

    /// <summary>Lowest power ratio among the berth's cranes, 0 to 1.</summary>
    public float PowerRatio = 1f;

    /// <summary>Cranes linked to this console on its grid.</summary>
    public int CraneCount;

    /// <summary>Linked cranes whose arm lane is blocked, so they do no work.</summary>
    public int BlockedCranes;

    /// <summary>Whether the cranes are still moving into place over the ship.</summary>
    public bool Deploying;

    /// <summary>Job progress from 0 to 1.</summary>
    public float Progress;

    /// <summary>Seconds until the running job is estimated to finish, 0 when idle.</summary>
    public float RemainingSeconds;

    /// <summary>Refit lines offered by the console.</summary>
    public List<WFDrydockUpgradeEntry> Upgrades = new();

    /// <summary>Whether this console books work for a berth instead of starting it.</summary>
    public bool Booking;

    /// <summary>Berth booked for this ship, or null if none.</summary>
    public string? BookedBerth;

    /// <summary>Seconds left on the booking.</summary>
    public float BookingSeconds;

    /// <summary>Localized line explaining why nothing can be booked, or null.</summary>
    public string? Notice;

    /// <summary>Whether this console charges materials instead of credits.</summary>
    public bool PaysWithMaterials;

    /// <summary>Bill of the next repair when paying with materials.</summary>
    public Dictionary<string, int> RepairMaterials = new();

    /// <summary>Parts the next repair takes from the parts silos in place of materials, by prototype.</summary>
    public Dictionary<string, int> RepairParts = new();

    /// <summary>Materials the working cranes and their silos hold.</summary>
    public Dictionary<string, int> StoredMaterials = new();

    /// <summary>Everything the working cranes' parts silos hold.</summary>
    public List<WFDrydockStoredPart> StoredParts = new();

    /// <summary>Reagents the working cranes' chemical silos hold, by prototype.</summary>
    public Dictionary<string, FixedPoint2> StoredChemicals = new();

    /// <summary>Whether this console breaks hulls.</summary>
    public bool CanBreak;

    /// <summary>Whether the ship in the berth may be broken here.</summary>
    public bool Breakable;

    /// <summary>Whether the running job breaks the ship.</summary>
    public bool Breaking;

    /// <summary>Materials breaking the ship would recover, within what is left of its caps.</summary>
    public Dictionary<string, int> BreakYield = new();

    /// <summary>Whether the console's berth lights are on.</summary>
    public bool BerthLightsOn;

    /// <summary>Grid the berth stands on; the berth plot draws in its frame.</summary>
    public NetEntity? Station;

    /// <summary>Berth console whose linked cranes the berth plot draws.</summary>
    public NetEntity? BerthConsole;

    /// <summary>Name of the berth shown on the plot, or null when there is no berth.</summary>
    public string? BerthLabel;

    /// <summary>Berth work area in the station grid's frame.</summary>
    public Box2 BerthArea;

    /// <summary>Direction from the berth's centre toward its mouth, in the station grid's frame.</summary>
    public Vector2 Approach;

    /// <summary>Ship in or booked to the berth.</summary>
    public NetEntity? Ship;

    /// <summary>Outstanding work on the ship, in the ship's frame, at most <see cref="MaxMarks"/>.</summary>
    public List<WFDrydockPlotMark> Marks = new();

    /// <summary>Most work marks sent to the berth plot.</summary>
    public const int MaxMarks = 512;
}

/// <summary>How many of one item the parts silos hold, by prototype and name, since salvaged flatpacks differ only by name.</summary>
[Serializable, NetSerializable]
public readonly record struct WFDrydockStoredPart(string Prototype, string Name, int Count);

/// <summary>What a berth plot mark shows.</summary>
[Serializable, NetSerializable]
public enum WFDrydockMarkKind : byte
{
    Tile,
    Part,
    Damage,
    Refit,
}

/// <summary>One piece of outstanding work on the berth plot, in the ship's frame.</summary>
[Serializable, NetSerializable]
public readonly record struct WFDrydockPlotMark(Vector2 Position, WFDrydockMarkKind Kind);

/// <summary>
/// One refit line in the console state.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFDrydockUpgradeEntry
{
    /// <summary>Refit prototype id.</summary>
    public string Id = string.Empty;

    /// <summary>Localized name.</summary>
    public string Name = string.Empty;

    /// <summary>Localized phrase in place of "to Mk N" on the keys, or null.</summary>
    public string? Mark;

    /// <summary>Localized text in place of the lamp numeral, or null.</summary>
    public string? Lamp;

    /// <summary>Units the refit covers on the berthed ship, such as machine parts.</summary>
    public int Units;

    /// <summary>Tier the ship owns; 0 is none.</summary>
    public int Tier;

    /// <summary>Highest tier of the refit.</summary>
    public int MaxTier;

    /// <summary>Tier the next refit would buy, past any its units already reach; 0 when there is none.</summary>
    public int NextTier;

    /// <summary>Price of the next refit, units the ship owns at its tier restored at the repair share.</summary>
    public int NextPrice;

    /// <summary>Bill of the next refit when paying with materials.</summary>
    public Dictionary<string, int> NextMaterials = new();

    /// <summary>Parts the next refit takes from the parts silos in place of materials, by prototype.</summary>
    public Dictionary<string, int> NextParts = new();
}

/// <summary>
/// Starts a repair on the berthed ship, or books one through a dockmaster.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFDrydockRepairMessage : BoundUserInterfaceMessage;

/// <summary>
/// Starts breaking up the berthed ship.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFDrydockBreakMessage : BoundUserInterfaceMessage;

/// <summary>
/// Turns the console's berth lights on or off.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFDrydockToggleBerthLightsMessage : BoundUserInterfaceMessage;

/// <summary>
/// Cancels the booking held for the caller's ship.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFDrydockCancelBookingMessage : BoundUserInterfaceMessage;

/// <summary>
/// Starts or books the next tier of a refit line.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFDrydockUpgradeMessage(string upgrade) : BoundUserInterfaceMessage
{
    /// <summary>Refit prototype id.</summary>
    public string Upgrade = upgrade;
}
