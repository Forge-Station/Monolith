using System;
using System.Numerics;
using System.Collections.Generic;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Destructible;
using Content.Shared.Doors.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Content.Shared.StepTrigger.Components;
using Content.Shared.Stunnable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.GameTicking;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Shared._WF.Vehicles.Grid;

/// <summary>Drives grid vehicles: integrates speed along a grid-local cardinal facing and rewrites the transform each fixed step.</summary>
public sealed partial class GridVehicleMoverSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDestructibleSystem _destructible = default!;
    [Dependency] private SharedDoorSystem _door = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private EntityQuery<MapGridComponent> gridQ;
    private EntityQuery<PhysicsComponent> physicsQ;
    private EntityQuery<FixturesComponent> fixtureQ;

    private const float Clearance = PhysicsConstants.PolygonRadius * 0.75f;
    private const double MobCollisionDamage = 8;

    /// <summary>Further damage to someone run over, per tile a second of the vehicle's speed.</summary>
    private const double MobCollisionDamagePerSpeed = 2;

    /// <summary>The most a single run-over does, whatever the speed and weight.</summary>
    private const double MobCollisionMaxDamage = 60;
    private static readonly TimeSpan MobCollisionKnockdown = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan MobCollisionCooldown = TimeSpan.FromSeconds(0.75);
    private const float MobCollisionMinKnockdownSpeed = 1.5f;
    private static readonly ProtoId<DamageTypePrototype> CollisionDamageType = "Blunt";
    private const int GridVehicleStaticBlockerMask =
        (int) (CollisionGroup.Impassable |
               CollisionGroup.HighImpassable |
               CollisionGroup.LowImpassable |
               CollisionGroup.MidImpassable);
    private const float AdoptMoveDistance = 0.5f;
    private const float MovementFixedStep = 1f / 60f;
    private const int MaxFixedStepsPerFrame = 6;


    public static readonly List<(EntityUid grid, Vector2i tile)> DebugTestedTiles = new();
    public static readonly List<DebugCollisionProbe> DebugCollisionProbes = new();
    public static readonly List<DebugCollision> DebugCollisions = new();
    public static readonly List<DebugMovementDecision> DebugMovementDecisions = new();
    public static bool CollisionDebugEnabled { get; set; }
    public static bool MovementDebugEnabled { get; set; }

    private readonly HashSet<EntityUid> _intersecting = new();
    private readonly List<EntityUid>[] _hitsBuffers = { new(), new(), new() };
    private int _hitsDepth;
    private readonly Dictionary<EntityUid, TimeSpan> _lastMobCollision = new();
    private readonly Dictionary<EntityUid, float> _movementAccumulator = new();
    private readonly HashSet<EntityUid>[] _pushIgnoredBuffers = { new(), new(), new() };
    private readonly HashSet<EntityUid> _bypassInitialBlockers = new();
    private readonly HashSet<EntityUid> _bypassSampleBlockers = new();

    /// <summary>How the mover treats something its probe meets.</summary>
    private enum VehicleCollisionClass : byte
    {
        Ignore = 0,
        SoftMob = 1,
        Breakable = 2,
        Hard = 3,
    }

    /// <summary>What the movement debug overlay reports for a drive decision.</summary>
    public enum DebugMovementDecisionKind : byte
    {
        DirectClear = 0,
        DirectBlocked = 1,
        LaneCorrection = 2,
        LaneCorrectionFailed = 3,
        ForwardAfterCorrection = 4,
        ForwardBlocked = 5,
    }

    /// <summary>A blocking collision, kept for the debug overlay.</summary>
    public readonly record struct DebugCollision(
        EntityUid Tested,
        EntityUid Blocker,
        Box2 TestedAabb,
        Box2 BlockerAabb,
        float Distance,
        float Skin,
        float Clearance,
        MapId Map);

    /// <summary>One collision probe of a step, kept for the debug overlay.</summary>
    public readonly record struct DebugCollisionProbe(
        EntityUid Tested,
        Box2 TestedAabb,
        Box2 MovementAabb,
        Box2Rotated FixtureBounds,
        Box2Rotated MovementBounds,
        Vector2 Position,
        Angle Rotation,
        bool Blocked,
        bool ApplyEffects,
        MapId Map);

    /// <summary>One movement decision of a step, kept for the debug overlay.</summary>
    public readonly record struct DebugMovementDecision(
        EntityUid Vehicle,
        EntityUid Grid,
        Vector2 Start,
        Vector2 End,
        Vector2 MoveDirection,
        DebugMovementDecisionKind Kind,
        bool Success);

    public override void Initialize()
    {
        base.Initialize();
        gridQ = GetEntityQuery<MapGridComponent>();
        physicsQ = GetEntityQuery<PhysicsComponent>();
        fixtureQ = GetEntityQuery<FixturesComponent>();
        SubscribeLocalEvent<GridVehicleMoverComponent, ComponentStartup>(OnMoverStartup);
        SubscribeLocalEvent<GridVehicleMoverComponent, ComponentShutdown>(OnMoverShutdown);
        SubscribeLocalEvent<GridVehicleMoverComponent, MoveEvent>(OnMoverMove);
        SubscribeLocalEvent<GridVehicleMoverComponent, ReAnchorEvent>(OnMoverReAnchor);
        SubscribeLocalEvent<GridVehicleMoverComponent, PreventCollideEvent>(OnMoverPreventCollide);
        SubscribeLocalEvent<GridVehicleMoverComponent, ThrownEvent>(OnMoverThrown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        InitializeSteeringMode();
    }

    /// <summary>Mobs the mover is holding a run-over cooldown for; emptied when the round restarts.</summary>
    public int TrackedMobCollisions => _lastMobCollision.Count;

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _lastMobCollision.Clear();
        _movementAccumulator.Clear();
        _plowContacts.Clear();
        _plowCooldowns.Clear();
    }

    private void OnMoverThrown(Entity<GridVehicleMoverComponent> ent, ref ThrownEvent args)
    {
        // A throw has already turned the body to face where it goes, which a body that cannot spin gets instead of spin.
        var xform = Transform(ent);
        if (xform.ParentUid == xform.GridUid)
        {
            if (TryGetLocalFacing(ent.Comp, out var facing))
                _transform.SetLocalRotation(ent, facing, xform);
            else if (ent.Comp.RestRotation is { } rest)
                _transform.SetLocalRotation(ent, rest, xform);
        }

        // The throw applies its impulse and body status after this event, so StabiliseBody settles the body on the next step.
        RemCompDeferred<ThrownItemComponent>(ent);
    }

    private void OnMoverStartup(Entity<GridVehicleMoverComponent> ent, ref ComponentStartup args)
    {
        // Physics shoves move the origin too, and the engine would hand the vehicle to whatever lies under it; the mover
        // does that itself after its own writes (see CheckGridTraversal).
        Transform(ent).GridTraversal = false;
        TrySyncMoverToCurrentGrid(ent, centerOnTile: false, force: true);
    }

    private void OnMoverShutdown(Entity<GridVehicleMoverComponent> ent, ref ComponentShutdown args)
    {
        _movementAccumulator.Remove(ent.Owner);

        if (!_net.IsClient)
            RemoveSteeringAction(ent.Comp, null);

        if (!TerminatingOrDeleted(ent))
            Transform(ent).GridTraversal = true;
    }

    private void OnMoverMove(Entity<GridVehicleMoverComponent> ent, ref MoveEvent args)
    {
        if (!args.ParentChanged)
            return;

        TrySyncMoverToCurrentGrid(ent, centerOnTile: false);
    }

    private void OnMoverReAnchor(Entity<GridVehicleMoverComponent> ent, ref ReAnchorEvent args)
    {
        TrySyncMoverToCurrentGrid(ent, centerOnTile: false);
    }

    private bool TrySyncMoverToCurrentGrid(
        Entity<GridVehicleMoverComponent> ent,
        bool centerOnTile,
        TransformComponent? xform = null,
        bool force = false)
    {
        var uid = ent.Owner;
        xform ??= Transform(uid);

        if (xform.GridUid is not { } grid || !gridQ.TryComp(grid, out var gridComp))
        {
            if (ent.Comp.SyncedGrid == null)
                return false;

            ent.Comp.SyncedGrid = null;
            ent.Comp.CurrentSpeed = 0f;
            ent.Comp.AngularVelocityDegrees = 0f;
            ent.Comp.IsCommittedToMove = false;
            ent.Comp.IsMoving = false;
            _movementAccumulator[uid] = 0f;
            Dirty(uid, ent.Comp);
            return true;
        }

        if (!force && ent.Comp.SyncedGrid == grid)
            return false;

        var coords = _transform.WithEntityId(xform.Coordinates, grid);
        var tile = _map.TileIndicesFor(grid, gridComp, coords);

        if (ent.Comp.SyncedGrid is { } previousGrid && !Deleted(previousGrid))
        {
            var turn = _transform.GetWorldRotation(previousGrid) - _transform.GetWorldRotation(grid);
            ent.Comp.TravelVelocity = turn.RotateVec(ent.Comp.TravelVelocity);
            ent.Comp.ImpactVelocity = turn.RotateVec(ent.Comp.ImpactVelocity);
        }
        ent.Comp.SyncedGrid = grid;
        ent.Comp.CurrentTile = tile;
        ent.Comp.TargetTile = tile;
        ent.Comp.Position = centerOnTile
            ? new Vector2(tile.X + 0.5f, tile.Y + 0.5f)
            : coords.Position;
        ent.Comp.TargetPosition = ent.Comp.Position;

        // Speed carries over to the new grid; only the facing, which is relative to the grid, is read again.
        RederiveFacing(ent, xform, grid);
        if (ent.Comp.CurrentDirection == Vector2i.Zero && xform.ParentUid == grid)
            ent.Comp.RestRotation = xform.LocalRotation;

        ent.Comp.NextTurnTime = TimeSpan.Zero;
        ent.Comp.InPlaceTurnBlockUntil = TimeSpan.Zero;
        ent.Comp.IsCommittedToMove = false;
        ent.Comp.IsMoving = MathF.Abs(ent.Comp.CurrentSpeed) > MinVehicleSpeed;
        _movementAccumulator[uid] = 0f;

        Dirty(uid, ent.Comp);
        return true;
    }

    /// <summary>Reads the facing back from the transform in the grid's frame, so a vehicle that changes grid keeps its heading in the world.</summary>
    private void RederiveFacing(Entity<GridVehicleMoverComponent> ent, TransformComponent xform, EntityUid grid)
    {
        if (ent.Comp.CurrentDirection == Vector2i.Zero)
            return;

        var rotation = xform.ParentUid == grid
            ? xform.LocalRotation
            : _transform.GetWorldRotation(ent.Owner) - _transform.GetWorldRotation(grid);
        var facing = GridVehicleGeometry.CardinalFromRotation(rotation);
        ent.Comp.CurrentDirection = facing;

        // A freely steered vehicle keeps the exact heading it had in the world.
        if (ent.Comp.FreeSteering)
        {
            ent.Comp.Heading = rotation.Reduced();
            return;
        }

        if (xform.ParentUid != grid)
            return;

        var snapped = DirectionToVehicleRotation(facing);
        if (!xform.LocalRotation.EqualsApprox(snapped))
            _transform.SetLocalRotation(ent.Owner, snapped, xform);
    }

    private void OnMoverPreventCollide(Entity<GridVehicleMoverComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryComp(ent.Owner, out RMCVehicleComponent? vehicle) || vehicle.MovementKind != VehicleMovementKind.Grid)
            return;

        if (args.OtherEntity == ent.Owner)
            return;

        // Vehicles meet through the mover, which blocks or pushes; the solver would only shove them off their lanes.
        if (HasComp<GridVehicleMoverComponent>(args.OtherEntity))
        {
            args.Cancelled = true;
            return;
        }

        if (HasComp<MobStateComponent>(args.OtherEntity) &&
            (_mobState.IsDead(args.OtherEntity) || _standing.IsDown(args.OtherEntity)))
        {
            args.Cancelled = true;
            return;
        }

        // What the mover smashes, or is stopped by instead, is the mover's to deal with. A loose locker the solver shoves
        // ahead of the vehicle is never reached by the mover, so it would never be smashed.
        if (TryGetSmashable(args.OtherEntity, out _))
        {
            args.Cancelled = true;
            return;
        }

        if (args.OtherBody.BodyType != BodyType.Static)
            return;

        if (!args.OtherFixture.Hard && HasComp<StepTriggerComponent>(args.OtherEntity))
            return;

        if (IsNormallyMobPassable(args.OtherFixture))
        {
            args.Cancelled = true;
            return;
        }

        if ((args.OtherFixture.CollisionLayer & GridVehicleStaticBlockerMask) == 0)
            return;

        args.Cancelled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (CollisionDebugEnabled)
        {
            DebugTestedTiles.Clear();
            DebugCollisionProbes.Clear();
            DebugCollisions.Clear();
        }

        if (MovementDebugEnabled)
            DebugMovementDecisions.Clear();

        _doorAccess.Clear();

        var q = EntityQueryEnumerator<GridVehicleMoverComponent, RMCVehicleComponent, TransformComponent>();

        while (q.MoveNext(out var uid, out var mover, out var vehicle, out var xform))
        {
            if (vehicle.MovementKind != VehicleMovementKind.Grid)
                continue;

            TrySyncMoverToCurrentGrid((uid, mover), centerOnTile: false, xform);

            if (_net.IsClient && !ShouldPredictVehicleMovement(vehicle))
                continue;

            if (!_net.IsClient)
                AdoptExternalMove((uid, mover), xform);

            if (xform.GridUid is not { } gridUid || !gridQ.HasComp(gridUid))
            {
                // No floor under it, so no step runs; it still must not drift off with a throw's velocity, and a
                // vehicle set down on the map over a deck joins the deck.
                StabiliseBody(uid);
                CheckGridTraversal(uid, xform);
                continue;
            }

            var input = GetMoverInput(uid, vehicle, xform);
            var accumulator = _movementAccumulator.GetValueOrDefault(uid) + frameTime;
            var maxAccum = MovementFixedStep * MaxFixedStepsPerFrame;
            if (accumulator > maxAccum)
                accumulator = maxAccum;

            var steps = 0;
            while (accumulator >= MovementFixedStep && steps < MaxFixedStepsPerFrame)
            {
                var currentXform = Transform(uid);
                TrySyncMoverToCurrentGrid((uid, mover), centerOnTile: false, currentXform);
                if (currentXform.GridUid is not { } currentGrid || !gridQ.TryComp(currentGrid, out var currentGridComp))
                    break;

                UpdateMovement(uid, mover, vehicle, currentGrid, currentGridComp, input, MovementFixedStep);
                accumulator -= MovementFixedStep;
                steps++;
            }

            _movementAccumulator[uid] = accumulator;
        }
    }

    /// <summary>A vehicle put elsewhere on its own grid by something other than the mover keeps the place it was put.</summary>
    private void AdoptExternalMove(Entity<GridVehicleMoverComponent> ent, TransformComponent xform)
    {
        if (ent.Comp.SyncedGrid is not { } grid || xform.ParentUid != grid)
            return;

        if ((xform.LocalPosition - ent.Comp.Position).LengthSquared() < AdoptMoveDistance * AdoptMoveDistance)
            return;

        if (physicsQ.TryComp(ent, out var body) && body.LinearVelocity != Vector2.Zero)
            return;

        TrySyncMoverToCurrentGrid(ent, centerOnTile: false, xform, force: true);
        ent.Comp.CurrentSpeed = 0f;
        ent.Comp.AngularVelocityDegrees = 0f;
        ent.Comp.IsMoving = false;
    }

    private bool ShouldPredictVehicleMovement(RMCVehicleComponent vehicle)
    {
        if (!_net.IsClient)
            return true;

        if (!_timing.InPrediction)
            return false;

        return vehicle.Operator != null && vehicle.Operator == _player.LocalEntity;
    }
}
