using Content.Shared.Body.Components;
using Content.Shared.Climbing.Events;
using Content.Shared.Climbing.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DragDrop;
using Content.Shared.Popups;
using Robust.Shared.Containers;

namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>
/// The half of the autodoc both sides need: whether a body is lying inside a pod. The surgery system asks
/// this instead of looking for a buckle and an operating table, so a pod occupant satisfies every surgery
/// that wants a table.
/// </summary>
public sealed class SharedAutodocSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedPopupSystem _popup = default!; // Playtest 3 SAM

    public override void Initialize()
    {
        base.Initialize();
        // The drag-and-drop check runs on the client too; answered only on the server it came back "unknown"
        // and whether a drop worked depended on whatever else sat on the pod.
        // Playtest 3 SAM: answered before the climb's, which would otherwise offer an occupied pod as a place to vault.
        SubscribeLocalEvent<AutodocComponent, CanDropTargetEvent>(OnCanDropTarget, before: new[] { typeof(ClimbSystem) });
        SubscribeLocalEvent<AutodocComponent, AttemptClimbEvent>(OnAttemptClimb);
        SubscribeLocalEvent<AutodocComponent, ItemSlotInsertAttemptEvent>(OnSlotInsertAttempt);
    }

    private void OnCanDropTarget(Entity<AutodocComponent> ent, ref CanDropTargetEvent args)
    {
        if (args.Handled)
            return;

        var empty = !_container.TryGetContainer(ent.Owner, AutodocComponent.BodyContainerId, out var container) ||
                    container.ContainedEntities.Count == 0;
        args.CanDrop = empty && !ent.Comp.Locked && HasComp<BodyComponent>(args.Dragged);
        args.Handled = true;
    }

    /// <summary>
    /// Playtest 3 SAM: nobody climbs onto the pod. Every constructible machine is climbable here, and a body lying on
    /// the pod's tile is drawn on its bed or under its lid, where it looks like a second patient.
    /// </summary>
    private void OnAttemptClimb(Entity<AutodocComponent> ent, ref AttemptClimbEvent args)
    {
        if (args.Cancelled)
            return;

        args.Cancelled = true;
        _popup.PopupClient(Loc.GetString("wolfmed-autodoc-no-climb"), args.User, args.User);
    }

    /// <summary>Playtest 3 SAM: the delivery tray takes limbs, organs and tools, never a whole body.</summary>
    private void OnSlotInsertAttempt(Entity<AutodocComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.Slot.ID == AutodocComponent.TraySlotId && HasComp<BodyComponent>(args.Item))
            args.Cancelled = true;
    }

    /// <summary>The pod this body is lying in, or null.</summary>
    public Entity<AutodocComponent>? GetPod(EntityUid body)
    {
        if (!_container.TryGetContainingContainer((body, null, null), out var container) ||
            container.ID != AutodocComponent.BodyContainerId ||
            !TryComp(container.Owner, out AutodocComponent? autodoc))
            return null;

        return (container.Owner, autodoc);
    }

    /// <summary>True while the body is inside a pod, which is what the operating-table condition wants.</summary>
    public bool OnOperatingPlatform(EntityUid body) => GetPod(body) != null;
}
