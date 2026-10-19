using Content.Shared.Damage;

namespace Content.Shared._WF.RMCCore.Damage;

[ByRefEvent]
public record struct DamageStateCritBeforeDamageEvent(DamageSpecifier Damage);
