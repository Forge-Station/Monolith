using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Gibbing.Events;

namespace Content.Shared._WF.Wolfmed.Compat;

/// <summary>Onyx-shaped body helpers mapped onto Wolfgate's Shitmed body system.</summary>
public sealed class WolfmedBodySystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundableComponent, AttemptEntityContentsGibEvent>(OnAttemptContentsGib);
    }

    /// <summary>
    /// Gibbing a part dumps every container on it. Wound entities sit in nullspace with no physics, so dropping
    /// them logs a fling error and leaves them pointing at a deleted part; keeping them contained deletes them
    /// with the part instead.
    /// </summary>
    private void OnAttemptContentsGib(Entity<WoundableComponent> part, ref AttemptEntityContentsGibEvent args)
    {
        args.ExcludedContainers ??= new List<string>();
        args.ExcludedContainers.Add(WoundableComponent.ContainerId);
    }

    /// <summary>Detaches a part from its parent slot and drops it, mirroring Onyx's TryDetachPart. `reparent` is ignored.</summary>
    public bool TryDetachPart(EntityUid part, bool reparent = true)
    {
        if (_body.GetParentPartAndSlotOrNull(part) is not { } parentSlot
            || !HasComp<BodyPartComponent>(part)
            || !_body.CanDetachPart(parentSlot.Parent, parentSlot.Slot, part))
            return false;

        // DropPart is protected; the amputate event is the public door to it, and it also drops held
        // items and raises BodyPartDroppedEvent, which Wolfgate appearance/cybernetics/targeting listen for.
        var ev = new AmputateAttemptEvent(part);
        RaiseLocalEvent(part, ref ev);
        return _body.GetParentPartOrNull(part) is null;
    }
}
