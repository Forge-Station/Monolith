using Content.Shared.Damage;

namespace Content.Shared._Forge.Weapons.ChainsawShield;

[RegisterComponent]
public sealed partial class ToggleableThrowDamageComponent : Component
{
    [DataField(required: true)]
    public DamageSpecifier InactiveDamage = default!;

    [DataField(required: true)]
    public DamageSpecifier ActiveDamage = default!;

    [DataField]
    public bool IgnoreResistances;
}
