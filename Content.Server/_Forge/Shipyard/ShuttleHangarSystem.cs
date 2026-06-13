using System.Linq;
using System.Threading.Tasks;
using DbHangarVessel = Content.Server.Database.HangarVessel;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Database;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Forge.CCVar;
using Content.Shared._Forge.Shipyard;
using Content.Shared.Atmos;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Shuttles.Components;
using Content.Server.StationRecords;
using Content.Shared.Station.Components;
using Content.Shared.StationRecords;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._Forge.Shipyard;

public sealed class ShuttleHangarSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly DockingSystem _docking = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedJointSystem _joints = default!;

    private ISawmill _sawmill = default!;
    private bool _hangarEnabled;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("hangar");
        Subs.CVar(_cfg, ForgeCVars.HangarEnabled, v => _hangarEnabled = v, true);
        SubscribeLocalEvent<RoundEndMessageEvent>(OnRoundEnd);
        SubscribeLocalEvent<DockingComponent, ComponentInit>(OnHangarDockInit);
    }

    /// <summary>
    /// Grids saved while docked may deserialize with invalid <see cref="DockingComponent.DockedWith"/> refs.
    /// Clear them before <see cref="DockingSystem"/> re-docks on <see cref="ComponentStartup"/>.
    /// </summary>
    private void OnHangarDockInit(Entity<DockingComponent> entity, ref ComponentInit args)
    {
        if (entity.Comp.DockedWith is { Valid: true })
            return;

        entity.Comp.DockedWith = null;
        entity.Comp.DockJointId = null;
        entity.Comp.DockJoint = null;
    }

    private void OnRoundEnd(RoundEndMessageEvent ev)
    {
        if (!_cfg.GetCVar(ForgeCVars.HangarRoundEndWarning))
            return;

        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { } uid)
                continue;

            if (!TryComp<ShuttleDeedComponent>(uid, out var deed) || deed.HangarState != HangarVesselState.Deployed)
                continue;

            if (deed.ShuttleUid is not { Valid: true })
                continue;

            _sawmill.Info($"Player {session.Name} has a deployed shuttle at round end — remind to store in hangar.");
        }
    }

    public async Task<List<HangarVesselEntry>> GetHangarEntriesAsync(NetUserId userId)
    {
        var rows = await _db.GetHangarVesselsAsync(userId);
        return rows.Select(r => new HangarVesselEntry(
            r.VesselGuid,
            r.VesselProtoId,
            r.CustomName,
            (HangarVesselState) r.State)).ToList();
    }

    public Task<DbHangarVessel?> GetHangarVesselAsync(Guid vesselGuid) =>
        _db.GetHangarVesselAsync(vesselGuid);

    public async Task<(int Used, int Max)> GetHangarSlotInfoAsync(NetUserId userId)
    {
        var entries = await _db.GetHangarVesselsAsync(userId);
        return (entries.Count(v => v.State == (int) HangarVesselState.InHangar), _cfg.GetCVar(ForgeCVars.HangarMaxSlots));
    }

    public async Task UpdateHangarCustomNameAsync(Guid vesselGuid, string customName)
    {
        var record = await _db.GetHangarVesselAsync(vesselGuid);
        if (record == null)
            return;

        record.CustomName = customName.Trim();
        await _db.UpsertHangarVesselAsync(record);
    }

    public bool CanStoreInHangar(EntityUid shuttleUid, EntityUid stationUid, EntityUid consoleUid, out ShipyardSystem.ShipyardSaleError error)
    {
        error = ValidateShuttle(shuttleUid, stationUid);
        return error == ShipyardSystem.ShipyardSaleError.Success;
    }

    public async Task<(bool Success, Guid? VesselGuid)> TryStoreInHangar(
        EntityUid player,
        EntityUid consoleUid,
        EntityUid stationUid,
        ShuttleDeedComponent deed)
    {
        if (!_hangarEnabled || deed.ShuttleUid is not { Valid: true } shuttleUid)
            return (false, null);

        if (ValidateShuttle(shuttleUid, stationUid) != ShipyardSystem.ShipyardSaleError.Success)
            return (false, null);

        if (!_player.TryGetSessionByEntity(player, out var session))
            return (false, null);

        var userId = session.UserId;
        var entries = await _db.GetHangarVesselsAsync(userId);
        await CleanupStaleDeployedRecordsAsync(userId, entries, deed.PersistedVesselId);
        entries = await _db.GetHangarVesselsAsync(userId);

        var maxSlots = _cfg.GetCVar(ForgeCVars.HangarMaxSlots);
        if (entries.Count(v => v.State == (int) HangarVesselState.InHangar) >= maxSlots)
            return (false, null);

        var vesselGuid = deed.PersistedVesselId ?? Guid.NewGuid();
        var savePath = new ResPath($"/persisted_shuttles/{vesselGuid}.yml");
        _res.UserData.CreateDir(savePath.Directory);

        var shipyard = EntityManager.System<ShipyardSystem>();
        PrepareShuttleForHangarSave(shuttleUid, consoleUid, shipyard);

        var saveOpts = SerializationOptions.Default with
        {
            MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
            ErrorOnOrphan = false,
        };

        if (!_mapLoader.TrySaveGrid(shuttleUid, savePath, saveOpts))
        {
            _sawmill.Error($"Failed to save hangar grid {shuttleUid}");
            return (false, null);
        }

        var vesselProto = TryComp<VesselComponent>(shuttleUid, out var vessel)
            ? vessel.VesselId.ToString()
            : "Unknown";

        var customName = $"{deed.ShuttleName} {deed.ShuttleNameSuffix}".Trim();

        await _db.UpsertHangarVesselAsync(new DbHangarVessel
        {
            VesselGuid = vesselGuid,
            OwnerUserId = userId.UserId,
            VesselProtoId = vesselProto,
            SavePath = savePath.ToString(),
            CustomName = customName,
            State = (int) HangarVesselState.InHangar,
            LastStored = DateTime.UtcNow,
        });

        QueueDel(shuttleUid);
        return (true, vesselGuid);
    }

    public async Task<(EntityUid? Shuttle, string? Name)> TryRetrieveFromHangar(
        EntityUid player,
        EntityUid targetId,
        EntityUid consoleUid,
        EntityUid stationUid,
        Guid vesselGuid)
    {
        if (!_hangarEnabled)
        {
            _sawmill.Warning("Hangar retrieve blocked: hangar disabled");
            return (null, null);
        }

        if (!_player.TryGetSessionByEntity(player, out var session))
        {
            _sawmill.Warning("Hangar retrieve blocked: no player session");
            return (null, null);
        }

        var record = await _db.GetHangarVesselAsync(vesselGuid);
        if (record == null || record.OwnerUserId != session.UserId.UserId)
        {
            _sawmill.Warning($"Hangar retrieve blocked: record missing or owner mismatch for {vesselGuid}");
            return (null, null);
        }

        if (record.State != (int) HangarVesselState.InHangar)
        {
            _sawmill.Warning($"Hangar retrieve blocked: vessel {vesselGuid} state is {record.State}, expected InHangar");
            return (null, null);
        }

        var entries = await _db.GetHangarVesselsAsync(session.UserId);
        await CleanupStaleDeployedRecordsAsync(session.UserId, entries, vesselGuid);

        var shipyard = EntityManager.System<ShipyardSystem>();
        shipyard.SetupShipyardIfNeeded();
        var shipyardMap = shipyard.ShipyardMap;
        if (shipyardMap == null)
        {
            _sawmill.Error("Hangar retrieve blocked: shipyard map unavailable");
            return (null, null);
        }

        var savePath = new ResPath(record.SavePath);
        EntityUid shuttleUid;
        try
        {
            if (!_mapLoader.TryLoadGrid(shipyardMap.Value, savePath, out var grid))
            {
                _sawmill.Error($"Failed to load hangar grid from {savePath}");
                return (null, null);
            }

            shuttleUid = grid!.Value.Owner;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Hangar grid load crashed for {savePath}: {ex}");
            return (null, null);
        }
        SanitizeHangarGridPhysics(shuttleUid);
        ReinitializeGridAtmosphere(shuttleUid);

        if (!TryComp<ShuttleComponent>(shuttleUid, out var shuttle))
        {
            QueueDel(shuttleUid);
            return (null, null);
        }

        if (!TryComp<StationDataComponent>(stationUid, out var stationData))
        {
            QueueDel(shuttleUid);
            return (null, null);
        }

        var targetGrid = _station.GetLargestGrid((stationUid, stationData));
        if (targetGrid == null)
        {
            QueueDel(shuttleUid);
            return (null, null);
        }

        if (!_shuttle.TryFTLDock(shuttleUid, shuttle, targetGrid.Value))
            _sawmill.Warning($"Hangar retrieve: FTL dock failed for {shuttleUid}, falling back to proximity placement");

        record.State = (int) HangarVesselState.Deployed;
        await _db.UpsertHangarVesselAsync(record);

        _sawmill.Info($"Retrieved hangar vessel {vesselGuid} as {shuttleUid}");
        return (shuttleUid, record.CustomName);
    }

    private async Task CleanupStaleDeployedRecordsAsync(
        NetUserId userId,
        List<DbHangarVessel> entries,
        Guid? activeVesselGuid)
    {
        foreach (var entry in entries)
        {
            if (entry.State != (int) HangarVesselState.Deployed)
                continue;

            if (activeVesselGuid == entry.VesselGuid)
                continue;

            await _db.DeleteHangarVesselAsync(entry.VesselGuid);
            _sawmill.Info($"Removed stale deployed hangar record {entry.VesselGuid} for {userId}");
        }
    }

    public async Task DeleteHangarRecordAsync(Guid vesselGuid, ResPath? savePath = null)
    {
        if (savePath == null)
        {
            var record = await _db.GetHangarVesselAsync(vesselGuid);
            if (record != null)
                savePath = new ResPath(record.SavePath);
        }

        await _db.DeleteHangarVesselAsync(vesselGuid);

        if (savePath is { } path && _res.UserData.Exists(path))
            _res.UserData.Delete(path);
    }

    private void PrepareShuttleForHangarSave(EntityUid gridUid, EntityUid consoleUid, ShipyardSystem shipyard)
    {
        SanitizeHangarGridPhysics(gridUid);

        shipyard.CleanGridForHangar(gridUid, consoleUid);

        if (_station.GetOwningStation(gridUid) is { Valid: true } stationUid)
        {
            RemComp<StationMemberComponent>(gridUid);
            Del(stationUid);
        }

        StripHangarComponentsOnGrid(gridUid);
    }

    /// <summary>
    /// Strips dock state and cross-grid weld joints so hangar grids can be saved/loaded without stale physics refs.
    /// </summary>
    private void SanitizeHangarGridPhysics(EntityUid gridUid)
    {
        _docking.UndockDocks(gridUid);
        ClearResidualDockState(gridUid);
        RemoveInvalidGridJoints(gridUid);
    }

    private void ClearResidualDockState(EntityUid gridUid)
    {
        foreach (var dock in _docking.GetDocks(gridUid))
        {
            dock.Comp.DockedWith = null;
            dock.Comp.DockJoint = null;
            dock.Comp.DockJointId = null;
        }
    }

    private void RemoveInvalidGridJoints(EntityUid gridUid)
    {
        var query = AllEntityQuery<JointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var jointComp, out var xform))
        {
            if (uid != gridUid && xform.GridUid != gridUid)
                continue;

            var toRemove = new List<string>();
            foreach (var (id, joint) in jointComp.GetJoints)
            {
                if (id.StartsWith("docking", StringComparison.Ordinal)
                    || !IsEntityOnGrid(joint.BodyAUid, gridUid)
                    || !IsEntityOnGrid(joint.BodyBUid, gridUid))
                {
                    toRemove.Add(id);
                }
            }

            foreach (var id in toRemove)
                _joints.RemoveJoint(uid, id);
        }
    }

    private bool IsEntityOnGrid(EntityUid uid, EntityUid gridUid)
    {
        if (uid == gridUid)
            return true;

        if (!uid.IsValid())
            return false;

        return TryComp(uid, out TransformComponent? xform) && xform.GridUid == gridUid;
    }

    private void StripHangarComponentsOnGrid(EntityUid gridUid)
    {
        var query = AllEntityQuery<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (uid != gridUid && xform.GridUid != gridUid)
                continue;

            RemComp<ShuttleDeedComponent>(uid);
            RemComp<StationMemberComponent>(uid);
            RemComp<StationRecordsComponent>(uid);

            if (TryComp<StationRecordKeyStorageComponent>(uid, out var keyStorage))
            {
                keyStorage.Key = null;
                Dirty(uid, keyStorage);
            }
        }
    }

    private ShipyardSystem.ShipyardSaleError ValidateShuttle(EntityUid shuttleUid, EntityUid stationUid)
    {
        if (!TryComp<StationDataComponent>(stationUid, out var stationGrid)
            || !HasComp<ShuttleComponent>(shuttleUid))
            return ShipyardSystem.ShipyardSaleError.InvalidShip;

        var targetGrid = _station.GetLargestGrid((stationUid, stationGrid));
        if (targetGrid == null)
            return ShipyardSystem.ShipyardSaleError.InvalidShip;

        var gridDocks = _docking.GetDocks(targetGrid.Value);
        var shuttleDocks = _docking.GetDocks(shuttleUid);
        var isDocked = shuttleDocks.Any(sd => gridDocks.Any(gd => sd.Comp.DockedWith == gd.Owner));
        if (!isDocked)
            return ShipyardSystem.ShipyardSaleError.Undocked;

        var shipyard = EntityManager.System<ShipyardSystem>();
        var mobQuery = GetEntityQuery<MobStateComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();
        if (shipyard.FoundOrganics(shuttleUid, mobQuery, xformQuery) != null)
            return ShipyardSystem.ShipyardSaleError.OrganicsAboard;

        return ShipyardSystem.ShipyardSaleError.Success;
    }

    private void ReinitializeGridAtmosphere(EntityUid gridUid)
    {
        if (!TryComp<GridAtmosphereComponent>(gridUid, out var atmos))
            return;

        foreach (var tile in atmos.Tiles.Values)
        {
            if (tile.Air is not { Immutable: false } air)
                continue;

            air.Clear();
            air.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            air.Temperature = Atmospherics.T20C;
        }

        if (TryComp<MapGridComponent>(gridUid, out var mapGrid))
            _atmosphere.InvalidateAllTiles((gridUid, mapGrid, atmos));
    }
}
