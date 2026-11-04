using System;
using System.Numerics;
using System.Collections.Generic;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Compat;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Crushing;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Foldable;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Collections;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Shared._WF.Vehicles.Grid;

/// <summary>Collision tests for the grid mover, in the grid's frame: what blocks, what is smashed and what is run over.</summary>
public sealed partial class GridVehicleMoverSystem : EntitySystem
{
    [Dependency] private VehicleSmashRuleSystem _smashRules = default!;

    /// <summary>Whether a door opened for a vehicle's driver when asked this tick, by vehicle and door; emptied every update.</summary>
    private readonly Dictionary<(EntityUid Vehicle, EntityUid Door), bool> _doorAccess = new();

    /// <summary>Whether the drive step goes on after a collision candidate was handled.</summary>
    private enum CollisionHandlingResult : byte
    {
        Continue = 0,
        Blocked = 1,
    }

    /// <summary>Something the collision probe found, with what the step needs to decide on it.</summary>
    /// <summary>
    /// The hull where a probe puts it, in the grid's frame: its bounds in its own axes, turned about its position. The
    /// upright boxes round it only pick candidates; whether the hull meets one is asked of this.
    /// </summary>
    private readonly record struct HullPose(Vector2 Position, Angle Rotation, Box2 Bounds, Box2 MovementBounds)
    {
        public bool Overlaps(Box2 other, bool movement)
        {
            return GridVehicleGeometry.Overlaps(Position, Rotation, movement ? MovementBounds : Bounds, other);
        }
    }

    private static readonly TimeSpan ImpactInterval = TimeSpan.FromSeconds(0.5);

    private readonly record struct CollisionCandidate(
        EntityUid Entity,
        Box2 Aabb,
        Box2 CollisionAabb,
        VehicleCollisionClass CollisionClass,
        DoorComponent? Door,
        MobStateComponent? MobState,
        bool IsVehicle,
        bool IsUnpoweredDoor,
        bool OpensForDriver,
        VehicleSmashSettings? Smash);

