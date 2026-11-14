using Content.Shared._WF.RMCCore.Actions;

namespace Content.Shared._WF.Xenonids.Actions;

public sealed partial class XenoActionsSystem : EntitySystem
{
    [Dependency] private XenoSystem _xeno = default!;

    public override void Initialize()
    {
        // WOLFGATE: Wolfgate has no ActionValidateEvent; the target check runs when the ability is used
        SubscribeLocalEvent<XenoOffensiveActionComponent, RMCActionUseAttemptEvent>(OnUseAttemptEntityTarget);
    }

    private void OnUseAttemptEntityTarget(Entity<XenoOffensiveActionComponent> ent, ref RMCActionUseAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (args.Target is not { } target)
            return;

        if (!_xeno.CanAbilityAttackTarget(args.User, target, false, true))
            args.Cancelled = true;
    }
}
