using System.Numerics;
using Content.Server._Forge.Drones.Components;
using Content.Server._Mono.Cleanup;
using Content.Server._Mono.NPC.HTN;
using Content.Server.NPC.HTN;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.DroneControl;
using Content.Shared._Forge.CCVars;
using Content.Shared.Damage;
using Content.Shared.Mind.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using static Content.Shared.Damage.DamageableSystem;

namespace Content.Server._Forge.Drones;

/// <summary>
/// Forces procedural core AI drones to flee the inner 0-18 km worldgen zone and despawn shortly after.
/// </summary>
public sealed partial class DroneInnerZoneFleeSystem : EntitySystem
{
    [Dependency] private CleanupHelperSystem _cleanupHelper = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShipSteeringSystem _steering = default!;
    [Dependency] private ShipTargetingSystem _targeting = default!;

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
    private EntityQuery<DroneInnerZoneFleeGridComponent> _fleeGridQuery;
    private EntityQuery<HTNComponent> _htnQuery;
    private EntityQuery<ShuttleConsoleComponent> _shuttleConsoleQuery;
    private EntityQuery<MindContainerComponent> _mindQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        base.Initialize();

        _droneControlQuery = GetEntityQuery<DroneControlComponent>();
        _cleanupGridQuery = GetEntityQuery<GridCleanupGridComponent>();
        _fleeGridQuery = GetEntityQuery<DroneInnerZoneFleeGridComponent>();
        _htnQuery = GetEntityQuery<HTNComponent>();
        _shuttleConsoleQuery = GetEntityQuery<ShuttleConsoleComponent>();
        _mindQuery = GetEntityQuery<MindContainerComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        Subs.CVar(_cfg, ForgeCCVars.DroneInnerZoneRadius, val => _innerZoneRadius = val, true);
        Subs.CVar(_cfg, ForgeCCVars.DroneInnerZoneFleeDeleteMin, val =>
        {
            _deleteMinSeconds = val;
            ClampDeleteTimes();
        }, true);
        Subs.CVar(_cfg, ForgeCCVars.DroneInnerZoneFleeDeleteMax, val =>
        {
            _deleteMaxSeconds = val;
            ClampDeleteTimes();
        }, true);
        Subs.CVar(_cfg, ForgeCCVars.DroneInnerZoneCheckInterval, val => _zoneCheckInterval = MathF.Max(0.1f, val), true);
        ClampDeleteTimes();