    private bool CanOccupyTransform(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        Vector2 gridPos,
        Angle? overrideRotation,
        float clearance,
        bool applyEffects,
        bool debug = true,
        HashSet<EntityUid>? blockers = null,
        HashSet<EntityUid>? ignoredEntities = null,
        bool allowVehiclePush = true)
    {
        if (!physicsQ.TryComp(uid, out var body) || !fixtureQ.TryComp(uid, out var fixtures))
            return true;

        EntityUid? operatorUid = null;
        if (TryComp<RMCVehicleComponent>(uid, out var vehicleComp))
            operatorUid = vehicleComp.Operator;

        if (!body.CanCollide)
            return true;

        if (!gridQ.TryComp(grid, out var gridComp))
            return true;

        // Boxes are measured in the movement grid's frame, where the vehicle is always square to the tiles, however the
        // grid is turned or moving in the world. Only the broadphase box and the debug drawing go through world space.
        var gridTx = _physics.GetPhysicsTransform(grid);
        var mapId = Transform(grid).MapID;
        var coords = new EntityCoordinates(grid, gridPos);

        var debugEnabled = debug && CollisionDebugEnabled;
        if (debugEnabled)
        {
            var tileIndices = _map.TileIndicesFor(grid, gridComp, coords);
            DebugTestedTiles.Add((grid, tileIndices));
        }

        var rotation = GetCollisionLocalRotation(uid, grid, overrideRotation);
        var tx = new PhysicsTransform(gridPos, rotation);

        if (!TryGetFixtureAabb(fixtures, tx, out var aabb))
            return true;

        var movementAabb = GetMovementAabb(aabb, mover);
        TryGetFixtureLocalAabb(fixtures, out var hullBounds);
        var hull = new HullPose(gridPos, rotation, hullBounds, GetMovementAabb(hullBounds, mover));

        void AddProbe(bool probeBlocked)
        {
            if (!debugEnabled)
                return;

            var worldTx = new PhysicsTransform(
                PhysicsTransform.Mul(gridTx, gridPos),
                new Angle(gridTx.Quaternion2D.Angle) + rotation);
            AddDebugCollisionProbe(
                uid,
                mover,
                fixtures,
                worldTx,
                GridVehicleGeometry.ToWorldBounds(aabb, gridTx),
                GridVehicleGeometry.ToWorldBounds(movementAabb, gridTx),
                mapId,
                probeBlocked,
                applyEffects);
        }

        if (!IsFootprintSupported(uid, mover, fixtures, grid, gridComp, gridTx, mapId, movementAabb, hull, out var emptyTile))
        {
            blockers?.Add(grid);
            if (debugEnabled)
            {
                AddBlockingCollision(
                    uid,
                    grid,
                    GridVehicleGeometry.ToWorldBounds(movementAabb, gridTx),
                    GridVehicleGeometry.ToWorldBounds(GridVehicleGeometry.GetTileBox(emptyTile, gridComp.TileSize), gridTx),
                    clearance,
                    mapId,
                    true,
                    null);
            }

            AddProbe(true);
            return false;
        }

        // A vehicle that pushes a vehicle that pushes a vehicle ends here rather than past the last buffer.
        if (_hitsDepth >= _hitsBuffers.Length)
            return false;

        _intersecting.Clear();
        _lookup.GetEntitiesIntersecting(mapId, GridVehicleGeometry.ToWorldBounds(aabb, gridTx), _intersecting, LookupFlags.Dynamic | LookupFlags.Static);
        var hits = _hitsBuffers[_hitsDepth++];
        try
        {
            hits.Clear();
            hits.AddRange(_intersecting);
            var playedCollisionSound = false;
            var mobHits = new ValueList<EntityUid>(0);

            foreach (var other in hits)
            {
                if (other == uid)
                    continue;

                if (ignoredEntities != null && ignoredEntities.Contains(other))
                    continue;

                // A grid is only its floor, and IsFootprintSupported has already answered for that.
                if (gridQ.HasComp(other))
                    continue;

                if (!TryBuildCollisionCandidate(
                        uid,
                        fixtures,
                        body,
                        other,
                        gridTx,
                        aabb,
                        movementAabb,
                        hull,
                        operatorUid,
                        out var candidate))
                {
                    continue;
                }

                if (candidate.IsVehicle && gridPos != mover.Position &&
                    rotation.Equals(GetCollisionLocalRotation(uid, grid, null)) &&
                    TryGetFixtureAabb(fixtures, new PhysicsTransform(mover.Position, rotation), out var currentBounds))
                {
                    var currentHull = new HullPose(mover.Position, rotation, hullBounds, GetMovementAabb(hullBounds, mover));
                    var previous = GetMovementAabb(currentBounds, mover);
                    var next = candidate.CollisionAabb;
                    if (currentHull.Overlaps(candidate.Aabb, true) &&
                        Penetration(next, candidate.Aabb) < Penetration(previous, candidate.Aabb) - 0.00001f &&
                        Vector2.DistanceSquared(next.Center, candidate.Aabb.Center) >
                        Vector2.DistanceSquared(previous.Center, candidate.Aabb.Center))
                        continue;
                }

                if (candidate.CollisionClass == VehicleCollisionClass.SoftMob &&
                    candidate.MobState != null &&
                    _standing.IsDown(candidate.Entity))
                {
                    continue;
                }

                // A vehicle with nobody at the wheel opens nothing: the door system takes a missing user as one with access.
                if (applyEffects && candidate.Door is { } door && operatorUid is { } driver && !_net.IsClient)
                {
                    if (candidate.OpensForDriver)
                    {
                        // The access check was made, and logged, when the door was classed; opening needs none of its own.
                        if (door.State is DoorState.Closed or DoorState.Closing or DoorState.Denying)
                            _door.StartOpening(candidate.Entity, door, driver);
                    }
                    else if (candidate.CollisionClass == VehicleCollisionClass.Breakable && !candidate.IsUnpoweredDoor)
                    {
                        // A powered door the driver may not open: asking shows the denial.
                        _door.TryOpen(candidate.Entity, door, driver);
                    }
                }

                if (candidate.CollisionClass == VehicleCollisionClass.Ignore)
                    continue;

                if (candidate.CollisionClass == VehicleCollisionClass.Breakable)
                {
                    if (candidate.Smash is not { } smash)
                        continue;

                    // The grille a hull window is set on is as safe from a vehicle too light for the window as the window is.
                    if (applyEffects &&
                        IsShelteredFromSmash(uid, mover, fixtures, body, grid, gridComp, gridTx, aabb, movementAabb, hull, operatorUid, hits, ignoredEntities, candidate, mapId))
                    {
                        continue;
                    }

                    var result = HandleBreakableCollision(
                        uid,
                        mover,
                        candidate.Entity,
                        smash,
                        debugEnabled ? GridVehicleGeometry.ToWorldBounds(candidate.CollisionAabb, gridTx) : default,
                        debugEnabled ? GridVehicleGeometry.ToWorldBounds(candidate.Aabb, gridTx) : default,
                        clearance,
                        mapId,
                        candidate.Door != null,
                        candidate.IsUnpoweredDoor,
                        applyEffects,
                        debugEnabled,
                        blockers,
                        ref playedCollisionSound);

                    if (result == CollisionHandlingResult.Blocked)
                    {
                        if (applyEffects)
                            TryPlowInto(uid, mover, fixtures, gridPos, rotation, candidate);

                        AddProbe(true);
                        return false;
                    }

                    continue;
                }

                if (candidate.CollisionClass == VehicleCollisionClass.Hard)
                {
                    var result = HandleHardCollision(
                        uid,
                        mover,
                        grid,
                        gridPos,
                        candidate.Entity,
                        candidate.CollisionAabb,
                        candidate.Aabb,
                        clearance,
                        mapId,
                        candidate.IsVehicle,
                        allowVehiclePush,
                        applyEffects,
                        debugEnabled,
                        blockers,
                        ref playedCollisionSound);

                    if (result == CollisionHandlingResult.Blocked)
                    {
                        if (applyEffects)
                            TryPlowInto(uid, mover, fixtures, gridPos, rotation, candidate);

                        AddProbe(true);
                        return false;
                    }

                    continue;
                }

                if (applyEffects &&
                    _net.IsClient &&
                    candidate.MobState != null &&
                    ShouldPredictVehicleInteractions(uid))
                {
                    PredictRunover(uid, candidate.Entity, candidate.MobState);
                }

                if (applyEffects && !_net.IsClient && candidate.MobState != null)
                {
                    if (!mobHits.Contains(candidate.Entity))
                        mobHits.Add(candidate.Entity);
                }
            }

            if (!_net.IsClient && mobHits.Count > 0)
            {
                foreach (var mobUid in mobHits)
                {
                    if (!TryComp(mobUid, out MobStateComponent? mob))
                        continue;

                    HandleMobCollision(uid, mobUid, mob, ref playedCollisionSound);
                }
            }

            AddProbe(false);
            return true;
        }
        finally
        {
            _hitsDepth--;
        }
    }

