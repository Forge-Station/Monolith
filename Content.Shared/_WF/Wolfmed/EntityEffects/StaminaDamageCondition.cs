using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.EntityEffects;

/// <summary>True while stamina damage on the target is strictly between Min and Max.</summary>
public sealed partial class StaminaDamageCondition : EntityEffectCondition
{
    /// <summary>Exclusive lower bound on current stamina damage.</summary>
    [DataField] public float Min = -1f;

    /// <summary>Exclusive upper bound on current stamina damage.</summary>
    [DataField] public float Max = float.PositiveInfinity;

    public override bool Condition(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent(args.TargetEntity, out StaminaComponent? stamina))
            return false;

        var damage = args.EntityManager.System<StaminaSystem>().GetStaminaDamage(args.TargetEntity, stamina);
        return damage > Min && damage < Max;
    }

    public override string GuidebookExplanation(IPrototypeManager prototype) => string.Empty;
}
