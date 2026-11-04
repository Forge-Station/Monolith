using System.Numerics;
using Content.Server.Administration.Managers;
using Content.Shared._WF.NpcVehicleCrew;
using Content.Shared.Administration;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WF.NpcVehicleCrew;

/// <summary>The admin panel's endpoint: spawn crews beside the admin, list them, give them orders, go to them.</summary>
public sealed partial class WFVehicleCrewSystem
{
    [Dependency] private IAdminManager _admins = default!;

    /// <summary>The most crews one press spawns.</summary>
    public const int MaxPanelSpawn = 8;

    /// <summary>Metres between vehicles spawned together, side by side.</summary>
    private const float SpawnSpacing = 7f;

    private void InitializePanel()
    {
        SubscribeNetworkEvent<WFVehicleCrewPanelRequest>(OnPanelRequest);
    }

    private void OnPanelRequest(WFVehicleCrewPanelRequest request, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!_admins.HasAdminFlag(session, AdminFlags.Admin))
            return;

        string? message = null;
        var error = false;
        switch (request.Action)
        {
            case WFVehicleCrewPanelAction.Spawn:
                error = !PanelSpawn(session, request, out message);
                break;
            case WFVehicleCrewPanelAction.Order:
                error = !PanelOrder(session, request, out message);
                break;
            case WFVehicleCrewPanelAction.HomeHere:
                error = !PanelHome(session, request, out message);
                break;
            case WFVehicleCrewPanelAction.GoTo:
                error = !PanelGoTo(session, request, out message);
                break;
            case WFVehicleCrewPanelAction.Delete:
                if (PanelCrew(request) is { } doomed)
                {
                    Clear(doomed.Comp.Group);
                    message = Loc.GetString("wf-vehiclecrew-panel-deleted", ("vehicle", Name(doomed)));
                }
                break;
            case WFVehicleCrewPanelAction.ClearAll:
                message = Loc.GetString("wf-vehiclecrew-cleared", ("count", Clear()));
                break;
        }

        RaiseNetworkEvent(new WFVehicleCrewPanelState { Crews = PanelEntries(), Message = message, Error = error }, session);
    }

    private bool PanelSpawn(ICommonSession session, WFVehicleCrewPanelRequest request, out string message)
    {
        if (session.AttachedEntity is not { } admin)
        {
            message = Loc.GetString("wf-vehiclecrew-no-entity");
            return false;
        }

        if (request.Crew is not { } crewId || !_prototypes.HasIndex<WFVehicleCrewPrototype>(crewId))
        {
            message = Loc.GetString("wf-vehiclecrew-unknown-crew", ("crew", request.Crew ?? string.Empty));
            return false;
        }

        var count = Math.Clamp(request.Count, 1, MaxPanelSpawn);
        var distance = Math.Clamp(request.Distance, 0f, 64f);
        var origin = Transform(admin).Coordinates;
        var spawned = 0;
        for (var i = 0; i < count; i++)
        {
            var coords = origin.Offset(new Vector2(distance, i * SpawnSpacing));
            if (!TrySpawn(crewId, coords, out var vehicle, request.Behaviour))
                continue;
            spawned++;
            if (request.Behaviour == WFVehicleBehaviour.Escort)
                SetLead(vehicle, admin);
        }

        message = Loc.GetString("wf-vehiclecrew-panel-spawned", ("count", spawned), ("crew", PanelCrewName(crewId)));
        return spawned > 0;
    }

    private bool PanelOrder(ICommonSession session, WFVehicleCrewPanelRequest request, out string message)
    {
        message = string.Empty;
        if (PanelCrew(request) is not { } vehicle || request.Behaviour is not { } behaviour)
            return false;

        if (vehicle.Comp.Bailed)
        {
            message = Loc.GetString("wf-vehiclecrew-panel-bailed", ("vehicle", Name(vehicle)));
            return false;
        }

        if (behaviour == WFVehicleBehaviour.Escort)
        {
            if (session.AttachedEntity is not { } admin)
            {
                message = Loc.GetString("wf-vehiclecrew-no-entity");
                return false;
            }
            SetLead(vehicle.AsNullable(), admin);
        }
        else
        {
            SetBehaviour(vehicle.AsNullable(), behaviour);
        }

        message = Loc.GetString("wf-vehiclecrew-panel-ordered", ("vehicle", Name(vehicle)),
            ("behaviour", Loc.GetString($"wf-vehiclecrew-behaviour-{behaviour.ToString().ToLowerInvariant()}")));
        return true;
    }

    private bool PanelHome(ICommonSession session, WFVehicleCrewPanelRequest request, out string message)
    {
        message = string.Empty;
        if (PanelCrew(request) is not { } vehicle)
            return false;
        if (session.AttachedEntity is not { } admin)
        {
            message = Loc.GetString("wf-vehiclecrew-no-entity");
            return false;
        }

        var here = _transform.GetMapCoordinates(admin);
        if (here.MapId != _transform.GetMapCoordinates(vehicle).MapId)
        {
            message = Loc.GetString("wf-vehiclecrew-panel-other-map", ("vehicle", Name(vehicle)));
            return false;
        }

        SetHome(vehicle.AsNullable(), here);
        message = Loc.GetString("wf-vehiclecrew-panel-homed", ("vehicle", Name(vehicle)));
        return true;
    }

    private bool PanelGoTo(ICommonSession session, WFVehicleCrewPanelRequest request, out string message)
    {
        message = string.Empty;
        if (PanelCrew(request) is not { } vehicle || session.AttachedEntity is not { } admin)
            return false;

        // Beside the hull rather than on it, so a ghost doesn't land inside the vehicle's own fixtures.
        _transform.SetCoordinates(admin, Transform(vehicle).Coordinates.Offset(new Vector2(0f, -3f)));
        _transform.AttachToGridOrMap(admin);
        return true;
    }

    private Entity<WFVehicleCrewComponent>? PanelCrew(WFVehicleCrewPanelRequest request)
    {
        return TryGetEntity(request.Vehicle, out var uid) && TryComp(uid, out WFVehicleCrewComponent? crew)
            ? (uid.Value, crew)
            : null;
    }

    private List<WFVehicleCrewPanelEntry> PanelEntries()
    {
        var entries = new List<WFVehicleCrewPanelEntry>();
        foreach (var (uid, crew) in GetCrews())
        {
            // A crew just deleted is only queued to go; the panel shouldn't list it.
            if (EntityManager.IsQueuedForDeletion(uid) || TerminatingOrDeleted(uid))
                continue;

            CollectMembers(crew);
            var total = _members.Count;
            var map = _transform.GetMapCoordinates(uid);
            var mapName = Transform(uid).MapUid is { } mapUid ? Name(mapUid) : "?";
            entries.Add(new WFVehicleCrewPanelEntry
            {
                Vehicle = GetNetEntity(uid),
                Name = Name(uid),
                Crew = PanelCrewName(crew.Prototype),
                Behaviour = crew.Behaviour,
                Bailed = crew.Bailed,
                Alive = CountAlive(crew),
                Total = total,
                Integrity = IntegrityFraction(uid),
                Location = $"{mapName} ({map.Position.X:0}, {map.Position.Y:0})",
            });
        }
        return entries;
    }

    private string PanelCrewName(string crewId)
    {
        return _prototypes.TryIndex<WFVehicleCrewPrototype>(crewId, out var proto) && proto.Name is { } name
            ? Loc.GetString(name)
            : crewId;
    }
}
