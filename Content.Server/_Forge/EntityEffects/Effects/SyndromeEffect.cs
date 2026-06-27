using Robust.Shared.Prototypes;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared._Forge.Body.Components;
using Content.Shared._Forge.Body.Syndromes;
using Content.Server._Forge.Body.Syndromes;

namespace Content.Server._Forge.EntityEffects.Effects;

public sealed partial class SyndromeEffect : EntityEffect
{
    [DataField(required: true)]
    public string Syndrome { get; set; } = string.Empty;

    [DataField]
    public bool RefreshFlare { get; set; } = true;

    [DataField]
    public float Severity { get; set; } = 2.0f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        if (!prototype.TryIndex<SyndromePrototype>(Syndrome, out var syndrome))
            return null;

        return Loc.GetString("reagent-effect-add-syndrome", ("syndrome", Loc.GetString(syndrome.Name)));
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.System<SyndromeSystem>().AddSyndrome(args.TargetEntity, Syndrome, Severity, refreshFlare: RefreshFlare);
    }
}
