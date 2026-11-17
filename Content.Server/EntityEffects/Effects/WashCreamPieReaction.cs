/// Forge-Change-Start
using Content.Server.Nutrition.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.Nutrition.Components;
using Content.Shared.SprayPainter.Components;
using Content.Shared.SprayPainter.Prototypes;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.EntityEffects.Effects;

[UsedImplicitly]
public sealed partial class WashReaction : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wash-cream-pie-reaction", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args.EntityManager.TryGetComponent(args.TargetEntity, out CreamPiedComponent? creamPied))
            args.EntityManager.System<CreamPieSystem>().SetCreamPied(args.TargetEntity, creamPied, false);

        if (!args.EntityManager.TryGetComponent<PaintedComponent>(args.TargetEntity, out var painted))
            return;

        var timing = IoCManager.Resolve<IGameTiming>();
        if (timing.CurTime > painted.DryTime)
            return;

        var appearance = args.EntityManager.System<SharedAppearanceSystem>();

        if (args.EntityManager.TryGetComponent<MetaDataComponent>(args.TargetEntity, out var meta)
            && meta.EntityPrototype is { } prototype)
        {
            // Reset paint visuals back to the entity's own base prototype.
            appearance.SetData(args.TargetEntity, PaintableVisuals.Prototype, prototype.ID);
        }
        else
        {
            appearance.RemoveData(args.TargetEntity, PaintableVisuals.Prototype);
        }

        args.EntityManager.RemoveComponent(args.TargetEntity, painted);
    }
}
/// Forge-Change-End
