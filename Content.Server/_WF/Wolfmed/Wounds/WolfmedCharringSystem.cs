using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>
/// Charring: the moment a burn reaches its worst stage the tissue under it is dead, and dead tissue is a
/// separate problem. The charring wound carries no damage types at all, so no ointment, no mesh and no
/// amount of damage removal touches it; it comes off in surgery, with a skin graft.
/// </summary>
/// <remarks>
/// The trigger is <see cref="WolfmedCharringBehavior"/> on the burn's top stage, so the threshold is the
/// stage's own severity rather than a number in this file. It fires once per crossing into that stage:
/// a part that is grafted and burned to the top again chars again, one that keeps burning does not char
/// twice for the same fire.
/// </remarks>
public sealed class WolfmedCharringSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WoundSystem _wounds = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        if (args.Kind == WolfmedWoundLifecycle.Removed || args.Severity <= args.OldSeverity ||
            !_prototypes.TryIndex(args.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(args.Severity, out WolfmedCharringBehavior behavior))
            return;

        // Only the crossing counts: a burn already in its top stage has already charred what it is going to.
        if (prototype.TryGetBehavior(args.OldSeverity, out WolfmedCharringBehavior _))
            return;

        TryChar(args.Part, behavior.Wound, behavior.Severity);
    }

    /// <summary>
    /// Leaves charring on the part. Returns the wound, or null when the part's profile does not support
    /// it (a chassis burns, it does not char). Public so a test can skip the burn ladder.
    /// </summary>
    public EntityUid? TryChar(EntityUid part, ProtoId<WoundPrototype> wound, FixedPoint2 severity)
    {
        if (TerminatingOrDeleted(part) || !_wounds.CanCreateWound(part, wound))
            return null;

        return _wounds.CreateOrMergeWound(part, wound, severity);
    }
}
