using System.Linq;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Components;
using Content.Shared.StationRecords;
using Robust.Server.GameObjects;
using Content.Shared.Roles; // Frontier
using Robust.Shared.Prototypes; // Frontier
using Content.Shared.Access.Systems; // Frontier
using Content.Server.Station.Components; // Frontier
using Content.Server._NF.Station.Components; // Frontier
using Content.Server.Administration.Logs; // Frontier
using Content.Shared.Database; // Frontier
using Content.Shared._NF.StationRecords; // Frontier
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Server.Popups;
using Content.Server._Forge.Persistence;
using Content.Server._NF.ShuttleRecords;
using Content.Server.Shuttles.Components;
using Content.Shared._Forge;
using Content.Shared._Forge.Persistence;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Components;
using Robust.Server.Player;
using Robust.Shared.Configuration;

namespace Content.Server.StationRecords.Systems;

public sealed partial class GeneralStationRecordConsoleSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private StationRecordsSystem _stationRecords = default!;
    [Dependency] private StationJobsSystem _stationJobsSystem = default!; // Frontier
    [Dependency] private AccessReaderSystem _access = default!; // Frontier
    [Dependency] private IPrototypeManager _proto = default!; // Frontier
    [Dependency] private IAdminLogManager _adminLog = default!; // Frontier
    [Dependency] private IServerDbManager _database = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private GridPersistenceService _gridPersistence = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private ShuttleRecordsSystem _shuttleRecords = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<GeneralStationRecordConsoleComponent, RecordModifiedEvent>(UpdateUserInterface);
        SubscribeLocalEvent<GeneralStationRecordConsoleComponent, AfterGeneralRecordCreatedEvent>(UpdateUserInterface);
        SubscribeLocalEvent<GeneralStationRecordConsoleComponent, RecordRemovedEvent>(UpdateUserInterface);

        Subs.BuiEvents<GeneralStationRecordConsoleComponent>(GeneralStationRecordConsoleKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(UpdateUserInterface);
            subs.Event<SelectStationRecord>(OnKeySelected);
            subs.Event<SetStationRecordFilter>(OnFiltersChanged);
            subs.Event<DeleteStationRecord>(OnRecordDelete);
            subs.Event<AdjustStationJobMsg>(OnAdjustJob); // Frontier
            subs.Event<SetStationAdvertisementMsg>(OnAdvertisementChanged); // Frontier
            subs.Event<RefreshShuttleCrewMessage>(OnCrewRefresh);
            subs.Event<AddShuttleCrewMemberMessage>(OnCrewAdd);
            subs.Event<RemoveShuttleCrewMemberMessage>(OnCrewRemove);
        });
    }

    private void OnRecordDelete(Entity<GeneralStationRecordConsoleComponent> ent, ref DeleteStationRecord args)
    {
        if (!ent.Comp.CanDeleteEntries)
            return;

        var owning = _station.GetOwningStation(ent.Owner);

        if (owning != null)
            _stationRecords.RemoveRecord(new StationRecordKey(args.Id, owning.Value));
        UpdateUserInterface(ent); // Apparently an event does not get raised for this.
    }

    private void UpdateUserInterface<T>(Entity<GeneralStationRecordConsoleComponent> ent, ref T args)
    {
        UpdateUserInterface(ent);
    }

    // TODO: instead of copy paste shitcode for each record console, have a shared records console comp they all use
    // then have this somehow play nicely with creating ui state
    // if that gets done put it in StationRecordsSystem console helpers section :)
    private void OnKeySelected(Entity<GeneralStationRecordConsoleComponent> ent, ref SelectStationRecord msg)
    {
        ent.Comp.ActiveKey = msg.SelectedKey;
        UpdateUserInterface(ent);
    }

    // Frontier: job counts, advertisements
    private void OnAdjustJob(Entity<GeneralStationRecordConsoleComponent> ent, ref AdjustStationJobMsg msg)
    {
        var stationUid = _station.GetOwningStation(ent);
        if (stationUid is EntityUid station)
        {
            // Frontier: check access - hack because we don't have an AccessReaderComponent, it's the station
            if (TryComp(stationUid, out StationJobsComponent? stationJobs) &&
                (stationJobs.Groups.Count > 0 || stationJobs.Tags.Count > 0))
            {
                var accessSources = _access.FindPotentialAccessItems(msg.Actor);
                var access = _access.FindAccessTags(msg.Actor, accessSources);

                // Check access groups and tags
                bool hasAccess = stationJobs.Tags.Any(access.Contains);
                if (!hasAccess)
                {
                    foreach (var group in stationJobs.Groups)
                    {
                        if (!_proto.TryIndex(group, out var accessGroup))
                            continue;

                        hasAccess = accessGroup.Tags.Any(access.Contains);
                        if (hasAccess)
                            break;
                    }
                }

                if (!hasAccess)
                {
                    UpdateUserInterface(ent);
                    return;
                }
            }
            // End Frontier
            _stationJobsSystem.TryAdjustJobSlot(station, msg.JobProto, msg.Amount, false, true);
            UpdateUserInterface(ent);
        }
    }
    private void OnFiltersChanged(Entity<GeneralStationRecordConsoleComponent> ent, ref SetStationRecordFilter msg)
    {
        if (ent.Comp.Filter == null ||
            ent.Comp.Filter.Type != msg.Type || ent.Comp.Filter.Value != msg.Value)
        {
            ent.Comp.Filter = new StationRecordsFilter(msg.Type, msg.Value);
            UpdateUserInterface(ent);
        }
    }

    private void OnAdvertisementChanged(Entity<GeneralStationRecordConsoleComponent> ent, ref SetStationAdvertisementMsg msg)
    {
        var stationUid = _station.GetOwningStation(ent);
        if (stationUid is EntityUid station
            && TryComp<ExtraShuttleInformationComponent>(station, out var vesselInfo))
        {
            vesselInfo.Advertisement = msg.Advertisement;
            _adminLog.Add(LogType.ShuttleInfoChanged, $"{ToPrettyString(msg.Actor):actor} set their shuttle {ToPrettyString(station)}'s ad text to {vesselInfo.Advertisement}");
            UpdateUserInterface(ent);
            _stationJobsSystem.UpdateJobsAvailable(); // Nasty - ideally this sends out partial information - one ship changed its advertisement.
        }
    }

    private void OnCrewRefresh(
        Entity<GeneralStationRecordConsoleComponent> ent,
        ref RefreshShuttleCrewMessage msg)
    {
        if (msg.Actor is { Valid: true } actor)
            RefreshCrewView(ent, actor);
    }

    private void OnCrewAdd(
        Entity<GeneralStationRecordConsoleComponent> ent,
        ref AddShuttleCrewMemberMessage msg)
    {
        if (msg.Actor is { Valid: true } actor)
            AddCrewMember(ent, actor, msg.PlayerUserId, msg.CharacterSlot);
    }

    private void OnCrewRemove(
        Entity<GeneralStationRecordConsoleComponent> ent,
        ref RemoveShuttleCrewMemberMessage msg)
    {
        if (msg.Actor is { Valid: true } actor)
            RemoveCrewMember(ent, actor, msg.PlayerUserId, msg.CharacterSlot);
    }

    private async void RefreshCrewView(
        Entity<GeneralStationRecordConsoleComponent> ent,
        EntityUid actor)
    {
        if (!TryGetConsoleShuttle(ent.Owner, out var shuttle))
        {
            ent.Comp.ShuttleCrew = null;
            ent.Comp.ShuttleCrewCandidates = null;
            ent.Comp.CanManageShuttleCrew = false;
            UpdateUserInterface(ent);
            return;
        }

        var ownership = await TryEnsureOwnedVessel(actor, shuttle);
        var crew = ownership.VesselId is { } vesselId
            ? await _database.GetHangarVesselCrew(vesselId)
            : [];

        ent.Comp.ShuttleCrew = crew;
        ent.Comp.ShuttleCrewCandidates = ownership.Success
            ? GetCrewCandidates(actor, crew)
            : [];
        ent.Comp.CanManageShuttleCrew = ownership.Success;
        UpdateUserInterface(ent);
    }

    private async void AddCrewMember(
        Entity<GeneralStationRecordConsoleComponent> ent,
        EntityUid actor,
        Guid playerUserId,
        int characterSlot)
    {
        if (!TryGetConsoleShuttle(ent.Owner, out var shuttle))
            return;

        var ownership = await TryEnsureOwnedVessel(actor, shuttle);
        if (!ownership.Success ||
            !TryGetOnlineCharacter(playerUserId, characterSlot, out var characterName))
        {
            _popup.PopupEntity(Loc.GetString("station-records-shuttle-crew-owner-only"), actor);
            return;
        }

        var member = new HangarVesselCrewRecord(
            ownership.VesselId!.Value,
            playerUserId,
            characterSlot,
            characterName);
        if (!await _database.AddHangarVesselCrew(member))
        {
            _popup.PopupEntity(Loc.GetString("station-records-shuttle-crew-update-failed"), actor);
            return;
        }

        await RefreshRuntimeCrew(shuttle, ownership.VesselId.Value);
        _popup.PopupEntity(
            Loc.GetString("station-records-shuttle-crew-added", ("name", characterName)),
            actor);
        RefreshCrewView(ent, actor);
    }

    private async void RemoveCrewMember(
        Entity<GeneralStationRecordConsoleComponent> ent,
        EntityUid actor,
        Guid playerUserId,
        int characterSlot)
    {
        if (!TryGetConsoleShuttle(ent.Owner, out var shuttle))
            return;

        var ownership = await TryEnsureOwnedVessel(actor, shuttle);
        if (!ownership.Success)
        {
            _popup.PopupEntity(Loc.GetString("station-records-shuttle-crew-owner-only"), actor);
            return;
        }

        await _database.RemoveHangarVesselCrew(
            ownership.VesselId!.Value,
            playerUserId,
            characterSlot);
        await RefreshRuntimeCrew(shuttle, ownership.VesselId.Value);
        RefreshCrewView(ent, actor);
    }

    private bool TryGetConsoleShuttle(EntityUid console, out EntityUid shuttle)
    {
        shuttle = default;
        if (Transform(console).GridUid is not { Valid: true } grid)
            return false;

        shuttle = grid;
        return true;
    }

    private async Task<(bool Success, Guid? VesselId)> TryEnsureOwnedVessel(
        EntityUid actor,
        EntityUid shuttle)
    {
        if (!_players.TryGetSessionByEntity(actor, out var session))
            return (false, null);

        var preferences = _preferences.GetPreferencesOrNull(session.UserId);
        if (preferences == null || preferences.SelectedCharacterIndex < 0)
            return (false, null);

        if (TryComp<ShuttleDeedComponent>(shuttle, out var existingDeed) &&
            existingDeed.PersistedVesselId is { } persistedId)
        {
            var persisted = await _database.GetHangarVessel(persistedId);
            if (persisted != null)
            {
                return (
                    persisted.PlayerUserId == session.UserId.UserId &&
                    persisted.CharacterSlot == preferences.SelectedCharacterIndex,
                    persistedId);
            }
        }

        if (!TryComp<ShipOwnershipComponent>(shuttle, out var ownership) ||
            ownership.OwnerUserId != session.UserId)
        {
            return (false, null);
        }

        var deed = EnsureComp<ShuttleDeedComponent>(shuttle);
        var vesselId = deed.PersistedVesselId ?? Guid.NewGuid();
        _shuttleRecords.SetPersistentVessel(
            (shuttle, deed),
            vesselId,
            HangarVesselState.Deployed);
        _shuttleRecords.AttachPersistedId(shuttle, vesselId);

        var prototype = TryComp<VesselComponent>(shuttle, out var vessel)
            ? vessel.VesselId.Id
            : null;
        var name = string.Join(" ", new[] { deed.ShuttleName, deed.ShuttleNameSuffix }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        var record = new HangarVesselRecord(
            vesselId,
            session.UserId.UserId,
            preferences.SelectedCharacterIndex,
            prototype,
            name,
            _gridPersistence.GetHangarSavePath(vesselId).ToString(),
            HangarVesselState.Deployed,
            DateTime.UtcNow);

        var maxSlots = Math.Max(0, _configuration.GetCVar(ForgeVars.HangarMaxSlots));
        return (await _database.UpsertHangarVessel(record, maxSlots), vesselId);
    }

    private List<StationRecordsCrewCandidate> GetCrewCandidates(
        EntityUid owner,
        IReadOnlyCollection<HangarVesselCrewRecord> crew)
    {
        var result = new List<StationRecordsCrewCandidate>();
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } attached ||
                attached == owner)
            {
                continue;
            }

            var preferences = _preferences.GetPreferencesOrNull(session.UserId);
            if (preferences == null || preferences.SelectedCharacterIndex < 0 ||
                crew.Any(member =>
                    member.PlayerUserId == session.UserId.UserId &&
                    member.CharacterSlot == preferences.SelectedCharacterIndex))
            {
                continue;
            }

            result.Add(new StationRecordsCrewCandidate(
                session.UserId.UserId,
                preferences.SelectedCharacterIndex,
                Name(attached)));
        }

        return result.OrderBy(candidate => candidate.CharacterName).ToList();
    }

    private bool TryGetOnlineCharacter(
        Guid playerUserId,
        int characterSlot,
        out string characterName)
    {
        foreach (var session in _players.Sessions)
        {
            var preferences = _preferences.GetPreferencesOrNull(session.UserId);
            if (session.UserId.UserId != playerUserId ||
                preferences?.SelectedCharacterIndex != characterSlot ||
                session.AttachedEntity is not { Valid: true } attached)
            {
                continue;
            }

            characterName = Name(attached);
            return true;
        }

        characterName = string.Empty;
        return false;
    }

    private async Task RefreshRuntimeCrew(EntityUid shuttle, Guid vesselId)
    {
        var crew = await _database.GetHangarVesselCrew(vesselId);
        EntityManager.System<PersistentShipCrewSystem>().SetCrew(shuttle, crew);
    }
    // End Frontier: job counts, advertisements

    private void UpdateUserInterface(Entity<GeneralStationRecordConsoleComponent> ent)
    {
        var (uid, console) = ent;
        var owningStation = _station.GetOwningStation(uid);

        // Frontier: jobs, advertisements
        IReadOnlyDictionary<ProtoId<JobPrototype>, int?>? jobList = null;
        string? advertisement = null;
        if (owningStation != null)
        {
            jobList = _stationJobsSystem.GetJobs(owningStation.Value);
            if (TryComp<ExtraShuttleInformationComponent>(owningStation, out var extraVessel))
                advertisement = extraVessel.Advertisement;
        }

        if (!TryComp<StationRecordsComponent>(owningStation, out var stationRecords))
        {
            _ui.SetUiState(uid, GeneralStationRecordConsoleKey.Key, new GeneralStationRecordConsoleState(
                null, null, null, jobList, console.Filter, ent.Comp.CanDeleteEntries, advertisement,
                console.ShuttleCrew, console.ShuttleCrewCandidates, console.CanManageShuttleCrew));
            return;
        }

        var listing = _stationRecords.BuildListing((owningStation.Value, stationRecords), console.Filter);

        switch (listing.Count)
        {
            case 0:
                var consoleState = new GeneralStationRecordConsoleState(
                    null, null, null, jobList, console.Filter, ent.Comp.CanDeleteEntries, advertisement,
                    console.ShuttleCrew, console.ShuttleCrewCandidates, console.CanManageShuttleCrew);
                _ui.SetUiState(uid, GeneralStationRecordConsoleKey.Key, consoleState);
                return;
            default:
                if (console.ActiveKey == null)
                    console.ActiveKey = listing.Keys.First();
                break;
        }

        if (console.ActiveKey is not { } id)
        {
            _ui.SetUiState(uid, GeneralStationRecordConsoleKey.Key, new GeneralStationRecordConsoleState(
                null, null, listing, jobList, console.Filter, ent.Comp.CanDeleteEntries, advertisement,
                console.ShuttleCrew, console.ShuttleCrewCandidates, console.CanManageShuttleCrew));
            return;
        }

        var key = new StationRecordKey(id, owningStation.Value);
        _stationRecords.TryGetRecord<GeneralStationRecord>(key, out var record, stationRecords);

        GeneralStationRecordConsoleState newState = new(
            id, record, listing, jobList, console.Filter, ent.Comp.CanDeleteEntries, advertisement,
            console.ShuttleCrew, console.ShuttleCrewCandidates, console.CanManageShuttleCrew);
        _ui.SetUiState(uid, GeneralStationRecordConsoleKey.Key, newState);
    }
}
