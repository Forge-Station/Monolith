using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.GameTicking.Events;
using Content.Server._Forge.Persistence;
using Content.Server.Shuttles.Components;
using Content.Shared._Forge;
using Content.Shared._Forge.Persistence;
using Content.Shared._Mono.FireControl;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.BUI;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Gravity;
using Content.Server._NF.Station.Components;
using Content.Server.StationRecords;
using Content.Shared.Maps;
using Content.Shared.StationRecords;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    [Dependency] private readonly IServerDbManager _hangarDatabase = default!;
    [Dependency] private readonly GridPersistenceService _gridPersistence = default!;

    private void InitializeHangar()
    {
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleHangarRefreshMessage>(OnHangarRefresh);
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleHangarStoreMessage>(OnHangarStore);
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleHangarRetrieveMessage>(OnHangarRetrieve);
        SubscribeLocalEvent<RoundStartingEvent>(OnHangarRoundStarting);
        SubscribeLocalEvent<BeforeRoundRestartWarningEvent>(OnHangarRoundEndWarning);
    }

    private void OnHangarRoundEndWarning(BeforeRoundRestartWarningEvent ev)
    {
        if (!_configManager.GetCVar(ForgeVars.HangarEnabled) ||
            !_configManager.GetCVar(ForgeVars.HangarRoundEndWarning))
        {
            return;
        }

        var warned = new HashSet<NetUserId>();
        var query = EntityQueryEnumerator<ShipOwnershipComponent>();
        while (query.MoveNext(out _, out var ownership))
        {
            if (!warned.Add(ownership.OwnerUserId) ||
                !_player.TryGetSessionById(ownership.OwnerUserId, out var session) ||
                session.AttachedEntity is not { Valid: true } actor)
            {
                continue;
            }

            ConsolePopup(actor, Loc.GetString("shipyard-hangar-round-end-warning"));
        }
    }

    private async void OnHangarRoundStarting(RoundStartingEvent ev)
    {
        if (!_configManager.GetCVar(ForgeVars.HangarEnabled))
            return;

        var purged = await _hangarDatabase.PurgeDeployedHangarVessels();
        if (purged > 0)
            Log.Info($"Removed {purged} deployed hangar records at round start.");
    }

    private async void OnHangarRefresh(
        EntityUid uid,
        ShipyardConsoleComponent component,
        ShipyardConsoleHangarRefreshMessage args)
    {
        if (args.Actor is not { Valid: true } actor)
            return;

        await RefreshHangarState(uid, actor, (ShipyardConsoleUiKey) args.UiKey);
    }

    private async void OnHangarStore(
        EntityUid uid,
        ShipyardConsoleComponent component,
        ShipyardConsoleHangarStoreMessage args)
    {
        if (!_configManager.GetCVar(ForgeVars.HangarEnabled) ||
            args.Actor is not { Valid: true } actor ||
            !TryGetCharacter(actor, out var playerId, out var characterSlot))
        {
            return;
        }

        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId ||
            !TryComp<ShuttleDeedComponent>(targetId, out var deed) ||
            deed.ShuttleUid is not { Valid: true } shuttle ||
            Deleted(shuttle))
        {
            HangarError(actor, uid, component, "shipyard-hangar-no-deployed-vessel");
            return;
        }

        if (_station.GetOwningStation(uid) is not { Valid: true } station ||
            !IsShuttleDockedToStation(station, shuttle))
        {
            HangarError(actor, uid, component, "shipyard-console-sale-not-docked");
            return;
        }

        var organic = FoundOrganics(
            shuttle,
            GetEntityQuery<MobStateComponent>(),
            GetEntityQuery<TransformComponent>());
        if (organic != null)
        {
            HangarError(actor,
                uid,
                component,
                "shipyard-console-sale-organic-aboard",
                ("name", organic));
            return;
        }

        var shuttleStation = _station.GetOwningStation(shuttle);
        DetachHangarShuttle(shuttle);

        var vesselId = deed.PersistedVesselId ?? Guid.NewGuid();
        var savePath = _gridPersistence.GetHangarSavePath(vesselId);
        if (!_gridPersistence.SaveGrid(shuttle, savePath, coldStartAtmosphere: true))
        {
            HangarError(actor, uid, component, "shipyard-hangar-save-failed");
            return;
        }

        var vesselPrototype = TryComp<VesselComponent>(shuttle, out var vessel)
            ? vessel.VesselId.Id
            : null;
        var record = new HangarVesselRecord(
            vesselId,
            playerId,
            characterSlot,
            vesselPrototype,
            GetFullName(deed),
            savePath.ToString(),
            HangarVesselState.InHangar,
            DateTime.UtcNow);

        var maxSlots = Math.Max(0, _configManager.GetCVar(ForgeVars.HangarMaxSlots));
        if (!await _hangarDatabase.UpsertHangarVessel(record, maxSlots))
        {
            _gridPersistence.Delete(savePath);
            HangarError(actor, uid, component, "shipyard-hangar-slots-full");
            return;
        }

        deed.PersistedVesselId = vesselId;
        deed.HangarState = HangarVesselState.InHangar;
        deed.ShuttleUid = null;
        Dirty(targetId, deed);

        if (TryComp<ShuttleDeedComponent>(shuttle, out var shuttleDeed))
        {
            shuttleDeed.PersistedVesselId = vesselId;
            shuttleDeed.HangarState = HangarVesselState.InHangar;
        }

        if (shuttleStation is { Valid: true } stationUid)
            _station.DeleteStation(stationUid);

        _shuttleRecordsSystem.AttachPersistedId(shuttle, vesselId);
        QueueDel(shuttle);
        ConsolePopup(actor, Loc.GetString("shipyard-hangar-stored", ("name", record.CustomName)));
        PlayConfirmSound(actor, uid, component);
        await RefreshHangarState(uid, actor, (ShipyardConsoleUiKey) args.UiKey);
    }

    private async void OnHangarRetrieve(
        EntityUid uid,
        ShipyardConsoleComponent component,
        ShipyardConsoleHangarRetrieveMessage args)
    {
        if (!_configManager.GetCVar(ForgeVars.HangarEnabled) ||
            args.Actor is not { Valid: true } actor ||
            !TryGetCharacter(actor, out var playerId, out var characterSlot))
        {
            return;
        }

        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId)
        {
            HangarError(actor, uid, component, "shipyard-console-no-idcard");
            return;
        }

        if (TryComp<ShuttleDeedComponent>(targetId, out var existingDeed) &&
            existingDeed.ShuttleUid is { Valid: true } existingShuttle &&
            !Deleted(existingShuttle))
        {
            HangarError(actor, uid, component, "shipyard-hangar-active-vessel");
            return;
        }

        var records = await _hangarDatabase.GetHangarVessels(playerId, characterSlot);
        var record = records.FirstOrDefault(vessel =>
            vessel.VesselId == args.VesselId && vessel.State == HangarVesselState.InHangar);
        if (record == null)
        {
            HangarError(actor, uid, component, "shipyard-hangar-not-found");
            return;
        }

        var savePath = new ResPath(record.SavePath);
        if (!_gridPersistence.FileExists(savePath) ||
            _station.GetOwningStation(uid) is not { Valid: true } station ||
            !TryPurchaseShuttle(station, savePath, out var shuttleResult))
        {
            HangarError(actor, uid, component, "shipyard-hangar-load-failed");
            return;
        }

        var shuttle = shuttleResult.Value;
        if (!await _hangarDatabase.SetHangarVesselState(record.VesselId, HangarVesselState.Deployed))
        {
            QueueDel(shuttle);
            HangarError(actor, uid, component, "shipyard-hangar-load-failed");
            return;
        }

        var deed = EnsureComp<ShuttleDeedComponent>(targetId);
        AssignShuttleDeedProperties(deed, shuttle, record.CustomName, Name(actor), false);
        deed.DeedHolder = targetId;
        deed.PersistedVesselId = record.VesselId;
        deed.HangarState = HangarVesselState.Deployed;
        Dirty(targetId, deed);

        var gridDeed = EnsureComp<ShuttleDeedComponent>(shuttle);
        AssignShuttleDeedProperties(gridDeed, shuttle, record.CustomName, Name(actor), false);
        gridDeed.PersistedVesselId = record.VesselId;
        gridDeed.HangarState = HangarVesselState.Deployed;

        if (!string.IsNullOrWhiteSpace(record.VesselPrototypeId))
            EnsureComp<VesselComponent>(shuttle).VesselId = record.VesselPrototypeId;

        if (_player.TryGetSessionByEntity(actor, out var session))
            _shipOwnership.RegisterShipOwnership(shuttle, session);

        var crew = await _hangarDatabase.GetHangarVesselCrew(record.VesselId);
        EntityManager.System<PersistentShipCrewSystem>().SetCrew(shuttle, crew);
        InitializeRetrievedShuttleStation(shuttle, record, crew, actor);
        EntityManager.System<AtmosphereSystem>().ColdStartGrid(shuttle);
        EntityManager.System<GravityGeneratorSystem>().SynchronizeGrid(shuttle);
        _shuttleRecordsSystem.RelinkPersistedRecord(
            record.VesselId,
            shuttle,
            record.CustomName,
            Name(actor));
        RelinkHangarConsoleLocks(shuttle);
        AddShipAccessToEntities(shuttle);
        _gridPersistence.Delete(savePath);

        ConsolePopup(actor, Loc.GetString("shipyard-hangar-retrieved", ("name", record.CustomName)));
        PlayConfirmSound(actor, uid, component);
        await RefreshHangarState(uid, actor, (ShipyardConsoleUiKey) args.UiKey);
    }

    private async Task RefreshHangarState(EntityUid console, EntityUid actor, ShipyardConsoleUiKey uiKey)
    {
        if (!TryGetCharacter(actor, out var playerId, out var characterSlot) ||
            !_ui.TryGetUiState<ShipyardConsoleInterfaceState>(console, uiKey, out var current))
        {
            return;
        }

        var records = await _hangarDatabase.GetHangarVessels(playerId, characterSlot);
        var entries = records
            .Where(record => record.State == HangarVesselState.InHangar)
            .Select(record => new HangarVesselUiEntry(
                record.VesselId,
                record.CustomName,
                record.VesselPrototypeId,
                record.State))
            .ToList();

        string? deedTitle = null;
        var sellValue = 0;
        if (TryComp<ShipyardConsoleComponent>(console, out var consoleComponent) &&
            consoleComponent.TargetIdSlot.ContainerSlot?.ContainedEntity is { Valid: true } targetId &&
            TryComp<ShuttleDeedComponent>(targetId, out var deed) &&
            deed.ShuttleUid is { Valid: true } shuttle &&
            !Deleted(shuttle))
        {
            deedTitle = GetFullName(deed);
            sellValue = CalculateShipResaleValue(
                (console, consoleComponent),
                (int) _pricing.AppraiseGrid(shuttle, LacksPreserveOnSaleComp));
        }

        _ui.SetUiState(console,
            uiKey,
            new ShipyardConsoleInterfaceState(
                current.Balance,
                current.AccessGranted,
                deedTitle,
                sellValue,
                current.IsTargetIdPresent,
                current.UiKey,
                current.ShipyardPrototypes,
                current.ShipyardName,
                current.FreeListings,
                current.SellRate,
                entries,
                Math.Max(0, _configManager.GetCVar(ForgeVars.HangarMaxSlots))));
    }

    private bool TryGetCharacter(EntityUid actor, out Guid playerId, out int characterSlot)
    {
        playerId = default;
        characterSlot = -1;
        if (!_player.TryGetSessionByEntity(actor, out var session))
            return false;

        var preferences = _prefManager.GetPreferencesOrNull(session.UserId);
        if (preferences == null || preferences.SelectedCharacterIndex < 0)
            return false;

        playerId = session.UserId.UserId;
        characterSlot = preferences.SelectedCharacterIndex;
        return true;
    }

    private bool IsShuttleDockedToStation(EntityUid station, EntityUid shuttle)
    {
        if (!TryComp<Content.Shared.Station.Components.StationDataComponent>(station, out var stationData) ||
            _station.GetLargestGrid((station, stationData)) is not { } stationGrid)
        {
            return false;
        }

        var stationDocks = _docking.GetDocks(stationGrid);
        foreach (var shuttleDock in _docking.GetDocks(shuttle))
        {
            if (stationDocks.Any(stationDock => shuttleDock.Comp.DockedWith == stationDock.Owner))
                return true;
        }

        return false;
    }

    private void InitializeRetrievedShuttleStation(
        EntityUid shuttle,
        HangarVesselRecord vessel,
        IReadOnlyCollection<HangarVesselCrewRecord> crew,
        EntityUid owner)
    {
        if (string.IsNullOrWhiteSpace(vessel.VesselPrototypeId) ||
            !_prototypeManager.TryIndex<GameMapPrototype>(vessel.VesselPrototypeId, out var mapPrototype) ||
            !mapPrototype.Stations.TryGetValue(vessel.VesselPrototypeId, out var stationConfig))
        {
            return;
        }

        var station = _station.InitializeNewStation(
            stationConfig,
            [shuttle],
            vessel.CustomName);
        EnsureComp<ExtraShuttleInformationComponent>(station).Vessel = vessel.VesselPrototypeId;

        if (!TryComp<StationRecordsComponent>(station, out var targetRecords))
            return;

        var names = new List<(string Name, bool Owner)> { (Name(owner), true) };
        names.AddRange(crew.Select(member => (member.CharacterName, false)));

        var sourceRecords = new List<(EntityUid Station, StationRecordsComponent Records)>();
        var recordsQuery = EntityQueryEnumerator<StationRecordsComponent>();
        while (recordsQuery.MoveNext(out var stationUid, out var records))
        {
            if (stationUid != station)
                sourceRecords.Add((stationUid, records));
        }

        foreach (var (name, isOwner) in names.DistinctBy(entry => entry.Name))
        {
            GeneralStationRecord? copied = null;
            foreach (var source in sourceRecords)
            {
                var id = _records.GetRecordByName(source.Station, name, source.Records);
                if (id == null ||
                    !_records.TryGetRecord(
                        new StationRecordKey(id.Value, source.Station),
                        out copied,
                        source.Records))
                {
                    continue;
                }

                break;
            }

            copied ??= new GeneralStationRecord
            {
                Name = name,
                JobTitle = Loc.GetString(isOwner
                    ? "station-records-shuttle-owner-job"
                    : "station-records-shuttle-crew-member-job"),
                JobPrototype = isOwner ? "Captain" : "Mercenary",
            };
            _records.AddRecordEntry(station, copied, targetRecords);
        }

        _records.Synchronize(station, targetRecords);
    }

    private void DetachHangarShuttle(EntityUid shuttle)
    {
        var docks = EntityQueryEnumerator<DockingComponent, TransformComponent>();
        var shuttleDocks = new List<Entity<DockingComponent>>();
        while (docks.MoveNext(out var dock, out var docking, out var transform))
        {
            if (transform.GridUid == shuttle && docking.DockedWith is { Valid: true })
                shuttleDocks.Add((dock, docking));
        }

        foreach (var dock in shuttleDocks)
        {
            if (dock.Comp.DockedWith is { Valid: true })
                _docking.Undock(dock);
        }

        // The station entity is deliberately not part of a hangar grid file.
        RemComp<StationMemberComponent>(shuttle);
    }

    private void RelinkHangarConsoleLocks(EntityUid shuttle)
    {
        var shuttleConsoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (shuttleConsoles.MoveNext(out var console, out _, out var transform))
        {
            if (transform.GridUid != shuttle)
                continue;

            var lockComponent = EnsureComp<ShuttleConsoleLockComponent>(console);
            _shuttleConsoleLock.SetShuttleId(console, shuttle.ToString(), lockComponent);
        }

        var gunneryConsoles = EntityQueryEnumerator<FireControlConsoleComponent, TransformComponent>();
        while (gunneryConsoles.MoveNext(out var console, out _, out var transform))
        {
            if (transform.GridUid != shuttle)
                continue;

            var lockComponent = EnsureComp<ShuttleConsoleLockComponent>(console);
            _shuttleConsoleLock.SetShuttleId(console, shuttle.ToString(), lockComponent);
        }
    }

    private void HangarError(
        EntityUid actor,
        EntityUid console,
        ShipyardConsoleComponent component,
        string message,
        params (string, object)[] args)
    {
        ConsolePopup(actor, Loc.GetString(message, args));
        PlayDenySound(actor, console, component);
    }

    private async void DeletePersistedVessel(Guid vesselId)
    {
        _gridPersistence.Delete(_gridPersistence.GetHangarSavePath(vesselId));
        await _hangarDatabase.DeleteHangarVessel(vesselId);
    }
}
