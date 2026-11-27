using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.WolfChem.Narcotics;

/// <summary>
/// Wolfmed sedation from a street drug: aims for <see cref="SedationPerUnit"/> per unit in the blood, times its grade,
/// divided by the family's tolerance factor, so breathing fails at the same level the high's overdose tier begins.
/// </summary>
public sealed partial class WFSedation : EntityEffect
{
    [DataField(required: true)]
    public ProtoId<WFDrugProfilePrototype> Profile;

    [DataField(required: true)]
    public float SedationPerUnit;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wf-sedation", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs { Reagent: { } reagent } reagentArgs
            || !IoCManager.Resolve<IPrototypeManager>().TryIndex(Profile, out var profile))
            return;

        var units = reagentArgs.Source?.GetTotalPrototypeQuantity(reagent.ID).Float() ?? reagentArgs.Quantity.Float();
        var grade = reagentArgs.Source?.GetPurity(reagent.ID) ?? 1f;
        var tolerance = args.EntityManager.System<SharedWFIntoxicationSystem>()
            .ToleranceFactor(args.TargetEntity, profile.Family);

        args.EntityManager.System<WolfmedPainReliefSystem>().AddDose(args.TargetEntity, reagent.ID,
            WolfmedPainReliefTier.Strong, 0f, TimeSpan.FromSeconds(4), units * grade / tolerance * SedationPerUnit);
    }
}
