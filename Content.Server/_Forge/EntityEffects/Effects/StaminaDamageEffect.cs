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

        var staminaSys = entMan.System<StaminaSystem>();

        if (ForceCrit)
        {
           
            var damage = MathF.Max(1f, stamina.CritThreshold - stamina.StaminaDamage + 1f);
            staminaSys.TakeStaminaDamage(args.TargetEntity, damage, stamina, visual: false, immediate: true);
            return;
        }

        var amount = Amount;
        if (args is EntityEffectReagentArgs reagentArgs)
            amount *= reagentArgs.Scale.Float();

        staminaSys.TakeStaminaDamage(args.TargetEntity, amount, stamina, visual: false);
    }
}