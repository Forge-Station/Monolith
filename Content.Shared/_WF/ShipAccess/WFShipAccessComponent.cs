using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipAccess;

/// <summary>
/// Who may open a purchased ship, applied through the normal airlock access readers of its doors and lockers:
/// whoever carries its deed, the players an admin tool registered it to, the ID cards on its allow list and, in
/// Faction mode, the company's access level. Lives on the grid. The list holds the crew record keys the cards carry, as a station airlock's own keys do.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFShipAccessComponent : Component
{
    /// <summary>The buyer's character name at purchase, shown on the tab. Grants nothing; the deed card does.</summary>
    [DataField, AutoNetworkedField]
    public string OwnerName = string.Empty;

    /// <summary>Who besides the deed and the allow list may enter; see <see cref="WFShipAccessMode"/>.</summary>
    [DataField, AutoNetworkedField]
    public WFShipAccessMode Mode = WFShipAccessMode.Private;

    /// <summary>
    /// ID cards that may open the ship, by the record key they carry. Round state, since record keys point at
    /// this round's stations, so not saved with the grid. Codes (F3.3) must never be added to a networked field here.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public List<WFShipAccessEntry> AllowList = new();

    /// <summary>
    /// The buyer's card key, taken at purchase. It stands in for the owner only while no ID card holding the deed has
    /// a crew record, as after a voucher purchase. Server only and round state.
    /// </summary>
    [ViewVariables]
    public WFShipAccessKey? BuyerKey;

    /// <summary>
    /// Players who own the ship whatever body they play, set by the admin vessel spawner and the ERT builder: they edit
    /// its access like the deed holder, and the ID card each one wears is keyed to its doors. Round state.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public List<NetUserId> OwnerUsers = new();

    /// <summary>
    /// The owner record keys the readers were last written with, so an ID card carrying one edits the ship's access
    /// like the deed holder. Written by the server; round state.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public List<WFShipAccessKey> OwnerKeys = new();

    /// <summary>The linked scenario crew; round-scoped and server-only.</summary>
    [ViewVariables]
    public int? CrewId;

    /// <summary>The linked crew's display name, independent of its access mode.</summary>
    [ViewVariables, AutoNetworkedField]
    public string? CrewName;

    /// <summary>Issued ID cards of current crew members, separate from deliberately invited guests.</summary>
    [ViewVariables, AutoNetworkedField]
    public List<WFShipAccessKey> CrewKeys = new();

    /// <summary>The old "lock ship" toggle. While set, every door and locker reader on the grid admits only the keys above.</summary>
    [DataField, AutoNetworkedField]
    public bool Locked;

    /// <summary>The player company linked for Company access.</summary>
    [ViewVariables]
    public int? CompanyId;

    [ViewVariables, AutoNetworkedField]
    public string? CompanyName;

    /// <summary>Issued IDs of the linked company's members playing this round.</summary>
    [ViewVariables, AutoNetworkedField]
    public List<WFShipAccessKey> CompanyKeys = new();
}

/// <summary>Private admits owners and guests; Faction adds company access, and Crew adds current crew cards.</summary>
[Serializable, NetSerializable]
public enum WFShipAccessMode : byte
{
    Private,
    Faction,
    Crew,
    Company,
}

/// <summary>A crew record key as the ship access lists hold it: the record's station and id, as written on an ID card.</summary>
[Serializable, NetSerializable]
public readonly record struct WFShipAccessKey(NetEntity Station, uint Id);

/// <summary>One ID card on a ship's allow list.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAccessEntry
{
    /// <summary>The record key the card carries; the door readers are given it.</summary>
    public WFShipAccessKey Key;

    /// <summary>The name on the card when it was added, or its holder's, for display.</summary>
    public string Name = string.Empty;

    /// <summary>Shown next to the name, such as "Guest". Stored and shown, not edited in F3.1.</summary>
    public string Label = string.Empty;

    /// <summary>Stored only in F3.1; a later step lets builders modify the ship.</summary>
    public bool Builder;
}
