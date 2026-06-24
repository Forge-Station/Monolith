using JetBrains.Annotations;
using Robust.Shared.Random;
using Content.Shared._Forge.Body.Components;
using Content.Server._Forge.Body.Syndromes;
using Content.Shared._Forge.Body.Syndromes;

namespace Content.Server._Forge.Body.Syndromes.Symptoms;

[UsedImplicitly]
public sealed partial class SpreadSymptom : SyndromeSymptom
{
    [DataField]
    public string SyndromeId { get; set; } = string.Empty;

    [DataField]
    public float Range { get; set; } = 1.5f;

    [DataField]
    public float Severity { get; set; } = 5f;

    public override void Apply(EntityUid organUid, EntityUid bodyUid, SyndromeData syndrome, IEntityManager entMan)
    {
        var targetId = string.IsNullOrEmpty(SyndromeId) ? syndrome.PrototypeId : SyndromeId;
        var coords = entMan.System<SharedTransformSystem>().GetMapCoordinates(bodyUid);
        var nearby = new HashSet<Entity<BodyPhysiologyComponent>>();

        entMan.System<EntityLookupSystem>().GetEntitiesInRange(coords, Range, nearby);
        foreach (var target in nearby)
        {
            if (target.Owner == bodyUid)
                continue;
            if (!IoCManager.Resolve<IRobustRandom>().Prob(Chance))
                continue;

            entMan.System<SyndromeSystem>().AddSyndrome(target.Owner, targetId, Severity);
        }
    }
}