    /// <summary>
    /// Whether something else on the tile of <paramref name="target"/> stops the vehicle without being smashed, as a hull
    /// window too tough for a light vehicle does. What shares the tile, the grille the window is set on, is then left alone:
    /// the vehicle is held where it is and breaks nothing there.
    /// </summary>
    private bool IsShelteredFromSmash(
        EntityUid vehicle,
        GridVehicleMoverComponent mover,
        FixturesComponent fixtures,
        PhysicsComponent body,
        EntityUid grid,
        MapGridComponent gridComp,
        PhysicsTransform gridTx,
        Box2 aabb,
        Box2 movementAabb,
        HullPose hull,
        EntityUid? operatorUid,
        List<EntityUid> hits,
        HashSet<EntityUid>? ignoredEntities,
        in CollisionCandidate target,
        MapId mapId)
    {
        var targetXform = Transform(target.Entity);
        if (!targetXform.Anchored)
            return false;

        var tile = _map.TileIndicesFor(grid, gridComp, targetXform.Coordinates);

        foreach (var other in hits)
        {
            if (other == vehicle || other == target.Entity || ignoredEntities != null && ignoredEntities.Contains(other))
                continue;

            var otherXform = Transform(other);
            if (!otherXform.Anchored ||
                otherXform.GridUid != targetXform.GridUid ||
                _map.TileIndicesFor(grid, gridComp, otherXform.Coordinates) != tile)
            {
                continue;
            }

            if (!TryBuildCollisionCandidate(vehicle, fixtures, body, other, gridTx, aabb, movementAabb, hull, operatorUid, out var neighbour) ||
                neighbour.CollisionClass != VehicleCollisionClass.Breakable ||
                neighbour.Smash is not { } smash)
            {
                continue;
            }

            var noSound = true;
            var result = HandleBreakableCollision(
                vehicle,
                mover,
                other,
                smash,
                default,
                default,
                0f,
                mapId,
                neighbour.Door != null,
                neighbour.IsUnpoweredDoor,
                applyEffects: false,
                debug: false,
                blockers: null,
                ref noSound);

            if (result == CollisionHandlingResult.Blocked)
                return true;
        }

        return false;
    }

    /// <summary>Raises <see cref="VehicleCollisionEvent"/> on the vehicle; server only, since its effects change shared state.</summary>
    private void RaiseVehicleCollision(
        EntityUid vehicle,
        GridVehicleMoverComponent mover,
        VehicleCollisionKind kind,
        float speed,
        EntityUid? other = null)
    {
        if (_net.IsClient)
            return;

        var ev = new VehicleCollisionEvent(kind, other, speed, GetModifiedMaxSpeed(vehicle, mover));
        RaiseLocalEvent(vehicle, ref ev);
    }

    private bool TryBuildCollisionCandidate(
        EntityUid vehicle,
        FixturesComponent vehicleFixtures,
        PhysicsComponent vehicleBody,
        EntityUid other,
        PhysicsTransform gridTransform,
        Box2 vehicleAabb,
        Box2 movementAabb,
        HullPose hull,
        EntityUid? operatorUid,
        out CollisionCandidate candidate)
    {
        candidate = default;

        var otherXform = Transform(other);
        if (!otherXform.Anchored && HasComp<ItemComponent>(other))
            return false;

        if (!physicsQ.TryComp(other, out var otherBody) || !otherBody.CanCollide)
            return false;

        var hasDoor = TryComp(other, out DoorComponent? door);
        var isFoldable = HasComp<FoldableComponent>(other);
        var isMob = TryComp(other, out MobStateComponent? mob);
        var isVehicle = HasComp<RMCVehicleComponent>(other);
        var isSmashable = TryGetSmashable(other, out var smash);

        if (!isMob &&
            !otherXform.Anchored &&
            otherBody.BodyType != BodyType.Static &&
            !isFoldable &&
            !isVehicle &&
            !isSmashable)
        {
            return false;
        }

        if (!fixtureQ.TryComp(other, out var otherFixtures))
            return false;

        var otherTx = PhysicsTransform.MulT(gridTransform, _physics.GetPhysicsTransform(other, otherXform));

        if (!TryGetFixtureAabb(otherFixtures, otherTx, out var otherAabb))
            return false;

        if (!vehicleAabb.Intersects(otherAabb))
            return false;

        var hardCollidable = VehicleHardCollision.IsHardCollidable((vehicle, vehicleFixtures, vehicleBody), (other, otherFixtures, otherBody));
        var collisionClass = ClassifyCollisionCandidate(
            other,
            otherXform,
            otherBody,
            otherFixtures,
            hardCollidable,
            isMob,
            isFoldable,
            hasDoor,
            isVehicle,
            isSmashable);

        var doorPowerKnown = TryGetDoorPowered(other, out var doorPowered);
        var isUnpoweredDoor = hasDoor && doorPowerKnown && !doorPowered;
        var opensForDriver = hasDoor && door != null && operatorUid is { } driver &&
                             CanDriverOpenDoor(vehicle, other, door, driver, isSmashable, doorPowerKnown, doorPowered);
        if (opensForDriver)
            collisionClass = VehicleCollisionClass.Ignore;

        var collisionAabb = GetCollisionAabb(collisionClass, vehicleAabb, movementAabb);
        if (!HasCollisionOverlap(collisionAabb, otherAabb))
            return false;

        // The upright box reaches past a turned hull's corners; only what the hull itself meets counts.
        if (!hull.Overlaps(otherAabb, collisionClass != VehicleCollisionClass.SoftMob))
            return false;

        candidate = new CollisionCandidate(
            other,
            otherAabb,
            collisionAabb,
            collisionClass,
            door,
            mob,
            isVehicle,
            isUnpoweredDoor,
            opensForDriver,
            isSmashable ? smash : null);

        return true;
    }

