using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Vehicles.Turret;

/// <summary>Turns turrets to the gunner's aim, places them and their guns on the vehicle, and holds a shot until the barrel is on aim.</summary>
public sealed partial class VehicleTurretSystem : EntitySystem
{
    private static readonly EntProtoId TurretVisual = "VehicleTurretVisual";

    private const float PixelsPerMeter = 32f;
    private const float FireAlignmentToleranceDegrees = 2f;

    /// <summary>How many ticks a client keeps the refusals it made, longer than it ever replays.</summary>
    private const int RefusalMemoryTicks = 256;

    /// <summary>Client: the shots this client refused as the first pass of a request, by gun and tick, so a replay of the request is refused too.</summary>
    private readonly HashSet<(EntityUid Gun, GameTick Tick)> _refusedShots = new();
    private readonly Queue<(EntityUid Gun, GameTick Tick)> _refusalOrder = new();

    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private VehicleTopologySystem _topology = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(GridVehicleMoverSystem));
        SubscribeLocalEvent<VehicleTurretComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<VehicleTurretComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<VehicleTurretComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<VehicleTurretComponent, AttemptShootEvent>(OnAttemptShoot, after: new[] { typeof(GunMuzzleOffsetSystem), typeof(VehicleTurretMuzzleSystem) });
        SubscribeNetworkEvent<VehicleTurretRotateEvent>(OnRotateEvent);
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient && _timing.ApplyingState)
            return;

        var query = EntityQueryEnumerator<VehicleTurretComponent>();
        while (query.MoveNext(out var uid, out var turret))
        {
            if (!ShouldUpdateTransforms(turret))
                continue;

            if (!TryGetVehicle(uid, out var vehicle))
            {
                CleanupVisual(turret);
                continue;
            }

            UpdateTurretRotation(uid, turret, vehicle, frameTime);
        }

        query = EntityQueryEnumerator<VehicleTurretComponent>();
        while (query.MoveNext(out var uid, out var turret))
        {
            if (!ShouldUpdateTransforms(turret))
                continue;

            if (!TryGetVehicle(uid, out var vehicle))
            {
                CleanupVisual(turret);
                continue;
            }

            TryGetAnchorTurret(uid, turret, out var anchorUid, out var anchorTurret);

            EnsureVisual(uid, turret, vehicle);
            InitializeRotation(anchorUid, anchorTurret, vehicle);
            UpdateTurretTransforms(uid, turret, vehicle, anchorUid, anchorTurret);
        }
    }

    private void OnInserted(Entity<VehicleTurretComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (_net.IsClient && _timing.ApplyingState)
            return;

        if (!ShouldUpdateTransforms(ent.Comp))
            return;

        if (!TryGetVehicle(ent.Owner, out var vehicle))
            return;

        UpdateTurretRotation(ent.Owner, ent.Comp, vehicle, 0f);
        TryGetAnchorTurret(ent.Owner, ent.Comp, out var anchorUid, out var anchorTurret);

        EnsureVisual(ent.Owner, ent.Comp, vehicle);
        InitializeRotation(anchorUid, anchorTurret, vehicle);
        UpdateTurretTransforms(ent.Owner, ent.Comp, vehicle, anchorUid, anchorTurret);
    }

    private void OnRemoved(Entity<VehicleTurretComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        CleanupVisual(ent.Comp);
    }

    private void OnShutdown(Entity<VehicleTurretComponent> ent, ref ComponentShutdown args)
    {
        CleanupVisual(ent.Comp);
        ForgetRefusals(ent.Owner);
    }

    private void OnRotateEvent(VehicleTurretRotateEvent args, EntitySessionEventArgs session)
    {
        if (_net.IsClient && !_timing.IsFirstTimePredicted)
            return;

        if (_net.IsClient && _timing.ApplyingState)
            return;

        var turretUid = GetEntity(args.Turret);
        if (!TryComp(turretUid, out VehicleTurretComponent? turret))
            return;

        if (!TryGetVehicle(turretUid, out var vehicle))
            return;

        if (!_net.IsClient)
        {
            if (session.SenderSession.AttachedEntity is not { } user)
                return;

            if (TryComp(user, out VehicleViewToggleComponent? viewToggle) && !viewToggle.IsOutside)
                return;

            if (!TryComp(user, out VehicleWeaponsOperatorComponent? operatorComp) ||
                operatorComp.Vehicle != vehicle ||
                operatorComp.SelectedWeapon != turretUid)
            {
                return;
            }
        }

        if (!TryResolveRotationTarget(turretUid, turret, out var targetUid, out var targetTurret))
            return;

        if (!targetTurret.RotateToCursor)
            return;

        if (!TryGetTurretOrigin(targetUid, out var originCoords))
            return;

        // The point comes off the wire: it must be a real spot on the vehicle's map before anything is worked out from it.
        if (!TryGetAimPoint(GetCoordinates(args.Coordinates), out var targetMap) ||
            targetMap.MapId != _transform.GetMapId(vehicle))
        {
            return;
        }

        var originMap = _transform.ToMapCoordinates(originCoords);
        var direction = targetMap.Position - originMap.Position;

        if (direction.LengthSquared() <= 0.0001f)
            return;

        var vehicleRot = _transform.GetWorldRotation(vehicle);
        var desiredRotation = targetTurret.StabilizedRotation
            ? direction.ToWorldAngle()
            : (direction.ToWorldAngle() - vehicleRot).Reduced();

        SetTargetRotation(targetUid, targetTurret, vehicle, desiredRotation, allowReverseDelay: true);
    }

    /// <summary>Resolves an aim point sent by a client; false for an unknown entity or a position that is not finite.</summary>
    private bool TryGetAimPoint(EntityCoordinates coordinates, out MapCoordinates map)
    {
        map = MapCoordinates.Nullspace;

        if (!coordinates.IsValid(EntityManager))
            return false;

        map = _transform.ToMapCoordinates(coordinates);
        return float.IsFinite(map.Position.X) && float.IsFinite(map.Position.Y);
    }

    private void EnsureVisual(EntityUid turretUid, VehicleTurretComponent turret, EntityUid vehicle)
    {
        if (_net.IsClient || !turret.ShowOverlay)
            return;

        if (turret.VisualEntity is { } existing && Exists(existing))
            return;

        var visual = Spawn(TurretVisual, new EntityCoordinates(vehicle, Vector2.Zero));
        var visualComp = EnsureComp<VehicleTurretVisualComponent>(visual);
        visualComp.Turret = GetNetEntity(turretUid);
        Dirty(visual, visualComp);
        turret.VisualEntity = visual;
    }

    private void CleanupVisual(VehicleTurretComponent turret)
    {
        if (_net.IsClient)
            return;

        if (turret.VisualEntity is not { } visual)
            return;

        if (Exists(visual) && !TerminatingOrDeleted(visual) && !EntityManager.IsQueuedForDeletion(visual))
            Del(visual);

        turret.VisualEntity = null;
    }

    private void UpdateTurretTransforms(
        EntityUid turretUid,
        VehicleTurretComponent turret,
        EntityUid vehicle,
        EntityUid anchorUid,
        VehicleTurretComponent anchorTurret)
    {
        var vehicleRot = _transform.GetWorldRotation(vehicle);
        var frameRot = GetFrameRotation(vehicle);
        var screenRot = (vehicleRot - frameRot).Reduced();
        var baseFacingAngle = (GetVehicleFacingAngle(vehicle, vehicleRot) - frameRot).Reduced();
        var anchorFacingAngle = GetOffsetFacing(anchorTurret, anchorTurret, screenRot, baseFacingAngle);
        var anchorLocalOffset = ToVehicleLocal(anchorTurret, GetPixelOffset(anchorTurret, anchorFacingAngle) / PixelsPerMeter, screenRot, anchorFacingAngle);
        var localRot = Angle.Zero;
        if (anchorTurret.RotateToCursor)
            localRot = anchorTurret.WorldRotation;

        EntityCoordinates turretCoords;
        Angle turretLocalRot;
        EntityCoordinates visualCoords;
        Angle visualLocalRot;

        if (anchorUid == turretUid)
        {
            turretCoords = new EntityCoordinates(vehicle, anchorLocalOffset);
            turretLocalRot = localRot;
            visualCoords = turretCoords;
            visualLocalRot = localRot;
        }
        else
        {
            var turretFacingAngle = GetOffsetFacing(turret, anchorTurret, screenRot, baseFacingAngle);
            var worldOffset = GetPixelOffset(turret, turretFacingAngle) / PixelsPerMeter;
            Vector2 relativeAnchorOffset;
            Vector2 turretLocalOffset;

            if (turret.OffsetRotatesWithTurret)
            {
                if (turret.UseDirectionalOffsets)
                {
                    var dir = GetDirectionalDir(turretFacingAngle);
                    var directionalOffset = (turret.PixelOffset + GetDirectionalOffset(turret, dir)) / PixelsPerMeter;
                    var snappedAngle = GetDirectionalAngle(dir);
                    relativeAnchorOffset = (-snappedAngle).RotateVec(directionalOffset);
                    turretLocalOffset = (localRot - snappedAngle).RotateVec(directionalOffset);
                }
                else
                {
                    relativeAnchorOffset = worldOffset;
                    turretLocalOffset = localRot.RotateVec(relativeAnchorOffset);
                }
            }
            else
            {
                turretLocalOffset = ToVehicleLocal(turret, worldOffset, screenRot, turretFacingAngle);
                relativeAnchorOffset = (-localRot).RotateVec(turretLocalOffset);
            }
            turretCoords = new EntityCoordinates(anchorUid, relativeAnchorOffset);
            turretLocalRot = Angle.Zero;
            visualCoords = new EntityCoordinates(vehicle, anchorLocalOffset + turretLocalOffset);
            visualLocalRot = localRot;
        }

        // Only re-pose a part under the parent that holds it; moving it to another parent would pull it out of its slot.
        var turretXform = Transform(turretUid);
        if (turretCoords.EntityId == turretXform.ParentUid)
        {
            _transform.SetCoordinates(turretUid, turretXform, turretCoords);
            _transform.SetLocalRotation(turretUid, turretLocalRot, turretXform);
        }

        if (turret.VisualEntity is not { } visual || !Exists(visual))
            return;

        var visualXform = Transform(visual);
        _transform.SetCoordinates(visual, visualXform, visualCoords);
        _transform.SetLocalRotation(visual, visualLocalRot, visualXform);
    }

    /// <summary>Finds the turret that steers this one: itself, or the turret it is attached to.</summary>
    public void TryGetAnchorTurret(
        EntityUid turretUid,
        VehicleTurretComponent turret,
        out EntityUid anchorUid,
        out VehicleTurretComponent anchorTurret)
    {
        anchorUid = turretUid;
        anchorTurret = turret;

        if (!HasComp<VehicleTurretAttachmentComponent>(turretUid))
            return;

        if (!TryGetParentTurret(turretUid, out var parentUid, out var parentTurret))
            return;

        anchorUid = parentUid;
        anchorTurret = parentTurret;
    }

    /// <summary>Finds where a turret sits on its vehicle.</summary>
    public bool TryGetTurretOrigin(EntityUid turretUid, out EntityCoordinates origin)
    {
        origin = default;

        return TryGetTurretPose(turretUid, out origin, out _);
    }

    /// <summary>Finds where a turret sits on its vehicle and which way it faces.</summary>
    public bool TryGetTurretPose(
        EntityUid turretUid,
        out EntityCoordinates origin,
        out Angle worldRotation,
        VehicleTurretComponent? turret = null)
    {
        origin = default;
        worldRotation = Angle.Zero;

        if (!Resolve(turretUid, ref turret, false) ||
            !TryGetVehicle(turretUid, out var vehicle))
        {
            return false;
        }

        TryGetAnchorTurret(turretUid, turret, out var anchorUid, out var anchorTurret);

        var vehicleRot = _transform.GetWorldRotation(vehicle);
        var frameRot = GetFrameRotation(vehicle);
        var screenRot = (vehicleRot - frameRot).Reduced();
        var baseFacingAngle = (GetVehicleFacingAngle(vehicle, vehicleRot) - frameRot).Reduced();
        var anchorFacingAngle = GetOffsetFacing(anchorTurret, anchorTurret, screenRot, baseFacingAngle);
        var localOffset = ToVehicleLocal(anchorTurret, GetPixelOffset(anchorTurret, anchorFacingAngle) / PixelsPerMeter, screenRot, anchorFacingAngle);
        var localRot = anchorTurret.RotateToCursor
            ? anchorTurret.WorldRotation
            : Angle.Zero;

        if (anchorUid != turretUid)
        {
            var turretFacingAngle = GetOffsetFacing(turret, anchorTurret, screenRot, baseFacingAngle);
            var worldOffset = GetPixelOffset(turret, turretFacingAngle) / PixelsPerMeter;
            Vector2 turretLocalOffset;

            if (turret.OffsetRotatesWithTurret)
            {
                if (turret.UseDirectionalOffsets)
                {
                    var dir = GetDirectionalDir(turretFacingAngle);
                    var directionalOffset = (turret.PixelOffset + GetDirectionalOffset(turret, dir)) / PixelsPerMeter;
                    var snappedAngle = GetDirectionalAngle(dir);
                    turretLocalOffset = (localRot - snappedAngle).RotateVec(directionalOffset);
                }
                else
                {
                    turretLocalOffset = localRot.RotateVec(worldOffset);
                }
            }
            else
            {
                turretLocalOffset = ToVehicleLocal(turret, worldOffset, screenRot, turretFacingAngle);
            }

            localOffset += turretLocalOffset;
        }

        origin = new EntityCoordinates(vehicle, localOffset);
        worldRotation = (vehicleRot + localRot).Reduced();
        return true;
    }

    /// <summary>The turret's pixel offset for the way the vehicle faces.</summary>
    public Vector2 GetPixelOffset(VehicleTurretComponent turret, Angle facing)
    {
        if (!turret.UseDirectionalOffsets)
            return turret.PixelOffset;

        var baseOffset = turret.PixelOffset;
        var normalized = facing.Theta % MathHelper.TwoPi;
        if (normalized < 0)
            normalized += MathHelper.TwoPi;

        var dir = GetDirectionalDir((float) normalized);
        return baseOffset + GetDirectionalOffset(turret, dir);
    }

    private static Vector2 GetDirectionalOffset(VehicleTurretComponent turret, Direction dir)
    {
        return dir switch
        {
            Direction.South => turret.PixelOffsetSouth,
            Direction.East => turret.PixelOffsetEast,
            Direction.North => turret.PixelOffsetNorth,
            Direction.West => turret.PixelOffsetWest,
            _ => Vector2.Zero
        };
    }

    private static Direction GetDirectionalDir(Angle facing)
    {
        return VehicleTurretDirectionHelpers.GetRenderAlignedCardinalDir(facing);
    }

    private static Direction GetDirectionalDir(float normalized)
    {
        return VehicleTurretDirectionHelpers.GetRenderAlignedCardinalDir(new Angle(normalized));
    }

    private static Angle GetDirectionalAngle(Direction dir)
    {
        return dir.ToAngle();
    }

    /// <summary>
    /// The rotation of the frame the vehicle's sprite is seen in: the deck it stands on. Its crew's view is level with
    /// that deck, so the sprite's four poses, and the pixel offsets that go with them, are picked by the facing in it.
    /// </summary>
    public Angle GetFrameRotation(EntityUid vehicle)
    {
        var parent = Transform(vehicle).ParentUid;
        return parent.IsValid() ? _transform.GetWorldRotation(parent) : Angle.Zero;
    }

    /// <summary>
    /// A pixel offset in the vehicle's own axes. One given per pose is drawn with the pose, which is turned by what is
    /// left of the hull's angle, so it is turned back by the pose's angle only; a plain one is turned back by the hull's.
    /// </summary>
    public static Vector2 ToVehicleLocal(VehicleTurretComponent turret, Vector2 offset, Angle vehicleFrameRot, Angle facing)
    {
        return turret.UseDirectionalOffsets
            ? VehicleTurretDirectionHelpers.GetLocalOffsetForRenderDirection(offset, facing)
            : (-vehicleFrameRot).RotateVec(offset);
    }

    /// <summary>The way the vehicle's sprite faces in the world: the mover's own facing, turned with the frame it drives on.</summary>
    public Angle GetVehicleFacingAngle(EntityUid vehicle, Angle vehicleRot)
    {
        if (!TryComp(vehicle, out GridVehicleMoverComponent? mover) ||
            !GridVehicleMoverSystem.TryGetLocalFacing(mover, out var facing))
        {
            return vehicleRot;
        }

        var parent = Transform(vehicle).ParentUid;
        return parent.IsValid()
            ? (facing + _transform.GetWorldRotation(parent)).Reduced()
            : facing;
    }

    /// <summary>The facing the turret's offset is picked for.</summary>
    public Angle GetOffsetFacing(
        VehicleTurretComponent turret,
        VehicleTurretComponent anchorTurret,
        Angle vehicleRot,
        Angle baseFacingAngle)
    {
        if (!turret.OffsetRotatesWithTurret)
            return baseFacingAngle;

        return (vehicleRot + anchorTurret.WorldRotation).Reduced();
    }

    /// <summary>The vehicle a turret or turret gun is mounted on; one carried in a crate or by a mob on the vehicle is not.</summary>
    public bool TryGetVehicle(EntityUid turretUid, out EntityUid vehicle)
    {
        return _topology.TryGetMountedVehicle(turretUid, out vehicle);
    }

    /// <summary>Finds the turret whose rotation steers this one.</summary>
    public bool TryResolveRotationTarget(
        EntityUid turretUid,
        out EntityUid targetUid,
        out VehicleTurretComponent targetTurret)
    {
        targetUid = default;
        targetTurret = default!;

        if (!TryComp(turretUid, out VehicleTurretComponent? turret))
            return false;

        return TryResolveRotationTarget(turretUid, turret, out targetUid, out targetTurret);
    }

    /// <summary>The way the turret's barrel points in the world.</summary>
    public bool TryGetBarrelWorldRotation(
        EntityUid turretUid,
        out Angle worldRotation,
        VehicleTurretComponent? turret = null)
    {
        worldRotation = Angle.Zero;

        if (!Resolve(turretUid, ref turret, false))
            return false;

        if (!TryResolveRotationTarget(turretUid, turret, out var targetUid, out var targetTurret))
            return false;

        if (!TryGetVehicle(targetUid, out var vehicle))
            return false;

        worldRotation = (targetTurret.WorldRotation + _transform.GetWorldRotation(vehicle)).Reduced();
        return true;
    }

    /// <summary>The way a shot flies, when the turret fires along its barrel.</summary>
    public bool TryGetShotBarrelWorldRotation(
        EntityUid turretUid,
        out Angle worldRotation,
        VehicleTurretComponent? turret = null)
    {
        worldRotation = Angle.Zero;

        if (!Resolve(turretUid, ref turret, false) ||
            !turret.UseBarrelDirectionForShots)
        {
            return false;
        }

        return TryGetBarrelWorldRotation(turretUid, out worldRotation, turret);
    }

    private bool TryResolveRotationTarget(
        EntityUid turretUid,
        VehicleTurretComponent turret,
        out EntityUid targetUid,
        out VehicleTurretComponent targetTurret)
    {
        targetUid = turretUid;
        targetTurret = turret;

        if (!HasComp<VehicleTurretAttachmentComponent>(turretUid))
            return true;

        if (!TryGetParentTurret(turretUid, out var parentUid, out var parentTurret))
            return true;

        targetUid = parentUid;
        targetTurret = parentTurret;
        return true;
    }

    /// <summary>Finds the turret that holds this one in a slot.</summary>
    public bool TryGetParentTurret(
        EntityUid turretUid,
        out EntityUid parentUid,
        out VehicleTurretComponent parentTurret)
    {
        parentUid = default;
        parentTurret = default!;
        var current = turretUid;

        while (_container.TryGetContainingContainer((current, null), out var container))
        {
            var owner = container.Owner;
            if (TryComp(owner, out VehicleTurretComponent? turret))
            {
                parentUid = owner;
                parentTurret = turret;
                return true;
            }

            current = owner;
        }

        return false;
    }

    /// <summary>Turns the turret to a target entity; false when it cannot.</summary>
    public bool TryAimAtTarget(EntityUid turretUid, EntityUid target, out EntityCoordinates targetCoords)
    {
        targetCoords = default;

        if (!TryResolveRotationTarget(turretUid, out var targetUid, out var targetTurret))
            return false;

        if (!targetTurret.RotateToCursor)
            return false;

        if (!TryGetVehicle(targetUid, out var vehicle))
            return false;

        if (!TryGetTurretOrigin(targetUid, out var originCoords))
            return false;

        targetCoords = Transform(target).Coordinates;
        var originMap = _transform.ToMapCoordinates(originCoords);
        var targetMap = _transform.ToMapCoordinates(targetCoords);
        var direction = targetMap.Position - originMap.Position;
        if (direction.LengthSquared() <= 0.0001f)
            return false;

        var vehicleRot = _transform.GetWorldRotation(vehicle);
        var desiredRotation = targetTurret.StabilizedRotation
            ? direction.ToWorldAngle()
            : (direction.ToWorldAngle() - vehicleRot).Reduced();

        targetTurret.TargetRotation = desiredRotation;
        if (targetTurret.RotationSpeed <= 0f)
        {
            var desiredLocal = targetTurret.StabilizedRotation
                ? (desiredRotation - vehicleRot).Reduced()
                : desiredRotation;
            targetTurret.WorldRotation = desiredLocal;
        }

        Dirty(targetUid, targetTurret);
        UpdateTurretTransforms(targetUid, targetTurret, vehicle, targetUid, targetTurret);
        return true;
    }

    /// <summary>Sets the turret's target rotation from a world angle.</summary>
    public bool TrySetTargetRotationWorld(EntityUid turretUid, Angle worldRotation)
    {
        if (!TryResolveRotationTarget(turretUid, out var targetUid, out var targetTurret))
            return false;

        if (!TryGetVehicle(targetUid, out var vehicle))
            return false;

        var vehicleRot = _transform.GetWorldRotation(vehicle);
        var desiredRotation = targetTurret.StabilizedRotation
            ? worldRotation
            : (worldRotation - vehicleRot).Reduced();

        targetTurret.TargetRotation = desiredRotation;
        if (targetTurret.RotationSpeed <= 0f)
        {
            var desiredLocal = targetTurret.StabilizedRotation
                ? (desiredRotation - vehicleRot).Reduced()
                : desiredRotation;
            targetTurret.WorldRotation = desiredLocal;
        }

        Dirty(targetUid, targetTurret);
        UpdateTurretTransforms(targetUid, targetTurret, vehicle, targetUid, targetTurret);
        return true;
    }

    private void InitializeRotation(EntityUid turretUid, VehicleTurretComponent turret, EntityUid vehicle)
    {
        if (_net.IsClient)
            return;

        if ((!turret.RotateToCursor && !turret.ShowOverlay) || turret.WorldRotation != Angle.Zero)
        {
            if (turret.TargetRotation == Angle.Zero && turret.WorldRotation != Angle.Zero)
            {
                var vehicleRot = _transform.GetWorldRotation(vehicle);
                turret.TargetRotation = turret.StabilizedRotation
                    ? (turret.WorldRotation + vehicleRot).Reduced()
                    : turret.WorldRotation;
                Dirty(turretUid, turret);
            }
            return;
        }

        var baseWorld = _transform.GetWorldRotation(vehicle);
        turret.WorldRotation = Angle.Zero;
        turret.TargetRotation = turret.StabilizedRotation ? baseWorld : Angle.Zero;
        turret.LastVehicleRotation = baseWorld;
        turret.LastVehicleRotationValid = true;
        Dirty(turretUid, turret);
    }

    private void UpdateTurretRotation(EntityUid turretUid, VehicleTurretComponent turret, EntityUid vehicle, float frameTime)
    {
        if (TryComp(vehicle, out WFVehicleEngineComponent? engine) && !engine.Powered)
        {
            // A powerless stabilizer follows the hull; restarting must not undo all intervening hull rotation.
            turret.LastVehicleRotationValid = false;
            return;
        }
        var vehicleRot = _transform.GetWorldRotation(vehicle);

        if (turret.StabilizedRotation && turret.RotationSpeed > 0f &&
            (!_net.IsClient || IsLocallyOperatedTurret(turretUid) || IsVehiclePredictedForLocalPlayer(vehicle)))
        {
            var dirty = false;

            if (turret.LastVehicleRotationValid)
            {
                var rotDelta = Angle.ShortestDistance(turret.LastVehicleRotation, vehicleRot);
                if (rotDelta.Theta != 0.0)
                {
                    turret.WorldRotation = (turret.WorldRotation - rotDelta).Reduced();
                    dirty = true;
                }
            }

            if (turret.LastVehicleRotation != vehicleRot || !turret.LastVehicleRotationValid)
            {
                turret.LastVehicleRotation = vehicleRot;
                turret.LastVehicleRotationValid = true;
                dirty = true;
            }

            if (dirty)
                Dirty(turretUid, turret);
        }

        if (!turret.RotateToCursor)
            return;

        if (_net.IsClient && !IsLocallyOperatedTurret(turretUid))
            return;

        ApplyPendingTargetRotation(turretUid, turret, vehicle);

        if (turret.TargetRotation == Angle.Zero && turret.WorldRotation != Angle.Zero)
        {
            turret.TargetRotation = turret.StabilizedRotation
                ? (turret.WorldRotation + vehicleRot).Reduced()
                : turret.WorldRotation;
            Dirty(turretUid, turret);
            return;
        }

        var target = turret.StabilizedRotation
            ? (turret.TargetRotation - vehicleRot).Reduced()
            : turret.TargetRotation;

        if (turret.RotationSpeed <= 0f)
        {
            if (turret.WorldRotation != target)
            {
                turret.WorldRotation = target;
                Dirty(turretUid, turret);
            }

            return;
        }

        var delta = Angle.ShortestDistance(turret.WorldRotation, target);
        var maxStep = MathHelper.DegreesToRadians(turret.RotationSpeed) * frameTime;
        if (Math.Abs(delta.Theta) <= maxStep)
        {
            if (turret.WorldRotation != target)
            {
                turret.WorldRotation = target;
                Dirty(turretUid, turret);
            }

            return;
        }

        var step = Math.Sign(delta.Theta) * maxStep;
        var next = (turret.WorldRotation + step).Reduced();
        if (next != turret.WorldRotation)
        {
            turret.WorldRotation = next;
            Dirty(turretUid, turret);
        }
    }

    private void OnAttemptShoot(Entity<VehicleTurretComponent> ent, ref AttemptShootEvent args)
    {
        if (TryGetVehicle(ent.Owner, out var vehicle) && TryComp(vehicle, out WFVehicleEngineComponent? engine) && !engine.Powered)
        {
            args.Cancelled = true;
            args.ResetCooldown = true;
            return;
        }
        if (!_net.IsClient)
        {
            CheckShot(ent, ref args);
            return;
        }

        // A replay judges the aim against a rewound turret, so it repeats what the first pass of the same request decided.
        if (!_timing.IsFirstTimePredicted)
        {
            if (_refusedShots.Contains((ent.Owner, _timing.CurTick)))
            {
                args.Cancelled = true;
                args.ResetCooldown = true;
            }

            return;
        }

        var wasCancelled = args.Cancelled;
        CheckShot(ent, ref args);

        if (!wasCancelled && args.Cancelled)
            RememberRefusal(ent.Owner);
    }

    private void RememberRefusal(EntityUid gun)
    {
        var key = (gun, _timing.CurTick);
        if (!_refusedShots.Add(key))
            return;

        _refusalOrder.Enqueue(key);

        while (_refusalOrder.TryPeek(out var oldest) &&
               oldest.Tick.Value + RefusalMemoryTicks < _timing.CurTick.Value)
        {
            _refusalOrder.Dequeue();
            _refusedShots.Remove(oldest);
        }
    }

    private void ForgetRefusals(EntityUid gun)
    {
        if (_refusedShots.Count == 0)
            return;

        _refusedShots.RemoveWhere(key => key.Gun == gun);
    }

    private void CheckShot(Entity<VehicleTurretComponent> ent, ref AttemptShootEvent args)
    {
        if (args.Cancelled)
            return;

        if (!IsTurretFunctional(ent.Owner))
        {
            args.Cancelled = true;
            args.ResetCooldown = true;
            return;
        }

        if (!CanOperatorUseTurret(ent.Owner, args.User))
        {
            args.Cancelled = true;
            args.ResetCooldown = true;
            return;
        }

        if (!TryResolveRotationTarget(ent.Owner, ent.Comp, out var targetUid, out var targetTurret))
            return;

        if (!targetTurret.RotateToCursor)
            return;

        var alignmentTolerance = MathHelper.DegreesToRadians(
            MathF.Max(FireAlignmentToleranceDegrees + ent.Comp.FireWhileRotatingGraceDegrees, 0f));

        if (!TryGetVehicle(targetUid, out var vehicle))
            return;

        var vehicleRot = _transform.GetWorldRotation(vehicle);

        var aimChecked = false;
        if (args.ToCoordinates != null &&
            TryGetTurretOrigin(targetUid, out var originCoords))
        {
            if (!TryGetAimPoint(args.ToCoordinates.Value, out var targetMap))
            {
                args.Cancelled = true;
                args.ResetCooldown = true;
                return;
            }

            var originMap = _transform.ToMapCoordinates(originCoords);
            var direction = targetMap.Position - originMap.Position;
            if (direction.LengthSquared() > 0.0001f)
            {
                aimChecked = true;
                var desiredWorldRotation = direction.ToWorldAngle();
                var currentWorldRotation = (targetTurret.WorldRotation + vehicleRot).Reduced();
                var desiredDelta = Angle.ShortestDistance(currentWorldRotation, desiredWorldRotation);
                if (Math.Abs(desiredDelta.Theta) > alignmentTolerance)
                {
                    args.Cancelled = true;
                    args.ResetCooldown = true;
                    return;
                }
            }
        }

        if (!aimChecked)
        {
            var worldRotation = (targetTurret.WorldRotation + vehicleRot).Reduced();
            var targetWorldRotation = targetTurret.StabilizedRotation
                ? targetTurret.TargetRotation
                : (targetTurret.TargetRotation + vehicleRot).Reduced();

            var delta = Angle.ShortestDistance(worldRotation, targetWorldRotation);
            if (Math.Abs(delta.Theta) > alignmentTolerance)
            {
                args.Cancelled = true;
                args.ResetCooldown = true;
                return;
            }
        }

        ApplyShotDirectionConstraint(ent.Comp, targetUid, targetTurret, vehicle, ref args);

        if (args.ToCoordinates is { } finalTarget && TryComp(ent.Owner, out GunComponent? gun))
        {
#pragma warning disable RA0002
            gun.ShootCoordinates = finalTarget;
#pragma warning restore RA0002
        }
    }

    private bool IsTurretFunctional(EntityUid turretUid)
    {
        if (TryComp(turretUid, out HardpointIntegrityComponent? integrity) &&
            integrity.Integrity <= 0f)
        {
            return false;
        }

        if (!HasComp<VehicleTurretAttachmentComponent>(turretUid))
            return true;

        if (!TryGetParentTurret(turretUid, out var parentUid, out _))
            return true;

        return !TryComp(parentUid, out HardpointIntegrityComponent? parentIntegrity) || parentIntegrity.Integrity > 0f;
    }

    private void ApplyShotDirectionConstraint(
        VehicleTurretComponent sourceTurret,
        EntityUid rotationTurretUid,
        VehicleTurretComponent rotationTurret,
        EntityUid vehicle,
        ref AttemptShootEvent args)
    {
        var muzzleMap = _transform.ToMapCoordinates(args.FromCoordinates);
        if (args.ToCoordinates is not { } currentTarget)
            return;

        var targetMap = _transform.ToMapCoordinates(currentTarget);
        if (targetMap.MapId != muzzleMap.MapId)
            return;

        var vehicleRot = _transform.GetWorldRotation(vehicle);
        var barrelWorldRotation = (rotationTurret.WorldRotation + vehicleRot).Reduced();

        const float MinBarrelForwardDist = 0.5f;
        var barrelDir = barrelWorldRotation.ToWorldVec();
        var forwardDist = Vector2.Dot(targetMap.Position - muzzleMap.Position, barrelDir);
        if (forwardDist < MinBarrelForwardDist)
        {
            targetMap = new MapCoordinates(muzzleMap.Position + barrelDir * MinBarrelForwardDist, targetMap.MapId);
            args = args with { ToCoordinates = _transform.ToCoordinates(targetMap) };
        }

        var maxCurveDegrees = MathF.Max(0f, sourceTurret.MaxShotCurvatureDegrees);
        if (!sourceTurret.UseBarrelDirectionForShots && maxCurveDegrees <= 0f)
            return;

        var shotWorldRotation = barrelWorldRotation;

        if (!sourceTurret.UseBarrelDirectionForShots && maxCurveDegrees > 0f)
        {
            var desiredWorldRotation = (targetMap.Position - muzzleMap.Position).ToWorldAngle();
            var maxCurveRadians = MathHelper.DegreesToRadians(maxCurveDegrees);
            var delta = Angle.ShortestDistance(barrelWorldRotation, desiredWorldRotation);
            var clamped = MathHelper.Clamp((float) delta.Theta, -maxCurveRadians, maxCurveRadians);
            shotWorldRotation = (barrelWorldRotation + clamped).Reduced();
        }

        var shotOriginMap = muzzleMap;

        var distance = (targetMap.Position - shotOriginMap.Position).Length();
        if (distance <= 0.0001f)
            return;

        var adjustedMap = new MapCoordinates(
            shotOriginMap.Position + shotWorldRotation.ToWorldVec() * distance,
            muzzleMap.MapId);
        var adjustedTarget = _transform.ToCoordinates(adjustedMap);
        args = args with { ToCoordinates = adjustedTarget };
    }

    private void ApplyPendingTargetRotation(EntityUid turretUid, VehicleTurretComponent turret, EntityUid vehicle)
    {
        if (turret.PendingTargetRotation is not { } pending)
            return;

        if (_timing.CurTime < turret.PendingTargetApplyAt)
            return;

        turret.PendingTargetRotation = null;
        turret.PendingTargetApplyAt = TimeSpan.Zero;

        var sign = turret.PendingDirectionSign;
        turret.PendingDirectionSign = 0;

        ApplyTargetRotation(turretUid, turret, vehicle, pending, sign);
    }

    private void SetTargetRotation(
        EntityUid turretUid,
        VehicleTurretComponent turret,
        EntityUid vehicle,
        Angle desiredRotation,
        bool allowReverseDelay)
    {
        var delta = Angle.ShortestDistance(turret.TargetRotation, desiredRotation);
        var deadzone = MathHelper.DegreesToRadians(MathF.Max(0f, turret.RotationInputDeadzoneDegrees));

        if (Math.Abs(delta.Theta) <= deadzone)
            return;

        var directionSign = Math.Sign(delta.Theta);

        if (allowReverseDelay &&
            turret.ReverseDirectionDelay > 0f &&
            directionSign != 0 &&
            turret.LastAppliedDirectionSign != 0 &&
            directionSign != turret.LastAppliedDirectionSign)
        {
            if (turret.PendingTargetRotation == null || turret.PendingDirectionSign != directionSign)
                turret.PendingTargetApplyAt = _timing.CurTime + TimeSpan.FromSeconds(turret.ReverseDirectionDelay);

            turret.PendingTargetRotation = desiredRotation;
            turret.PendingDirectionSign = directionSign;
            return;
        }

        turret.PendingTargetRotation = null;
        turret.PendingTargetApplyAt = TimeSpan.Zero;
        turret.PendingDirectionSign = 0;
        ApplyTargetRotation(turretUid, turret, vehicle, desiredRotation, directionSign);
    }

    private void ApplyTargetRotation(
        EntityUid turretUid,
        VehicleTurretComponent turret,
        EntityUid vehicle,
        Angle desiredRotation,
        int directionSign)
    {
        var changed = false;

        if (turret.TargetRotation != desiredRotation)
        {
            turret.TargetRotation = desiredRotation;
            changed = true;
        }

        if (directionSign != 0)
            turret.LastAppliedDirectionSign = directionSign;

        if (turret.RotationSpeed <= 0f)
        {
            var vehicleRot = _transform.GetWorldRotation(vehicle);
            var desiredLocal = turret.StabilizedRotation
                ? (desiredRotation - vehicleRot).Reduced()
                : desiredRotation;

            if (turret.WorldRotation != desiredLocal)
            {
                turret.WorldRotation = desiredLocal;
                changed = true;
            }
        }

        if (changed)
            Dirty(turretUid, turret);
    }

    private bool IsVehiclePredictedForLocalPlayer(EntityUid vehicle)
    {
        return _player.LocalEntity is { } local &&
               TryComp(vehicle, out RMCVehicleComponent? vehicleComp) &&
               vehicleComp.Operator == local;
    }

    private bool IsLocallyOperatedTurret(EntityUid turretUid)
    {
        if (_player.LocalEntity is not { } local)
            return false;

        if (!TryComp(local, out VehicleWeaponsOperatorComponent? operatorComp) ||
            operatorComp.SelectedWeapon is not { } selectedWeapon)
        {
            return false;
        }

        return TryResolveRotationTarget(selectedWeapon, out var targetUid, out _) &&
               targetUid == turretUid;
    }

    private bool CanOperatorUseTurret(EntityUid turretUid, EntityUid user)
    {
        if (!TryGetVehicle(turretUid, out var vehicle))
            return false;

        if (!TryComp(user, out VehicleWeaponsOperatorComponent? operatorComp) ||
            operatorComp.Vehicle != vehicle)
            return true;

        if (operatorComp.SelectedWeapon != turretUid)
            return false;

        if (TryComp(user, out VehicleViewToggleComponent? viewToggle) && !viewToggle.IsOutside)
            return false;

        return true;
    }

    private static bool ShouldUpdateTransforms(VehicleTurretComponent turret)
    {
        if (turret.RotateToCursor || turret.ShowOverlay || turret.UseDirectionalOffsets)
            return true;

        return turret.PixelOffset != Vector2.Zero ||
               turret.PixelOffsetNorth != Vector2.Zero ||
               turret.PixelOffsetEast != Vector2.Zero ||
               turret.PixelOffsetSouth != Vector2.Zero ||
               turret.PixelOffsetWest != Vector2.Zero;
    }
}

/// <summary>Sent by a gunner's client: aim the turret that holds this weapon at these coordinates.</summary>
[Serializable, NetSerializable]
public sealed class VehicleTurretRotateEvent : EntityEventArgs
{
    public NetEntity Turret;
    public NetCoordinates Coordinates;
}
