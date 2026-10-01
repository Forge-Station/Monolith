using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.EntityEffects;

/// <summary>
/// Cools an overheated chassis. Sits on the water touch reaction of both the humanoid base and the silicon
/// base, so an extinguisher, a spray bottle, a puddle and a splash all take heat out of an IPC or a
/// cybernetic limb, following W4's wash effect.
/// </summary>
public sealed partial class WolfmedCoolOverheating : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-cool-overheating", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.System<WolfmedOverheatingSystem>().Douse(args.TargetEntity);
    }
}