    /// <summary>Finds how a vehicle smashes <paramref name="target"/>: its component, else the matching smash rule.</summary>
    public bool TryGetSmashable(EntityUid target, out VehicleSmashSettings settings)
    {
        if (TryComp(target, out VehicleSmashableComponent? smashable))
        {
            settings = VehicleSmashSettings.From(smashable);
            return true;
        }

        return _smashRules.TryGetSettings(target, out settings);
    }

    /// <summary>Whether a door opens for the driver, so the vehicle goes through it.</summary>
    private bool CanDriverOpenDoor(
        EntityUid vehicle,
        EntityUid doorUid,
        DoorComponent door,
        EntityUid driver,
        bool isSmashable,
        bool powerKnown,
        bool powered)
    {
        if (isSmashable ? !(powerKnown && powered) : (!door.BumpOpen || (powerKnown && !powered)))
            return false;

        if (door.State is DoorState.Opening or DoorState.Open)
            return true;

        var key = (vehicle, doorUid);
        if (_doorAccess.TryGetValue(key, out var known))
            return known;

        var allowed = _door.CanOpen(doorUid, door, driver);
        _doorAccess[key] = allowed;
        return allowed;
    }

    private CollisionHandlingResult HandleBreakableCollision(
        EntityUid vehicle,
        GridVehicleMoverComponent mover,
        EntityUid other,
        VehicleSmashSettings smashable,
        Box2 collisionAabb,
        Box2 otherAabb,
        float clearance,
        MapId mapId,
        bool hasDoor,
        bool isUnpoweredDoor,
        bool applyEffects,
        bool debug,
        HashSet<EntityUid>? blockers,
        ref bool playedCollisionSound)
    {
        // Already smashed earlier in this tick and waiting to be deleted: a second hit would run its destruction a second
        // time, so it only stops the vehicle where the first hit did.
        if (!_net.IsClient && IsGone(other))
        {
            if (smashable.MinContinueWeightClass is not { } minContinueWeight || mover.WeightClass >= minContinueWeight)
                return CollisionHandlingResult.Continue;

            AddBlockingCollision(vehicle, other, collisionAabb, otherAabb, clearance, mapId, debug, blockers);
            return CollisionHandlingResult.Blocked;
        }

        if (smashable.RequiresDoorUnpowered && hasDoor && !isUnpoweredDoor)
        {
            if (applyEffects)
            {
                PlayCollisionSound(vehicle, ref playedCollisionSound);
                RaiseVehicleCollision(vehicle, mover, VehicleCollisionKind.Impact, mover.CurrentSpeed, other);
                RaiseSmashBlocked(vehicle, other, VehicleSmashBlockReason.DoorPowered);
            }

            AddBlockingCollision(vehicle, other, collisionAabb, otherAabb, clearance, mapId, debug, blockers);
            return CollisionHandlingResult.Blocked;
        }

        if (smashable.MinDestroyWeightClass is { } minDestroy && mover.WeightClass < minDestroy)
        {
            if (applyEffects)
            {
                PlayCollisionSound(vehicle, ref playedCollisionSound);
                RaiseVehicleCollision(vehicle, mover, VehicleCollisionKind.Impact, mover.CurrentSpeed, other);
                RaiseSmashBlocked(vehicle, other, VehicleSmashBlockReason.TooLight);
            }

            AddBlockingCollision(vehicle, other, collisionAabb, otherAabb, clearance, mapId, debug, blockers);
            return CollisionHandlingResult.Blocked;
        }

        if (!applyEffects)
            return CollisionHandlingResult.Continue;

        TrySmash(other, vehicle, smashable, ref playedCollisionSound);

        if (smashable.MinDestroyWeightClass == null && smashable.MinContinueWeightClass == null)
            return CollisionHandlingResult.Continue;

        if (!IsSmashedAway(other, smashable))
        {
            AddBlockingCollision(vehicle, other, collisionAabb, otherAabb, clearance, mapId, debug, blockers);
            return CollisionHandlingResult.Blocked;
        }

        if (mover.WeightClass < smashable.MinContinueWeightClass)
            return CollisionHandlingResult.Blocked;

        return CollisionHandlingResult.Continue;
    }

