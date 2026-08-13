using Robust.Shared.Prototypes;
using Content.Shared.Roles;
using Robust.Shared.Serialization;
using Content.Shared._Forge.Persistence;

namespace Content.Shared.StationRecords;

[Serializable, NetSerializable]
public enum GeneralStationRecordConsoleKey : byte
{
    Key
}

/// <summary>
///     General station records console state. There are a few states:
///     - SelectedKey null, Record null, RecordListing null
///         - The station record database could not be accessed.
///     - SelectedKey null, Record null, RecordListing non-null
///         - Records are populated in the database, or at least the station has
///           the correct component.
///     - SelectedKey non-null, Record null, RecordListing non-null
///         - The selected key does not have a record tied to it.
///     - SelectedKey non-null, Record non-null, RecordListing non-null
///         - The selected key has a record tied to it, and the record has been sent.
///
///     - there is added new filters and so added new states
///         -SelectedKey null, Record null, RecordListing null, filters non-null
///            the station may have data, but they all did not pass through the filters
///
///     Other states are erroneous.
/// </summary>
[Serializable, NetSerializable]
public sealed class GeneralStationRecordConsoleState : BoundUserInterfaceState
{
    /// <summary>
    /// Current selected key.
    /// Station is always the station that owns the console.
    /// </summary>
    public readonly uint? SelectedKey;
    public readonly GeneralStationRecord? Record;
    public readonly Dictionary<uint, string>? RecordListing;
    public IReadOnlyDictionary<ProtoId<JobPrototype>, int?>? JobList { get; } // Frontier
    public readonly StationRecordsFilter? Filter;
    public readonly bool CanDeleteEntries;
    public readonly string? Advertisement; // Frontier
    public readonly List<HangarVesselCrewRecord>? ShuttleCrew;
    public readonly List<StationRecordsCrewCandidate>? ShuttleCrewCandidates;
    public readonly bool CanManageShuttleCrew;

    public GeneralStationRecordConsoleState(uint? key, GeneralStationRecord? record,
        Dictionary<uint, string>? recordListing, IReadOnlyDictionary<ProtoId<JobPrototype>, int?>? jobList,
        StationRecordsFilter? newFilter, bool canDeleteEntries, string? advertisement,
        List<HangarVesselCrewRecord>? shuttleCrew = null,
        List<StationRecordsCrewCandidate>? shuttleCrewCandidates = null,
        bool canManageShuttleCrew = false) // Frontier: add jobList, advertisement
    {
        SelectedKey = key;
        Record = record;
        RecordListing = recordListing;
        Filter = newFilter;
        JobList = jobList; // Frontier
        CanDeleteEntries = canDeleteEntries;
        Advertisement = advertisement; // Frontier
        ShuttleCrew = shuttleCrew;
        ShuttleCrewCandidates = shuttleCrewCandidates;
        CanManageShuttleCrew = canManageShuttleCrew;
    }

    public GeneralStationRecordConsoleState() : this(null, null, null, null, null, false, string.Empty)
    {
    }

    public bool IsEmpty() => SelectedKey == null
        && Record == null && RecordListing == null;
}

[Serializable, NetSerializable]
public sealed record StationRecordsCrewCandidate(
    Guid PlayerUserId,
    int CharacterSlot,
    string CharacterName);

/// <summary>
/// Select a specific crewmember's record, or deselect.
/// Used by any kind of records console including general and criminal.
/// </summary>
[Serializable, NetSerializable]
public sealed class SelectStationRecord : BoundUserInterfaceMessage
{
    public readonly uint? SelectedKey;

    public SelectStationRecord(uint? selectedKey)
    {
        SelectedKey = selectedKey;
    }
}


[Serializable, NetSerializable]
public sealed class DeleteStationRecord : BoundUserInterfaceMessage
{
    public DeleteStationRecord(uint id)
    {
        Id = id;
    }

    public readonly uint Id;
}

[Serializable, NetSerializable]
public sealed class RefreshShuttleCrewMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class AddShuttleCrewMemberMessage(Guid playerUserId, int characterSlot)
    : BoundUserInterfaceMessage
{
    public Guid PlayerUserId { get; } = playerUserId;
    public int CharacterSlot { get; } = characterSlot;
}

[Serializable, NetSerializable]
public sealed class RemoveShuttleCrewMemberMessage(Guid playerUserId, int characterSlot)
    : BoundUserInterfaceMessage
{
    public Guid PlayerUserId { get; } = playerUserId;
    public int CharacterSlot { get; } = characterSlot;
}
