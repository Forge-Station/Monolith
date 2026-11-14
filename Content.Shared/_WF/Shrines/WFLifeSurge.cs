using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.EntityEffects;
using Content.Shared.Rejuvenate;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Shrines;

/// <summary>
/// Heals everything a rejuvenate heals: damage, wounds, organs, lost limbs, blood, pain. Handcuffs stay on; the
/// fountain mends the body, it doesn't free prisoners.
/// </summary>
public sealed partial class WFLifeSurge : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wf-life-surge", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var entMan = args.EntityManager;
        var target = args.TargetEntity;

        // A rejuvenate empties the cuff container onto the floor.
        List<EntityUid>? cuffs = null;
        if (entMan.TryGetComponent(target, out CuffableComponent? cuffable) && cuffable.Container.ContainedEntities.Count > 0)
            cuffs = new List<EntityUid>(cuffable.Container.ContainedEntities);

        entMan.EventBus.RaiseLocalEvent(target, new RejuvenateEvent());

        if (cuffs == null)
            return;

        var cuffSystem = entMan.System<SharedCuffableSystem>();
        foreach (var cuff in cuffs)
        {
            if (!entMan.Deleted(cuff))
                cuffSystem.TryAddNewCuffs(target, target, cuff);
        }
    }
}
