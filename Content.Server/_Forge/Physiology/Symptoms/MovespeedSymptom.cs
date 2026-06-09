using JetBrains.Annotations;
using Content.Shared.Movement.Systems;
using Content.Shared.StatusEffect;
using Content.Shared._Forge.Physiology.Disease;
using Content.Shared._Forge.Physiology.Disease.Symptoms;

namespace Content.Server._Forge.Physiology.Disease.Symptoms;

[UsedImplicitly]
public sealed partial class MovespeedSymptom : DiseaseSymptom
{
    [DataField]
    public float WalkModifier { get; set; } = 1f;

    [DataField]
    public float SprintModifier { get; set; } = 1f;

    [DataField(required: true)]
    public string Key { get; set; } = string.Empty;

    [DataField]
    public float Time { get; set; } = 12.0f;

    public override void Apply(EntityUid organUid, EntityUid bodyUid, DiseaseData disease, IEntityManager entMan)
    {
        disease.ActiveStatus.Add(Key);
        entMan.System<StatusEffectsSystem>().TryAddStatusEffect<DiseaseMovespeedComponent>(bodyUid, Key, TimeSpan.FromSeconds(Time), refresh: true);

        if (!entMan.TryGetComponent(bodyUid, out DiseaseMovespeedComponent? comp))
            return;

        comp.WalkSpeedModifier = WalkModifier;
        comp.SprintSpeedModifier = SprintModifier;

        entMan.System<MovementSpeedModifierSystem>().RefreshMovementSpeedModifiers(bodyUid);
    }
}
