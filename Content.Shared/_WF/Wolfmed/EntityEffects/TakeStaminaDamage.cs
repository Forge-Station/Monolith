using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.EntityEffects;

/// <summary>Applies stamina damage from a metabolised reagent.</summary>
public sealed partial class TakeStaminaDamage : EntityEffect
{
    /// <summary>Stamina damage applied per full metabolism tick.</summary>
    [DataField] public float Amount = 10f;

    /// <summary>Whether an already-critical target is dropped immediately instead of waiting for decay.</summary>
    [DataField] public bool Immediate;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-take-stamina-damage",
            ("chance", Probability), ("amount", Amount));

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent(args.TargetEntity, out StaminaComponent? stamina))
            return;

        // WOLFGATE (P4-D5): Onyx gates on Scale == 1 because its framework can hand out partial ticks;
        // Wolfgate's MetabolizerSystem does the same (scale = mostToRemove / rate), so the gate is kept.
        if (args is EntityEffectReagentArgs reagent && reagent.Scale != FixedPoint2.New(1))
            return;

        // WOLFGATE (P4-D5): Wolfgate's StaminaSystem has the `immediate` mode Onyx's comment says vanilla lacks,
        // and defaults it to true; Onyx's datafield default is false, which is what this effect passes.
        args.EntityManager.System<StaminaSystem>()
            .TakeStaminaDamage(args.TargetEntity, Amount, stamina, visual: false, immediate: Immediate);
    }
}
