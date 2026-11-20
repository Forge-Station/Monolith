using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared.Body.Part;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Robust.Shared.Audio;
using System.Linq;

namespace Content.Server._WF.Wolfmed.Autodoc;

/// <summary>
/// Playtest 3 SAM: the pod undresses what is in its way. It still cuts only the suit and the jumpsuit; anything else
/// the armour check reads comes off whole, into the delivery tray or onto the floor beside the pod, and is never
/// destroyed. Something it cannot take off is named, and an AUTO pod gives that procedure up after its delay.
/// </summary>
public sealed partial class AutodocSystem
{
    /// <summary>
    /// The slots Shitmed's armour check reads for each part (<c>SharedSurgerySystem.CanPerformStep</c>): a garment in
    /// any of them refuses every step on the part with <c>StepInvalidReason.Armor</c>. Mirrored here because the check
    /// keeps its table inline; <c>PodUndressesWhatItCannotCutTest</c> holds the two together.
    /// </summary>
    public static SlotFlags ArmorSlots(BodyPartType type) => type switch
    {
        BodyPartType.Head => SlotFlags.HEAD,
        BodyPartType.Torso => SlotFlags.OUTERCLOTHING | SlotFlags.INNERCLOTHING,
        BodyPartType.Arm => SlotFlags.OUTERCLOTHING | SlotFlags.INNERCLOTHING,
        BodyPartType.Hand => SlotFlags.GLOVES,
        BodyPartType.Leg => SlotFlags.OUTERCLOTHING | SlotFlags.LEGS,
        BodyPartType.Foot => SlotFlags.FEET,
        _ => SlotFlags.NONE,
    };

    /// <summary>The part the procedure at the head of the queue is working on, and its type.</summary>
    private (EntityUid Part, BodyPartType Type)? BlockedPart(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (ent.Comp.Queue.Count == 0 || ResolvePart(body, ent.Comp.Queue[0].Part) is not { } part ||
            !TryComp(part, out BodyPartComponent? comp))
            return null;

        return (part, comp.PartType);
    }

    /// <summary>Every garment the armour check reads on the blocked part, with the slot it is worn in.</summary>
    private List<(SlotDefinition Slot, EntityUid Item)> Blockers(Entity<AutodocComponent> ent, EntityUid body)
    {
        var result = new List<(SlotDefinition, EntityUid)>();
        if (BlockedPart(ent, body) is not { } blocked)
            return result;

        var flags = ArmorSlots(blocked.Type);
        if (flags == SlotFlags.NONE || !_inventory.TryGetSlots(body, out var slots))
            return result;

        foreach (var slot in slots)
        {
            if ((slot.SlotFlags & flags) != 0 && _inventory.TryGetSlotEntity(body, slot.Name, out var item))
                result.Add((slot, item.Value));
        }

        return result;
    }

    /// <summary>
    /// Clears the blocked part: the suit and the jumpsuit are cut and destroyed as before, everything else the armour
    /// check reads is taken off whole. A hardsuit helmet comes off with its suit, which is a cut slot. Returns what
    /// was cut and what was taken off; anything left behind is named in <see cref="AutodocComponent.BlockingGarment"/>.
    /// </summary>
    private (bool Cut, List<EntityUid> Removed) Undress(Entity<AutodocComponent> ent, EntityUid body)
    {
        var cut = false;
        var removed = new List<EntityUid>();
        foreach (var (slot, item) in Blockers(ent, body))
        {
            if (TerminatingOrDeleted(item) || !_inventory.TryGetSlotEntity(body, slot.Name, out var still) || still != item)
                continue;

            if ((slot.SlotFlags & CutSlots) != 0)
            {
                cut |= Cut(ent, body, slot.Name);
                continue;
            }

            if (TryComp(item, out AttachedClothingComponent? attached) && WornIn(body, attached.AttachedUid, CutSlots) is { } suit)
            {
                cut |= Cut(ent, body, suit);
                continue;
            }

            if (!CanTakeOff(ent, body, slot, item) ||
                !_inventory.TryUnequip(body, slot.Name, out var taken, silent: true, force: true))
                continue;

            if (!StowInTray(ent, taken.Value))
                SlideOff(ent, taken.Value);

            removed.Add(taken.Value);
        }

        // Whatever is still there after all that is what the pod is waiting on.
        ent.Comp.BlockingGarment = null;
        ent.Comp.BlockingSlot = null;
        ent.Comp.GarmentStuck = false;
        foreach (var (slot, item) in Blockers(ent, body))
        {
            ent.Comp.BlockingGarment = item;
            ent.Comp.BlockingSlot = slot.Name;
            ent.Comp.GarmentStuck = true;
            break;
        }

        return (cut, removed);
    }