    private CollisionHandlingResult HandleHardCollision(
        EntityUid vehicle,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        Vector2 gridPos,
        EntityUid other,
        Box2 collisionAabb,
        Box2 otherAabb,
        float clearance,
        MapId mapId,
        bool isVehicle,
        bool allowVehiclePush,
        bool applyEffects,
        bool debug,
        HashSet<EntityUid>? blockers,
        ref bool playedCollisionSound)
    {
        if (allowVehiclePush && isVehicle && MathF.Abs(mover.CurrentSpeed) < 0.75f && TryPushVehicle(vehicle, mover, grid, gridPos, other, applyEffects))
            return CollisionHandlingResult.Continue;

        // One impact per contact, not one per step of it: the wheels pay for the hit, not for how long it lasts.
        if (applyEffects && _timing.CurTime >= mover.NextImpactTime)
        {
            var impactSpeed = mover.CurrentSpeed;
            mover.NextImpactTime = _timing.CurTime + ImpactInterval;
            if (isVehicle && !_net.IsClient && TryComp(other, out GridVehicleMoverComponent? struck) &&
                physicsQ.TryComp(vehicle, out var sourceBody) && physicsQ.TryComp(other, out var targetBody))
            {
                var struckPosition = Transform(other).Coordinates.WithEntityId(grid, _transform, EntityManager).Position;
                var targetToGrid = struck.SyncedGrid is { } targetGrid
                    ? _transform.GetWorldRotation(targetGrid) - _transform.GetWorldRotation(grid)
                    : Angle.Zero;
                var delta = struckPosition - mover.Position;
                if (delta.LengthSquared() > 0.001f)
                {
                    var normal = Vector2.Normalize(delta);
                    var driveVelocity = mover.FreeSteering ? mover.TravelVelocity :
                        GetCollisionLocalRotation(vehicle, grid, null).ToWorldVec() * mover.CurrentSpeed;
                    var otherDriveVelocity = struck.FreeSteering ? targetToGrid.RotateVec(struck.TravelVelocity) :
                        GetCollisionLocalRotation(other, grid, null).ToWorldVec() * struck.CurrentSpeed;
                    var velocity = driveVelocity + mover.ImpactVelocity;
                    var otherVelocity = otherDriveVelocity + targetToGrid.RotateVec(struck.ImpactVelocity);
                    var closing = MathF.Max(0f, Vector2.Dot(velocity - otherVelocity, normal));
                    var sourceMass = MathF.Max(1f, sourceBody.Mass);
                    var targetMass = MathF.Max(1f, targetBody.Mass);
                    var impulse = normal * GridVehicleMotionSimulator.CollisionImpulse(closing, sourceMass, targetMass);
                    struck.ImpactVelocity += (-targetToGrid).RotateVec(impulse / targetMass);
                    if (closing > 0f)
                    {
                        mover.ImpactVelocity = velocity - impulse / sourceMass;
                        mover.CurrentSpeed = 0f;
                        mover.TravelVelocity = Vector2.Zero;
                    }
                    var contact = Vector2.Clamp(collisionAabb.Center, otherAabb.BottomLeft, otherAabb.TopRight);
                    var arm = contact - struckPosition;
                    var torque = arm.X * impulse.Y - arm.Y * impulse.X;
                    struck.ImpactSpin += Math.Clamp(torque / MathF.Max(1f, targetBody.Inertia) * 57.29578f, -90f, 90f);
                    var sourceArm = contact - mover.Position;
                    var sourceTorque = sourceArm.Y * impulse.X - sourceArm.X * impulse.Y;
                    mover.ImpactSpin += Math.Clamp(sourceTorque / MathF.Max(1f, sourceBody.Inertia) * 57.29578f, -90f, 90f);
                    Dirty(other, struck);
                    Dirty(vehicle, mover);
                }
            }
            PlayCollisionSound(vehicle, ref playedCollisionSound);
            RaiseVehicleCollision(vehicle, mover, VehicleCollisionKind.Impact, impactSpeed, other);
        }

        var gridTransform = _physics.GetPhysicsTransform(grid);
        AddBlockingCollision(vehicle, other,
            debug ? GridVehicleGeometry.ToWorldBounds(collisionAabb, gridTransform) : default,
            debug ? GridVehicleGeometry.ToWorldBounds(otherAabb, gridTransform) : default,
            clearance, mapId, debug, blockers);
        return CollisionHandlingResult.Blocked;
    }

    private static void AddBlockingCollision(
        EntityUid vehicle,
        EntityUid blocker,
        Box2 collisionAabb,
        Box2 blockerAabb,
        float clearance,
        MapId mapId,
        bool debug,
        HashSet<EntityUid>? blockers)
    {
        blockers?.Add(blocker);
        if (debug)
            DebugCollisions.Add(new DebugCollision(vehicle, blocker, collisionAabb, blockerAabb, 0f, 0f, clearance, mapId));
    }

