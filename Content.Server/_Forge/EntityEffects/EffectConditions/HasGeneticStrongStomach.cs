using Content.Shared._Forge.Genetics.Mutations;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.EntityEffects.EffectConditions;

/// <summary>
/// Passes when the metabolizing body does or does not have GeneticStrongStomach.
/// </summary>
public sealed partial class HasGeneticStrongStomach : EntityEffectCondition
{
    [DataField]
    public bool ShouldHave = true;

    public override bool Condition(EntityEffectBaseArgs args)
    {
        var has = args.EntityManager.HasComponent<GeneticStrongStomachComponent>(args.TargetEntity);
        return has == ShouldHave;
    }

    public override string GuidebookExplanation(IPrototypeManager prototype)
    {
        return Loc.GetString("reagent-effect-condition-guidebook-genetic-strong-stomach",
            ("shouldhave", ShouldHave));
    }
}
