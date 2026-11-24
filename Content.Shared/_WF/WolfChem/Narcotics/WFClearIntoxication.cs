using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.WolfChem.Narcotics;

/// <summary>
/// Lowers every profile of a family, for antagonists such as Naloxone.
/// </summary>
public sealed partial class WFClearIntoxication : EntityEffect
{
    [DataField(required: true)]
    public WFDrugFamily Family;

    /// <summary>Level removed; inside a reagent, scaled by the metabolism scale.</summary>
    [DataField]
    public float Amount = 2f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wf-clear-intoxication",
            ("chance", Probability),
            ("family", Loc.GetString($"wf-drug-family-{Family.ToString().ToLowerInvariant()}")));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var amount = Amount;
        if (args is EntityEffectReagentArgs reagent)
            amount *= reagent.Scale.Float();

        args.EntityManager.System<SharedWFIntoxicationSystem>().ReduceFamily(args.TargetEntity, Family, amount);
    }
}