    private static void AddDebugCollisionProbe(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        FixturesComponent fixtures,
        PhysicsTransform transformData,
        Box2 aabb,
        Box2 movementAabb,
        MapId map,
        bool blocked,
        bool applyEffects)
    {
        if (!TryGetFixtureLocalAabb(fixtures, out var localAabb))
            return;

        var localMovementAabb = GetMovementAabb(localAabb, mover);
        var rotation = new Angle(transformData.Quaternion2D.Angle);
        var fixtureBounds = new Box2Rotated(localAabb.Translated(transformData.Position), rotation, transformData.Position);
        var movementBounds = new Box2Rotated(localMovementAabb.Translated(transformData.Position), rotation, transformData.Position);

        DebugCollisionProbes.Add(new DebugCollisionProbe(
            uid,
            aabb,
            movementAabb,
            fixtureBounds,
            movementBounds,
            transformData.Position,
            rotation,
            blocked,
            applyEffects,
            map));
    }

    private static Box2 GetCollisionAabb(VehicleCollisionClass collisionClass, Box2 fullAabb, Box2 movementAabb)
    {
        return collisionClass == VehicleCollisionClass.SoftMob
            ? fullAabb
            : movementAabb;
    }

    /// <summary>Translation needed to separate two overlapping boxes, including a contained hull.</summary>
    private static float Penetration(Box2 hull, Box2 other)
    {
        return MathF.Min(MathF.Min(hull.Right - other.Left, other.Right - hull.Left),
            MathF.Min(hull.Top - other.Bottom, other.Top - hull.Bottom));
    }

    private static bool HasCollisionOverlap(Box2 vehicleAabb, Box2 otherAabb)
    {
        var intersection = vehicleAabb.Intersect(otherAabb);
        return intersection.Width > 0f && intersection.Height > 0f;
    }

    private static Box2 GetMovementAabb(Box2 aabb, GridVehicleMoverComponent mover)
    {
        var inset = Math.Clamp(mover.MovementCollisionInset, 0f, 0.45f);
        if (inset <= 0f)
            return aabb;

        var adjusted = aabb.Enlarged(-inset);
        return adjusted.IsValid() ? adjusted : aabb;
    }

    /// <summary>The vehicle's rotation in the grid's frame: the one being tested, or the one it has now.</summary>
    private Angle GetCollisionLocalRotation(EntityUid uid, EntityUid grid, Angle? overrideRotation)
    {
        if (overrideRotation is { } localRotation)
            return localRotation;

        var xform = Transform(uid);
        return xform.ParentUid == grid
            ? xform.LocalRotation
            : _transform.GetWorldRotation(uid) - _transform.GetWorldRotation(grid);
    }

    /// <summary>Whether a door has power, if it has a power state.</summary>
    internal bool TryGetDoorPowered(EntityUid target, out bool powered)
    {
        if (TryComp(target, out AirlockComponent? airlock))
        {
            powered = airlock.Powered;
            return true;
        }

        if (TryComp(target, out FirelockComponent? firelock))
        {
            powered = firelock.Powered;
            return true;
        }

        powered = false;
        return false;
    }

    /// <summary>The box that covers an entity's fixtures at a transform.</summary>
    internal static bool TryGetFixtureAabb(FixturesComponent fixtures, PhysicsTransform transformData, out Box2 aabb)
    {
        var first = true;
        aabb = default;

        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            for (var i = 0; i < fixture.Shape.ChildCount; i++)
            {
                var child = fixture.Shape.ComputeAABB(transformData, i);

                if (first)
                {
                    aabb = child;
                    first = false;
                }
                else
                {
                    aabb = aabb.Union(child);
                }
            }
        }

