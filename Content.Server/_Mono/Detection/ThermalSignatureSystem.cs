using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Detection;
using Content.Shared._Mono.Ships;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using System.Diagnostics.CodeAnalysis;

namespace Content.Server._Mono.Detection;

/// <summary>
///     Handles the logic for thermal signatures.
/// </summary>
public sealed partial class ThermalSignatureSystem : EntitySystem
{
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const float UpdateIntervalSeconds = 1f;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(UpdateIntervalSeconds);
    private TimeSpan _nextUpdateTime;

    // Forge-Change-Start: thresholds tuned for large-fleet servers.
    // Small heats are gated by an absolute change to suppress dirty noise on cold/idle grids.
    private const float HeatChangeThresholdRelative = 1.10f;
    private const float HeatChangeThresholdAbsolute = 5f;
    private const float HeatChangeAbsoluteCutoff = 100f;
    private const float ActiveHeatThreshold = 0.01f;
    private static readonly TimeSpan MinDirtyInterval = TimeSpan.FromSeconds(2);

    // After this many consecutive zero-emission, near-zero-stored-heat ticks, prune from the active set.
    private const byte IdleTicksToPrune = 3;
    // Forge-Change-End

    // Forge-Change-Start: pooled buffers reused each tick to avoid per-tick allocations.
    private readonly List<Entity<ThermalSignatureComponent>> _gridQueue = new();
    private readonly Dictionary<EntityUid, float> _gridTotals = new();
    private readonly List<EntityUid> _activeSnapshot = new();
    private readonly List<EntityUid> _trackedSnapshot = new();
    // Forge-Change-End

    private readonly HashSet<EntityUid> _activeSources = new();
    private readonly HashSet<EntityUid> _trackedGrids = new();

    private EntityQuery<ThermalSignatureComponent> _sigQuery;
    private EntityQuery<GunComponent> _gunQuery;
    private EntityQuery<MapGridComponent> _mapGridQuery;
    private EntityQuery<PassiveThermalSignatureComponent> _passiveQuery;
    private EntityQuery<MachineThermalSignatureComponent> _machineQuery;
    private EntityQuery<PowerSupplierComponent> _powerSupplierQuery;
    private EntityQuery<ThrusterComponent> _thrusterQuery;
    private EntityQuery<FTLDriveComponent> _ftlDriveQuery;
    private EntityQuery<TransformComponent> _xformQuery; // Forge-Change

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridInitializeEvent>(OnGridInitialized);

        // some of this could also be handled in shared but there's no point since PVS is a thing
        SubscribeLocalEvent<MachineThermalSignatureComponent, GetThermalSignatureEvent>(OnMachineGetSignature);
        SubscribeLocalEvent<PassiveThermalSignatureComponent, GetThermalSignatureEvent>(OnPassiveGetSignature);
        SubscribeLocalEvent<ThermalSignatureComponent, ComponentStartup>(OnThermalStartup);
        SubscribeLocalEvent<ThermalSignatureComponent, ComponentShutdown>(OnThermalShutdown);

        SubscribeLocalEvent<ThermalSignatureComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<PowerSupplierComponent, GetThermalSignatureEvent>(OnPowerGetSignature);
        SubscribeLocalEvent<ThrusterComponent, GetThermalSignatureEvent>(OnThrusterGetSignature);
        SubscribeLocalEvent<FTLDriveComponent, GetThermalSignatureEvent>(OnFTLGetSignature);

        // Forge-Change-Start: emission-cache invalidation hooks.
        SubscribeLocalEvent<MachineThermalSignatureComponent, PowerChangedEvent>(OnMachinePowerChanged);
        SubscribeLocalEvent<PassiveThermalSignatureComponent, ComponentStartup>(OnPassiveAdded);
        SubscribeLocalEvent<MachineThermalSignatureComponent, ComponentStartup>(OnMachineAdded);
        // Forge-Change-End

