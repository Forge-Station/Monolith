using Content.Shared.Damage;

namespace Content.Shared._WF.Xenonids.Stab;

[ByRefEvent]
public record struct RMCGetTailStabBonusDamageEvent(DamageSpecifier Damage);