        return !first;
    }

    private static bool TryGetFixtureLocalAabb(FixturesComponent fixtures, out Box2 aabb)
    {
        return TryGetFixtureAabb(fixtures, PhysicsTransform.Empty, out aabb);
    }

    private bool TryPushVehicle(
        EntityUid pusher,
        GridVehicleMoverComponent pusherMover,
        EntityUid grid,
        Vector2 pusherTargetPosition,
        EntityUid pushed,
        bool applyEffects)
    {
        if (!pusherMover.CanPushVehicles)
            return false;

        if (!TryComp(pushed, out RMCVehicleComponent? pushedVehicle) ||
            pushedVehicle.MovementKind != VehicleMovementKind.Grid)
        {
            return false;
        }

        if (!TryComp(pushed, out GridVehicleMoverComponent? pushedMover))
            return false;

        if (!gridQ.TryComp(grid, out var gridComp))
            return false;

        var pushedXform = Transform(pushed);
        if (pushedXform.GridUid != grid)
            return false;

        var pushDelta = pusherTargetPosition - pusherMover.Position;
        if (pushDelta.LengthSquared() <= MinMoveDistance * MinMoveDistance)
            return false;

        TrySyncMoverToCurrentGrid((pushed, pushedMover), centerOnTile: false, pushedXform);
        if (pushedMover.SyncedGrid != grid)
            return false;

        // One set per depth, so a pushed vehicle that pushes the next does not empty the set its own check is reading.
        var ignored = _pushIgnoredBuffers[_hitsDepth - 1];
        ignored.Clear();
        ignored.Add(pusher);
        var pushedTarget = pushedMover.Position + pushDelta;
        if (!CanOccupyTransform(
                pushed,
                pushedMover,
                grid,
                pushedTarget,
                null,
                Clearance,
                applyEffects: false,
                debug: false,
                ignoredEntities: ignored))
        {
            return false;
        }

        if (!applyEffects)
            return true;

        if (!CanOccupyTransform(
                pushed,
                pushedMover,
                grid,
                pushedTarget,
                null,
                Clearance,
                applyEffects: true,
                debug: false,
                ignoredEntities: ignored))
        {
            return false;
        }

        pushedMover.Position = pushedTarget;
        pushedMover.CurrentSpeed = 0f;
        pushedMover.IsCommittedToMove = false;
        pushedMover.IsMoving = true;
        UpdateDerivedTileState(grid, gridComp, pushedMover);
        SetGridPosition(pushed, grid, pushedMover.Position);
        _physics.WakeBody(pushed);
        Dirty(pushed, pushedMover);
        return true;
    }

    private bool TrySmash(EntityUid target, EntityUid vehicle, VehicleSmashSettings smashable, ref bool playedCollisionSound)
    {
        PlayCollisionSound(vehicle, ref playedCollisionSound);

        if (TryComp(vehicle, out GridVehicleMoverComponent? mover))
            ApplySmashSlowdown(vehicle, mover, smashable);

        if (_net.IsClient)
            return true;

        if (smashable.SmashSound != null)
            _audio.PlayPvs(smashable.SmashSound, Transform(target).Coordinates);

        SmashTarget(target, vehicle, smashable);

        return true;
    }

    /// <summary>Hits a smashed target for its damage, sized by the target's own say, and destroys it when the settings say so; shared with the landing of a falling vehicle.</summary>
    internal void SmashTarget(EntityUid target, EntityUid vehicle, VehicleSmashSettings smashable)
    {
        var amount = smashable.DamageOnHit;
        if (smashable.DeleteOnHit)
        {
            // The destruction thresholds live on the server: the target says how big a hit runs its normal destruction.
            var hit = new VehicleSmashDamageEvent(vehicle, amount);
            RaiseLocalEvent(target, ref hit);
            amount = hit.Damage;
        }

        var damage = new DamageSpecifier
        {
            DamageDict =
            {
                [CollisionDamageType] = amount,
            },
        };

        _damageable.TryChangeDamage(target, damage, true, origin: vehicle, tool: vehicle);

        if (!smashable.DeleteOnHit)
            return;

        // Only what the damage left standing is deleted outright; destruction that already ran queued its own deletion.
        if (IsGone(target))
            return;

        _destructible.DestroyEntity(target);
    }

    /// <summary>Whether the target is deleted, or about to be: destruction queues the deletion, which only happens at the end of the tick.</summary>
    private bool IsGone(EntityUid target)
    {
        return TerminatingOrDeleted(target) || EntityManager.IsQueuedForDeletion(target);
    }

    /// <summary>Whether a smash took the target away; the client predicts what the server will do.</summary>
    private bool IsSmashedAway(EntityUid target, VehicleSmashSettings smashable)
    {
        return _net.IsClient ? smashable.DeleteOnHit : IsGone(target);
    }

    /// <summary>Raises <see cref="VehicleSmashBlockedEvent"/> on the vehicle; server only, since it only tells the driver.</summary>
    private void RaiseSmashBlocked(EntityUid vehicle, EntityUid target, VehicleSmashBlockReason reason)
    {
        if (_net.IsClient)
            return;

        var ev = new VehicleSmashBlockedEvent(target, reason);
        RaiseLocalEvent(vehicle, ref ev);
    }

    private void PlayCollisionSound(EntityUid uid, ref bool played)
    {
        if (played)
            return;

        if (!TryComp<VehicleSoundComponent>(uid, out var sound))
            return;

        if (sound.CollisionSound == null)
            return;

        if (_net.IsClient)
            return;

        var now = _timing.CurTime;
        if (sound.NextCollisionSound > now)
            return;

        _audio.PlayPvs(sound.CollisionSound, uid);
        sound.NextCollisionSound = now + TimeSpan.FromSeconds(sound.CollisionSoundCooldown);
        Dirty(uid, sound);
        played = true;
    }

    private void PlayMobCollisionSound(EntityUid uid, ref bool played)
    {
        if (played)
            return;

        if (!TryComp<VehicleSoundComponent>(uid, out var sound))
            return;

        var mobSound = sound.MobCollisionSound ?? sound.CollisionSound;
        if (mobSound == null)
            return;

        if (_net.IsClient)
            return;

        var now = _timing.CurTime;
        if (sound.NextCollisionSound > now)
            return;

        _audio.PlayPvs(mobSound, uid);
        sound.NextCollisionSound = now + TimeSpan.FromSeconds(sound.CollisionSoundCooldown);
        Dirty(uid, sound);
        played = true;
    }

    private void HandleMobCollision(EntityUid vehicle, EntityUid target, MobStateComponent mobState, ref bool playedCollisionSound)
    {
        if (_net.IsClient || _mobState.IsDead(target, mobState))
            return;

        if (TryComp(vehicle, out GridVehicleMoverComponent? vehicleMover) &&
            MathF.Abs(vehicleMover.CurrentSpeed) < MobCollisionMinKnockdownSpeed)
        {
            return;
        }

        var now = _timing.CurTime;
        if (_lastMobCollision.TryGetValue(target, out var last) && now < last + MobCollisionCooldown)
            return;

        _lastMobCollision[target] = now;

        PlayMobCollisionSound(vehicle, ref playedCollisionSound);

        // Harder the faster and the heavier the vehicle.
        var speed = vehicleMover == null ? 0f : MathF.Abs(vehicleMover.CurrentSpeed);
        var weight = (vehicleMover?.WeightClass ?? VehicleWeightClass.Weak) switch
        {
            VehicleWeightClass.Light => 1.25,
            VehicleWeightClass.Medium => 1.75,
            VehicleWeightClass.Heavy => 2.25,
            _ => 1.0,
        };
        var amount = Math.Min((MobCollisionDamage + MobCollisionDamagePerSpeed * speed) * weight, MobCollisionMaxDamage);
        var damage = new DamageSpecifier
        {
            DamageDict =
            {
                [CollisionDamageType] = amount,
            },
        };

        _damageable.TryChangeDamage(target, damage, origin: vehicle);

        var ranOver = new VehicleRanOverEvent(vehicle, speed);
        RaiseLocalEvent(target, ref ranOver);

        _stun.TryKnockdown(target, MobCollisionKnockdown, true);
        var runover = EnsureComp<VehicleRunoverComponent>(target);
        runover.Vehicle = vehicle;
        runover.Duration = MobCollisionKnockdown;
        runover.ExpiresAt = now + runover.Duration + VehicleRunoverSystem.StandUpGrace;
        Dirty(target, runover);

        if (physicsQ.TryComp(target, out var targetBody))
        {
            _physics.SetLinearVelocity(target, Vector2.Zero, body: targetBody);
            _physics.SetAngularVelocity(target, 0f, body: targetBody);
        }
    }

    private bool ShouldPredictVehicleInteractions(EntityUid vehicle)
    {
        if (!_net.IsClient || !_timing.InPrediction)
            return false;

        if (!physicsQ.TryComp(vehicle, out var vehicleBody) || !vehicleBody.Predict)
            return false;

        if (!TryComp(vehicle, out RMCVehicleComponent? vehicleComp))
            return false;

        return vehicleComp.Operator != null && vehicleComp.Operator == _player.LocalEntity;
    }

    private void PredictRunover(EntityUid vehicle, EntityUid mob, MobStateComponent mobState)
    {
        if (!ShouldPredictVehicleInteractions(vehicle))
            return;

        if (_mobState.IsDead(mob, mobState) || _standing.IsDown(mob))
            return;

        if (TryComp(vehicle, out GridVehicleMoverComponent? vehicleMover) &&
            MathF.Abs(vehicleMover.CurrentSpeed) < MobCollisionMinKnockdownSpeed)
        {
            return;
        }

        _stun.TryKnockdown(mob, MobCollisionKnockdown, true);

        var runover = EnsureComp<VehicleRunoverComponent>(mob);
        runover.Vehicle = vehicle;
        runover.Duration = MobCollisionKnockdown;
        runover.ExpiresAt = _timing.CurTime + runover.Duration + VehicleRunoverSystem.StandUpGrace;
        Dirty(mob, runover);

        if (physicsQ.TryComp(mob, out var mobBody))
        {
            _physics.SetLinearVelocity(mob, Vector2.Zero, body: mobBody);
            _physics.SetAngularVelocity(mob, 0f, body: mobBody);
        }
    }

    private VehicleCollisionClass ClassifyCollisionCandidate(
        EntityUid other,
        TransformComponent otherXform,
        PhysicsComponent otherBody,
        FixturesComponent otherFixtures,
        bool hardCollidable,
        bool isMob,
        bool isFoldable,
        bool hasDoor,
        bool isVehicle,
        bool isSmashable)
    {
        if (!otherXform.Anchored && HasComp<ItemComponent>(other))
            return VehicleCollisionClass.Ignore;

        if (isMob)
            return VehicleCollisionClass.SoftMob;

        if (IsNormallyMobPassable(otherFixtures))
            return VehicleCollisionClass.Ignore;

        var isLooseDynamic =
            !otherXform.Anchored &&
            otherBody.BodyType != BodyType.Static &&
            !isMob &&
            !isFoldable &&
            !isVehicle &&
            !isSmashable;

        if (isLooseDynamic)
            return VehicleCollisionClass.Ignore;

        if (isSmashable)
            return VehicleCollisionClass.Breakable;

        if (isFoldable && !hardCollidable)
            return VehicleCollisionClass.Ignore;

        return hardCollidable
            ? VehicleCollisionClass.Hard
            : VehicleCollisionClass.Ignore;
    }

    private static bool IsNormallyMobPassable(FixturesComponent fixtures)
    {
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!IsNormallyMobPassable(fixture))
                return false;
        }

        return true;
    }

    private static bool IsNormallyMobPassable(Fixture fixture)
    {
        const int mobMask = (int) CollisionGroup.MobMask;
        const int mobLayer = (int) CollisionGroup.MobLayer;
        const int vehicle = (int) CollisionGroup.Vehicle;

        if (fixture.Hard && ((fixture.CollisionMask & vehicle) != 0 || (fixture.CollisionLayer & vehicle) != 0))
            return false;

        return !fixture.Hard ||
               ((fixture.CollisionMask & mobLayer) == 0 &&
                (fixture.CollisionLayer & mobMask) == 0);
    }
}
