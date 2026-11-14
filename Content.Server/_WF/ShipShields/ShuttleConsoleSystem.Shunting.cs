using System.Linq;
using Content.Server._WF.ShipShields;
using Content.Shared._WF.ShipShields;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private WFShipShieldShuntSystem _wfShieldShunts = default!;

    private float _wfShieldHelmAccumulator;
    private readonly Dictionary<EntityUid, WFShipShieldShuntState> _wfShieldHelmStates = new();

    /// <summary>Refreshes open helms when shield availability or visible integrity changes.</summary>
    private void UpdateWolfgateShieldHelms(float frameTime)
    {
        _wfShieldHelmAccumulator += frameTime;
        if (_wfShieldHelmAccumulator < 0.2f)
            return;
        _wfShieldHelmAccumulator = 0f;
        var open = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<ShuttleConsoleComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_ui.IsUiOpen(uid, ShuttleConsoleUiKey.Key))
                continue;
            open.Add(uid);
            var selected = new ConsoleShuttleEvent { Console = uid };
            RaiseLocalEvent(uid, ref selected);
            var grid = selected.Console is { } console && TryComp(console, out TransformComponent? transform)
                ? transform.GridUid : null;
            var current = GetWolfgateShieldShuntState(grid);
            if (_wfShieldHelmStates.TryGetValue(uid, out var previous) &&
                previous.Available == current.Available && previous.Active == current.Active && previous.Enabled == current.Enabled &&
                WFShipShieldGeneratorStats.Same(previous.Stats, current.Stats) &&
                previous.RecoveryStatus == current.RecoveryStatus && previous.RecoverySeconds == current.RecoverySeconds &&
                MathF.Round(previous.Health * 100f) == MathF.Round(current.Health * 100f) &&
                (previous.Health < 0.1f) == (current.Health < 0.1f) &&
                previous.DirectionRadians == current.DirectionRadians &&
                previous.Concentration == current.Concentration && previous.ArcRadians == current.ArcRadians &&
                previous.TargetDirectionRadians == current.TargetDirectionRadians && previous.TargetConcentration == current.TargetConcentration &&
                previous.TargetArcRadians == current.TargetArcRadians)
                continue;
            _wfShieldHelmStates[uid] = current;
            _ui.ServerSendUiMessage(uid, ShuttleConsoleUiKey.Key, new WFShipShieldHelmUpdateMessage(current));
        }
        foreach (var uid in _wfShieldHelmStates.Keys.Where(uid => !open.Contains(uid)).ToArray())
            _wfShieldHelmStates.Remove(uid);
    }

    /// <summary>Includes the target ship's shield allocation in each helm refresh.</summary>
    private WFShipShieldShuntState GetWolfgateShieldShuntState(EntityUid? grid)
    {
        return _wfShieldShunts.GetState(grid);
    }
}
