using Content.Shared.Interaction.Events;

namespace Content.Shared._Starlight.NullSpace;

public abstract partial class SharedShowNullSpaceSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShowNullSpaceComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<ShowNullSpaceComponent, AttackAttemptEvent>(OnAttackAttempt);
    }

    private void OnAttackAttempt(EntityUid uid, ShowNullSpaceComponent component, AttackAttemptEvent args)
    {
        if (HasComp<NullSpaceComponent>(uid))
            return;

        if (HasComp<NullSpaceComponent>(args.Target))
            args.Cancel();
    }

    private void OnInteractionAttempt(EntityUid uid, ShowNullSpaceComponent component, ref InteractionAttemptEvent args)
    {
        if (HasComp<NullSpaceComponent>(uid))
            return;

        if (HasComp<NullSpaceComponent>(args.Target))
            args.Cancelled = true;
    }
}
