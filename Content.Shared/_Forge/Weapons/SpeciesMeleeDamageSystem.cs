using Content.Shared.Humanoid;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Components;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._Forge.Weapons;

public sealed class SpeciesMeleeDamageSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HumanoidAppearanceComponent, GetMeleeDamageEvent>(OnGetMeleeDamage, after: [typeof(SharedMeleeWeaponSystem)]);
    }

    private void OnGetMeleeDamage(EntityUid uid, HumanoidAppearanceComponent humanoidAppearanceComponent, ref GetMeleeDamageEvent args)
    {
        if (!TryComp<SpeciesMeleeDamageComponent>(args.Weapon, out var speciesMeleeDamageComponent))
            return;

        if (speciesMeleeDamageComponent.ExemptSpecies.Contains(humanoidAppearanceComponent.Species))
        {
            if (TryComp<BonusMeleeDamageComponent>(uid, out var bonusMeleeDamageComponent) && bonusMeleeDamageComponent.DamageModifierSet != null)
                args.Modifiers.Remove(bonusMeleeDamageComponent.DamageModifierSet);

            return;
        }

        args.Damage *= speciesMeleeDamageComponent.DamageMultiplier;
    }
}