    /// <summary>Cuts one garment off and destroys it: the shears, not the hands. What it carried lands on the tile.</summary>
    private bool Cut(Entity<AutodocComponent> ent, EntityUid body, string slot)
    {
        if (!_inventory.TryUnequip(body, slot, out var garment, silent: true, force: true))
            return false;

        // Pre-merge review: deleting a coat deleted its pockets' contents with it.
        var coordinates = Transform(ent).Coordinates;
        foreach (var container in _containers.GetAllContainers(garment.Value).ToArray())
            _containers.EmptyContainer(container, true, coordinates);

        QueueDel(garment);
        return true;
    }

    /// <summary>The cut slot this garment is worn in, if any.</summary>
    private string? WornIn(EntityUid body, EntityUid garment, SlotFlags flags)
    {
        if (!_inventory.TryGetSlots(body, out var slots))
            return null;

        foreach (var slot in slots)
        {
            if ((slot.SlotFlags & flags) != 0 && _inventory.TryGetSlotEntity(body, slot.Name, out var worn) && worn == garment)
                return slot.Name;
        }

        return null;
    }

    /// <summary>
    /// The same refusals a person taking it off would meet, asked as the pod: the slot's container (unremoveable
    /// items), then the garment itself (locked or unremovable clothing, a helmet still attached to its suit).
    /// </summary>
    private bool CanTakeOff(Entity<AutodocComponent> ent, EntityUid body, SlotDefinition slot, EntityUid item)
    {
        if (!_inventory.TryGetSlotContainer(body, slot.Name, out var container, out _) ||
            !_containers.CanRemove(item, container))
            return false;

        var attempt = new BeingUnequippedAttemptEvent(ent.Owner, body, item, slot);
        RaiseLocalEvent(item, attempt, true);
        return !attempt.Cancelled;
    }

    /// <summary>Into the delivery tray if it is empty, restoring its lock after.</summary>
    private bool StowInTray(Entity<AutodocComponent> ent, EntityUid item)
    {
        if (_slots.GetItemOrNull(ent.Owner, AutodocComponent.TraySlotId) != null ||
            !_slots.TryGetSlot(ent.Owner, AutodocComponent.TraySlotId, out var tray))
            return false;

        var locked = tray.Locked;
        _slots.SetLock(ent.Owner, AutodocComponent.TraySlotId, false);
        var stowed = _slots.TryInsert(ent.Owner, AutodocComponent.TraySlotId, item, null);
        _slots.SetLock(ent.Owner, AutodocComponent.TraySlotId, locked);
        return stowed;
    }

    /// <summary>
    /// An AUTO pod that has waited its delay on something it cannot take off gives the procedure up the way a stall
    /// does, without the stall's line: the clothing line already said what is wrong.
    /// </summary>
    private void AbandonForClothing(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (ent.Comp.Queue.Count == 0)
            return;

        StallProcedure(ent, body, ent.Comp.Queue[0], speak: false);
    }

    /// <summary>What the WAITING line says: the garment and the slot it is in, when the pod knows which.</summary>
    private string? BlockingStatus(Entity<AutodocComponent> ent)
    {
        if (ent.Comp.BlockingGarment is not { } garment || TerminatingOrDeleted(garment))
            return null;

        var slot = ent.Comp.BlockingSlot ?? string.Empty;
        if (GetOccupant(ent) is { } body && _inventory.TryGetSlots(body, out var slots))
        {
            foreach (var definition in slots)
            {
                if (definition.Name == ent.Comp.BlockingSlot && !string.IsNullOrEmpty(definition.DisplayName))
                    slot = definition.DisplayName;
            }
        }

        // The template's display names are plain words ("Gloves", "Shoes"), not locale ids.
        return Loc.GetString("wolfmed-autodoc-status-waiting-garment",
            ("item", Name(garment).ToUpperInvariant()),
            ("slot", slot.ToUpperInvariant()));
    }

    /// <summary>The shears' sound and line, and the line for what came off whole.</summary>
    private void AnnounceUndress(Entity<AutodocComponent> ent, bool cut, List<EntityUid> removed)
    {
        if (cut)
        {
            Speak(ent, AutodocVoiceEvent.Cutting);
            _audio.PlayPvs(ent.Comp.CutSound, ent.Owner, DuckedParams(ent, AudioParams.Default));
        }

        if (removed.Count == 0)
            return;

        var names = new List<string>();
        foreach (var item in removed)
            names.Add(Name(item).ToUpperInvariant());

        Speak(ent, AutodocVoiceEvent.Removing, ("item", string.Join(", ", names)));
    }
}
