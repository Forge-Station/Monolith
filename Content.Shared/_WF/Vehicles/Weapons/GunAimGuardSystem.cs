using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Turret;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>Refuses a vehicle gun's shot at an aim point that is not a real spot on the muzzle's map, before anything is fired.</summary>
/// <remarks>
/// The stock client always aims from the grid under the gun, so only a modified one sends a point relative to something on another
/// map, such as the gunner's own body on the interior map, or one that is not a number. The shot's direction and range are worked
/// out from the bare coordinates, so such a point would let it fly along a course no barrel or arc ever allowed.
/// </remarks>
public sealed partial class GunAimGuardSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        // Ahead of every handler that reads the aim, so none of them sees the point.
        var before = new[] { typeof(GunMuzzleOffsetSystem), typeof(GunFireArcSystem), typeof(VehicleTurretSystem), typeof(VehicleTurretMuzzleSystem) };
        SubscribeLocalEvent<HardpointItemComponent, AttemptShootEvent>(OnHardpointAttemptShoot, before: before);
        SubscribeLocalEvent<VehiclePortGunComponent, AttemptShootEvent>(OnPortGunAttemptShoot, before: before);
    }

    private void OnHardpointAttemptShoot(Entity<HardpointItemComponent> ent, ref AttemptShootEvent args)
    {
        Guard(ref args);
    }

    private void OnPortGunAttemptShoot(Entity<VehiclePortGunComponent> ent, ref AttemptShootEvent args)
    {
        Guard(ref args);
    }

    private void Guard(ref AttemptShootEvent args)
    {
        if (args.Cancelled || args.ToCoordinates is not { } to || IsUsableAim(args.FromCoordinates, to))
            return;

        args.Cancelled = true;
        args.ResetCooldown = true;
    }

    /// <summary>Whether <paramref name="to"/> is a finite point on the map <paramref name="from"/> is on.</summary>
    public bool IsUsableAim(EntityCoordinates from, EntityCoordinates to)
    {
        return from.IsValid(EntityManager) && IsUsableAim(_transform.ToMapCoordinates(from), to);
    }

    /// <inheritdoc cref="IsUsableAim(EntityCoordinates, EntityCoordinates)"/>
    public bool IsUsableAim(MapCoordinates from, EntityCoordinates to)
    {
        if (from.MapId == MapId.Nullspace || !to.IsValid(EntityManager))
            return false;

        var toMap = _transform.ToMapCoordinates(to);
        return toMap.MapId == from.MapId && float.IsFinite(toMap.Position.X) && float.IsFinite(toMap.Position.Y);
    }
}
