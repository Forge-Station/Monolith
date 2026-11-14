using Content.Shared.Damage;

namespace Content.Shared._WF.NpcCrew;

/// <summary>
/// Raised from the one <see cref="DamageModifyEvent"/> subscriber so crew skill can scale a hit
/// without a second subscription on <c>DamageableComponent</c>.
/// </summary>
[ByRefEvent]
public record struct WFCrewDamageScaleEvent
{
    public EntityUid? Origin;
    public DamageSpecifier Damage;

    public WFCrewDamageScaleEvent(EntityUid? origin, DamageSpecifier damage)
    {
        Origin = origin;
        Damage = damage;
    }
}
