using Content.Server.Stunnable;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityEffects;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.EntityEffects.Effects;

[UsedImplicitly]
public sealed partial class StaminaDamageEffect : EntityEffect
{
    [DataField]
    public float Amount = 10f;

    /// <summary>
    /// Puts the target into stamina crit. While the effect keeps firing, the knockdown is refreshed:
    /// <see cref="StaminaSystem.TakeStaminaDamage"/> does not extend a crit that is already active.
    /// </summary>
    [DataField]
    public bool ForceCrit;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => ForceCrit
            ? Loc.GetString("reagent-effect-guidebook-stamina-crit", ("chance", Probability))
            : Loc.GetString("reagent-effect-guidebook-stamina-damage", ("chance", Probability), ("amount", Amount));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var entMan = args.EntityManager;
        if (!entMan.TryGetComponent<StaminaComponent>(args.TargetEntity, out var stamina))
            return;

        if (ForceCrit)
        {
            if (stamina.Critical)
            {
                entMan.System<StunSystem>().TryParalyze(args.TargetEntity, TimeSpan.FromSeconds(2), refresh: true);
                return;
            }

            var damage = MathF.Max(1f, stamina.CritThreshold - stamina.StaminaDamage + 1f);
            entMan.System<StaminaSystem>().TakeStaminaDamage(args.TargetEntity, damage, stamina, visual: false, immediate: true);
            return;
        }

        var amount = Amount;
        if (args is EntityEffectReagentArgs reagentArgs)
            amount *= reagentArgs.Scale.Float();

        entMan.System<StaminaSystem>().TakeStaminaDamage(args.TargetEntity, amount, stamina, visual: false);
    }
}
