using JetBrains.Annotations;

namespace Content.Shared._Forge.Body.Syndromes;

[ImplicitDataDefinitionForInheritors]
[MeansImplicitUse]
public abstract partial class SyndromeSymptom
{
    [DataField]
    public bool FlareOnly { get; set; } = false;

    [DataField]
    public float Chance { get; set; } = 1.0f;

    public abstract void Apply(EntityUid organUid, EntityUid bodyUid, SyndromeData syndrome, IEntityManager entMan);
}
