using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.WolfChem.Narcotics;

/// <summary>
/// Doses a drug profile. On a reagent the level follows the units left in the blood, which the intoxication system
/// reads each tick, and this only counts the metabolised units towards dependence. Anywhere else it feeds
/// <see cref="PerUnit"/> to the level. Does nothing to a body whose brain is non-organic.
/// </summary>
public sealed partial class WFDose : EntityEffect
{
    [DataField(required: true)]
    public ProtoId<WFDrugProfilePrototype> Profile;

    /// <summary>Level per effective unit in the blood; outside a reagent, the whole amount.</summary>
    [DataField]
    public float PerUnit = 1f;

    /// <summary>On a reagent, counts towards dependence without raising the level, for medicines that give no high.</summary>
    [DataField]
    public bool DependenceOnly;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wf-dose", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var intoxication = args.EntityManager.System<SharedWFIntoxicationSystem>();
        if (intoxication.IsNeurowareHost(args.TargetEntity))
            return;

        if (args is not EntityEffectReagentArgs reagent)
        {
            intoxication.AddLevel(args.TargetEntity, Profile, PerUnit);
            return;
        }

        var grade = reagent.Source != null && reagent.Reagent != null ? reagent.Source.GetPurity(reagent.Reagent.ID) : 1f;
        intoxication.RegisterDose(args.TargetEntity, Profile, PerUnit * reagent.Quantity.Float() * grade,
            !DependenceOnly);
    }
}
