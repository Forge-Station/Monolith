using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.EntityEffects;

/// <summary>
/// Antiseptic on open wounds. Sits on the humanoid base's touch reaction for the reagents that actually
/// sterilise, so pouring, spraying or swabbing all clean a wound through one path.
/// </summary>
/// <remarks>
/// Raises <see cref="WolfmedCleanWoundsEvent"/> rather than calling a system: the infection model is
/// server-side and this effect is not.
/// </remarks>
public sealed partial class WolfmedCleanWounds : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-clean-wounds", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var cleaned = new WolfmedCleanWoundsEvent();
        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity, ref cleaned);
    }
}
