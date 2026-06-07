using Content.Server.Atmos.EntitySystems;
using Content.Shared._Forge.Weapons;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Server._Forge.Weapons;

public sealed partial class DrakePlasmaGasWeaponSystem : SharedDrakePlasmaGasWeaponSystem
{
    [Dependency] private readonly GasTankSystem _gasTank = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DrakePlasmaGasWeaponComponent, GunShotEvent>(OnGunShot);
    }

    private void OnGunShot(EntityUid uid, DrakePlasmaGasWeaponComponent component, ref GunShotEvent args)
    {
        if (component.GasUsage <= 0f)
            return;

        var gas = GetGasTank(uid, component);
        if (gas == null)
            return;

        if (GetPlasmaMoles(gas.Value) < component.GasUsage)
            return;

        gas.Value.Comp.Air.AdjustMoles(Gas.Plasma, -component.GasUsage);
        _gasTank.CheckStatus(gas.Value);

        if (GetPlasmaMoles(gas.Value) >= component.GasUsage)
            return;

        _slots.TryEject(uid, DrakePlasmaGasWeaponComponent.TankSlotId, args.User, out _);

        if (args.User is { } user)
            _popup.PopupEntity(Loc.GetString("drake-plasma-depleted"), uid, user);
    }
}
