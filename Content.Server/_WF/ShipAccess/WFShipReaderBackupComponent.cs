using Content.Shared.Access;
using Content.Shared.StationRecords;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ShipAccess;

/// <summary>
/// The access a reader had before ship access took it over, put back when the ship is unlocked and the door has no
/// rule, when the reader leaves the ship, or when the ship is resold. Lives on the reader itself (an airlock's door
/// electronics), so it moves with the board. Server only; saved with the grid so a resale can restore it.
/// </summary>
[RegisterComponent]
public sealed partial class WFShipReaderBackupComponent : Component
{
    /// <summary>The reader's access lists as the mapper or the door's electronics set them.</summary>
    [DataField]
    public List<HashSet<ProtoId<AccessLevelPrototype>>> Access = new();

    /// <summary>The reader's own record keys, rarely set on a ship.</summary>
    [DataField]
    public HashSet<StationRecordKey> Keys = new();

    /// <summary>Whether the ship last wrote the locked-ship access into the reader.</summary>
    [DataField]
    public bool Locked;

    /// <summary>
    /// Set once something else rewrote a reader the ship had locked (an emag, an access configurator). The ship
    /// leaves it alone from then on, as that change would stick on any airlock.
    /// </summary>
    [DataField]
    public bool Released;
}

/// <summary>
/// Marks a locker or crate that came with the ship. Ship access takes such storage over whatever it asks for;
/// storage brought aboard later is only taken over when it asks for no access at all, so the ship never unlocks
/// someone else's secure locker for its owner.
/// </summary>
[RegisterComponent]
public sealed partial class WFShipStorageComponent : Component;
