using Content.Shared._Onyx.Wounds;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.EntityEffects;

/// <summary>Adds keyed, decaying pain suppression to a wound host and speeds natural pain recovery while active.</summary>
public sealed partial class SuppressPain : EntityEffect
{
    /// <summary>Pain suppressed per metabolism tick, before reagent scaling.</summary>
    [DataField(required: true)] public FixedPoint2 Amount;

    /// <summary>How long an applied dose takes to decay away.</summary>
    [DataField(required: true)] public TimeSpan DecayDuration;

    /// <summary>Suppression key; doses sharing a key stack instead of overwriting.</summary>
    [DataField] public string Identifier = "PainSuppressant";

    /// <summary>Multiplier applied to natural pain recovery while the suppression is active.</summary>
    [DataField] public float RecoveryMultiplier = 1f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-suppress-pain",
            ("chance", Probability),
            ("amount", Amount.Float()),
            ("duration", DecayDuration.TotalSeconds),
            ("recoveryMultiplier", RecoveryMultiplier));

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent(args.TargetEntity, out PainComponent? pain))
            return;

        var scale = args is EntityEffectReagentArgs reagent ? reagent.Scale : FixedPoint2.New(1);
        args.EntityManager.System<PainSystem>()
            .SuppressPain((args.TargetEntity, pain), Identifier, Amount * scale, DecayDuration, RecoveryMultiplier);
    }
}
