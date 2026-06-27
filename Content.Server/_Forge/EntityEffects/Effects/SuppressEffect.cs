using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Content.Shared.EntityEffects;
using Content.Shared._Forge.Body.Components;
using Content.Shared._Forge.Body.Syndromes;
using Content.Server._Forge.Body.Syndromes;

namespace Content.Server._Forge.EntityEffects.Effects;

[UsedImplicitly]
public sealed partial class SuppressEffect : EntityEffect
{
    [DataField(required: true)]
    public string Syndrome = string.Empty;

    [DataField]
    public float Duration = 60f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        if (!prototype.TryIndex<SyndromePrototype>(Syndrome, out var syndrome))
            return null;

        return Loc.GetString("reagent-effect-guidebook-suppress-withdrawal", ("syndrome", Loc.GetString(syndrome.Name)));
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.System<SyndromeSystem>().SuppressFlare(args.TargetEntity, Syndrome, Duration);
    }
}
