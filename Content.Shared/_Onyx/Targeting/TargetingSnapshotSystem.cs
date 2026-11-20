using Content.Shared._Shitmed.Targeting; // WOLFGATE(Wolfmed): D10/WP3#8: Onyx's own Targeting stack is not vendored; use Shitmed's TargetingComponent/TargetBodyPart and HOOK 5's IsSelectable
using Content.Shared.Throwing;

namespace Content.Shared._Onyx.Targeting;

public sealed partial class TargetingSnapshotSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ThrownEvent>(OnThrown);
    }

    private void OnThrown(ref ThrownEvent args)
    {
        if (args.User is { } thrower)
            Capture(args.Thrown, thrower);
    }

    public bool Capture(EntityUid carrier, EntityUid? shooter)
    {
        if (shooter is not { } source ||
            !TryComp(source, out TargetingComponent? targeting) ||
            !SharedTargetingSystem.IsSelectable(targeting.Target))
        {
            RemComp<TargetingSnapshotComponent>(carrier);
            return false;
        }

        var snapshot = EnsureComp<TargetingSnapshotComponent>(carrier);
        snapshot.RequestedTarget = targeting.Target;
        snapshot.Shooter = source;
        Dirty(carrier, snapshot);
        return true;
    }

    public bool Refresh(EntityUid carrier, EntityUid shooter)
    {
        // Preserve the previous request when a reflector has no targeting intent of its own.
        return TryComp(shooter, out TargetingComponent? targeting) &&
               SharedTargetingSystem.IsSelectable(targeting.Target) &&
               Capture(carrier, shooter);
    }
}
