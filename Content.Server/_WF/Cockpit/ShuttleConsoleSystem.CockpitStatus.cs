using System.Linq;
using Content.Server._Mono.NPC.HTN;
using Content.Server.NPC.HTN;
using Content.Server.Physics.Controllers;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._WF.Cockpit;
using Content.Shared.Construction.Components;
using Content.Shared.NPC;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private IGameTiming _wfCockpitTiming = default!;

    private float _wfCockpitStatusAccumulator;
    private readonly Dictionary<EntityUid, bool?> _wfCockpitAutopilotStates = new();
    private readonly Dictionary<EntityUid, Dictionary<EntityUid, GameTick>> _wfCockpitStatusViewers = new();

    /// <summary>Queues fresh lamps for each real helm open, including a reopen between status ticks.</summary>
    public void WfCockpitConsoleOpened(EntityUid console, EntityUid actor)
    {
        if (!_wfCockpitStatusViewers.TryGetValue(console, out var viewers))
            _wfCockpitStatusViewers[console] = viewers = new Dictionary<EntityUid, GameTick>();
        viewers[actor] = _wfCockpitTiming.CurTick;
        _wfShieldHelmStates.Remove(console);
        // The cached helm state can predate a drive change made while the helm was closed.
        if (TryComp<ShuttleConsoleComponent>(console, out var helm))
            RefreshWfFtlDrive(console, helm);
    }

    /// <summary>Checks active steering rather than an old destination or the destination selection button.</summary>
    public bool? GetWfCockpitAutopilotStatus(EntityUid? console)
    {
        if (console is not { } uid || !TryComp<HTNComponent>(uid, out var htn) ||
            !TryComp<ShuttleConsoleComponent>(uid, out var helm))
            return null;
        return htn.Enabled && HasComp<ActiveNPCComponent>(uid) &&
            TryComp<ShipSteererComponent>(uid, out var steering) && steering.Status == ShipSteeringStatus.Moving &&
            htn.Blackboard.TryGetValue<EntityCoordinates>(helm.AutopilotTargetKey, out var target, EntityManager) &&
            !TerminatingOrDeleted(target.EntityId) &&
            TryComp<TransformComponent>(uid, out var transform) && transform.GridUid is { } grid &&
            (!HasComp<AnchorableComponent>(uid) || transform.Anchored) && this.IsPowered(uid, EntityManager) &&
            HasComp<ShuttleComponent>(grid) && TryComp<PilotedShuttleComponent>(grid, out var piloted) &&
            piloted.InputSources.Contains(uid);
    }

    /// <summary>Refreshes only changed autopilot lamps on open helms, independently of the selected MFD.</summary>
    private void UpdateWfCockpitStatuses(float frameTime)
    {
        _wfCockpitStatusAccumulator += frameTime;
        if (_wfCockpitStatusAccumulator < 0.25f)
            return;
        _wfCockpitStatusAccumulator = 0;
        var tick = _wfCockpitTiming.CurTick;
        var open = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<ShuttleConsoleComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_ui.IsUiOpen(uid, ShuttleConsoleUiKey.Key))
                continue;
            open.Add(uid);
            var selected = new ConsoleShuttleEvent { Console = uid };
            RaiseLocalEvent(uid, ref selected);
            var active = GetWfCockpitAutopilotStatus(selected.Console);
            var changed = !_wfCockpitAutopilotStates.TryGetValue(uid, out var previous) || previous != active;
            if (changed)
            {
                _wfCockpitAutopilotStates[uid] = active;
                _ui.ServerSendUiMessage(uid, ShuttleConsoleUiKey.Key, new WFCockpitAutopilotUpdateMessage(active));
            }
            if (!_wfCockpitStatusViewers.TryGetValue(uid, out var viewers))
                continue;
            // A client can drop a message sent in the tick its window is created, so wait one tick.
            foreach (var (actor, opened) in viewers.ToArray())
            {
                if (opened >= tick)
                    continue;
                viewers.Remove(actor);
                if (!changed && _ui.IsUiOpen(uid, ShuttleConsoleUiKey.Key, actor))
                    _ui.ServerSendUiMessage(uid, ShuttleConsoleUiKey.Key, new WFCockpitAutopilotUpdateMessage(active), actor);
            }
            if (viewers.Count == 0)
                _wfCockpitStatusViewers.Remove(uid);
        }
        foreach (var uid in _wfCockpitAutopilotStates.Keys.Where(uid => !open.Contains(uid)).ToArray())
        {
            _wfCockpitAutopilotStates.Remove(uid);
        }
        foreach (var uid in _wfCockpitStatusViewers.Keys.Where(uid => !open.Contains(uid)).ToArray())
            _wfCockpitStatusViewers.Remove(uid);
    }
}
