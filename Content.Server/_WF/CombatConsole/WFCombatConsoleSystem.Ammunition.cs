using Content.Shared._WF.CombatConsole;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Containers;

namespace Content.Server._WF.CombatConsole;

public sealed partial class WFCombatConsoleSystem
{
    /// <summary>Reads the firing provider's semantics and its existing ammunition-count event.</summary>
    public WFWeaponSupply GetWeaponSupply(EntityUid weapon)
    {
        var kind = GetSupplyKind(weapon);
        if (kind is WFWeaponSupplyKind.Unknown or WFWeaponSupplyKind.Infinite)
            return new WFWeaponSupply(kind, null, null);
        var ammunition = new GetAmmoCountEvent();
        RaiseLocalEvent(weapon, ref ammunition);
        return new WFWeaponSupply(kind, ammunition.Count, ammunition.Capacity);
    }

    private WFWeaponSupplyKind GetSupplyKind(EntityUid weapon)
    {
        if (TryComp<BasicEntityAmmoProviderComponent>(weapon, out var basic))
            return basic.Count == null ? WFWeaponSupplyKind.Infinite :
                HasComp<RechargeBasicEntityAmmoComponent>(weapon) ? WFWeaponSupplyKind.Recharging : WFWeaponSupplyKind.Finite;
        if (TryComp<BallisticAmmoProviderComponent>(weapon, out var ballistic))
            return ballistic.InfiniteUnspawned ? WFWeaponSupplyKind.Infinite : WFWeaponSupplyKind.Finite;
        if (HasComp<ProjectileBatteryAmmoProviderComponent>(weapon) || HasComp<HitscanBatteryAmmoProviderComponent>(weapon))
            return WFWeaponSupplyKind.Energy;
        if (HasComp<MagazineAmmoProviderComponent>(weapon) || HasComp<ChamberMagazineAmmoProviderComponent>(weapon))
        {
            return _containers.TryGetContainer(weapon, "gun_magazine", out var container) &&
                container is ContainerSlot { ContainedEntity: { } magazine }
                ? GetSupplyKind(magazine) : WFWeaponSupplyKind.Finite;
        }
        return HasComp<RevolverAmmoProviderComponent>(weapon) || HasComp<ContainerAmmoProviderComponent>(weapon) ||
            HasComp<SolutionAmmoProviderComponent>(weapon) ? WFWeaponSupplyKind.Finite : WFWeaponSupplyKind.Unknown;
    }
}
