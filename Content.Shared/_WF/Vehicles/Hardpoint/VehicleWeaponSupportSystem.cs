using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared._WF.Vehicles.Hardpoint;

/// <summary>Applies the fire rate modifier of the vehicle's support hardpoints to the guns mounted on it.</summary>
public sealed partial class VehicleWeaponSupportSystem : EntitySystem
{
    [Dependency] private VehicleTopologySystem _topology = default!;

    public override void Initialize()
    {
        // On HardpointItem rather than Gun, so the (Gun, GunRefreshModifiersEvent) pair stays free for other systems.
        SubscribeLocalEvent<HardpointItemComponent, GunRefreshModifiersEvent>(OnGunRefresh);
    }

    private void OnGunRefresh(Entity<HardpointItemComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (!_topology.TryGetVehicle(ent.Owner, out var vehicle))
            return;

        if (!TryComp(vehicle, out VehicleWeaponSupportModifierComponent? mods))
            return;

        args.FireRate *= mods.FireRateMultiplier;
    }
}
