using Content.Shared.Damage;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>Injuries dealt to a wound host before its body was built, laid on it at map init.</summary>
[RegisterComponent]
public sealed partial class WolfmedSpawnInjuryComponent : Component
{
    [DataField]
    public DamageSpecifier Damage = new();
}
