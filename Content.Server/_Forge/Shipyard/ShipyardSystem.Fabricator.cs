using Content.Server._NF.Station.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Forge.Shipyard;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Maps;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    public bool TryAssignFabricatedShuttle(
        EntityUid player,
        EntityUid targetId,
        EntityUid shuttleUid,
        VesselPrototype vessel)
    {
        if (TryComp<ShuttleDeedComponent>(targetId, out var existingDeed) && existingDeed.ShuttleUid is { Valid: true })
            return false;

        if (HasComp<ShuttleDeedComponent>(targetId))
            RemComp<ShuttleDeedComponent>(targetId);

        var name = vessel.Name;

        if (_prototypeManager.TryIndex<GameMapPrototype>(vessel.ID, out var stationProto))
        {
            List<EntityUid> gridUids = [shuttleUid];
            var shuttleStation = _station.InitializeNewStation(stationProto.Stations[vessel.ID], gridUids);
            name = Name(shuttleStation);

            var vesselInfo = EnsureComp<ExtraShuttleInformationComponent>(shuttleStation);
            vesselInfo.Vessel = vessel.ID;
        }

        EnsureComp<FTLLockComponent>(shuttleUid);
        var shuttleConsoleSystem = EntityManager.System<ShuttleConsoleSystem>();
        shuttleConsoleSystem.ToggleFTLLock(shuttleUid, new List<NetEntity>(), true);

        var shuttleOwner = Name(player).Trim();
        var deedId = EnsureComp<ShuttleDeedComponent>(targetId);
        AssignShuttleDeedProperties(deedId, shuttleUid, name, shuttleOwner, false);
        deedId.DeedHolder = targetId;
        deedId.HangarState = HangarVesselState.Deployed;

        var deedShuttle = EnsureComp<ShuttleDeedComponent>(shuttleUid);
        AssignShuttleDeedProperties(deedShuttle, shuttleUid, name, shuttleOwner, false);
        deedShuttle.DeedHolder = targetId;
        deedShuttle.HangarState = HangarVesselState.Deployed;

        LockShuttleConsolesForDeed(shuttleUid, targetId);

        if (TryComp<ActorComponent>(player, out var actorComp) && actorComp.PlayerSession != null)
            _shipOwnership.RegisterShipOwnership(shuttleUid, actorComp.PlayerSession);

        EnsureComp<VesselComponent>(shuttleUid).VesselId = vessel.ID;
        return true;
    }
}
