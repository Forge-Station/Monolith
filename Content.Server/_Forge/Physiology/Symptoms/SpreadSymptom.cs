using JetBrains.Annotations;
using Robust.Shared.Random;
using Content.Shared._Forge.Physiology;
using Content.Shared._Forge.Physiology.Disease;
using Content.Shared._Forge.Physiology.Disease.Symptoms;

namespace Content.Server._Forge.Physiology.Disease.Symptoms;

[UsedImplicitly]
public sealed partial class SpreadSymptom : DiseaseSymptom
{
    [DataField]
    public string DiseaseId { get; set; } = string.Empty;

    [DataField]
    public float Range { get; set; } = 1.5f;

    [DataField]
    public float Severity { get; set; } = 5f;

    public override void Apply(EntityUid organUid, EntityUid bodyUid, DiseaseData disease, IEntityManager entMan)
    {
        var targetId = string.IsNullOrEmpty(DiseaseId) ? disease.PrototypeId : DiseaseId;
        var coords = entMan.System<SharedTransformSystem>().GetMapCoordinates(bodyUid);
        var nearby = new HashSet<Entity<BodyPhysiologyComponent>>();

        entMan.System<EntityLookupSystem>().GetEntitiesInRange(coords, Range, nearby);
        foreach (var target in nearby)
        {
            if (target.Owner == bodyUid)
                continue;
            if (!IoCManager.Resolve<IRobustRandom>().Prob(Chance))
                continue;

            entMan.System<PhysiologyDiseaseSystem>().AddDisease(target.Owner, targetId, Severity);
        }
    }
}
