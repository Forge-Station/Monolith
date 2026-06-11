using Robust.Shared.Prototypes;
using Content.Shared.EntityEffects;
using Content.Shared._Forge.Body.Components;
using Content.Shared._Forge.Body.Syndromes;
using Content.Server._Forge.Body.Syndromes;

namespace Content.Server._Forge.EntityEffects.Effects;

public sealed partial class RemoveSyndromeEffect : EntityEffect
{
    [DataField(required: true)]
    public string Syndrome { get; set; } = string.Empty;

    [DataField]
    public float Severity = 2f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        if (!prototype.TryIndex<SyndromePrototype>(Syndrome, out var syndrome))
            return null;

        return Loc.GetString("reagent-effect-remove-syndrome", ("syndrome", Loc.GetString(syndrome.Name)));
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.System<SyndromeSystem>().ModifySeverity(args.TargetEntity, Syndrome, -Severity);
    }
}
