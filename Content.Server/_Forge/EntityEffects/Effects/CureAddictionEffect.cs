using Content.Server._Forge.Chemistry.Addiction;
using Content.Shared.EntityEffects;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Chemistry.Addiction;

[UsedImplicitly]
public sealed partial class CureAddictionEffect : EntityEffect
{
    // If null = reduces all addiction
    [DataField]
    public string? AddictionId { get; set; }

    // Tolerance reduction per 1u of reagent
    [DataField]
    public float Amount { get; set; } = 0.05f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-cure-addiction");

    public override void Effect(EntityEffectBaseArgs args)
    {
        var sys = args.EntityManager.System<AddictionSystem>();

        var amount = args is EntityEffectReagentArgs reagentArgs
            ? Amount * (float)reagentArgs.Quantity
            : Amount;

        if (AddictionId != null)
            sys.ReduceTolerance(args.TargetEntity, AddictionId, amount);
        else
            sys.ReduceAllTolerances(args.TargetEntity, amount);
    }
}
