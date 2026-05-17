using Content.Server.NPC.HTN;
using Content.Shared._Mono.CCVar;
using Content.Shared.Mind.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Cleanup;

/// <summary>
///     Maintains <see cref="SpaceCleanupTargetComponent"/> on entities so that
///     <see cref="SpaceCleanupSystem"/> can iterate only the small set of entities
///     drifting in open space rather than every physics body in the world.
/// </summary>
/// <remarks>
///     Forge-Change. Event-driven tagging via <see cref="EntParentChangedMessage"/> with
///     a periodic safety sweep that retroactively tags anything the events missed.
/// </remarks>
public sealed class SpaceCleanupTargetSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private EntityQuery<CleanupImmuneComponent> _immuneQuery;
    private EntityQuery<HTNComponent> _htnQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<MindContainerComponent> _mindQuery;
    private EntityQuery<SpaceCleanupTargetComponent> _targetQuery;

    private bool _debug;
    private TimeSpan _safetyInterval = TimeSpan.FromMinutes(15);
    private TimeSpan _nextSafetyScan;

    public override void Initialize()
    {
        base.Initialize();

        _immuneQuery = GetEntityQuery<CleanupImmuneComponent>();
        _htnQuery = GetEntityQuery<HTNComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _mindQuery = GetEntityQuery<MindContainerComponent>();
        _targetQuery = GetEntityQuery<SpaceCleanupTargetComponent>();

        Subs.CVar(_cfg, MonoCVars.CleanupDebug, val => _debug = val, true);
        Subs.CVar(_cfg, MonoCVars.CleanupTargetSafetyScanSeconds, val =>
        {
            _safetyInterval = TimeSpan.FromSeconds(Math.Max(val, 30f));
        }, true);

        SubscribeLocalEvent<PhysicsComponent, EntParentChangedMessage>(OnParentChanged);
    }

    private void OnParentChanged(EntityUid uid, PhysicsComponent _, ref EntParentChangedMessage args)
    {
        UpdateTarget(uid, args.Transform);
    }

    /// <summary>
    ///     Returns true when an entity should currently carry the cleanup marker.
    ///     Centralized so the event handler and safety sweep stay consistent.
    /// </summary>
    private bool IsCandidate(EntityUid uid, TransformComponent xform)
    {
        if (xform.MapUid == null || xform.ParentUid != xform.MapUid)
            return false;

        return !_gridQuery.HasComp(uid)
            && !_htnQuery.HasComp(uid)
            && !_immuneQuery.HasComp(uid)
            && !_mindQuery.HasComp(uid);
    }

    private void UpdateTarget(EntityUid uid, TransformComponent xform)
    {
        var has = _targetQuery.HasComp(uid);
        var should = IsCandidate(uid, xform);

        if (should && !has)
            AddComp<SpaceCleanupTargetComponent>(uid);
        else if (!should && has)
            RemComp<SpaceCleanupTargetComponent>(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSafetyScan)
            return;
        var debugSafetyInterval = TimeSpan.FromSeconds(30);
        _nextSafetyScan = _timing.CurTime + (_debug ? debugSafetyInterval : _safetyInterval);

        // Belt-and-suspenders sweep: retroactively reconcile markers for any entity whose
        // parent-change event was missed (unusual spawn flows, hot reloads, etc.).
        var scanStart = _timing.RealTime;
        var seen = 0;
        var added = 0;
        var removed = 0;
        var query = EntityQueryEnumerator<PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            seen++;
            var has = _targetQuery.HasComp(uid);
            var should = IsCandidate(uid, xform);
            if (should && !has)
            {
                AddComp<SpaceCleanupTargetComponent>(uid);
                added++;
            }
            else if (!should && has)
            {
                RemComp<SpaceCleanupTargetComponent>(uid);
                removed++;
            }
        }
        var scanMs = (_timing.RealTime - scanStart).TotalMilliseconds;
        Log.Debug($"SpaceCleanupTarget safety scan: {seen} physics ents, +{added} -{removed} markers, {scanMs:F1}ms");
    }
}
