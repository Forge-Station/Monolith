using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.EntityEffects.Effects;

public sealed partial class GenericDescEffect : EntityEffect
{
    [DataField(required: true)]
    public LocId Description = string.Empty;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString(Description);
    }

    public override void Effect(EntityEffectBaseArgs args)
    {

    }
}
