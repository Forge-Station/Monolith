using System.Numerics;
using Content.Server._Forge.Drones.Components;
using Content.Server._Mono.Cleanup;
using Content.Server._Mono.NPC.HTN;
using Content.Server.Damage.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.DroneControl;
using Content.Shared._Forge.CCVar;
using Content.Shared.Damage.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Drones;

/// <summary>
/// Forces procedural core AI drones to flee the inner 0-18 km worldgen zone and despawn shortly after.
/// </summary>
public sealed class DroneInnerZoneFleeSystem : EntitySystem
{
    [Dependency] private readonly CleanupHelperSystem _cleanupHelper = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly GodmodeSystem _godmode = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ShipSteeringSystem _steering = default!;
    [Dependency] private readonly ShipTargetingSystem _targeting = default!;

    private static readonly SoundSpecifier FleeSound = new SoundPathSpecifier("/Audio/Effects/Shuttle/hyperspace_begin.ogg")
    {
        Params = AudioParams.Default.WithVolume(-4f),
    };

    private float _innerZoneRadius = 18_000f;
    private float _deleteMinSeconds = 10f;
    private float _deleteMaxSeconds = 15f;
    private float _zoneCheckInterval = 2f;

    private float _zoneCheckTimer;

    private readonly HashSet<EntityUid> _pendingDeletes = new();
    private readonly HashSet<EntityUid> _occupiedGrids = new();

    private EntityQuery<DroneControlComponent> _droneControlQuery;
    private EntityQuery<GridCleanupGridComponent> _cleanupGridQuery;
    private EntityQuery<DroneInnerZoneFleeComponent> _fleeQuery;
    private EntityQuery<HTNComponent> _htnQuery;
    private EntityQuery<ShuttleConsoleComponent> _shuttleConsoleQuery;

    public override void Initialize()
    {
        base.Initialize();

        _droneControlQuery = GetEntityQuery<DroneControlComponent>();
        _cleanupGridQuery = GetEntityQuery<GridCleanupGridComponent>();
        _fleeQuery = GetEntityQuery<DroneInnerZoneFleeComponent>();
        _htnQuery = GetEntityQuery<HTNComponent>();
        _shuttleConsoleQuery = GetEntityQuery<ShuttleConsoleComponent>();

        Subs.CVar(_cfg, ForgeCVars.DroneInnerZoneRadius, val => _innerZoneRadius = val, true);
        Subs.CVar(_cfg, ForgeCVars.DroneInnerZoneFleeDeleteMin, val => _deleteMinSeconds = val, true);
        Subs.CVar(_cfg, ForgeCVars.DroneInnerZoneFleeDeleteMax, val => _deleteMaxSeconds = val, true);
        Subs.CVar(_cfg, ForgeCVars.DroneInnerZoneCheckInterval, val => _zoneCheckInterval = MathF.Max(0.1f, val), true);
    }

    public override void Update(float frameTime)
    {
        RefreshOccupiedGrids();
        ProcessPendingDeletes();
        UpdateFleeingDrones();

        _zoneCheckTimer += frameTime;
        if (_zoneCheckTimer >= _zoneCheckInterval)
        {
            _zoneCheckTimer = 0f;
            TryStartFleeForInnerZoneDrones();
        }
    }

    private void RefreshOccupiedGrids()
    {
        _occupiedGrids.Clear();
        _cleanupHelper.CollectOccupiedGrids(_occupiedGrids);
    }

    private bool HasPlayerOnGrid(EntityUid gridUid) => _occupiedGrids.Contains(gridUid);

    /// <summary>
    /// Used by <see cref="ShuttleSystem"/> to skip impact damage for fleeing drone grids.
    /// </summary>
    public bool IsFleeingGrid(EntityUid uid) => HasComp<DroneInnerZoneFleeGridComponent>(uid);

    private void ProcessPendingDeletes()
    {
        if (_pendingDeletes.Count == 0)
            return;

        _pendingDeletes.RemoveWhere(gridUid =>
        {
            if (TerminatingOrDeleted(gridUid))
                return true;

            if (HasPlayerOnGrid(gridUid))
                return false;

            QueueDel(gridUid);
            return true;
        });
    }

