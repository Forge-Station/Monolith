using Content.Shared._Forge.Weapons.ThrowingAmmoProvider;
using Content.Server.Damage.Systems;

namespace Content.Server._Forge.Weapons.ThrowingAmmoProvider;

public sealed partial class ThrowingAmmoDamageBoostSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ThrowingAmmoDamageBoostComponent, GetThrowDamageModifierEvent>(OnGetThrowDamageModifier);
    }

    private void OnGetThrowDamageModifier(
        EntityUid uid,
        ThrowingAmmoDamageBoostComponent component,
        ref GetThrowDamageModifierEvent args)
    {
        if (component.DamageMultiplier <= 0f)
            return;

        args.Multiplier *= component.DamageMultiplier;

        if (!args.Consume)
            return;

        component.DamageMultiplier = 0f;
        RemCompDeferred<ThrowingAmmoDamageBoostComponent>(uid);
    }
}
