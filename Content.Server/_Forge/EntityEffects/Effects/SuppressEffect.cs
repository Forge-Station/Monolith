using Content.Shared.EntityEffects;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Content.Server._Forge.Chemistry.Addiction;

namespace Content.Server._Forge.EntityEffects.Effects;

[UsedImplicitly]
public sealed partial class SuppressEffect : EntityEffect
{
    [DataField]
    public string? AddictionId { get; set; } = null;

    [DataField]
    public int Time { get; set; } = 30;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-suppress-withdrawal");

    public override void Effect(EntityEffectBaseArgs args)
    {
        var addictionSys = args.EntityManager.System<AddictionSystem>();

        if (AddictionId != null)
            addictionSys.SuppressWithdrawal(args.TargetEntity, AddictionId, Time);
    }
}
