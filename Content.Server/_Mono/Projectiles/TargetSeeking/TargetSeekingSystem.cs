using System.Numerics;
using Content.Server._Mono.FireControl; // Forge-change
using Content.Server.NPC.HTN; // Forge-change
using Content.Server.Shuttles.Components; // Forge-change
using Content.Shared._Crescent.DroneControl; // Forge-change
using Content.Shared.Interaction;
using Content.Shared.Projectiles;
using Content.Shared._Mono.FireControl; // Forge-change
using Robust.Server.GameObjects;
using Robust.Shared.Map; // Forge-change
using Robust.Shared.Map.Components; // Forge-change
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Mono.Projectiles.TargetSeeking;

/// <summary>
///     Handles the logic for target-seeking projectiles.
/// </summary>
public sealed partial class TargetSeekingSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = null!;
    [Dependency] private RotateToFaceSystem _rotateToFace = null!;
    [Dependency] private PhysicsSystem _physics = null!;
    [Dependency] private IGameTiming _gameTiming = default!; // Mono

    private EntityQuery<ProjectileComponent> _projectileQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;

    // Reusable per-tick scratch buffer so all seekers in the same Update share a single
    // pre-built candidate list instead of each one re-iterating + re-resolving Transforms.
    private readonly List<TargetCandidate> _targetCandidates = new();

    private readonly record struct TargetCandidate(EntityUid ActualTarget, Vector2 Position, EntityUid? GridUid);

    public override void Initialize()
    {
        base.Initialize();

        _projectileQuery = GetEntityQuery<ProjectileComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        SubscribeLocalEvent<TargetSeekingComponent, ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<TargetSeekingComponent, EntParentChangedMessage>(OnParentChanged);

        SubscribeLocalEvent<TargetSeekingComponent, ComponentShutdown>(OnTargetSeekingShutdown);
    }

    private void OnTargetSeekingShutdown(Entity<TargetSeekingComponent> seekerEntity, ref ComponentShutdown args)
    {
        if (seekerEntity.Comp.CurrentTarget is { } oldTargetEntity)
            OnChangingSeekingTarget(seekerEntity, oldTargetEntity);
    }

    /// <summary>
    /// Called on a seeker when its <see cref="TargetSeekingComponent.CurrentTarget"/> is changed, directed at the new target.
    /// </summary>
    private void OnStartingSeeking(Entity<TargetSeekingComponent, TransformComponent?> seekerTransformEntity, EntityUid newTargetUid)
    {
        if (!Resolve(seekerTransformEntity, ref seekerTransformEntity.Comp2))
            return;

        var startedSeekingEvent = new EntityStartedBeingSeekedTargetEvent(seekerTransformEntity!, seekerTransformEntity.Comp1.ExposesTracking);
        RaiseLocalEvent(newTargetUid, ref startedSeekingEvent);
    }

    /// <summary>
    /// Called on a seeker when it either loses or changes its <see cref="TargetSeekingComponent.CurrentTarget"/>, directed at the old target.
    /// </summary>
    private void OnChangingSeekingTarget(Entity<TargetSeekingComponent, TransformComponent?> seekerTransformEntity, EntityUid oldTargetUid)
    {
        if (!Resolve(seekerTransformEntity, ref seekerTransformEntity.Comp2))
            return;

        // because you shouldn't be calling this outside of SetSeekerTarget and OnTargetSeekingShutdown improperly
        DebugTools.AssertNotNull(seekerTransformEntity.Comp1.CurrentTarget, "When raising EntityStoppedBeingSeekedTarget, CurrentTarget was already set to null!");

        var changedSeekingEvent = new EntityStoppedBeingSeekedTargetEvent(seekerTransformEntity!, seekerTransformEntity.Comp1.ExposesTracking);
        RaiseLocalEvent(oldTargetUid, ref changedSeekingEvent);
    }

    /// <summary>
    /// Sets a target-seeking projectile's <see cref="TargetSeekingComponent.CurrentTarget"/>, and raises
    /// the appropriate events.
    /// </summary>
    // NOTE: In the future, someone could want to change this to separate whether `CurrentTarget` is null with whether the seeker is actually targeting something.
    //       If so, change this to take in whether the seeker should be targeting something, rather than whether the target exists.
    //       Then, you'd be free to set `CurrentTarget` without needing to use this function.. ideally.
    public void SetSeekerTarget(Entity<TargetSeekingComponent> seekerEntity, EntityUid? targetUid, TransformComponent? seekerTransform = null)
    {
        var (_, seekerComponent) = seekerEntity;

        var targetChanged = seekerComponent.CurrentTarget != targetUid; // Forge-change: Add a parameter for the seeker transform.

        // if the new target is different from the old target,
        if (targetChanged) // Forge-change
        {
            // and we had an old target, then raise changing-seeking
            if (seekerComponent.CurrentTarget is { } oldTargetUid)
                OnChangingSeekingTarget((seekerEntity, seekerComponent, seekerTransform), oldTargetUid);

            // and our new target isn't null, then raise starting-seeking
            if (targetUid != null)
                OnStartingSeeking((seekerEntity, seekerComponent, seekerTransform), targetUid.Value);
        }

        seekerComponent.CurrentTarget = targetUid;
        // Forge-change-start
        UpdateSeekerAimPoint(seekerEntity.Owner, seekerComponent, targetUid, seekerTransform);

        if (targetChanged && targetUid != null && seekerComponent.RetargetInterval > 0f)
            seekerComponent.RetargetAccumulator = seekerComponent.RetargetInterval;
    }

    private void ClearSeekerAimPoint(TargetSeekingComponent seeker)
    {
        seeker.TargetAimCoordinates = null;
        seeker.AimEntity = null;
    }
    // Forge-change-end

    // Forge-change-start: New system for targeting of missles
    private void UpdateSeekerAimPoint(
        EntityUid seekerUid,
        TargetSeekingComponent seeker,
        EntityUid? targetUid,
        TransformComponent? seekerXform = null)
    {
        if (targetUid == null)
        {
            ClearSeekerAimPoint(seeker);
            return;
        }

        if (HasComp<MapGridComponent>(targetUid))
        {
            seekerXform ??= Transform(seekerUid);
            var preference = _transform.ToMapCoordinates(seekerXform.Coordinates).Position;

            if (TryResolveGridAimPoint(targetUid.Value, preference, out var aimCoords, out var aimEntity))
            {
                seeker.TargetAimCoordinates = aimCoords;
                seeker.AimEntity = aimEntity;
            }
            else
            {
                seeker.TargetAimCoordinates = Transform(targetUid.Value).Coordinates;
                seeker.AimEntity = null;
            }

            return;
        }

        ClearSeekerAimPoint(seeker);
    }

    private void RefreshGridAimIfNeeded(EntityUid seekerUid, TargetSeekingComponent seeker) // Forge-change: Add a parameter for the seeker transform.
    {
        if (seeker.CurrentTarget is not { } gridUid || !HasComp<MapGridComponent>(gridUid))
            return;

        if (seeker.AimEntity is { } aimEntity && !TerminatingOrDeleted(aimEntity))
            return;

        UpdateSeekerAimPoint(seekerUid, seeker, gridUid); // Forge-change: Add a parameter for the seeker transform.
    }

    /// <summary>
    /// Picks a guidance point on a grid: gunnery console, gunnery server, shuttle console,
    /// autonomous drone HTN core (no <see cref="DroneControlComponent"/>), then drone control server.
    /// Among entities of the same priority, picks the one closest to <paramref name="preferenceWorldPos"/> (the seeker).
    /// </summary>
    private bool TryResolveGridAimPoint(
        EntityUid gridUid,
        Vector2 preferenceWorldPos,
        out EntityCoordinates aimCoords,
        out EntityUid aimEntity)
    {
        if (TryPickClosestOnGrid<FireControlConsoleComponent>(gridUid, preferenceWorldPos, out aimCoords, out aimEntity))
            return true;

        if (TryPickClosestOnGrid<FireControlServerComponent>(gridUid, preferenceWorldPos, out aimCoords, out aimEntity))
            return true;

        if (TryPickClosestOnGrid<ShuttleConsoleComponent>(gridUid, preferenceWorldPos, out aimCoords, out aimEntity))
            return true;

        if (TryPickClosestAutonomousDroneCore(gridUid, preferenceWorldPos, out aimCoords, out aimEntity))
            return true;

        if (TryPickClosestOnGrid<DroneControlComponent>(gridUid, preferenceWorldPos, out aimCoords, out aimEntity))
            return true;

        return false;
    }

    private bool TryPickClosestOnGrid<TComp>(
        EntityUid gridUid,
        Vector2 preferenceWorldPos,
        out EntityCoordinates aimCoords,
        out EntityUid aimEntity) where TComp : IComponent
    {
        aimCoords = default;
        aimEntity = default;

        var bestDistSq = float.MaxValue;
        var found = false;

        var query = EntityQueryEnumerator<TComp, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != gridUid || TerminatingOrDeleted(uid))
                continue;

            var mapPos = _transform.ToMapCoordinates(xform.Coordinates).Position;
            var distSq = (mapPos - preferenceWorldPos).LengthSquared();
            if (distSq >= bestDistSq)
                continue;

            bestDistSq = distSq;
            aimCoords = xform.Coordinates;
            aimEntity = uid;
            found = true;
        }
        return found;
    }

    /// <summary>
    /// HTN drone core without player drone control (see <see cref="DroneControlComponent"/>).
    /// </summary>
    private bool TryPickClosestAutonomousDroneCore(
        EntityUid gridUid,
        Vector2 preferenceWorldPos,
        out EntityCoordinates aimCoords,
        out EntityUid aimEntity)
    {
        aimCoords = default;
        aimEntity = default;

        var bestDistSq = float.MaxValue;
        var found = false;

        var query = EntityQueryEnumerator<TargetSeekingTargetComponent, HTNComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.GridUid != gridUid || TerminatingOrDeleted(uid) || HasComp<DroneControlComponent>(uid))
                continue;

            var mapPos = _transform.ToMapCoordinates(xform.Coordinates).Position;
            var distSq = (mapPos - preferenceWorldPos).LengthSquared();
            if (distSq >= bestDistSq)
                continue;

            bestDistSq = distSq;
            aimCoords = xform.Coordinates;
            aimEntity = uid;
            found = true;
        }

        return found;
    }

    private Vector2 GetTargetWorldPosition(TargetSeekingComponent seeking, TransformComponent targetXform)
    {
        if (seeking.TargetAimCoordinates is { } aim)
            return _transform.ToMapCoordinates(aim).Position;

        return _transform.GetWorldPosition(targetXform);
    }
    // Forge-change-end

    /// <summary>
    /// Called when a target-seeking projectile hits something.
    /// </summary>
    private void OnProjectileHit(EntityUid uid, TargetSeekingComponent component, ref ProjectileHitEvent args)
    {
        // If we hit our actual target, we could perform additional effects here
        if (component.CurrentTarget.HasValue && component.CurrentTarget.Value == args.Target)
        {
            // Target hit successfully
        }

        // Reset the target since we've hit something
        SetSeekerTarget((uid, component), null);
    }

    /// <summary>
    /// Called when a target-seeking projectile changes parent (e.g., enters a grid).
    /// </summary>
    private void OnParentChanged(Entity<TargetSeekingComponent> seekerEntity, ref EntParentChangedMessage args)
    {
        // Check if the projectile has entered a grid
        if (args.Transform.GridUid == null)
            return;

        // Get the shooter's grid to compare
        if (!_projectileQuery.TryGetComponent(seekerEntity.Owner, out var projectile) ||
            !TryComp(projectile.Shooter, out TransformComponent? shooterTransform))
            return;

        var shooterGridUid = shooterTransform.GridUid;
        var currentGridUid = args.Transform.GridUid;

        // If we've entered a different grid than the shooter's grid, disable seeking
        if (currentGridUid != shooterGridUid)
            seekerEntity.Comp.SeekingDisabled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var ticktime = _gameTiming.TickPeriod;

        // Pre-build the candidate target list once per tick. Previously each seeker without a
        // current target re-iterated every TargetSeekingTargetComponent entity and re-resolved
        // Transform per candidate, giving O(missiles * targets) Transform lookups per tick.
        var targetsBuilt = false;

        var query = EntityQueryEnumerator<TargetSeekingComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var seekingComp, out var body, out var xform))
        {
            var acceleration = seekingComp.Acceleration * frameTime;
            // Initialize launch speed.
            if (seekingComp.Launched == false)
            {
                acceleration += seekingComp.LaunchSpeed;
                seekingComp.Launched = true;
            }

            // Apply acceleration in the direction the projectile is facing, then clamp to max speed in one write.
            var newVel = body.LinearVelocity + _transform.GetWorldRotation(xform).ToWorldVec() * acceleration;
            var velLen = newVel.Length();
            if (velLen > seekingComp.MaxSpeed)
                newVel *= seekingComp.MaxSpeed / velLen;
            _physics.SetLinearVelocity(uid, newVel, body: body);

            // Skip seeking behavior if disabled (e.g., after entering an enemy grid)
            if (seekingComp.SeekingDisabled)
                continue;

            if (seekingComp.TrackDelay > 0f)
            {
                seekingComp.TrackDelay -= frameTime;
                continue;
            }

            // Forge-change-start: Retargeting of missles
            if (seekingComp.RetargetInterval > 0f)
            {
                seekingComp.RetargetAccumulator -= frameTime;
                if (seekingComp.RetargetAccumulator <= 0f)
                {
                    seekingComp.RetargetAccumulator = seekingComp.RetargetInterval;
                    if (seekingComp.CurrentTarget != null)
                        SetSeekerTarget((uid, seekingComp), null, xform);
                }
            }
            // Forge-change-end

            // If we have a target, track it using the selected algorithm
            if (seekingComp.CurrentTarget.HasValue && !TerminatingOrDeleted(seekingComp.CurrentTarget))
            {
                RefreshGridAimIfNeeded(uid, seekingComp); // Forge-change: Retargeting of missles

                var target = seekingComp.CurrentTarget.Value;
                if (!_physicsQuery.TryGetComponent(target, out var targetBody))
                    continue;

                var targetXform = Transform(target);

                Angle wantAngle = new Angle(0);
                switch (seekingComp.TrackingAlgorithm)
                {
                    case TrackingMethod.Direct:
                        wantAngle = ApplyDirectTracking((uid, seekingComp, xform), (target, targetXform), frameTime); break; // Forge-change
                    case TrackingMethod.Predictive:
                        wantAngle = ApplyPredictiveTracking((uid, seekingComp, body, xform), (target, targetBody, targetXform), frameTime); break;
                    case TrackingMethod.AdvancedPredictive:
                        wantAngle = ApplyAdvancedTracking((uid, seekingComp, body, xform), (target, targetBody, targetXform), frameTime); break;
                }

                _rotateToFace.TryRotateTo(
                    uid,
                    wantAngle,
                    frameTime,
                    seekingComp.Tolerance,
                    seekingComp.TurnRate?.Theta ?? MathF.PI * 2,
                    xform
                );
            }
            else
            {
                // Try to acquire a new target
                if (!targetsBuilt)
                {
                    BuildTargetCandidates();
                    targetsBuilt = true;
                }
                AcquireTarget(uid, seekingComp, xform);
            }
        }

        _targetCandidates.Clear();
    }

    /// <summary>
    /// Populate <see cref="_targetCandidates"/> once per tick from all <see cref="TargetSeekingTargetComponent"/> entities.
    /// </summary>
    private void BuildTargetCandidates()
    {
        _targetCandidates.Clear();
        var targetQuery = EntityQueryEnumerator<TargetSeekingTargetComponent, TransformComponent>();
        while (targetQuery.MoveNext(out var targetUid, out _, out var targetXform))
        {
            var actualTarget = targetXform.GridUid ?? targetUid;
            var pos = _transform.ToMapCoordinates(targetXform.Coordinates).Position;
            _targetCandidates.Add(new TargetCandidate(actualTarget, pos, targetXform.GridUid));
        }
    }

    /// <summary>
    /// Finds the closest valid target within range and tracking parameters using the
    /// pre-built per-tick candidate list.
    /// </summary>
    public void AcquireTarget(EntityUid uid, TargetSeekingComponent component, TransformComponent transform)
    {
        if (_targetCandidates.Count == 0)
            return;

        // Resolve seeker info once.
        var sourcePos = _transform.ToMapCoordinates(transform.Coordinates).Position;
        var currentRotation = _transform.GetWorldRotation(transform);
        var halfScan = component.ScanArc * 0.5f;
        var detectionRangeSq = component.DetectionRange * component.DetectionRange;

        EntityUid? shooterGrid = null;
        if (_projectileQuery.TryGetComponent(uid, out var projectile)
            && TryComp(projectile.Shooter, out TransformComponent? shooterTransform))
        {
            shooterGrid = shooterTransform.GridUid;
        }

        var closestDistanceSq = float.MaxValue;
        EntityUid? bestTarget = null;

        for (var i = 0; i < _targetCandidates.Count; i++)
        {
            var candidate = _targetCandidates[i];

            // Squared-distance early reject.
            var delta = candidate.Position - sourcePos;
            var distSq = delta.LengthSquared();
            if (distSq > detectionRangeSq)
                continue;

            // Skip if the target is our own ship.
            if (shooterGrid != null && candidate.GridUid == shooterGrid)
                continue;

            // FOV check.
            var angleToTarget = delta.ToWorldAngle();
            var angleDifference = Angle.ShortestDistance(currentRotation, angleToTarget).Degrees;
            if (MathF.Abs((float)angleDifference) > halfScan)
                continue;

            if (closestDistanceSq > distSq)
            {
                closestDistanceSq = distSq;
                bestTarget = candidate.ActualTarget;
            }
        }

        if (bestTarget.HasValue)
            SetSeekerTarget((uid, component), bestTarget, transform);
    }

    /// <summary>
    /// Advanced tracking that predicts where the target will be based on its velocity.
    /// </summary>
    public Angle ApplyPredictiveTracking(Entity<TargetSeekingComponent, PhysicsComponent, TransformComponent> ent, Entity<PhysicsComponent, TransformComponent> target, float frameTime)
    {
        // Get current positions
        var currentTargetPosition = GetTargetWorldPosition(ent.Comp1, target.Comp2); // Forge-change: New system for targeting of missles
        var sourcePosition = _transform.GetWorldPosition(ent.Comp3);

        // Calculate current distance
        var toTargetVec = currentTargetPosition - sourcePosition;
        var currentDistance = toTargetVec.Length();

        var targetVelocity = _physics.GetMapLinearVelocity(target, target.Comp1, target.Comp2);
        var ourVelocity = _physics.GetMapLinearVelocity(ent, ent.Comp2, ent.Comp3);
        var relVel = ourVelocity - targetVelocity;

        // Calculate time to intercept (using closing rate)
        var closingRate = Vector2.Dot(relVel, toTargetVec) / toTargetVec.Length();
        var timeToIntercept = currentDistance / closingRate;

        // Prevent negative or very small intercept times that could cause erratic behavior
        timeToIntercept = MathF.Max(timeToIntercept, 0.1f);

        // Predict where the target will be when we reach it
        var predictedPosition = currentTargetPosition + (targetVelocity * timeToIntercept);

        // Calculate angle to the predicted position
        var targetAngle = (predictedPosition - sourcePosition).ToWorldAngle();

        return targetAngle;
    }

    /// <summary>
    /// More advanced and accurate tracking.
    /// Works best for missiles with low friction and high max speed, where they spend all or most of their lifetime accelerating and being under max speed.
    /// </summary>
    // see: https://github.com/Ilya246/orbitfight/blob/master/src/entities.cpp for original
    public Angle ApplyAdvancedTracking(Entity<TargetSeekingComponent, PhysicsComponent, TransformComponent> ent, Entity<PhysicsComponent, TransformComponent> target, float frameTime)
    {
        var accel = ent.Comp1.Acceleration;

        var ownVel = _physics.GetMapLinearVelocity(ent, ent.Comp2, ent.Comp3);
        var ownPos = _transform.GetWorldPosition(ent.Comp3);
        var targetVel = _physics.GetMapLinearVelocity(target, target.Comp1, target.Comp2);
        var targetPos = GetTargetWorldPosition(ent.Comp1, target.Comp2); // Forge-change: New system for targeting of missles
        var relVel = targetVel - ownVel;
        var relPos = targetPos - ownPos;

        return CalculateAdvancedTracking(relPos, relVel, accel);
    }

    public float CalculateAdvancedTrackingTime(Vector2 relPos, Vector2 relVel, float accel)
    {
        const int guidanceIterations = 3;

        var vel = relVel.Length();
        var refVec = vel == 0f ? new Vector2(1f, 0f) : relVel / vel;
        var projX = Vector2.Dot(relPos, refVec);
        var projY = relPos.Y * refVec.X - relPos.X * refVec.Y;
        var itime = GuessInterceptTime(0f, -projX, -vel, projY, accel);
        for (var i = 0; i < guidanceIterations; i++)
            itime = GuessInterceptTime(itime, -projX, -vel, projY, accel);

        return itime;

        // the explanation for how this works would take more space than the enclosing method so it's not included here
        float GuessInterceptTime(float prev, float x0, float vel, float y0, float accel)
        {
            var x = x0 + vel * prev;
            var d = MathF.Sqrt(x * x + y0 * y0);
            var dd = vel * x / d;
            return (dd + MathF.Sqrt(dd * dd + 2f * accel * (d - dd * prev))) / (accel);
        }
    }

    public Angle CalculateAdvancedTracking(Vector2 relPos, Vector2 relVel, float accel)
    {
        var itime = CalculateAdvancedTrackingTime(relPos, relVel, accel);
        var targetRot = (relPos + relVel * itime).ToWorldAngle();

        return targetRot;
    }

    /// <summary>
    /// Basic tracking that points directly at the current target position.
    /// </summary>
    public Angle ApplyDirectTracking(Entity<TargetSeekingComponent, TransformComponent> ent, Entity<TransformComponent> target, float frameTime) // Forge-change: New system for targeting of missles
    {
        // Get the angle directly toward the target
        var angleToTarget = (GetTargetWorldPosition(ent.Comp1, target.Comp) - _transform.GetWorldPosition(ent.Comp2)).ToWorldAngle(); // Forge-change: New system for targeting of missles

        return angleToTarget;
    }
}