    private void TryStartFleeForInnerZoneDrones()
    {
        var query = EntityQueryEnumerator<HTNComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (_fleeQuery.HasComp(uid))
                continue;

            if (xform.GridUid is not { } gridUid || !IsProceduralDroneGrid(gridUid))
                continue;

            if (!IsFleeEligibleAiCore(uid, gridUid))
                continue;

            if (HasPlayerOnGrid(gridUid))
                continue;

            var mapPos = _transform.GetMapCoordinates(uid, xform);
            if (mapPos.Position.Length() >= _innerZoneRadius)
                continue;

            if (!_htnQuery.TryGetComponent(uid, out var htn))
                continue;

            StartFlee((uid, htn), gridUid);
        }
    }

    private void UpdateFleeingDrones()
    {
        var query = EntityQueryEnumerator<DroneInnerZoneFleeComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var flee, out var xform))
        {
            if (xform.GridUid is not { } gridUid || TerminatingOrDeleted(gridUid))
            {
                RemComp<DroneInnerZoneFleeComponent>(uid);
                continue;
            }

            if (!IsFleeEligibleAiCore(uid, gridUid))
            {
                AbortFlee(gridUid, uid);
                continue;
            }

            if (HasPlayerOnGrid(gridUid))
            {
                AbortFlee(gridUid, uid);
                continue;
            }

            if (_pendingDeletes.Contains(gridUid))
                continue;

            if (_timing.CurTime >= flee.DeleteAt)
            {
                ScheduleGridDelete(gridUid, uid);
                continue;
            }

            UpdateFleeSteering(uid, flee, xform);
        }
    }

    private void StartFlee(Entity<HTNComponent> ent, EntityUid gridUid)
    {
        if (HasPlayerOnGrid(gridUid))
            return;

        _htn.ShutdownPlan(ent.Comp);
        _htn.SetHTNEnabled(ent, false);
        _targeting.Stop((ent.Owner, null));

        var deleteDelay = TimeSpan.FromSeconds(_random.NextFloat(_deleteMinSeconds, _deleteMaxSeconds));
        var flee = EnsureComp<DroneInnerZoneFleeComponent>(ent);
        flee.DeleteAt = _timing.CurTime + deleteDelay;
        flee.SteeringConfigured = false;

        EnsureComp<CleanupImmuneComponent>(gridUid);
        EnsureComp<DroneInnerZoneFleeGridComponent>(gridUid);
        ProtectGrid(gridUid);

        UpdateFleeSteering(ent, flee, Transform(ent));

        Log.Debug($"Drone {ToPrettyString(ent)} started inner-zone flee, deleting grid {ToPrettyString(gridUid)} at {flee.DeleteAt}");
    }

    private void UpdateFleeSteering(EntityUid aiUid, DroneInnerZoneFleeComponent flee, TransformComponent aiXform)
    {
        if (aiXform.GridUid is not { } gridUid)
            return;

        var gridMapPos = _transform.GetMapCoordinates(gridUid);
        var fleeCoords = GetFleeCoordinates(gridMapPos, flee.FleeTargetDistance);

        if (flee.SteeringConfigured && TryComp<ShipSteererComponent>(aiUid, out var existing))
        {
            existing.Coordinates = fleeCoords;
            existing.Status = ShipSteeringStatus.Moving;
            return;
        }

        var steerer = _steering.Steer(aiUid, fleeCoords);
        if (steerer == null)
            return;

        steerer.Mode = ShipSteeringMode.GoToRange;
        steerer.Range = 1000f;
        steerer.RangeTolerance = 500f;
        steerer.InRangeMaxSpeed = null;
        steerer.NoFinish = true;
        steerer.LeadingEnabled = false;
        steerer.AvoidCollisions = true;
        steerer.FinishOnCollide = false;
        steerer.Status = ShipSteeringStatus.Moving;
        flee.SteeringConfigured = true;
    }

    private void ScheduleGridDelete(EntityUid gridUid, EntityUid aiUid)
    {
        if (_pendingDeletes.Contains(gridUid) || TerminatingOrDeleted(gridUid))
            return;

        if (!IsFleeEligibleAiCore(aiUid, gridUid) || HasPlayerOnGrid(gridUid))
            return;

        _steering.Stop((aiUid, null));

        if (TryComp<PilotedShuttleComponent>(gridUid, out var piloted))
            piloted.InputSources.Remove(aiUid);

        if (TryComp<PhysicsComponent>(gridUid, out var body))
            _physics.SetAwake(gridUid, body, false);

        var coords = Transform(gridUid).Coordinates;
        _audio.PlayPvs(FleeSound, coords);

        _pendingDeletes.Add(gridUid);
    }

    private EntityCoordinates GetFleeCoordinates(MapCoordinates mapPos, float fleeTargetDistance)
    {
        var pos = mapPos.Position;
        var dir = pos.LengthSquared() > 1f ? Vector2.Normalize(pos) : -Vector2.UnitY;
        var mapUid = _map.GetMapOrInvalid(mapPos.MapId);

        return new EntityCoordinates(mapUid, pos + dir * fleeTargetDistance);
    }

    private void ProtectGrid(EntityUid gridUid)
    {
        _godmode.EnableGodmode(gridUid);

        foreach (var entity in _lookup.GetEntitiesIntersecting(gridUid))
        {
            _godmode.EnableGodmode(entity);
        }
    }

    private void UnprotectGrid(EntityUid gridUid)
    {
        if (HasComp<GodmodeComponent>(gridUid))
            _godmode.DisableGodmode(gridUid);

        foreach (var entity in _lookup.GetEntitiesIntersecting(gridUid))
        {
            if (HasComp<GodmodeComponent>(entity))
                _godmode.DisableGodmode(entity);
        }
    }

    private void AbortFlee(EntityUid gridUid, EntityUid aiUid)
    {
        _pendingDeletes.Remove(gridUid);
        _steering.Stop((aiUid, null));

        if (TryComp<PilotedShuttleComponent>(gridUid, out var piloted))
            piloted.InputSources.Remove(aiUid);

        RemComp<DroneInnerZoneFleeComponent>(aiUid);
        RemComp<DroneInnerZoneFleeGridComponent>(gridUid);
        RemComp<CleanupImmuneComponent>(gridUid);
        UnprotectGrid(gridUid);

        if (TryComp<HTNComponent>(aiUid, out var htn))
            _htn.SetHTNEnabled((aiUid, htn), true);
    }

    /// <summary>
    /// HTN cores on procedural drone grids. Many worldgen hulls use NpcStationAi* spawners (rammers, attackers),
    /// so eligibility is tied to the grid marker rather than the NpcDroneAi* prefix alone.
    /// Fixed POI station AI is excluded because those grids lack the SpawnDroneBase cleanup marker.
    /// </summary>
    private bool IsFleeEligibleAiCore(EntityUid uid, EntityUid gridUid)
    {
        if (_shuttleConsoleQuery.HasComp(uid) || _droneControlQuery.HasComp(uid))
            return false;

        if (!_htnQuery.HasComp(uid))
            return false;

        if (IsProceduralDroneGrid(gridUid))
            return true;

        var protoId = MetaData(uid).EntityPrototype?.ID;
        return protoId != null && protoId.StartsWith("NpcDroneAi", StringComparison.Ordinal);
    }

    /// <summary>
    /// SpawnDroneBase sets ignorePowered/ignoreIFF; GridCleanupSystem.EnsureComp uses defaults on player grids.
    /// </summary>
    private bool IsProceduralDroneGrid(EntityUid gridUid)
    {
        if (!_cleanupGridQuery.TryGetComponent(gridUid, out var cleanup))
            return false;

        return cleanup.IgnoreIFF && cleanup.IgnorePowered;
    }
}
