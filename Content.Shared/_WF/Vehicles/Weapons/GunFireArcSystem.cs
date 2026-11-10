using Content.Shared._WF.Vehicles.Turret;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>Refuses shots outside a gun's <see cref="GunFireArcComponent"/> cone, without leaving the gun on cooldown.</summary>
public sealed partial class GunFireArcSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private VehicleTurretSystem _vehicleTurret = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<GunFireArcComponent, AttemptShootEvent>(OnAttemptShoot);
    }

    private void OnAttemptShoot(Entity<GunFireArcComponent> ent, ref AttemptShootEvent args)
    {
        if (args.Cancelled)
            return;

        if (args.ToCoordinates == null)
            return;

        if (!args.FromCoordinates.IsValid(EntityManager) || !args.ToCoordinates.Value.IsValid(EntityManager))
        {
            args.Cancelled = true;
            args.ResetCooldown = true;
            return;
        }

        // A gun on a turret that turns to the cursor is held to the turret's limits; one mounted on the hull is held to its
        // arc, whether it is fixed there or traverses on its own mount.
        if (TryComp(ent.Owner, out VehicleTurretComponent? turret) &&
            turret.UseBarrelDirectionForShots &&
            _vehicleTurret.TryResolveRotationTarget(ent.Owner, out var rotationTargetUid, out var rotationTarget) &&
            rotationTarget.RotateToCursor &&
            rotationTargetUid != ent.Owner)
        {
            return;
        }

        var fromMap = _transform.ToMapCoordinates(args.FromCoordinates);
        var toMap = _transform.ToMapCoordinates(args.ToCoordinates.Value);
        var direction = toMap.Position - fromMap.Position;

        if (direction.LengthSquared() <= 0.0001f)
            return;

        var aimAngle = direction.ToWorldAngle();

        var facing = _transform.GetWorldRotation(ent.Owner);
        if (_container.TryGetContainingContainer((ent.Owner, null), out var container))
            facing = _transform.GetWorldRotation(container.Owner);

        var center = facing + ent.Comp.AngleOffset;
        var halfArc = Angle.FromDegrees(ent.Comp.Arc.Degrees / 2f);
        var diff = Angle.ShortestDistance(aimAngle, center);

        if (diff <= halfArc && diff >= -halfArc)
            return;

        args.Cancelled = true;
        args.ResetCooldown = true;
        args.Message = Loc.GetString("rmc-gun-arc-blocked");
    }
}
