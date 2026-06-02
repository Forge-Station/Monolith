using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.StatusEffect;
using Robust.Shared.Prototypes;
using System.Numerics;

namespace Content.Server._Forge.EntityEffects.Effects;

public sealed partial class TunnelVision : EntityEffect
{
    [DataField]
    public float Outer = 1.0f;
    [DataField]
    public float Inner = 0.5f;
    [DataField]
    public float Time = 2.0f;
    [DataField]
    public float Alpha = 0.8f;
    [DataField]
    public Vector3 Color = new(1f, 0f, 0f);
    [DataField]
    public bool Refresh = true;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var time = Time;
        if (args is EntityEffectReagentArgs reagentArgs)
            time *= reagentArgs.Scale.Float();

        var statusSys = args.EntityManager.EntitySysManager.GetEntitySystem<StatusEffectsSystem>();

        var comp = args.EntityManager.EnsureComponent<TunnelVisionComponent>(args.TargetEntity);
        comp.OuterRadius = Outer;
        comp.InnerRadius = Inner;
        comp.DarknessAlpha = Alpha;
        comp.Color = Color;

        statusSys.TryAddStatusEffect<TunnelVisionComponent>(args.TargetEntity, "TunnelVision", TimeSpan.FromSeconds(time), Refresh);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("reagent-effect-guidebook-tunnel-vision", ("chance", Probability));
}
