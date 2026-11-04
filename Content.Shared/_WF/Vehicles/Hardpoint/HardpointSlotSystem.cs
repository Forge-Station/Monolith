using System.Collections.Generic;
using Content.Shared._WF.Vehicles.Turret;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Network;

namespace Content.Shared._WF.Vehicles.Hardpoint;

/// <summary>Installs and removes hardpoints: the interaction, do-afters and the removal window messages.</summary>
public sealed partial class HardpointSlotSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private HardpointSystem _hardpoints = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private Core.VehicleTopologySystem _topology = default!;

    private readonly HashSet<(EntityUid Owner, string SlotId)> _completingRemovals = new();
    private readonly List<(EntityUid Owner, string SlotId)> _invalidParts = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HardpointSlotsComponent, ItemSlotInsertAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<HardpointSlotsComponent, HardpointInsertDoAfterEvent>(OnInsertDoAfter);
        SubscribeLocalEvent<HardpointSlotsComponent, InteractUsingEvent>(OnSlotsInteractUsing, before: new[] { typeof(ItemSlotsSystem) });
        SubscribeLocalEvent<HardpointSlotsComponent, BoundUIOpenedEvent>(OnHardpointUiOpened);
        SubscribeLocalEvent<HardpointSlotsComponent, BoundUIClosedEvent>(OnHardpointUiClosed);
        SubscribeLocalEvent<HardpointSlotsComponent, HardpointRemoveMessage>(OnHardpointRemoveMessage);
        SubscribeLocalEvent<HardpointSlotsComponent, HardpointRemoveDoAfterEvent>(OnHardpointRemoveDoAfter);
        SubscribeLocalEvent<HardpointSlotsComponent, ItemSlotEjectAttemptEvent>(OnHardpointEjectAttempt);
    }

    private void OnInsertAttempt(Entity<HardpointSlotsComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.User == null)
            return;

        var state = EnsureComp<HardpointStateComponent>(ent.Owner);

        if (!_hardpoints.TryGetSlot(ent.Comp, args.Slot.ID, out var slot))
            return;

        if (state.CompletingInserts.Contains(slot.Id))
            return;

        if (!_hardpoints.IsValidHardpoint(args.Item, ent.Comp, slot))
        {
            args.Cancelled = true;
            return;
        }

        if (slot.InsertDelay <= 0f)
            return;

        args.Cancelled = true;
    }

    private void OnInsertDoAfter(Entity<HardpointSlotsComponent> ent, ref HardpointInsertDoAfterEvent args)
    {
        HardpointSlotLocation? resolvedLocation = null;
        if (_hardpoints.TryResolveSlotLocation(ent.Owner, ent.Comp, args.SlotId, out var location))
        {
            resolvedLocation = location;
            location.State.PendingInserts.Remove(location.Definition.Id);
        }
        else
        {
            EnsureComp<HardpointStateComponent>(ent.Owner).PendingInserts.Remove(args.SlotId);
        }

        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (args.Used is not { } item || string.IsNullOrEmpty(args.SlotId))
            return;

        if (resolvedLocation is not { } finalLocation &&
            !_hardpoints.TryResolveSlotLocation(ent.Owner, ent.Comp, args.SlotId, out finalLocation))
        {
            return;
        }

        if (!_hardpoints.IsValidHardpoint(item, finalLocation.Slots, finalLocation.Definition))
            return;

        finalLocation.State.CompletingInserts.Add(finalLocation.Definition.Id);
        _itemSlots.TryInsert(finalLocation.Owner, finalLocation.Slot, item, args.User, excludeUserAudio: false);
        finalLocation.State.CompletingInserts.Remove(finalLocation.Definition.Id);
    }

    private void OnSlotsInteractUsing(Entity<HardpointSlotsComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (TryStartHardpointInsert(ent, args.User, args.Used))
        {
            args.Handled = true;
            return;
        }

        if (HasComp<HardpointItemComponent>(args.Used) &&
            HasComp<VehicleTurretAttachmentComponent>(args.Used))
        {
            _popup.PopupClient(Loc.GetString("rmc-vehicle-turret-no-base"), ent.Owner, args.User);
            args.Handled = true;
            return;
        }

        if (!_tool.HasQuality(args.Used, ent.Comp.RemoveToolQuality))
            return;

        if (_ui.TryOpenUi(ent.Owner, HardpointUiKey.Key, args.User))
        {
            _hardpoints.UpdateHardpointUi(ent.Owner, ent.Comp);
            args.Handled = true;
        }
    }

    private bool TryStartHardpointInsert(Entity<HardpointSlotsComponent> ent, EntityUid user, EntityUid used)
    {
        if (!HasComp<HardpointItemComponent>(used))
            return false;

        if (!_hardpoints.TryFindEmptyInstallLocation(ent.Owner, ent.Comp, used, out var targetLocation))
            return false;

        var vehicle = _topology.TryGetVehicle(ent.Owner, out var root) ? root : ent.Owner;
        var fit = new HardpointFitAttemptEvent(user, used);
        RaiseLocalEvent(vehicle, ref fit);
        if (fit.Cancelled)
            return true;

        if (targetLocation.Definition.InsertDelay <= 0f)
        {
            targetLocation.State.CompletingInserts.Add(targetLocation.Definition.Id);
            _itemSlots.TryInsertFromHand(targetLocation.Owner, targetLocation.Slot, user);
            targetLocation.State.CompletingInserts.Remove(targetLocation.Definition.Id);
            return true;
        }

        if (IsClientSide(ent.Owner))
            return true;

        DropOrphanedInserts(targetLocation.State);

        if (targetLocation.State.PendingInserts.ContainsValue(user))
            return true;

        if (!targetLocation.State.PendingInserts.TryAdd(targetLocation.Definition.Id, user))
            return true;

        var slotId = targetLocation.Path.ToCompositeId();
        var doAfter = new DoAfterArgs(EntityManager, user, targetLocation.Definition.InsertDelay, new HardpointInsertDoAfterEvent(slotId), ent.Owner, ent.Owner, used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            BreakOnHandChange = true,
            BreakOnDropItem = true,
            BreakOnWeightlessMove = true,
            NeedHand = true,
            RequireCanInteract = true,
            AttemptFrequency = AttemptFrequency.StartAndEnd,
            DuplicateCondition = DuplicateConditions.SameEvent,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            targetLocation.State.PendingInserts.Remove(targetLocation.Definition.Id);
            return true;
        }

        return true;
    }

    /// <summary>Forgets installs whose installer was deleted mid do-after: the do-after went with them and never reports back, so the slot would stay claimed.</summary>
    private void DropOrphanedInserts(HardpointStateComponent state)
    {
        if (state.PendingInserts.Count == 0)
            return;

        List<string>? orphaned = null;
        foreach (var (slotId, user) in state.PendingInserts)
        {
            if (TerminatingOrDeleted(user))
                (orphaned ??= new List<string>()).Add(slotId);
        }

        if (orphaned == null)
            return;

        foreach (var slotId in orphaned)
        {
            state.PendingInserts.Remove(slotId);
        }
    }

    private void OnHardpointUiOpened(Entity<HardpointSlotsComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!Equals(args.UiKey, HardpointUiKey.Key))
            return;

        var state = EnsureComp<HardpointStateComponent>(ent.Owner);
        state.LastUiError = null;
        _hardpoints.UpdateHardpointUi(ent.Owner, ent.Comp, state: state);
    }

    private void OnHardpointUiClosed(Entity<HardpointSlotsComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!Equals(args.UiKey, HardpointUiKey.Key))
            return;

        var state = EnsureComp<HardpointStateComponent>(ent.Owner);
        state.PendingRemovals.Clear();
        state.LastUiError = null;
    }

    public override void Update(float frameTime)
    {
        if (_invalidParts.Count == 0)
            return;

        var queued = _invalidParts.ToArray();
        _invalidParts.Clear();

        foreach (var (owner, slotId) in queued)
        {
            if (TerminatingOrDeleted(owner) ||
                !TryComp(owner, out HardpointSlotsComponent? slots) ||
                !_hardpoints.TryGetSlot(slots, slotId, out var slot) ||
                !_itemSlots.TryGetSlot(owner, slotId, out var itemSlot) ||
                itemSlot.Item is not { } item ||
                _hardpoints.IsValidHardpoint(item, slots, slot))
            {
                continue;
            }

            TryEjectInvalidPart(owner, slotId);
        }
    }

    /// <summary>Queues a part that sits in a slot it does not fit to be taken out on the next update; the container cannot give it up while it is still being inserted.</summary>
    public void QueueEjectInvalidPart(EntityUid owner, string slotId)
    {
        if (_net.IsClient)
            return;

        _invalidParts.Add((owner, slotId));
    }

    /// <summary>Takes a part that does not fit its slot back out, outside the removal do-after the slot otherwise insists on.</summary>
    private bool TryEjectInvalidPart(EntityUid owner, string slotId)
    {
        var key = (owner, slotId);
        _completingRemovals.Add(key);

        try
        {
            return _itemSlots.TryEject(owner, slotId, null, out _, excludeUserAudio: true);
        }
        finally
        {
            _completingRemovals.Remove(key);
        }
    }

    private void OnHardpointEjectAttempt(Entity<HardpointSlotsComponent> ent, ref ItemSlotEjectAttemptEvent args)
    {
        if (args.Slot.ID is not { } slotId)
            return;

        if (!_hardpoints.TryGetSlot(ent.Comp, slotId, out _))
            return;

        if (!_completingRemovals.Contains((ent.Owner, slotId)))
            args.Cancelled = true;
    }

    private void OnHardpointRemoveMessage(Entity<HardpointSlotsComponent> ent, ref HardpointRemoveMessage args)
    {
        if (!Equals(args.UiKey, HardpointUiKey.Key))
            return;

        if (args.Actor == default || !Exists(args.Actor))
            return;

        TryStartHardpointRemoval(ent.Owner, ent.Comp, args.Actor, args.SlotId);
    }

    private void OnHardpointRemoveDoAfter(Entity<HardpointSlotsComponent> ent, ref HardpointRemoveDoAfterEvent args)
    {
        var state = EnsureComp<HardpointStateComponent>(ent.Owner);

        void SetErrorAndRefresh(string? error)
        {
            state.LastUiError = error;
            _hardpoints.SetContainingVehicleUiError(ent.Owner, error);
            _hardpoints.UpdateHardpointUi(ent.Owner, ent.Comp, state: state);
            _hardpoints.UpdateContainingVehicleUi(ent.Owner);
        }

        HardpointSlotLocation? resolvedLocation = null;
        if (_hardpoints.TryResolveSlotLocation(ent.Owner, ent.Comp, args.SlotId, out var location))
        {
            resolvedLocation = location;
            location.State.PendingRemovals.Remove(location.Definition.Id);
        }
        else
        {
            state.PendingRemovals.Remove(args.SlotId);
        }

        if (args.Cancelled || args.Handled)
        {
            SetErrorAndRefresh(args.Cancelled ? Loc.GetString("wf-vehicles-hardpoint-error-cancelled") : null);
            return;
        }

        args.Handled = true;

        if (resolvedLocation is not { } finalLocation &&
            !_hardpoints.TryResolveSlotLocation(ent.Owner, ent.Comp, args.SlotId, out finalLocation))
        {
            SetErrorAndRefresh(Loc.GetString("wf-vehicles-hardpoint-error-no-access"));
            return;
        }

        if (!finalLocation.Slot.HasItem)
        {
            SetErrorAndRefresh(Loc.GetString("wf-vehicles-hardpoint-error-empty-slot"));
            return;
        }

        var key = (finalLocation.Owner, finalLocation.Definition.Id);
        _completingRemovals.Add(key);
        var ejected = _itemSlots.TryEject(finalLocation.Owner, finalLocation.Slot, null, out var ejectedItem, true);
        _completingRemovals.Remove(key);

        if (!ejected || ejectedItem == null)
        {
            SetErrorAndRefresh(Loc.GetString("wf-vehicles-hardpoint-error-eject-failed"));
            return;
        }

        if (TryComp(ejectedItem.Value, out HardpointIntegrityComponent? ejectedIntegrity) && ejectedIntegrity.Integrity <= 0f)
        {
            if (_net.IsServer)
            {
                _popup.PopupEntity(Loc.GetString("rmc-hardpoint-disintegrates", ("item", ejectedItem.Value)), finalLocation.Owner, PopupType.MediumCaution);
                QueueDel(ejectedItem.Value);
            }

            SetErrorAndRefresh(null);
            _hardpoints.RefreshCanRun(ent.Owner);
            return;
        }

        if (!_hands.TryPickupAnyHand(args.User, ejectedItem.Value))
            _transform.SetCoordinates(ejectedItem.Value, Transform(args.User).Coordinates);

        SetErrorAndRefresh(null);
        _hardpoints.RefreshCanRun(ent.Owner);
    }

    private void TryStartHardpointRemoval(
        EntityUid uid,
        HardpointSlotsComponent component,
        EntityUid user,
        string? slotId,
        EntityUid? uiOwnerUid = null)
    {
        uiOwnerUid ??= uid;
        var uiOwnerState = EnsureComp<HardpointStateComponent>(uiOwnerUid.Value);

        void SetError(string error)
        {
            uiOwnerState.LastUiError = error;
        }

        void RefreshUi()
        {
            _hardpoints.UpdateHardpointUi(uiOwnerUid.Value, state: uiOwnerState);
        }

        uiOwnerState.LastUiError = null;

        if (string.IsNullOrWhiteSpace(slotId))
        {
            SetError(Loc.GetString("wf-vehicles-hardpoint-error-invalid-slot"));
            RefreshUi();
            return;
        }

        if (!_hardpoints.TryResolveSlotLocation(uid, component, slotId, out var location))
        {
            SetError(Loc.GetString("wf-vehicles-hardpoint-error-unknown-slot"));
            RefreshUi();
            return;
        }

        if (location.Slot.Item is not { } installed)
        {
            SetError(Loc.GetString("wf-vehicles-hardpoint-error-empty-slot"));
            RefreshUi();
            return;
        }

        if (TryComp(installed, out HardpointSlotsComponent? attachedSlots) &&
            TryComp(installed, out ItemSlotsComponent? attachedItemSlots) &&
            _hardpoints.HasAttachedHardpoints(installed, attachedSlots, attachedItemSlots))
        {
            var error = Loc.GetString("wf-vehicles-hardpoint-error-has-attachments");
            _popup.PopupEntity(error, location.Owner, user);
            SetError(error);
            RefreshUi();
            return;
        }

        DropOrphanedInserts(location.State);

        if (location.State.PendingInserts.ContainsKey(location.Definition.Id) ||
            location.State.CompletingInserts.Contains(location.Definition.Id))
        {
            var error = Loc.GetString("wf-vehicles-hardpoint-error-still-installing");
            _popup.PopupEntity(error, user, user);
            SetError(error);
            RefreshUi();
            return;
        }

        var delay = location.Definition.RemoveDelay > 0f ? location.Definition.RemoveDelay : location.Definition.InsertDelay;
        var slotPath = location.Path.ToCompositeId();

        if (!_hardpoints.TryGetPryingTool(user, location.Slots.RemoveToolQuality, out var tool))
        {
            var error = Loc.GetString("wf-vehicles-hardpoint-error-need-tool");
            _popup.PopupEntity(error, user, user);
            SetError(error);
            RefreshUi();
            return;
        }

        if (!location.State.PendingRemovals.Add(location.Definition.Id))
        {
            SetError(Loc.GetString("wf-vehicles-hardpoint-error-already-removing"));
            RefreshUi();
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, user, delay, new HardpointRemoveDoAfterEvent(slotPath), uiOwnerUid.Value, uiOwnerUid.Value, tool)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            BreakOnHandChange = true,
            BreakOnDropItem = true,
            BreakOnWeightlessMove = true,
            NeedHand = true,
            RequireCanInteract = true,
            DuplicateCondition = DuplicateConditions.SameEvent,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            location.State.PendingRemovals.Remove(location.Definition.Id);
            SetError(Loc.GetString("wf-vehicles-hardpoint-error-start-failed"));
            RefreshUi();
            return;
        }

        uiOwnerState.LastUiError = null;
        RefreshUi();
    }
}
