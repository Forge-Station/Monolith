using System.Numerics;
using Content.Server._WF.CombatConsole;
using Content.Shared._Mono.FireControl;
using Content.Shared.Shuttles.BUIStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Mono.FireControl;

public sealed partial class FireControlSystem
{
    private TimeSpan? _wfDockSnapshotTime;
    private Dictionary<NetEntity, List<DockingPortState>> _wfDockSnapshot = new();

    /// <summary>Shares docking discovery across gunnery snapshots produced in the same server tick.</summary>
    private Dictionary<NetEntity, List<DockingPortState>> WfGetDocks()
    {
        if (_wfDockSnapshotTime != _timing.CurTime)
        {
            _wfDockSnapshotTime = _timing.CurTime;
            _wfDockSnapshot = _shuttleConsoleSystem.GetAllDocks();
        }
        return _wfDockSnapshot;
    }

    /// <summary>Finds a clear lane, preferring the direction away from the ship's center.</summary>
    public bool WfFlareTarget(EntityUid weapon, out NetCoordinates target)
    {
        var xform = Transform(weapon);
        var origin = _xform.GetWorldPosition(xform);
        var center = xform.GridUid is { } grid && TryComp<MapGridComponent>(grid, out var hull)
            ? _xform.ToMapCoordinates(new EntityCoordinates(grid, hull.LocalAABB.Center)).Position
            : origin;
        var outward = origin - center;
        var bearing = outward.LengthSquared() > 0.01f ? outward.ToAngle() : _xform.GetWorldRotation(xform);
        for (var i = 0; i < 16; i++)
        {
            var step = (i + 1) / 2 * (i % 2 == 0 ? -1 : 1);
            var point = origin + (bearing + Angle.FromDegrees(step * 22.5)).ToVec() * 30;
            if (!HasLineOfSight(weapon, origin, point, xform.MapID))
                continue;
            target = GetNetCoordinates(_xform.ToCoordinates(new MapCoordinates(point, xform.MapID)));
            return true;
        }
        target = default;
        return false;
    }

    /// <summary>Refreshes the original radar and weapon state after a console setting changes.</summary>
    public void WfRefreshConsole(EntityUid uid, bool periodic = false)
    {
        if (TerminatingOrDeleted(uid))
            return;
        var settings = EnsureComp<WFCombatConsoleComponent>(uid);
        if (periodic && settings.NextTelemetry > _timing.CurTime)
            return;
        if (periodic && settings.NextRadarTelemetry > _timing.CurTime &&
            TryComp<FireControlConsoleComponent>(uid, out var console) &&
            _ui.TryGetUiState<FireControlConsoleBoundInterfaceState>(uid, FireControlConsoleUiKey.Key, out var previous))
        {
            settings.NextTelemetry = _timing.CurTime + TimeSpan.FromSeconds(0.25);
            var combat = EntityManager.System<WFCombatConsoleSystem>();
            var state = new FireControlConsoleBoundInterfaceState(combat.TryGetServer(uid, console, out _, out _),
                previous.FireControllables, previous.NavState) { Combat = combat.GetState(uid, console) };
            WfPublishState(uid, state);
            return;
        }
        UpdateUi(uid);
    }

    /// <summary>Keeps unchanged snapshots stable so native windows and cockpit viewers avoid redundant updates.</summary>
    private void WfPublishState(EntityUid uid, FireControlConsoleBoundInterfaceState state)
    {
        if (_ui.TryGetUiState<FireControlConsoleBoundInterfaceState>(uid, FireControlConsoleUiKey.Key, out var previous) &&
            WFCombatSnapshotEquality.Same(previous, state))
            return;
        _ui.SetUiState(uid, FireControlConsoleUiKey.Key, state);
    }

    private void WfUpdateCombatState(EntityUid uid, FireControlConsoleComponent console,
        FireControlConsoleBoundInterfaceState state)
    {
        var settings = EnsureComp<WFCombatConsoleComponent>(uid);
        settings.NextTelemetry = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        settings.NextRadarTelemetry = _timing.CurTime + TimeSpan.FromSeconds(1);
        state.NavState.NetworkPortNames = new Dictionary<string, string>(state.NavState.NetworkPortNames);
        var system = EntityManager.System<WFCombatConsoleSystem>();
        state.Connected &= system.TryGetServer(uid, console, out _, out _);
        state.Combat = system.GetState(uid, console);
    }
}
