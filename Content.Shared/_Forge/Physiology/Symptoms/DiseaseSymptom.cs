using JetBrains.Annotations;

namespace Content.Shared._Forge.Physiology.Disease.Symptoms;

[ImplicitDataDefinitionForInheritors]
[MeansImplicitUse]
public abstract partial class DiseaseSymptom
{
    [DataField]
    public bool FlareOnly { get; set; } = false;

    [DataField]
    public float Chance { get; set; } = 1.0f;

    public abstract void Apply(EntityUid organUid, EntityUid bodyUid, DiseaseData disease, IEntityManager entMan);
}
