using System.Linq;
using Content.Server._NF.Station.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.StationEvents.Components;
using Content.Server.StationRecords;
using Content.Shared._Mono.Company;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._NF.ShuttleRecords;
using Content.Shared.Access.Components;
using Content.Shared.Forensics.Components;
using Content.Shared.Maps;
using Content.Shared.Preferences;
using Content.Shared.Shuttles.Components;
using Content.Shared.StationRecords;
using Content.Shared.Tag;
using Robust.Shared.Player;

namespace Content.Server._NF.Shipyard.Systems;

/// <summary>
/// Wolfgate: registers an already-spawned ship to an owner the way a shipyard purchase would. Lives in the
/// shipyard partial because the deed and lock components are access-restricted to it.
/// </summary>
public sealed partial class ShipyardSystem
{
    /// <summary>
    /// Runs every post-payment step of a shipyard purchase on an existing ship: company, late-join station,
    /// FTL lock, deeds, console locks, ownership, station and shuttle records, ship access, lifecycle,
    /// vessel tags and crew requirement. Skipped on purpose: payment, vouchers, purchase-attempt checks,
    /// ID access levels and job title, and the shipyard console messages.
    /// </summary>
    /// <param name="vessel">Design the ship was built from, if it is still known.</param>
    /// <param name="shipName">Name to keep, for a ship that already had one. Null takes the design's.</param>
    public bool TryAssignDeed(EntityUid shuttleUid, EntityUid idCard, ICommonSession owner, VesselPrototype? vessel, string? shipName = null)
    {
        if (!HasComp<ShuttleComponent>(shuttleUid) || !TryComp<IdCardComponent>(idCard, out var card))
            return false;

        var ownerEntity = owner.AttachedEntity is { Valid: true } attached ? attached : (EntityUid?)null;
        var ownerName = ownerEntity != null ? Name(ownerEntity.Value).Trim() : owner.Name;
        var name = vessel?.Name ?? Name(shuttleUid);

        if (!string.IsNullOrEmpty(card.CompanyName))
        {
            var company = EnsureComp<CompanyComponent>(shuttleUid);
            company.CompanyName = card.CompanyName;
            Dirty(shuttleUid, company);
        }

        // Ships with a matching game map get a station so players can late-join onto them.
        EntityUid? shuttleStation = null;
        if (vessel != null
            && _prototypeManager.TryIndex<GameMapPrototype>(vessel.ID, out var stationProto)
            && stationProto.Stations.TryGetValue(vessel.ID, out var stationConfig))
        {
            shuttleStation = _station.InitializeNewStation(stationConfig, new List<EntityUid> { shuttleUid });
            name = Name(shuttleStation.Value);
            EnsureComp<ExtraShuttleInformationComponent>(shuttleStation.Value).Vessel = vessel.ID;
        }

        // A ship that is changing hands rather than rolling off the line keeps the name it had.
        if (shipName != null)
            name = shipName;

        EnsureComp<FTLLockComponent>(shuttleUid);
        EntityManager.System<ShuttleConsoleSystem>().ToggleFTLLock(shuttleUid, new List<NetEntity>(), true);

        var deedId = EnsureComp<ShuttleDeedComponent>(idCard);
        AssignShuttleDeedProperties(deedId, shuttleUid, name, ownerName, false);
        deedId.DeedHolder = idCard;
        Dirty(idCard, deedId);

        var deedShuttle = EnsureComp<ShuttleDeedComponent>(shuttleUid);
        AssignShuttleDeedProperties(deedShuttle, shuttleUid, name, ownerName, false);
        Dirty(shuttleUid, deedShuttle);

        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var consoleUid, out _, out var xform))
        {
            if (xform.GridUid != shuttleUid)
                continue;

            var lockComp = EnsureComp<ShuttleConsoleLockComponent>(consoleUid);
            _shuttleConsoleLock.SetShuttleId(consoleUid, shuttleUid.ToString(), lockComp);
        }

        _shipOwnership.RegisterShipOwnership(shuttleUid, owner);

        if (shuttleStation != null)
            MoveOwnerRecord(shuttleStation.Value, idCard, ownerEntity, owner);

        AddShipAccessToEntities(shuttleUid);
        EnsureComp<LinkedLifecycleGridParentComponent>(shuttleUid);

        if (vessel != null)
        {
            EnsureComp<VesselComponent>(shuttleUid).VesselId = vessel.ID;
            EnsureComp<TagComponent>(shuttleUid);
            _tagSystem.TryAddTags(shuttleUid, vessel.Tags);
            if (vessel.RequireCrew || vessel.Classes.Contains(VesselClass.Capital) || _tagSystem.HasTag(shuttleUid, CrewedShuttleTag))
                EnsureComp<CrewedShuttleComponent>(shuttleUid);
        }

        _shuttleRecordsSystem.AddRecord(new ShuttleRecord(
            name: deedShuttle.ShuttleName ?? string.Empty,
            suffix: deedShuttle.ShuttleNameSuffix ?? string.Empty,
            ownerName: ownerName,
            entityUid: GetNetEntity(shuttleUid),
            purchasedWithVoucher: false,
            purchasePrice: (uint)(vessel?.Price ?? 0)));

        if (ownerEntity != null)
        {
            var purchaseEv = new ShipyardShuttlePurchaseEvent(shuttleUid, ownerEntity.Value);
            RaiseLocalEvent(purchaseEv);
        }

        var fullName = GetFullName(deedShuttle);
        _metaData.SetEntityName(shuttleUid, fullName);
        if (shuttleStation != null)
        {
            _station.RenameStation(shuttleStation.Value, fullName, loud: false);
            _metaData.SetEntityName(shuttleStation.Value, fullName);
        }

        return true;
    }

    /// <summary>
    /// Copies the owner's general station record onto the ship's station, or creates a Captain record
    /// from their profile. Same behaviour as a purchase, guarded so ghosts and admin bodies can't crash it.
    /// </summary>
    private void MoveOwnerRecord(EntityUid shuttleStation, EntityUid idCard, EntityUid? ownerEntity, ICommonSession owner)
    {
        var copied = false;
        if (TryComp<StationRecordKeyStorageComponent>(idCard, out var keyStorage)
            && keyStorage.Key != null
            && _records.TryGetRecord<GeneralStationRecord>(keyStorage.Key.Value, out var record))
        {
            _records.AddRecordEntry(shuttleStation, record);
            copied = true;
        }

        if (!copied
            && ownerEntity != null
            && _prefManager.GetPreferencesOrNull(owner.UserId)?.SelectedCharacter is HumanoidCharacterProfile profile
            && TryComp<FingerprintComponent>(ownerEntity, out var fingerprint)
            && TryComp<DnaComponent>(ownerEntity, out var dna)
            && TryComp<StationRecordsComponent>(shuttleStation, out var stationRecords))
        {
            _records.CreateGeneralRecord(shuttleStation, idCard, profile.Name, profile.Age, profile.Species, profile.Gender,
                "Captain", fingerprint.Fingerprint, dna.DNA, profile, stationRecords);
        }

        _records.Synchronize(shuttleStation);
    }
}
