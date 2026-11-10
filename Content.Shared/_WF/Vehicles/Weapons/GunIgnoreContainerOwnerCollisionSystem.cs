using Content.Shared._WF.Vehicles.Base;
using Content.Shared.Projectiles;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Events;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>Keeps a shot from a gun mounted in a vehicle from hitting that vehicle.</summary>
public sealed partial class GunIgnoreContainerOwnerCollisionSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<RMCVehicleComponent, PreventCollideEvent>(OnPreventCollide);
    }

    private void OnPreventCollide(Entity<RMCVehicleComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled ||
            !_projectileQuery.TryComp(args.OtherEntity, out var projectile) ||
            projectile.Weapon is not { } weapon ||
            !HasComp<GunIgnoreContainerOwnerCollisionComponent>(weapon))
        {
            return;
        }

        var current = weapon;
        while (_container.TryGetContainingContainer((current, null), out var container))
        {
            if (container.Owner == ent.Owner)
            {
                args.Cancelled = true;
                return;
            }

            current = container.Owner;
        }
    }
}
