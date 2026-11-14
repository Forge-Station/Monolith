using Content.Shared._WF.Xenonids;
using Content.Shared.StatusEffect;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.RMCCore.StatusEffect;

public sealed partial class RMCStatusEffectSystem : EntitySystem
{
    private static readonly ProtoId<StatusEffectPrototype> Knockdown = "KnockedDown";
    private static readonly ProtoId<StatusEffectPrototype> Stun = "Stun";
    private static readonly ProtoId<StatusEffectPrototype> Unconscious = "Unconscious";

    public override void Initialize()
    {
        base.Initialize();

        // WOLFGATE: RMC's endurance skill reduction is cut with Skills
        SubscribeLocalEvent<XenoComponent, RMCStatusEffectTimeEvent>(OnXenoStatusEffectTime);
        SubscribeLocalEvent<RMCStunResistanceComponent, RMCStatusEffectTimeEvent>(OnStunResistanceStatusEffectTime);
    }

    private void OnXenoStatusEffectTime(Entity<XenoComponent> ent, ref RMCStatusEffectTimeEvent args)
    {
        if (args.Key != Knockdown && args.Key != Stun && args.Key != Unconscious)
            return;

        args.Duration *= 0.667;
    }

    private void OnStunResistanceStatusEffectTime(Entity<RMCStunResistanceComponent> ent, ref RMCStatusEffectTimeEvent args)
    {
        if (args.Key != Knockdown && args.Key != Stun && args.Key != Unconscious)
            return;

        args.Duration /= ent.Comp.Resistance;
    }

    public void GiveStunResistance(EntityUid target, float resistance)
    {
        var resistanceComp = EnsureComp<RMCStunResistanceComponent>(target);
        resistanceComp.Resistance = resistance;
        Dirty(target, resistanceComp);
    }
}
