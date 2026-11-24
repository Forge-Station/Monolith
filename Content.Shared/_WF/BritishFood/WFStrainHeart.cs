using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.BritishFood;

/// <summary>Takes health off a wound host's heart, as too much saturated fat at once does.</summary>
public sealed partial class WFStrainHeart : EntityEffect
{
    /// <summary>Heart health taken per metabolism tick, before reagent scaling.</summary>
    [DataField]
    public FixedPoint2 Amount = 0.02;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("wf-reagent-effect-guidebook-strain-heart",
            ("chance", Probability),
            ("amount", Amount.Float()));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var scale = args is EntityEffectReagentArgs reagent ? reagent.Scale : FixedPoint2.New(1);
        var strain = new WFStrainHeartEvent(Amount * scale);
        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity, ref strain);
    }
}

/// <summary>Raised on an entity to take this much health off its heart.</summary>
[ByRefEvent]
public readonly record struct WFStrainHeartEvent(FixedPoint2 Amount);
