using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._Forge.Shipyard;
using Content.Shared._Forge.CCVar;
using Content.Shared._Forge.Shipyard;
using Content.Shared._Forge.Shipyard.Events;
using Content.Shared._Mono.FireControl;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.BUI;
using Content.Shared._NF.Shipyard.Components;
using Content.Server._NF.Station.Components;
using Content.Server.Shuttles.Components;
using Content.Shared.Maps;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    [Dependency] private readonly ShuttleHangarSystem _hangar = default!;

    private void InitializeHangar()
    {
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleStoreHangarMessage>(OnStoreHangarMessage);
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleRetrieveHangarMessage>(OnRetrieveHangarMessage);
    }

    private async void OnStoreHangarMessage(EntityUid uid, ShipyardConsoleComponent component, ShipyardConsoleStoreHangarMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId)
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-no-idcard"));
            PlayDenySound(player, uid, component);
            return;
        }

        if (!TryComp<ShuttleDeedComponent>(targetId, out var deed) || deed.ShuttleUid is not { Valid: true })
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-no-deed"));
            PlayDenySound(player, uid, component);
            return;
        }

        if (_station.GetOwningStation(uid) is not { Valid: true } stationUid)
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-invalid-station"));
            PlayDenySound(player, uid, component);
            return;
        }

        if (_player.TryGetSessionByEntity(player, out var storeSession))
        {
            var (used, max) = await _hangar.GetHangarSlotInfoAsync(storeSession.UserId);
            if (used >= max)
            {
                ConsolePopup(player, Loc.GetString("shipyard-hangar-slots-full", ("used", used), ("max", max)));
                PlayDenySound(player, uid, component);
                return;
            }
        }

        var customName = GetFullName(deed);
        var (stored, vesselGuid) = await _hangar.TryStoreInHangar(player, uid, stationUid, deed);
        if (!stored || vesselGuid == null)
        {
            ConsolePopup(player, Loc.GetString("shipyard-hangar-store-failed"));
            PlayDenySound(player, uid, component);
            return;
        }

        await _hangar.UpdateHangarCustomNameAsync(vesselGuid.Value, customName);
        ClearDeedAfterHangarStore(targetId);

        ConsolePopup(player, Loc.GetString("shipyard-hangar-store-success"));
        PlayConfirmSound(player, uid, component);
        await RefreshHangarState(uid, component, player, targetId, args.UiKey);
    }

    private async void OnRetrieveHangarMessage(EntityUid uid, ShipyardConsoleComponent component, ShipyardConsoleRetrieveHangarMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId)
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-no-idcard"));
            PlayDenySound(player, uid, component);
            return;
        }

        if (TryComp<ShuttleDeedComponent>(targetId, out var existingDeed) && existingDeed.ShuttleUid is { Valid: true })
        {
            ConsolePopup(player, Loc.GetString("shipyard-hangar-already-deployed"));
            PlayDenySound(player, uid, component);
            return;
        }

        if (_station.GetOwningStation(uid) is not { Valid: true } stationUid)
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-invalid-station"));
            PlayDenySound(player, uid, component);
            return;
        }

        if (args.VesselGuid == Guid.Empty)
        {
            ConsolePopup(player, Loc.GetString("shipyard-hangar-select-vessel"));
            PlayDenySound(player, uid, component);
            return;
        }

        var vesselGuid = args.VesselGuid;
        var (shuttleUid, shuttleName) = await _hangar.TryRetrieveFromHangar(player, targetId, uid, stationUid, vesselGuid);
        if (shuttleUid == null)
        {
            ConsolePopup(player, Loc.GetString("shipyard-hangar-retrieve-failed"));
            PlayDenySound(player, uid, component);
            return;
        }

        var hangarRecord = await _hangar.GetHangarVesselAsync(vesselGuid);
        var name = !string.IsNullOrWhiteSpace(hangarRecord?.CustomName)
            ? hangarRecord!.CustomName
            : shuttleName ?? Name(shuttleUid.Value);

        if (hangarRecord != null
            && _prototypeManager.TryIndex<GameMapPrototype>(hangarRecord.VesselProtoId, out var stationProto)
            && stationProto.Stations.TryGetValue(hangarRecord.VesselProtoId, out var stationConfig))
        {
            var shuttleStation = _station.InitializeNewStation(stationConfig, new List<EntityUid> { shuttleUid.Value });

            var vesselInfo = EnsureComp<ExtraShuttleInformationComponent>(shuttleStation);
            vesselInfo.Vessel = hangarRecord.VesselProtoId;

            _station.RenameStation(shuttleStation, name, loud: false);
            _metaData.SetEntityName(shuttleUid.Value, name);
            _metaData.SetEntityName(shuttleStation, name);
        }

        var shuttleOwner = Name(player).Trim();

        RemComp<ShuttleDeedComponent>(targetId);
        var deedID = EnsureComp<ShuttleDeedComponent>(targetId);
        AssignShuttleDeedProperties(deedID, shuttleUid, name, shuttleOwner, false);
        deedID.PersistedVesselId = vesselGuid;
        deedID.HangarState = HangarVesselState.Deployed;
        deedID.DeedHolder = targetId;

        var deedShuttle = EnsureComp<ShuttleDeedComponent>(shuttleUid.Value);
        AssignShuttleDeedProperties(deedShuttle, shuttleUid, name, shuttleOwner, false);
        deedShuttle.PersistedVesselId = vesselGuid;
        deedShuttle.HangarState = HangarVesselState.Deployed;
        deedShuttle.DeedHolder = targetId;

        LockShuttleConsolesForDeed(shuttleUid.Value, targetId);

        if (TryComp<ActorComponent>(player, out var actorComp) && actorComp.PlayerSession != null)
            _shipOwnership.RegisterShipOwnership(shuttleUid.Value, actorComp.PlayerSession);

        ConsolePopup(player, Loc.GetString("shipyard-hangar-retrieve-success"));
        PlayConfirmSound(player, uid, component);
        await RefreshHangarState(uid, component, player, targetId, args.UiKey);
    }

    private void LockShuttleConsolesForDeed(EntityUid shuttleUid, EntityUid targetId)
    {
        var shuttleConsoleQuery = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (shuttleConsoleQuery.MoveNext(out var consoleUid, out _, out var transform))
        {
            if (transform.GridUid != shuttleUid)
                continue;

            var lockComp = EnsureComp<ShuttleConsoleLockComponent>(consoleUid);
            _shuttleConsoleLock.SetShuttleId(consoleUid, shuttleUid.ToString(), lockComp);
        }

        var gunneryConsoleQuery = EntityQueryEnumerator<FireControlConsoleComponent, TransformComponent>();
        while (gunneryConsoleQuery.MoveNext(out var consoleUid, out _, out var transform))
        {
            if (transform.GridUid != shuttleUid)
                continue;

            var lockComp = EnsureComp<ShuttleConsoleLockComponent>(consoleUid);
            _shuttleConsoleLock.SetShuttleId(consoleUid, shuttleUid.ToString(), lockComp);
        }
    }

    /// <summary>
    /// Removes deed only when its deployed shuttle was deleted — not when stored in hangar (ShuttleUid is null).
    /// </summary>
    public void CleanupStaleShuttleDeed(EntityUid targetId, ShuttleDeedComponent deed)
    {
        if (deed.ShuttleUid is not { Valid: true } shuttleUid)
            return;

        if (Deleted(shuttleUid))
            RemComp<ShuttleDeedComponent>(targetId);
    }

    private async Task RefreshHangarState(
        EntityUid uid,
        ShipyardConsoleComponent component,
        EntityUid player,
        EntityUid targetId,
        ShipyardConsoleUiKey uiKey,
        bool freeListings = false)
    {
        var balance = TryComp<BankAccountComponent>(player, out var bank) ? bank.Balance : 0;
        var hasTargetId = component.TargetIdSlot.ContainerSlot?.ContainedEntity is { Valid: true };
        TryComp<ShuttleDeedComponent>(targetId, out var deed);
        var deedName = deed != null ? GetFullName(deed) : null;

        var sellValue = 0;
        if (deed?.ShuttleUid is { Valid: true } shuttle)
        {
            sellValue = (int) _pricing.AppraiseGrid(shuttle, LacksPreserveOnSaleComp);
            sellValue = CalculateShipResaleValue((uid, component), sellValue);
        }

        var hangarEnabled = _configManager.GetCVar(ForgeCVars.HangarEnabled);
        var hangarEntries = new List<HangarVesselEntry>();
        var canStore = false;
        var canRetrieve = false;

        var hangarSlotsUsed = 0;
        var hangarSlotsMax = _configManager.GetCVar(ForgeCVars.HangarMaxSlots);
        if (hangarEnabled && _player.TryGetSessionByEntity(player, out var session))
        {
            hangarEntries = await _hangar.GetHangarEntriesAsync(session.UserId);
            (hangarSlotsUsed, hangarSlotsMax) = await _hangar.GetHangarSlotInfoAsync(session.UserId);
            hangarEntries = hangarEntries
                .Where(e => e.State == HangarVesselState.InHangar)
                .OrderByDescending(e => e.CustomName)
                .ToList();

            if (deed?.ShuttleUid is { Valid: true } deployedUid && _station.GetOwningStation(uid) is { Valid: true } stationUid)
                canStore = _hangar.CanStoreInHangar(deployedUid, stationUid, uid, out _);

            canRetrieve = hasTargetId
                && hangarEntries.Count > 0
                && deed?.ShuttleUid is not { Valid: true };
        }

        var newState = new ShipyardConsoleInterfaceState(
            balance,
            true,
            deedName,
            sellValue,
            hasTargetId,
            (byte) uiKey,
            GetAvailableShuttles(uid, uiKey, targetId: hasTargetId ? targetId : null),
            uiKey.ToString(),
            freeListings,
            CalculateSellRate(uid),
            hangarEnabled,
            hangarEntries,
            canStore,
            canRetrieve,
            null,
            hangarSlotsUsed,
            hangarSlotsMax);

        _ui.SetUiState(uid, uiKey, newState);
    }

    /// <summary>
    /// Hangar vessels are tracked in the database; the ID card only carries a deed while deployed.
    /// </summary>
    public void ClearDeedAfterHangarStore(EntityUid targetId)
    {
        RemComp<ShuttleDeedComponent>(targetId);
    }

    public void SyncHangarCustomNameFromDeed(ShuttleDeedComponent deed)
    {
        if (deed.PersistedVesselId is not { } guid)
            return;

        _ = _hangar.UpdateHangarCustomNameAsync(guid, GetFullName(deed));
    }

    public Task DeleteHangarRecordOnSell(Guid vesselGuid) =>
        _hangar.DeleteHangarRecordAsync(vesselGuid);

    public void TransferHangarDeed(
        EntityUid sellerId,
        EntityUid buyerId,
        EntityUid buyerPlayer,
        Guid hangarGuid,
        ShuttleDeedComponent sellerDeed)
    {
        RemComp<ShuttleDeedComponent>(sellerId);
        var buyerDeed = EnsureComp<ShuttleDeedComponent>(buyerId);
        buyerDeed.PersistedVesselId = hangarGuid;
        buyerDeed.HangarState = HangarVesselState.InHangar;
        buyerDeed.ShuttleUid = null;
        buyerDeed.ShuttleName = sellerDeed.ShuttleName;
        buyerDeed.ShuttleNameSuffix = sellerDeed.ShuttleNameSuffix;
        buyerDeed.ShuttleOwner = Name(buyerPlayer);
        buyerDeed.DeedHolder = buyerId;
        Dirty(buyerId, buyerDeed);
    }
}
