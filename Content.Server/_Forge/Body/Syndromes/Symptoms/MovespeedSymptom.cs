using JetBrains.Annotations;
using Content.Shared.Movement.Systems;
using Content.Shared.StatusEffect;
using Content.Shared._Forge.Body.Syndromes;

namespace Content.Server._Forge.Body.Syndromes.Symptoms;

[UsedImplicitly]
public sealed partial class MovespeedSymptom : SyndromeSymptom
{
    [DataField]
    public float WalkModifier { get; set; } = 1f;

    [DataField]
    public float SprintModifier { get; set; } = 1f;

    [DataField(required: true)]
    public string Key { get; set; } = string.Empty;

    [DataField]
    public float Time { get; set; } = 12.0f;

    public override void Apply(EntityUid organUid, EntityUid bodyUid, SyndromeData syndrome, IEntityManager entMan)
    {
        syndrome.ActiveStatus.Add(Key);
        entMan.System<StatusEffectsSystem>().TryAddStatusEffect<SyndromeMovespeedComponent>(bodyUid, Key, TimeSpan.FromSeconds(Time), refresh: true);

        if (!entMan.TryGetComponent(bodyUid, out SyndromeMovespeedComponent? comp))
            return;

        comp.WalkSpeedModifier = WalkModifier;
        comp.SprintSpeedModifier = SprintModifier;

        entMan.System<MovementSpeedModifierSystem>().RefreshMovementSpeedModifiers(bodyUid);
    }
}
