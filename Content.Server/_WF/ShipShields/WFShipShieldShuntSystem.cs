using Content.Server._Crescent.ShipShields;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.ShipShields;
using Content.Shared.Access.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Physics.Components;
using System.Linq;
using Content.Server.Power.Components;
using Content.Server._Crescent.ShipShields.Components;

namespace Content.Server._WF.ShipShields;

/// <summary>Applies authorized helm allocations to the ship selected by the console.</summary>
public sealed class WFShipShieldShuntSystem : EntitySystem
{
    [Dependency] private ShipShieldsSystem _shields = default!;
    [Dependency] private ShuttleConsoleLockSystem _locks = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    private float _generatorUiAccumulator;
    private readonly Dictionary<EntityUid, WFShipShieldShuntState> _generatorStates = new();

    /// <summary>What each open gunnery console was last sent, and who was looking at it.</summary>
    private readonly Dictionary<EntityUid, (WFShipShieldShuntState State, HashSet<EntityUid> Viewers)> _gunneryStates = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<WFShipShieldSetShuntMessage>(OnSetShunt);
            subs.Event<WFShipShieldSetEnabledMessage>(OnHelmSetEnabled);
        });
        Subs.BuiEvents<ShipShieldEmitterComponent>(WFShipShieldUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnGeneratorOpened);
            subs.Event<WFShipShieldSetShuntMessage>(OnGeneratorSetShunt);
            subs.Event<WFShipShieldSetEnabledMessage>(OnGeneratorSetEnabled);
        });
        // The gunnery console's own systems handle everything else on this interface; only shield requests are taken here.
        Subs.BuiEvents<FireControlConsoleComponent>(FireControlConsoleUiKey.Key, subs =>
        {
            subs.Event<WFShipShieldSetShuntMessage>(OnGunnerySetShunt);
            subs.Event<WFShipShieldSetEnabledMessage>(OnGunnerySetEnabled);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _shields.UpdateWolfgateShieldShunts(frameTime);
        _generatorUiAccumulator += frameTime;
        if (_generatorUiAccumulator < 0.2f)
            return;
        _generatorUiAccumulator = 0f;
        var open = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<ShipShieldEmitterComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_ui.IsUiOpen(uid, WFShipShieldUiKey.Key))
                continue;
            open.Add(uid);
            UpdateGeneratorUi(uid);
        }
        foreach (var uid in _generatorStates.Keys.Where(uid => !open.Contains(uid)).ToArray())
            _generatorStates.Remove(uid);
        UpdateGunneryConsoles();
    }

    /// <summary>Sends each open gunnery console its ship's shield state when it changes or someone new looks.</summary>
    private void UpdateGunneryConsoles()
    {
        var open = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<FireControlConsoleComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            var viewers = _ui.GetActors(uid, FireControlConsoleUiKey.Key).ToHashSet();
            if (viewers.Count == 0)
                continue;
            open.Add(uid);
            var state = GetState(GunneryGrid(uid));
            // Who, not how many: one gunner leaving as another arrives still leaves the newcomer with nothing.
            if (_gunneryStates.TryGetValue(uid, out var previous) && previous.Viewers.SetEquals(viewers) && Unchanged(previous.State, state))
                continue;
            _gunneryStates[uid] = (state, viewers);
            _ui.ServerSendUiMessage(uid, FireControlConsoleUiKey.Key, new WFShipShieldHelmUpdateMessage(state));
        }
        foreach (var uid in _gunneryStates.Keys.Where(uid => !open.Contains(uid)).ToArray())
            _gunneryStates.Remove(uid);
    }

    /// <summary>Whether two snapshots would show the same thing on a control panel.</summary>
    private static bool Unchanged(WFShipShieldShuntState previous, WFShipShieldShuntState state)
    {
        return previous.Available == state.Available && previous.Active == state.Active && previous.Enabled == state.Enabled &&
            WFShipShieldGeneratorStats.Same(previous.Stats, state.Stats) &&
            previous.RecoveryStatus == state.RecoveryStatus && previous.RecoverySeconds == state.RecoverySeconds &&
            MathF.Round(previous.Health * 100f) == MathF.Round(state.Health * 100f) &&
            (previous.Health < 0.1f) == (state.Health < 0.1f) &&
            previous.DirectionRadians == state.DirectionRadians && previous.Concentration == state.Concentration &&
            previous.ArcRadians == state.ArcRadians &&
            previous.TargetDirectionRadians == state.TargetDirectionRadians && previous.TargetConcentration == state.TargetConcentration &&
            previous.TargetArcRadians == state.TargetArcRadians;
    }

    /// <summary>The ship a working gunnery console speaks for.</summary>
    private EntityUid? GunneryGrid(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) && Transform(uid).Anchored && this.IsPowered(uid, EntityManager)
            ? Transform(uid).GridUid : null;
    }

    private void OnGunnerySetShunt(Entity<FireControlConsoleComponent> ent, ref WFShipShieldSetShuntMessage args)
    {
        if (TryGetGunneryGrid(ent.Owner, args.Actor) is { } grid)
            _shields.RequestWolfgateShieldShunt(grid, args.DirectionRadians, args.Concentration, args.ArcRadians);
    }

    private void OnGunnerySetEnabled(Entity<FireControlConsoleComponent> ent, ref WFShipShieldSetEnabledMessage args)
    {
        if (TryGetGunneryGrid(ent.Owner, args.Actor) is { } grid)
            _shields.SetWolfgateShieldEnabled(grid, args.Enabled);
    }

    /// <summary>A gunner may work the shields of the ship their powered, anchored console sits on, if it has any.</summary>
    private EntityUid? TryGetGunneryGrid(EntityUid uid, EntityUid actor)
    {
        return GunneryGrid(uid) is { } grid && _blocker.CanInteract(actor, uid) && _access.IsAllowed(actor, uid) &&
            HasShieldGenerator(grid) ? grid : null;
    }

    private void OnGeneratorOpened(Entity<ShipShieldEmitterComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateGeneratorUi(ent.Owner, true);
    }

    private void UpdateGeneratorUi(EntityUid uid, bool force = false)
    {
        var state = GetState(Transform(uid).Anchored ? Transform(uid).GridUid : null);
        if (!force && _generatorStates.TryGetValue(uid, out var previous) && Unchanged(previous, state))
            return;
        _generatorStates[uid] = state;
        _ui.SetUiState(uid, WFShipShieldUiKey.Key, new WFShipShieldGeneratorUiState(state));
    }

    private void OnGeneratorSetShunt(Entity<ShipShieldEmitterComponent> ent, ref WFShipShieldSetShuntMessage args)
    {
        if (TryGetGeneratorGrid(ent.Owner, args.Actor) is { } grid)
        {
            _shields.RequestWolfgateShieldShunt(grid, args.DirectionRadians, args.Concentration, args.ArcRadians);
            UpdateGeneratorUi(ent.Owner, true);
        }
    }

    private void OnGeneratorSetEnabled(Entity<ShipShieldEmitterComponent> ent, ref WFShipShieldSetEnabledMessage args)
    {
        if (TryGetGeneratorGrid(ent.Owner, args.Actor) is { } grid)
        {
            _shields.SetWolfgateShieldEnabled(grid, args.Enabled);
            UpdateGeneratorUi(ent.Owner, true);
        }
    }

    private EntityUid? TryGetGeneratorGrid(EntityUid uid, EntityUid actor)
    {
        return !TerminatingOrDeleted(uid) && Transform(uid).Anchored &&
            _blocker.CanInteract(actor, uid) && _access.IsAllowed(actor, uid)
            ? Transform(uid).GridUid : null;
    }

    private void OnHelmSetEnabled(Entity<ShuttleConsoleComponent> ent, ref WFShipShieldSetEnabledMessage args)
    {
        if (TryGetHelmGrid(ent, args.Actor) is { } grid)
            _shields.SetWolfgateShieldEnabled(grid, args.Enabled);
    }

    private void OnSetShunt(Entity<ShuttleConsoleComponent> ent, ref WFShipShieldSetShuntMessage args)
    {
        if (TryGetHelmGrid(ent, args.Actor) is { } grid)
            _shields.RequestWolfgateShieldShunt(grid, args.DirectionRadians, args.Concentration, args.ArcRadians);
    }

    private EntityUid? TryGetHelmGrid(Entity<ShuttleConsoleComponent> ent, EntityUid actor)
    {
        if (!TryComp<PilotComponent>(actor, out var pilot) || pilot.Console != ent.Owner ||
            !Transform(ent).Anchored || !this.IsPowered(ent, EntityManager) ||
            !_blocker.CanInteract(actor, ent) || !_access.IsAllowed(actor, ent) || IsLocked(ent))
            return null;
        var selected = new ConsoleShuttleEvent { Console = ent.Owner };
        RaiseLocalEvent(ent.Owner, ref selected);
        if (selected.Console is not { } console || IsLocked(console) || !Transform(console).Anchored || !this.IsPowered(console, EntityManager) ||
            Transform(console).GridUid is not { } grid || !HasShieldGenerator(grid))
            return null;
        return grid;
    }

    private bool IsLocked(EntityUid console)
    {
        return TryComp<ShuttleConsoleLockComponent>(console, out var component) &&
            _locks.GetEffectiveLockState(console, component);
    }

    private bool HasShieldGenerator(EntityUid grid)
    {
        return FindShieldGenerator(grid) != null;
    }

    private EntityUid? FindShieldGenerator(EntityUid grid)
    {
        var query = EntityQueryEnumerator<ShipShieldEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
        {
            if (transform.GridUid == grid && transform.Anchored && !TerminatingOrDeleted(uid) && !EntityManager.IsQueuedForDeletion(uid))
                return uid;
        }
        return null;
    }

    /// <summary>Selects the installed emitter that can restore protection first.</summary>
    private EntityUid? FindRecoveryEmitter(EntityUid grid, bool enabled, out WFShipShieldRecoveryStatus status, out int seconds)
    {
        status = WFShipShieldRecoveryStatus.None;
        seconds = 0;
        EntityUid? selected = null;
        var disabled = HasComp<ShipShieldDisabledGridComponent>(grid);
        var query = EntityQueryEnumerator<ShipShieldEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var emitter, out var transform))
        {
            if (transform.GridUid != grid || !transform.Anchored || TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid))
                continue;
            var hasReceiver = TryComp<ApcPowerReceiverComponent>(uid, out var receiver);
            var estimate = ShipShieldsSystem.EstimateWolfgateShieldRecovery(emitter,
                hasReceiver && receiver!.Powered, enabled, disabled);
            if (selected != null && (estimate.Seconds < 0 || seconds >= 0 && estimate.Seconds >= seconds))
                continue;
            selected = uid;
            status = estimate.Status;
            seconds = estimate.Seconds;
        }
        return selected;
    }

    /// <summary>Returns allocation and availability for the console's authoritative target grid.</summary>
    public WFShipShieldShuntState GetState(EntityUid? grid)
    {
        var allocation = grid is { } uid && TryComp<WFShipShieldShuntComponent>(uid, out var component) ? component : null;
        var recoveryStatus = WFShipShieldRecoveryStatus.None;
        var recoverySeconds = 0;
        var installed = grid is { } emitterGrid ? FindShieldGenerator(emitterGrid) : null;
        var health = installed is { } emitterUid
            ? ShipShieldsSystem.GetWolfgateShieldHealth(Comp<ShipShieldEmitterComponent>(emitterUid)) : 0f;
        EntityUid? statsEmitter = null;
        var active = false;
        var ftlLocked = grid is { } ftlGrid && _shields.IsWolfgateShieldFtlLocked(ftlGrid);
        if (!ftlLocked && grid is { } activeGrid && TryComp<ShipShieldedComponent>(activeGrid, out var shielded) &&
            !TerminatingOrDeleted(shielded.Shield) && !EntityManager.IsQueuedForDeletion(shielded.Shield) &&
            TryComp<PhysicsComponent>(shielded.Shield, out var physics) && physics.CanCollide &&
            TryComp<WFShipShieldVisualsComponent>(shielded.Shield, out var visuals))
        {
            if (shielded.Source is { } source && TryComp<ShipShieldEmitterComponent>(source, out var emitter))
            {
                statsEmitter = source;
                health = ShipShieldsSystem.GetWolfgateShieldHealth(emitter);
            }
            else
            {
                statsEmitter = null;
                health = visuals.Health;
            }
            active = true;
            recoveryStatus = WFShipShieldRecoveryStatus.None;
            recoverySeconds = 0;
        }
        else if (grid is { } recoveringGrid)
        {
            installed = FindRecoveryEmitter(recoveringGrid, allocation?.Enabled ?? true, out recoveryStatus, out recoverySeconds);
            statsEmitter = installed;
            health = installed is { } recoveringEmitter
                ? ShipShieldsSystem.GetWolfgateShieldHealth(Comp<ShipShieldEmitterComponent>(recoveringEmitter)) : 0f;
        }
        if (installed != null && ftlLocked)
        {
            recoveryStatus = WFShipShieldRecoveryStatus.FtlLocked;
            recoverySeconds = -1;
        }
        return new WFShipShieldShuntState(installed != null, active, health,
            allocation?.DirectionRadians ?? MathF.PI / 2f, allocation?.Concentration ?? 0f,
            allocation?.ArcRadians ?? MathF.PI / 2f, allocation?.Enabled ?? true)
        {
            TargetDirectionRadians = allocation is { TargetInitialized: true } ? allocation.TargetDirectionRadians : allocation?.DirectionRadians ?? MathF.PI / 2f,
            TargetConcentration = allocation is { TargetInitialized: true } ? allocation.TargetConcentration : allocation?.Concentration ?? 0f,
            TargetArcRadians = allocation is { TargetInitialized: true } ? allocation.TargetArcRadians : allocation?.ArcRadians ?? MathF.PI / 2f,
            Stats = statsEmitter is { } selected ? _shields.GetWolfgateGeneratorStats(selected, Comp<ShipShieldEmitterComponent>(selected)) : null,
            RecoveryStatus = recoveryStatus,
            RecoverySeconds = recoverySeconds,
        };
    }
}
