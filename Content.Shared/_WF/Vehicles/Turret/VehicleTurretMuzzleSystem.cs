using System.Numerics;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Shared._WF.Vehicles.Turret;

/// <summary>Moves a shot to the left or right barrel of a gun with <see cref="VehicleTurretMuzzleComponent"/> and alternates them.</summary>
public sealed partial class VehicleTurretMuzzleSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private VehicleTurretSystem _vehicleTurret = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<VehicleTurretMuzzleComponent, AttemptShootEvent>(OnAttemptShoot, after: new[] { typeof(GunMuzzleOffsetSystem) });
        SubscribeLocalEvent<VehicleTurretMuzzleComponent, GunShotEvent>(OnGunShot);
    }

    private void OnAttemptShoot(Entity<VehicleTurretMuzzleComponent> ent, ref AttemptShootEvent args)
    {
        if (args.Cancelled)
            return;

        args.FromCoordinates = GetMuzzleCoordinates(ent.Owner, ent.Comp, args.FromCoordinates);
    }

    private void OnGunShot(Entity<VehicleTurretMuzzleComponent> ent, ref GunShotEvent args)
    {
        if (!ent.Comp.Alternate || args.Ammo.Count == 0)
            return;

        ent.Comp.UseRightNext = !ent.Comp.UseRightNext;

        Dirty(ent);
    }

    /// <summary>Where the next shot of a turret gun leaves, alternating between the barrels.</summary>
    public EntityCoordinates GetMuzzleCoordinates(
        EntityUid uid,
        VehicleTurretMuzzleComponent muzzle,
        EntityCoordinates baseCoords)
    {
        var offset = GetWorldOffset(uid, muzzle);
        if (offset == Vector2.Zero || !baseCoords.EntityId.IsValid())
            return baseCoords;

        // The coordinates are relative to the grid the vehicle drives on, which turns with a ship; the offset is a world one.
        return baseCoords.Offset((-_transform.GetWorldRotation(baseCoords.EntityId)).RotateVec(offset));
    }

    /// <summary>The muzzle offset of a barrel in the world's axes.</summary>
    public Vector2 GetWorldOffset(
        EntityUid uid,
        VehicleTurretMuzzleComponent muzzle,
        bool? useRightOverride = null)
    {
        var baseRotation = _vehicleTurret.TryGetShotBarrelWorldRotation(uid, out var shotRotation)
            ? shotRotation
            : _transform.GetWorldRotation(uid);
        var useRight = useRightOverride ?? (muzzle.Alternate && muzzle.UseRightNext);

        // The barrel's pose is picked by its rotation on the deck, not in the world.
        var frameRotation = Transform(uid).GridUid is { } deck ? _transform.GetWorldRotation(deck) : Angle.Zero;
        var offset = GetOffset(muzzle, baseRotation - frameRotation, useRight);
        return offset == Vector2.Zero ? Vector2.Zero : baseRotation.RotateVec(offset);
    }

    private Vector2 GetOffset(VehicleTurretMuzzleComponent muzzle, Angle baseRotation, bool useRight)
    {
        if (!muzzle.UseDirectionalOffsets)
        {
            var baseOffset = useRight ? muzzle.OffsetRight : muzzle.OffsetLeft;
            return baseOffset;
        }

        return VehicleTurretDirectionHelpers.GetRenderAlignedCardinalDir(baseRotation) switch
        {
            Direction.North => useRight ? muzzle.OffsetRightNorth : muzzle.OffsetLeftNorth,
            Direction.East => useRight ? muzzle.OffsetRightEast : muzzle.OffsetLeftEast,
            Direction.South => useRight ? muzzle.OffsetRightSouth : muzzle.OffsetLeftSouth,
            Direction.West => useRight ? muzzle.OffsetRightWest : muzzle.OffsetLeftWest,
            _ => useRight ? muzzle.OffsetRight : muzzle.OffsetLeft
        };
    }
}
