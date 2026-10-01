using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Wounds; // WOLFGATE(Wolfmed)
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.Effects;

public sealed partial class ModifyBleedAmount : EntityEffect
{
    [DataField]
    public bool Scaled = false;

    [DataField]
    public float Amount = -1.0f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-modify-bleed-amount", ("chance", Probability),
            ("deltasign", MathF.Sign(Amount)));

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args.EntityManager.TryGetComponent<BloodstreamComponent>(args.TargetEntity, out var blood))
        {
            var sys = args.EntityManager.System<BloodstreamSystem>();
            var amt = Amount;
            if (args is EntityEffectReagentArgs reagentArgs) {
                if (Scaled)
                    amt *= reagentArgs.Quantity.Float();
                amt *= reagentArgs.Scale.Float();
            }

            // WOLFGATE(Wolfmed) START: a wound host's bleeding is its wounds' (GUARD E3), so a reagent treats the wounds.
            if (args.EntityManager.HasComponent<WoundHostComponent>(args.TargetEntity))
            {
                args.EntityManager.System<WoundBleedingSystem>().ApplyReagentBleeding(args.TargetEntity, amt);
                return;
            }
            // WOLFGATE END

            sys.TryModifyBleedAmount(args.TargetEntity, amt, blood);
        }
    }
}
