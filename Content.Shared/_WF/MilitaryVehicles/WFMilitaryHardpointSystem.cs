using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Popups;
using Content.Shared.Prototypes;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.MilitaryVehicles;

/// <summary>Weapon and turret hardpoints fit military chassis only.</summary>
public sealed class WFMilitaryHardpointSystem : EntitySystem
{
    /// <summary>The hardpoint type of turrets, which count as weapons whether or not they carry a gun.</summary>
    public static readonly EntProtoId TurretType = "HardpointTypeTurret";

    [Dependency] private SharedPopupSystem _popup = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RMCVehicleComponent, HardpointFitAttemptEvent>(OnFitAttempt);
    }

    private void OnFitAttempt(Entity<RMCVehicleComponent> ent, ref HardpointFitAttemptEvent args)
    {
        if (args.Cancelled || CanFit(ent, args.Part))
            return;

        args.Cancelled = true;
        _popup.PopupClient(Loc.GetString("wf-military-hardpoint-civilian", ("vehicle", ent.Owner)), ent, args.User);
    }

    /// <summary>Whether a part may go on a vehicle: anything on a military chassis, no weapon on any other.</summary>
    public bool CanFit(EntityUid vehicle, EntityUid part)
    {
        return HasComp<WFMilitaryVehicleComponent>(vehicle) || !IsWeapon(part);
    }

    /// <summary>Whether a part is a gun, a turret, or carries a gun inside it.</summary>
    public bool IsWeapon(EntityUid part)
    {
        if (HasComp<GunComponent>(part))
            return true;

        if (TryComp<HardpointItemComponent>(part, out var hardpoint))
        {
            var type = hardpoint.HardpointType;
            if (type == TurretType)
                return true;
        }

        if (!TryComp<ContainerManagerComponent>(part, out var containers))
            return false;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
            {
                if (IsWeapon(contained))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Whether a part prototype is a weapon: a gun or a turret.</summary>
    public bool IsWeapon(EntityPrototype part)
    {
        if (part.HasComponent<GunComponent>(EntityManager.ComponentFactory))
            return true;

        return part.TryGetComponent<HardpointItemComponent>(out var hardpoint, EntityManager.ComponentFactory)
               && hardpoint.HardpointType == TurretType;
    }
}
