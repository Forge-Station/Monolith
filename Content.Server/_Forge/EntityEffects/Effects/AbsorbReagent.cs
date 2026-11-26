using Content.Server.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.EntityEffects.Effects;

/// <summary>
/// Copies the metabolized quantity into the bloodstream.
/// Lungs metabolize gases far faster than the heart metabolizes poisons, so gas effects
/// that should linger (sleep, a crit finisher) belong in the blood, not in the breath.
/// </summary>
[UsedImplicitly]
public sealed partial class AbsorbReagent : EntityEffect
{
    [DataField]
    public string Solution = BloodstreamComponent.DefaultBloodSolutionName;

    [DataField]
    public float Ratio = 1f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-absorb-reagent", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs reagentArgs || reagentArgs.Reagent == null)
            return;

        var amount = reagentArgs.Quantity * Ratio;
        if (amount <= FixedPoint2.Zero)
            return;

        var solutionSys = args.EntityManager.System<SharedSolutionContainerSystem>();
        if (!solutionSys.TryGetSolution(args.TargetEntity, Solution, out var solution))
            return;

        solutionSys.TryAddReagent(solution.Value, reagentArgs.Reagent.ID, amount);
    }
}
