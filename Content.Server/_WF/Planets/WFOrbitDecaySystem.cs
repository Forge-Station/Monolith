using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.ShipPa;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Planets;
using Content.Shared._WF.ShipPa;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Planets;

/// <summary>Drops grids on an orbit layer into the atmosphere after a warning once they lose station-keeping.</summary>
// Swept, not subscribed: thruster power, docking and map changes come through paths this module doesn't own.
public sealed partial class WFOrbitDecaySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private ShipAlertSystem _alert = default!;
    [Dependency] private ShipPaSystem _pa = default!;
    [Dependency] private WFOrbitEntrySystem _orbitEntry = default!;

    /// <summary>Situation code for a decaying hull; not pilot-selectable.</summary>
    public const string AlertOrbitDecay = "WFAlertOrbitDecay";

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    // Grace after arriving on an orbit layer; FTL arrival briefly switches thrusters off.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(10);

    private readonly List<EntityUid> _orbiters = new();

    private readonly List<EntityUid> _stale = new();

    private readonly Dictionary<EntityUid, TimeSpan> _settleAt = new();

    private readonly List<EntityUid> _settleStale = new();

    // Decay needs two adrift sweeps running.
    private readonly HashSet<EntityUid> _adriftLastSweep = new();

    private readonly HashSet<EntityUid> _adriftThisSweep = new();

    private readonly HashSet<EntityUid> _thrusting = new();

    private readonly HashSet<EntityUid> _dockSeen = new();
    private readonly Queue<EntityUid> _dockQueue = new();

    private TimeSpan _nextSweep;

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + SweepInterval;
        Sweep();
    }

    /// <summary>Updates decay for every grid in orbit and clears it from grids that have left.</summary>
    private void Sweep()
    {
        CollectThrusting();
        CollectOrbiters();

        _adriftThisSweep.Clear();

        foreach (var grid in _orbiters)
        {
            if (TerminatingOrDeleted(grid))
                continue;

            _settleAt.TryAdd(grid, _timing.CurTime + SettleDelay);

            // Clear rather than skip, so a grid that became detached terrain loses an existing countdown.
            if (HasComp<WFDetachedTerrainComponent>(grid) || HasStationKeeping(grid))
            {
                ClearDecay(grid);
                continue;
            }

            _adriftThisSweep.Add(grid);

            if (_timing.CurTime < _settleAt.GetValueOrDefault(grid))
                continue;

            if (!_adriftLastSweep.Contains(grid))
                continue;

            Decay(grid);
        }

        _adriftLastSweep.Clear();
        _adriftLastSweep.UnionWith(_adriftThisSweep);

        // Grids that left orbit some other way than decaying.
        _stale.Clear();

        var query = EntityQueryEnumerator<WFOrbitDecayComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            if (Transform(uid).MapUid is not { } mapUid || !HasComp<WFOrbitLayerComponent>(mapUid))
                _stale.Add(uid);
        }

        foreach (var grid in _stale)
        {
            ClearDecay(grid);
        }

        // Forget settles for grids that left, so a return gets a fresh one.
        _settleStale.Clear();

        foreach (var (grid, _) in _settleAt)
        {
            if (TerminatingOrDeleted(grid) || Transform(grid).MapUid is not { } mapUid || !HasComp<WFOrbitLayerComponent>(mapUid))
                _settleStale.Add(grid);
        }

        foreach (var grid in _settleStale)
        {
            _settleAt.Remove(grid);
        }
    }

    /// <summary>Collects every grid with a running linear thruster.</summary>
    private void CollectThrusting()
    {
        _thrusting.Clear();

        var query = EntityQueryEnumerator<ThrusterComponent, TransformComponent>();

        while (query.MoveNext(out _, out var thruster, out var xform))
        {
            // IsOn, since broken or unpowered thrusters keep their component.
            if (thruster.Type != ThrusterType.Linear || !thruster.Enabled || !thruster.IsOn)
                continue;

            if (xform.ParentUid is { Valid: true } grid && HasComp<MapGridComponent>(grid))
                _thrusting.Add(grid);
        }
    }

    /// <summary>Collects every grid on an orbit layer.</summary>
    private void CollectOrbiters()
    {
        _orbiters.Clear();

        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var xform))
        {
            // The orbit map is itself a grid and must never be dropped.
            if (HasComp<MapComponent>(uid) || xform.MapUid is not { } mapUid || !HasComp<WFOrbitLayerComponent>(mapUid))
                continue;

            _orbiters.Add(uid);
        }
    }

    /// <summary>True when the grid or anything docked to it has a running linear thruster or force anchor.</summary>
    public bool HasStationKeeping(EntityUid grid)
    {
        _dockSeen.Clear();
        _dockQueue.Clear();
        _dockSeen.Add(grid);
        _dockQueue.Enqueue(grid);

        while (_dockQueue.Count > 0)
        {
            var member = _dockQueue.Dequeue();

            if (_thrusting.Contains(member) || HasComp<ForceAnchorComponent>(member))
                return true;

            foreach (var dock in _docking.GetDocks(member))
            {
                if (dock.Comp.DockedWith is not { } other || TerminatingOrDeleted(other))
                    continue;

                if (Transform(other).GridUid is not { } neighbour || !_dockSeen.Add(neighbour))
                    continue;

                _dockQueue.Enqueue(neighbour);
            }
        }

        return false;
    }

    /// <summary>Starts or continues one grid's countdown.</summary>
    private void Decay(EntityUid grid)
    {
        var comp = EnsureComp<WFOrbitDecayComponent>(grid);

        if (!comp.Announced)
        {
            comp.DecayAt = _timing.CurTime + comp.Grace;
            comp.PriorCode = CompOrNull<ShipAlertComponent>(grid)?.Code;
            comp.Announced = true;

            Warn(grid, comp);
        }

        if (_timing.CurTime < comp.DecayAt)
            return;

        Drop(grid, comp);
    }

    /// <summary>Sets the decay code and announces it manually so the message can include the countdown.</summary>
    private void Warn(EntityUid grid, WFOrbitDecayComponent comp)
    {
        _alert.SetCode(grid, AlertOrbitDecay, announce: false);

        if (!_proto.TryIndex<ShipAlertCodePrototype>(AlertOrbitDecay, out var proto))
            return;

        _pa.Announce(grid,
            Loc.GetString("wf-alert-orbit-decay-countdown",
                ("ship", _pa.GetShipName(grid)),
                ("seconds", (int) Math.Round(comp.Grace.TotalSeconds))),
            proto.Sound,
            color: proto.Color);
    }

    /// <summary>Drops the hull into the atmosphere through the console's own descent path.</summary>
    private void Drop(EntityUid grid, WFOrbitDecayComponent comp)
    {
        // Restore first so lift-lost remembers the ship's own code, not this warning.
        Restore(grid, comp);

        if (!_orbitEntry.TryDropFromOrbit(grid, out _))
        {
            // Refused; re-raise the warning and retry next sweep.
            if (comp.Announced)
                _alert.SetCode(grid, AlertOrbitDecay, announce: false);

            return;
        }

        RemComp<WFOrbitDecayComponent>(grid);
    }

    /// <summary>Cancels the countdown and restores the ship's prior situation code.</summary>
    public void ClearDecay(EntityUid grid)
    {
        if (!TryComp<WFOrbitDecayComponent>(grid, out var comp))
            return;

        Restore(grid, comp);
        RemComp<WFOrbitDecayComponent>(grid);
    }

    /// <summary>Silently restores the prior code, unless the crew changed it during the warning.</summary>
    private void Restore(EntityUid grid, WFOrbitDecayComponent comp)
    {
        if (!comp.Announced || comp.PriorCode is not { } prior)
            return;

        if (CompOrNull<ShipAlertComponent>(grid)?.Code != AlertOrbitDecay)
            return;

        _alert.SetCode(grid, prior, announce: false);
    }
}
