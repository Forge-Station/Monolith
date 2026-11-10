using System.Numerics;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Turret;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>Moves the shot origin of a gun from its mount to its muzzle, along the barrel for a gun on a turret.</summary>
public sealed partial class GunMuzzleOffsetSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private VehicleTurretSystem _vehicleTurret = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<GunMuzzleOffsetComponent, AttemptShootEvent>(OnAttemptShoot);
    }

    private void OnAttemptShoot(Entity<GunMuzzleOffsetComponent> ent, ref AttemptShootEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryGetMuzzleCoordinates(ent.Owner, ent.Comp, args.ToCoordinates, out var fromCoords, out _))
            return;

        args.FromCoordinates = fromCoords;
    }

    /// <summary>Where a gun's shot leaves, with its muzzle offset applied.</summary>
    public bool TryGetMuzzleCoordinates(
        EntityUid uid,
        GunMuzzleOffsetComponent component,
        EntityCoordinates? toCoordinates,
        out EntityCoordinates muzzleCoords,
        out Angle muzzleRotation)
    {
        muzzleCoords = default;
        muzzleRotation = Angle.Zero;

        if (component.Offset == Vector2.Zero &&
            component.MuzzleOffset == Vector2.Zero &&
            !component.UseDirectionalOffsets)
        {
            return false;
        }

        var baseUid = uid;
        if (component.UseContainerOwner &&
            _container.TryGetContainingContainer((uid, null), out var container))
        {
            baseUid = container.Owner;
        }

        // A turret gun starts from the turret's pose, and from its barrel's rotation when it shoots along the barrel.
        var hasTurretPose = _vehicleTurret.TryGetTurretPose(uid, out var turretCoords, out var turretRotation);
        var useShotRotation = _vehicleTurret.TryGetShotBarrelWorldRotation(uid, out var shotRotation);
        var useBarrelDirectionOffsets = hasTurretPose || useShotRotation;

        // Offsets are applied in the frame the coordinates are relative to, the grid's, and that frame turns with a ship.
        var baseCoords = hasTurretPose
            ? _transform.GetMoverCoordinates(turretCoords)
            : _transform.GetMoverCoordinates(baseUid);
        var frameRotation = _transform.GetWorldRotation(baseCoords.EntityId);
        var worldBaseRotation = useShotRotation
            ? shotRotation + component.AngleOffset
            : hasTurretPose
                ? turretRotation + component.AngleOffset
                : GetBaseRotation(baseUid, frameRotation) + component.AngleOffset + frameRotation;
        var baseRotation = worldBaseRotation - frameRotation;

        // The pose a barrel's or a mount's sprite is drawn in is picked by its rotation in the deck's frame, which the crew's view is level with.
        var (offset, rotateOffset) = GetOffset(component, baseUid, baseRotation, useBarrelDirectionOffsets);
        muzzleCoords = rotateOffset
            ? baseCoords.Offset(baseRotation.RotateVec(offset))
            : baseCoords.Offset(offset);

        muzzleRotation = worldBaseRotation;

        if (component.MuzzleOffset == Vector2.Zero)
            return true;

        if (!useShotRotation &&
            component.UseAimDirection &&
            toCoordinates != null)
        {
            var pivotMap = _transform.ToMapCoordinates(muzzleCoords);
            var targetMap = _transform.ToMapCoordinates(toCoordinates.Value);
            if (pivotMap.MapId == targetMap.MapId)
            {
                var direction = targetMap.Position - pivotMap.Position;
                if (direction.LengthSquared() > 0.0001f)
                    muzzleRotation = direction.ToWorldAngle() + component.AngleOffset;
            }
        }

        muzzleCoords = muzzleCoords.Offset((muzzleRotation - frameRotation).RotateVec(component.MuzzleOffset));

        return true;
    }

    /// <summary>The facing in the frame's own axes: the mover's cardinal, or whatever the transform says.</summary>
    private Angle GetBaseRotation(EntityUid baseUid, Angle frameRotation)
    {
        if (TryComp(baseUid, out GridVehicleMoverComponent? mover) &&
            GridVehicleMoverSystem.TryGetLocalFacing(mover, out var facing))
        {
            return facing;
        }

        return _transform.GetWorldRotation(baseUid) - frameRotation;
    }

    private (Vector2 Offset, bool Rotate) GetOffset(
        GunMuzzleOffsetComponent muzzle,
        EntityUid baseUid,
        Angle baseRotation,
        bool useBaseRotationDirection)
    {
        if (!muzzle.UseDirectionalOffsets)
            return (muzzle.Offset, true);

        var dir = GetBaseDirection(baseUid, baseRotation, useBaseRotationDirection);
        var offset = dir switch
        {
            Direction.North => muzzle.OffsetNorth,
            Direction.East => muzzle.OffsetEast,
            Direction.South => muzzle.OffsetSouth,
            Direction.West => muzzle.OffsetWest,
            _ => muzzle.Offset,
        };

        return (offset, muzzle.RotateDirectionalOffsets);
    }

    private Direction GetBaseDirection(EntityUid baseUid, Angle baseRotation, bool useBaseRotationDirection)
    {
        if (!useBaseRotationDirection &&
            TryComp(baseUid, out GridVehicleMoverComponent? mover) &&
            !mover.FreeSteering &&
            mover.CurrentDirection != Vector2i.Zero)
        {
            return mover.CurrentDirection.AsDirection();
        }

        return VehicleTurretDirectionHelpers.GetRenderAlignedCardinalDir(baseRotation);
    }
}
