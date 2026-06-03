using Content.Shared.EntityEffects;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Hands;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.EntityEffects.Effects;

[UsedImplicitly]
public sealed partial class DropItemEffect : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-drop-items");

    public override void Effect(EntityEffectBaseArgs args)
    {
        var handsSys = args.EntityManager.System<SharedHandsSystem>();

        handsSys.TryDrop(args.TargetEntity, args.TargetEntity, checkActionBlocker: false);
    }
}