        _sigQuery = GetEntityQuery<ThermalSignatureComponent>();
        _gunQuery = GetEntityQuery<GunComponent>();
        _mapGridQuery = GetEntityQuery<MapGridComponent>();
        _passiveQuery = GetEntityQuery<PassiveThermalSignatureComponent>();
        _machineQuery = GetEntityQuery<MachineThermalSignatureComponent>();
        _powerSupplierQuery = GetEntityQuery<PowerSupplierComponent>();
        _thrusterQuery = GetEntityQuery<ThrusterComponent>();
        _ftlDriveQuery = GetEntityQuery<FTLDriveComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>(); // Forge-Change
    }

    private void OnGridInitialized(GridInitializeEvent args)
    {
        EnsureComp<ThermalSignatureComponent>(args.EntityUid);
    }

    private void OnThermalStartup(Entity<ThermalSignatureComponent> ent, ref ComponentStartup args)
    {
        // Forge-Change-Start: track at startup only if a meaningful emission is plausible.
        // - Grids: always (they aggregate stored heat from on-grid sources).
        // - Passives: yes (constant emission needs warm-up).
        // - Powered machines: yes; unpowered ones wait for PowerChangedEvent.
        // - Dynamic emitters (PowerSupplier/Thruster/FTL): yes (live readings).
        // Plain ThermalSignatureComponent entities (e.g. fired-from grids) are added lazily by event hooks.
        var uid = ent.Owner;
        if (_mapGridQuery.HasComp(uid)
            || _passiveQuery.HasComp(uid)
            || ShouldTrackAsActiveDynamic(uid)
            || (_machineQuery.HasComp(uid) && _power.IsPowered(uid)))
        {
            _activeSources.Add(uid);
        }
        // Forge-Change-End
    }

    private void OnThermalShutdown(Entity<ThermalSignatureComponent> ent, ref ComponentShutdown args)
    {
        _activeSources.Remove(ent.Owner);
        _trackedGrids.Remove(ent.Owner);
    }

    private void OnGunShot(Entity<ThermalSignatureComponent> ent, ref GunShotEvent args)
    {
        if (_gunQuery.TryComp(ent, out var gun))
        {
            ent.Comp.StoredHeat += gun.ShootThermalSignature;
            ent.Comp.IdleTicks = 0; // Forge-Change
            _activeSources.Add(ent.Owner);
        }
    }

    private void OnMachineGetSignature(Entity<MachineThermalSignatureComponent> ent, ref GetThermalSignatureEvent args)
    {
        if (_power.IsPowered(ent.Owner))
            args.Signature += ent.Comp.Signature;
    }

    private void OnPassiveGetSignature(Entity<PassiveThermalSignatureComponent> ent, ref GetThermalSignatureEvent args)
    {
        args.Signature += ent.Comp.Signature;
    }

    private void OnPowerGetSignature(Entity<PowerSupplierComponent> ent, ref GetThermalSignatureEvent args)
    {
        args.Signature += ent.Comp.CurrentSupply * ent.Comp.HeatSignatureRatio;
    }

    private void OnThrusterGetSignature(Entity<ThrusterComponent> ent, ref GetThermalSignatureEvent args)
    {
        if (ent.Comp.Firing)
            args.Signature += ent.Comp.Thrust * ent.Comp.HeatSignatureRatio;
    }

    private void OnFTLGetSignature(Entity<FTLDriveComponent> ent, ref GetThermalSignatureEvent args)
    {
        var xform = _xformQuery.GetComponent(ent); // Forge-Change
        if (!TryComp<FTLComponent>(xform.GridUid, out var ftl))
            return;

        if (ftl.State == FTLState.Starting || ftl.State == FTLState.Cooldown)
            args.Signature += ent.Comp.ThermalSignature;
    }

    // Forge-Change-Start: emission-cache invalidation and lazy activation.
    private void OnMachinePowerChanged(EntityUid uid, MachineThermalSignatureComponent _, ref PowerChangedEvent args)
    {
        if (_sigQuery.TryComp(uid, out var sig))
        {
            sig.EmissionDirty = true;
            sig.IdleTicks = 0;
            _activeSources.Add(uid);
        }
    }

    private void OnPassiveAdded(EntityUid uid, PassiveThermalSignatureComponent _, ref ComponentStartup args)
    {
        if (_sigQuery.HasComp(uid))
        {
            // Passive sources have a constant emission and need to warm their stored heat once.
            var sig = _sigQuery.Comp(uid);
            sig.EmissionDirty = true;
            sig.IdleTicks = 0;
            _activeSources.Add(uid);
        }
    }

    private void OnMachineAdded(EntityUid uid, MachineThermalSignatureComponent _, ref ComponentStartup args)
    {
        if (!_sigQuery.HasComp(uid))
            return;
        // Lazy: only activate if currently powered. Otherwise wait for PowerChangedEvent.
        if (_power.IsPowered(uid))
        {
            var sig = _sigQuery.Comp(uid);
            sig.EmissionDirty = true;
            sig.IdleTicks = 0;
            _activeSources.Add(uid);
        }
    }
    // Forge-Change-End

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdateTime)
            return;

        _nextUpdateTime = _timing.CurTime + UpdateInterval;

        // Forge-Change: reuse pooled buffers instead of allocating per tick.
        _gridQueue.Clear();
        _gridTotals.Clear();
        _activeSnapshot.Clear();
        _activeSnapshot.AddRange(_activeSources);

        foreach (var uid in _activeSnapshot)
        {
            if (!_sigQuery.TryGetComponent(uid, out var sigComp))
            {
                _activeSources.Remove(uid);
                continue;
            }

            // Forge-Change-Start: cached emission for stable sources avoids RaiseLocalEvent every tick.
            float emission;
            if (sigComp.EmissionDirty || HasDynamicEmitter(uid))
            {
                var ev = new GetThermalSignatureEvent();
                RaiseLocalEvent(uid, ref ev);
                emission = ev.Signature;
                if (!HasDynamicEmitter(uid))
                {
                    sigComp.CachedEmission = emission;
                    sigComp.EmissionDirty = false;
                }
            }
            else
            {
                emission = sigComp.CachedEmission;
            }
            // Forge-Change-End

            sigComp.StoredHeat += emission * UpdateIntervalSeconds;
            sigComp.StoredHeat *= MathF.Pow(sigComp.HeatDissipation, UpdateIntervalSeconds);

            if (_mapGridQuery.HasComp(uid))
            {
                _gridQueue.Add((uid, sigComp));
                continue;
            }
            else
            {
                var xform = _xformQuery.GetComponent(uid); // Forge-Change
                sigComp.TotalHeat = sigComp.StoredHeat;
                if (xform.GridUid != null && _sigQuery.HasComp(xform.GridUid.Value))
                    _gridTotals[xform.GridUid.Value] = _gridTotals.GetValueOrDefault(xform.GridUid.Value) + sigComp.StoredHeat;
            }

            // Forge-Change-Start: idle pruning for sources without dynamic emitters. Dynamic emitters
            // (Thruster/PowerSupplier/FTL) aren't pruned — they have no re-add event when state flips.
            // Passive sources also stay tracked because their constant emission would normally get them
            // re-added by events, but they don't fire those events.
            if (!_passiveQuery.HasComp(uid) && !ShouldTrackAsActiveDynamic(uid)
                && MathF.Abs(emission) <= ActiveHeatThreshold && sigComp.StoredHeat <= ActiveHeatThreshold)
            {
                if (sigComp.IdleTicks < byte.MaxValue)
                    sigComp.IdleTicks++;
                if (sigComp.IdleTicks >= IdleTicksToPrune)
                    _activeSources.Remove(uid);
            }
            else
            {
                sigComp.IdleTicks = 0;
            }
            // Forge-Change-End
        }

        foreach (var ent in _gridQueue)
        {
            ent.Comp.TotalHeat = ent.Comp.StoredHeat + _gridTotals.GetValueOrDefault(ent.Owner);
            _gridTotals.Remove(ent.Owner);
            _trackedGrids.Add(ent.Owner);

            // Forge-Change: adaptive Dirty threshold + per-grid min interval.
            TryDirtyGrid(ent.Owner, ent.Comp);

            if (!ShouldTrackGridAsActive(ent.Owner, ent.Comp))
                _activeSources.Remove(ent.Owner);
        }

        foreach (var (uid, totalHeat) in _gridTotals)
        {
            if (!_sigQuery.TryGetComponent(uid, out var gridSig))
                continue;

            gridSig.TotalHeat = totalHeat;
            _trackedGrids.Add(uid);
            TryDirtyGrid(uid, gridSig);
        }

        _trackedSnapshot.Clear();
        _trackedSnapshot.AddRange(_trackedGrids);

        foreach (var uid in _trackedSnapshot)
        {
            if (_gridTotals.ContainsKey(uid))
                continue;

            if (!_sigQuery.TryGetComponent(uid, out var gridSig))
            {
                _trackedGrids.Remove(uid);
                _activeSources.Remove(uid);
                continue;
            }

            if (_mapGridQuery.HasComp(uid) && gridSig.TotalHeat != 0f)
            {
                gridSig.TotalHeat = 0f;
                TryDirtyGrid(uid, gridSig);
            }

            if (!ShouldTrackGridAsActive(uid, gridSig))
            {
                _trackedGrids.Remove(uid);
                _activeSources.Remove(uid);
            }
        }
    }

    private void TryDirtyGrid(EntityUid uid, ThermalSignatureComponent sigComp)
    {
        // Forge-Change-Start: combine adaptive threshold with a per-grid min interval to suppress flapping.
        var diff = MathF.Abs(sigComp.TotalHeat - sigComp.LastUpdateHeat);

        bool changed;
        if (sigComp.TotalHeat < HeatChangeAbsoluteCutoff && sigComp.LastUpdateHeat < HeatChangeAbsoluteCutoff)
        {
            changed = diff > HeatChangeThresholdAbsolute;
        }
        else
        {
            // Relative band: |new - old| / max(old, 1) >= (rel - 1)
            var baseHeat = MathF.Max(sigComp.LastUpdateHeat, 1f);
            changed = diff / baseHeat >= HeatChangeThresholdRelative - 1f;
        }

        if (!changed)
            return;

        if (_timing.CurTime - sigComp.LastDirtyTime < MinDirtyInterval)
            return;

        sigComp.LastUpdateHeat = sigComp.TotalHeat;
        sigComp.LastDirtyTime = _timing.CurTime;
        Dirty(uid, sigComp);
        // Forge-Change-End
    }

    // Forge-Change-Start: split the original ShouldTrackAsActive into "is this a dynamic emitter" (used to
    // decide whether to bypass the emission cache) and grid-level activeness (kept for grid bookkeeping).
    private bool HasDynamicEmitter(EntityUid uid)
    {
        return _powerSupplierQuery.HasComp(uid)
               || _thrusterQuery.HasComp(uid)
               || _ftlDriveQuery.HasComp(uid)
               || _gunQuery.HasComp(uid);
    }

    private bool ShouldTrackAsActiveDynamic(EntityUid uid)
    {
        return _powerSupplierQuery.HasComp(uid)
               || _thrusterQuery.HasComp(uid)
               || _ftlDriveQuery.HasComp(uid);
    }

    private bool ShouldTrackGridAsActive(EntityUid uid, [NotNullWhen(true)] ThermalSignatureComponent? sigComp = null)
    {
        if (sigComp != null && sigComp.StoredHeat > ActiveHeatThreshold)
            return true;

        return _passiveQuery.HasComp(uid)
               || _machineQuery.HasComp(uid)
               || _powerSupplierQuery.HasComp(uid)
               || _thrusterQuery.HasComp(uid)
               || _ftlDriveQuery.HasComp(uid);
    }
    // Forge-Change-End
}
