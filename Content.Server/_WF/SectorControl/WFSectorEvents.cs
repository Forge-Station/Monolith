using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.SectorControl;

/// <summary>Raised, by ref and broadcast, before a faction claims or contests a cell; set Cancelled to veto it.</summary>
[ByRefEvent]
public record struct WFSectorClaimAttemptEvent(MapId Map, WFSectorCell Cell, ProtoId<WFSectorFactionPrototype> Faction, EntityUid? Source)
{
    public bool Cancelled;
}

/// <summary>Raised, by ref and broadcast, when a cell's owner changes; a null owner is unheld.</summary>
[ByRefEvent]
public readonly record struct WFSectorCellChangedEvent(
    MapId Map,
    WFSectorCell Cell,
    ProtoId<WFSectorFactionPrototype>? Previous,
    ProtoId<WFSectorFactionPrototype>? Owner);

/// <summary>Raised, by ref and broadcast, when a faction contests a cell or the contest's deadline moves. Drivers announce it.</summary>
[ByRefEvent]
public readonly record struct WFSectorCellContestedEvent(
    MapId Map,
    WFSectorCell Cell,
    ProtoId<WFSectorFactionPrototype> Faction,
    TimeSpan Deadline);

/// <summary>Raised, by ref and broadcast, when a contest ends: claimed at its deadline, or dropped.</summary>
[ByRefEvent]
public readonly record struct WFSectorContestResolvedEvent(
    MapId Map,
    WFSectorCell Cell,
    ProtoId<WFSectorFactionPrototype> Faction,
    bool Claimed);

/// <summary>Raised, by ref and broadcast, when a cell becomes or stops being disputed: a faction's ship and a crewed player ship are in it together.</summary>
[ByRefEvent]
public readonly record struct WFSectorCellDisputedEvent(
    MapId Map,
    WFSectorCell Cell,
    ProtoId<WFSectorFactionPrototype> Faction,
    bool Disputed);

/// <summary>Raised, by ref and broadcast, before a held cell is retaken from its faction; set Cancelled to veto it.</summary>
[ByRefEvent]
public record struct WFSectorRetakeAttemptEvent(MapId Map, WFSectorCell Cell, ProtoId<WFSectorFactionPrototype> Faction)
{
    public bool Cancelled;
}

/// <summary>Raised, by ref and broadcast, when a retake countdown starts on a held cell, with the time it frees it, or stops with none.</summary>
[ByRefEvent]
public readonly record struct WFSectorCellRetakingEvent(MapId Map, WFSectorCell Cell, ProtoId<WFSectorFactionPrototype> Faction, TimeSpan? Deadline);

/// <summary>
/// Raised, by ref and broadcast, when a held cell was retaken and freed; <c>Faction</c> is the one that held it and
/// <c>Ships</c> the crewed player ships that held the cell through the whole countdown.
/// </summary>
[ByRefEvent]
public readonly record struct WFSectorCellRetakenEvent(
    MapId Map,
    WFSectorCell Cell,
    ProtoId<WFSectorFactionPrototype> Faction,
    IReadOnlyList<EntityUid>? Ships = null);

/// <summary>Raised, by ref and broadcast, when a freed cell starts clearing until a time; <c>Previous</c> is the faction that held it.</summary>
[ByRefEvent]
public readonly record struct WFSectorCellClearingEvent(MapId Map, WFSectorCell Cell, TimeSpan Until, ProtoId<WFSectorFactionPrototype> Previous);

/// <summary>Raised, by ref and broadcast, when a cell's clearing period runs out; a cell claimed again while clearing raises nothing.</summary>
[ByRefEvent]
public readonly record struct WFSectorCellClearedEvent(MapId Map, WFSectorCell Cell);

/// <summary>Raised, by ref and broadcast, when territory was cleared by a switch or a resize and is on again; drivers claim what they hold anew.</summary>
[ByRefEvent]
public readonly record struct WFSectorResetEvent;
