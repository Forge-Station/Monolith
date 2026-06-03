using Content.Shared._Forge.Chemistry.Addiction;
using Content.Shared.EntityEffects;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Content.Shared.StatusEffect;

namespace Content.Server._Forge.EntityEffects.Effects;

/// <summary>
///    This shit exists exclusively for the AddictionSystem for one simple reason:
///    <see cref="MovementSpeedModifierSystem"/> overwrite the original modifier when the stage changes.
///    Protection against the overwriting curve and persistence
///    at the wrong speed even after successful addiction treatment.
/// </summary>

[UsedImplicitly]
public sealed partial class AddictionMovespeedEffect : EntityEffect
{
    [DataField(required: true)]
    public string Key { get; set; } = default!;

    [DataField]
    public float WalkSpeedModifier { get; set; } = 1f;

    [DataField]
    public float SprintSpeedModifier { get; set; } = 1f;

    [DataField]
    public float StatusLifetime = 9999f;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var statusSys = args.EntityManager.System<StatusEffectsSystem>();

        statusSys.TryAddStatusEffect<AddictionMovespeedComponent>(args.TargetEntity,
            Key,
            TimeSpan.FromSeconds(StatusLifetime),
            true);

        if (args.EntityManager.TryGetComponent(args.TargetEntity, out AddictionMovespeedComponent? comp))
        {
            comp.WalkSpeedModifier = WalkSpeedModifier;
            comp.SprintSpeedModifier = SprintSpeedModifier;
        }
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => null;

}