        SubscribeLocalEvent<DamageableComponent, BeforeDamageChangedEvent>(OnFleeGridDamage);
    }

    public override void Update(float frameTime)
    {
        _zoneCheckTimer += frameTime;
        var scanZone = _zoneCheckTimer >= _zoneCheckInterval;
        if (scanZone)
            _zoneCheckTimer = 0f;

        if (scanZone || _pendingDeletes.Count > 0 || AnyFleeing())
            RefreshOccupiedGrids();

        ProcessPendingDeletes();
        UpdateFleeingDrones();

        if (scanZone)
            TryStartFleeForInnerZoneDrones();
    }

    private void ClampDeleteTimes()
    {
        if (_deleteMinSeconds > _deleteMaxSeconds)
            (_deleteMinSeconds, _deleteMaxSeconds) = (_deleteMaxSeconds, _deleteMinSeconds);

        _deleteMinSeconds = MathF.Max(0.1f, _deleteMinSeconds);
        _deleteMaxSeconds = MathF.Max(_deleteMinSeconds, _deleteMaxSeconds);
    }

    private bool AnyFleeing()
    {
        var query = EntityQueryEnumerator<DroneInnerZoneFleeGridComponent>();
        return query.MoveNext(out _, out _);
    }

    private void RefreshOccupiedGrids()
    {
        _occupiedGrids.Clear();
        _cleanupHelper.CollectOccupiedGrids(_occupiedGrids);
    }

    private bool HasPlayerOnGrid(EntityUid gridUid) => _occupiedGrids.Contains(gridUid);

    /// <summary>
    /// Used by shuttle impact handling to skip damage for fleeing drone grids.
    /// </summary>
    public bool IsFleeingGrid(EntityUid uid) => _fleeGridQuery.HasComp(uid);

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
        var query = EntityQueryEnumerator<GridCleanupGridComponent, TransformComponent>();

        while (query.MoveNext(out var gridUid, out var cleanup, out var gridXform))
        {
            if (!cleanup.IgnoreIFF || !cleanup.IgnorePowered)
                continue;

            if (_fleeGridQuery.HasComp(gridUid) || TerminatingOrDeleted(gridUid))
                continue;

            if (HasPlayerOnGrid(gridUid))
                continue;

            var mapPos = _transform.GetMapCoordinates(gridUid, gridXform);
            if (mapPos.Position.Length() >= _innerZoneRadius)
                continue;

            if (!TryFindFleeAiCore(gridUid, out var aiUid, out var htn))
                continue;

            StartFlee((aiUid, htn), gridUid);
        }
    }

    private bool TryFindFleeAiCore(EntityUid gridUid, out EntityUid aiUid, out HTNComponent htn)
    {
        aiUid = default;
        htn = default!;

        if (!_xformQuery.TryGetComponent(gridUid, out var gridXform))
            return false;

        var enumerator = gridXform.ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            if (!IsFleeEligibleAiCore(child, gridUid))
                continue;

            if (!_htnQuery.TryGetComponent(child, out var found))
                continue;

            aiUid = child;
            htn = found;
            return true;
        }

        return false;
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

            // Abort even after the BSS timer: a boarded player must restore the AI and cancel deletion.
            if (!IsFleeEligibleAiCore(uid, gridUid) || HasPlayerOnGrid(gridUid))
            {
                AbortFlee(gridUid, uid);
                continue;
            }

            if (_timing.CurTime >= flee.DeleteAt)
            {
                ScheduleGridDelete(gridUid, uid);
                continue;
            }

            if (_pendingDeletes.Contains(gridUid))
                continue;

            UpdateFleeSteering(uid, flee, xform);
        }
    }

    private void StartFlee(Entity<HTNComponent> ent, EntityUid gridUid)
    {
        if (HasPlayerOnGrid(gridUid) || _fleeGridQuery.HasComp(gridUid))
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

        UpdateFleeSteering(ent, flee, Transform(ent));

        Log.Info($"Drone {ToPrettyString(ent)} started inner-zone flee, deleting grid {ToPrettyString(gridUid)} at {flee.DeleteAt}");
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
            _physics.SetAwake((gridUid, body), false);

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

    /// <summary>
    /// Cancels damage on entities whose <see cref="TransformComponent.GridUid"/> is a fleeing drone grid.
    /// Uses grid membership, not AABB overlap, and never applies godmode/Rejuvenate.
    /// </summary>
    private void OnFleeGridDamage(Entity<DamageableComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled)
            return;

        if (!_xformQuery.TryGetComponent(ent, out var xform))
            return;

        var gridUid = xform.GridUid ?? ent.Owner;
        if (!_fleeGridQuery.HasComp(gridUid))
            return;

        if (_mindQuery.TryGetComponent(ent, out var mind) && mind.HasMind)
            return;

        args.Cancelled = true;
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

        if (TryComp<HTNComponent>(aiUid, out var htn))
            _htn.SetHTNEnabled((aiUid, htn), true);
    }

    /// <summary>
    /// Worldgen drone hulls use NpcDroneAi* and NpcStationAi* cores. Eligibility also requires
    /// the SpawnDroneBase cleanup marker so fixed POI station AI is ignored.
    /// </summary>
    private bool IsFleeEligibleAiCore(EntityUid uid, EntityUid gridUid)
    {
        if (_shuttleConsoleQuery.HasComp(uid) || _droneControlQuery.HasComp(uid))
            return false;

        if (!_htnQuery.HasComp(uid))
            return false;

        if (!IsProceduralDroneGrid(gridUid))
            return false;

        var protoId = MetaData(uid).EntityPrototype?.ID;
        if (string.IsNullOrEmpty(protoId))
            return false;

        return protoId.StartsWith("NpcDroneAi", StringComparison.Ordinal)
               || protoId.StartsWith("NpcStationAi", StringComparison.Ordinal);
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
