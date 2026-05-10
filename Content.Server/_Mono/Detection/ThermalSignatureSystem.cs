using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Detection;
using Content.Shared._Mono.Ships;
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
public sealed class ThermalSignatureSystem : EntitySystem
{
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const float UpdateIntervalSeconds = 1f;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(UpdateIntervalSeconds);
    private TimeSpan _nextUpdateTime;

    private const float HeatChangeThreshold = 1.02f;
    private const float ActiveHeatThreshold = 0.01f;

    private List<Entity<ThermalSignatureComponent>> _gridQueue = new();
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

        _sigQuery = GetEntityQuery<ThermalSignatureComponent>();
        _gunQuery = GetEntityQuery<GunComponent>();
        _mapGridQuery = GetEntityQuery<MapGridComponent>();
        _passiveQuery = GetEntityQuery<PassiveThermalSignatureComponent>();
        _machineQuery = GetEntityQuery<MachineThermalSignatureComponent>();
        _powerSupplierQuery = GetEntityQuery<PowerSupplierComponent>();
        _thrusterQuery = GetEntityQuery<ThrusterComponent>();
        _ftlDriveQuery = GetEntityQuery<FTLDriveComponent>();
    }

    private void OnGridInitialized(GridInitializeEvent args)
    {
        EnsureComp<ThermalSignatureComponent>(args.EntityUid);
    }

    private void OnThermalStartup(Entity<ThermalSignatureComponent> ent, ref ComponentStartup args)
    {
        if (ShouldTrackAsActive(ent.Owner, ent.Comp))
            _activeSources.Add(ent.Owner);
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
        var xform = Transform(ent);
        if (!TryComp<FTLComponent>(xform.GridUid, out var ftl))
            return;

        if (ftl.State == FTLState.Starting || ftl.State == FTLState.Cooldown)
            args.Signature += ent.Comp.ThermalSignature;
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdateTime)
            return;

        _nextUpdateTime = _timing.CurTime + UpdateInterval;

        _gridQueue.Clear();
        var gridTotals = new Dictionary<EntityUid, float>();
        var activeSnapshot = new List<EntityUid>(_activeSources.Count);
        activeSnapshot.AddRange(_activeSources);

        foreach (var uid in activeSnapshot)
        {
            if (!_sigQuery.TryGetComponent(uid, out var sigComp))
            {
                _activeSources.Remove(uid);
                continue;
            }

            var ev = new GetThermalSignatureEvent();
            RaiseLocalEvent(uid, ref ev);

            sigComp.StoredHeat += ev.Signature * UpdateIntervalSeconds;
            sigComp.StoredHeat *= MathF.Pow(sigComp.HeatDissipation, UpdateIntervalSeconds);

            if (_mapGridQuery.HasComp(uid))
            {
                _gridQueue.Add((uid, sigComp));
                continue;
            }
            else
            {
                var xform = Transform(uid);
                sigComp.TotalHeat = sigComp.StoredHeat;
                if (xform.GridUid != null && _sigQuery.TryGetComponent(xform.GridUid.Value, out var gridSig))
                    gridTotals[xform.GridUid.Value] = gridTotals.GetValueOrDefault(xform.GridUid.Value) + sigComp.StoredHeat;
            }

            if (!ShouldTrackAsActive(uid, sigComp) && MathF.Abs(ev.Signature) <= ActiveHeatThreshold)
                _activeSources.Remove(uid);
        }

        foreach (var ent in _gridQueue)
        {
            ent.Comp.TotalHeat = ent.Comp.StoredHeat + gridTotals.GetValueOrDefault(ent.Owner);
            gridTotals.Remove(ent.Owner);
            _trackedGrids.Add(ent.Owner);

            // don't sync it if it didn't change heat much since last time, we don't need to sync 500 cold asteroids every system update
            if (ent.Comp.TotalHeat <= ent.Comp.LastUpdateHeat * HeatChangeThreshold
                && ent.Comp.TotalHeat >= ent.Comp.LastUpdateHeat / HeatChangeThreshold)
            {
                if (!ShouldTrackAsActive(ent.Owner, ent.Comp))
                    _activeSources.Remove(ent.Owner);
                continue;
            }

            ent.Comp.LastUpdateHeat = ent.Comp.TotalHeat;
            Dirty(ent);

            if (!ShouldTrackAsActive(ent.Owner, ent.Comp))
                _activeSources.Remove(ent.Owner);
        }

        foreach (var (uid, totalHeat) in gridTotals)
        {
            if (!_sigQuery.TryGetComponent(uid, out var gridSig))
                continue;

            gridSig.TotalHeat = totalHeat;
            _trackedGrids.Add(uid);
            TryDirtyGrid(uid, gridSig);
        }

        var trackedSnapshot = new List<EntityUid>(_trackedGrids.Count);
        trackedSnapshot.AddRange(_trackedGrids);

        foreach (var uid in trackedSnapshot)
        {
            if (gridTotals.ContainsKey(uid))
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

            if (!ShouldTrackAsActive(uid, gridSig))
            {
                _trackedGrids.Remove(uid);
                _activeSources.Remove(uid);
            }
        }
    }

    private void TryDirtyGrid(EntityUid uid, ThermalSignatureComponent sigComp)
    {
        if (sigComp.TotalHeat <= sigComp.LastUpdateHeat * HeatChangeThreshold
            && sigComp.TotalHeat >= sigComp.LastUpdateHeat / HeatChangeThreshold)
            return;

        sigComp.LastUpdateHeat = sigComp.TotalHeat;
        Dirty(uid, sigComp);
    }

    private bool ShouldTrackAsActive(EntityUid uid, [NotNullWhen(true)] ThermalSignatureComponent? sigComp = null)
    {
        if (sigComp != null && sigComp.StoredHeat > ActiveHeatThreshold)
            return true;

        return _passiveQuery.HasComp(uid)
               || _machineQuery.HasComp(uid)
               || _powerSupplierQuery.HasComp(uid)
               || _thrusterQuery.HasComp(uid)
               || _ftlDriveQuery.HasComp(uid);
    }
}
