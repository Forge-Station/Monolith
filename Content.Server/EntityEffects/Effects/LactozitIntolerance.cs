/// Forge Change Start
using Content.Server.Traits.Assorted;
using Content.Shared.EntityEffects;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.Effects;

/// <summary>
/// Triggers a unique reaction on entities with Lactozit intolerance.
/// </summary>
[UsedImplicitly]
public sealed partial class LactozitIntolerance : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-lactozitium-reaction", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.EntitySysManager.GetEntitySystem<LactozitIntoleranceSystem>()
            .TryTriggerLactozitiumReaction(args.TargetEntity);
    }
}
/// Forge Change End
