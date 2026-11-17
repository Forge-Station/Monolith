using Content.Shared.EntityEffects;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.Effects;

/// <summary>
/// Adds to a satiation. Reagent metabolism multiplies <see cref="Factor"/> by the reagent quantity.
/// </summary>
public sealed partial class Satiate : EntityEffect
{
    [DataField]
    public float Factor = 1.5f;

    [DataField(required: true)]
    public ProtoId<SatiationTypePrototype> SatiationType;

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent(args.TargetEntity, out SatiationComponent? satiation))
            return;

        var amount = Factor;
        if (args is EntityEffectReagentArgs reagentArgs)
            amount *= (float) reagentArgs.Quantity;

        args.EntityManager.System<SatiationSystem>().ModifyValue((args.TargetEntity, satiation), SatiationType, amount);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        var typeName = prototype.TryIndex(SatiationType, out SatiationTypePrototype? type)
            ? type.Name
            : SatiationType.Id;
        return Loc.GetString("reagent-effect-guidebook-satiate-hunger", ("chance", Probability), ("relative", Factor), ("type", typeName));
    }
}
