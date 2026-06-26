using Content.Shared._Forge.Overlay;
using Content.Shared.EntityEffects;
using Content.Shared.StatusEffect;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Forge.EntityEffects.Effects;

[DataDefinition]
public sealed partial class TunnelVision : EntityEffect
{
    [ValidatePrototypeId<StatusEffectPrototype>]
    public const string Key = "TunnelVision";

    [DataField(required: true)]
    public float Intensity { get; set; } = 0.3f;

    [DataField]
    public float Pulse { get; set; } = 3f;

    [DataField]
    public float Darkness { get; set; } = 0.8f;

    [DataField]
    public Color Color { get; set; } = Color.Red;

    [DataField]
    public float Time { get; set; } = 10f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var uid = args.TargetEntity;
        var entMan = args.EntityManager;
        var statusSys = entMan.System<StatusEffectsSystem>();
        statusSys.TryAddStatusEffect<TunnelVisionComponent>(uid, Key, TimeSpan.FromSeconds(Time), true);

        if (!entMan.TryGetComponent<TunnelVisionComponent>(uid, out var comp))
            return;

        comp.Intensity = Intensity;
        comp.PulseRate = Pulse;
        comp.DarknessAlpha = Math.Clamp(Darkness, 0f, 1f);
        comp.Color = Color;
    }
}
