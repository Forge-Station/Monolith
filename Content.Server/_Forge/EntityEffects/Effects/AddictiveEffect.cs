using Content.Shared.EntityEffects;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Chemistry.Addiction;

[UsedImplicitly]
public sealed partial class AddictiveEffect : EntityEffect
{
    [DataField(required: true)]
    public string AddictionId { get; set; } = default!;

    // Tolerance gain per 1u of reagent
    [DataField]
    public float ToleranceGain { get; set; } = 0.05f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-addictive");

    public override void Effect(EntityEffectBaseArgs args)
    {
        var addictionSys = args.EntityManager.System<AddictionSystem>();

        var gain = args is EntityEffectReagentArgs reagentArgs
            ? ToleranceGain * (float)reagentArgs.Quantity
            : ToleranceGain;

        addictionSys.ApplyDose(args.TargetEntity, AddictionId, gain);
    }
}
