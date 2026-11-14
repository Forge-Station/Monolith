using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Shared.EntityEffects.Effects;

/// <summary>
///     Washes a cream pie off the entity. Water and space cleaner trigger this on touch.
/// </summary>
public sealed partial class WashCreamPieReaction : EntityEffect
{
    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent(args.TargetEntity, out CreamPiedComponent? creamPied))
            return;

        args.EntityManager.System<SharedCreamPieSystem>().SetCreamPied(args.TargetEntity, creamPied, false);
    }

    protected override string ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString("reagent-effect-guidebook-wash-cream-pie-reaction", ("chance", Probability));
    }
}
