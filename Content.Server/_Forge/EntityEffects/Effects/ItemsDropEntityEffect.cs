using Content.Shared.EntityEffects;
using Content.Shared.Standing;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.EntityEffects.Effects;

[UsedImplicitly]
public sealed partial class ItemsDropEffect : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString("reagent-effect-guidebook-drop-items", ("chance", Probability));
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        var ev = new DropHandItemsEvent();
        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity, ref ev);
    }
}