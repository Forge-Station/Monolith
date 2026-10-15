using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Containers;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;

namespace Content.Server._Forge.KIAS.Controllers;

public sealed class KiasControllerPhysicalSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private IAdminManager _admins = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<KiasControllerCardComponent, MapInitEvent>(OnCardInit);
        SubscribeLocalEvent<KiasControllerCardComponent, ComponentStartup>(OnCardStartup);
        SubscribeLocalEvent<KiasControllerRackComponent, MapInitEvent>(OnRackInit);
        SubscribeLocalEvent<KiasControllerRackComponent, EntInsertedIntoContainerMessage>(OnRackInsert);
        SubscribeLocalEvent<KiasControllerRackComponent, EntRemovedFromContainerMessage>(OnRackRemove);
        SubscribeLocalEvent<KiasControllerRackComponent, InteractUsingEvent>(OnRackUse, before: new[] { typeof(ItemSlotsSystem) });
        SubscribeLocalEvent<KiasControllerRackComponent, ItemSlotInsertAttemptEvent>(OnRackInsertAttempt);
        SubscribeLocalEvent<KiasControllerRackComponent, ItemSlotEjectAttemptEvent>(OnRackEjectAttempt);
        SubscribeLocalEvent<KiasControllerRackComponent, ExaminedEvent>(OnRackExamine);
        SubscribeLocalEvent<KiasControllerProgrammerComponent, EntInsertedIntoContainerMessage>(OnProgrammerInsert);
        SubscribeLocalEvent<KiasControllerProgrammerComponent, MapInitEvent>(OnProgrammerInit);
        SubscribeLocalEvent<KiasControllerProgrammerComponent, EntRemovedFromContainerMessage>(OnProgrammerRemove);
        SubscribeLocalEvent<KiasControllerProgrammerComponent, ItemSlotInsertAttemptEvent>(OnProgrammerInsertAttempt);
        SubscribeLocalEvent<KiasControllerProgrammerComponent, ItemSlotEjectAttemptEvent>(OnProgrammerEjectAttempt);
        SubscribeLocalEvent<KiasControllerProgrammerComponent, ContainerIsRemovingAttemptEvent>(OnProgrammerRemoveAttempt);
    }

    private void OnCardInit(Entity<KiasControllerCardComponent> ent, ref MapInitEvent args) => SyncCardMetadata(ent, ent.Comp);

    private void OnCardStartup(Entity<KiasControllerCardComponent> ent, ref ComponentStartup args) => SyncCardMetadata(ent, ent.Comp);

    public void SyncCardMetadata(EntityUid uid, KiasControllerCardComponent card)
    {
        var name = card.Program.Name == "Controller" && card.Revision == 0
            ? Loc.GetString("ent-KiasProgrammableController") : card.Program.Name;
        _metadata.SetEntityName(uid, name);
        _metadata.SetEntityDescription(uid, Loc.GetString("ent-KiasProgrammableController.desc"));
    }

    public bool CanConfigure(EntityUid machine, EntityUid actor) => !TerminatingOrDeleted(machine)
        && (CanMapEdit(machine, actor) || Transform(machine).GridUid is { } grid && _kias.CanConfigure(grid, actor));

    public bool CanMapEdit(EntityUid machine, EntityUid actor) => !TerminatingOrDeleted(machine)
        && !TerminatingOrDeleted(actor) && HasComp<KiasControllerProgrammerComponent>(machine)
        && Transform(machine).MapUid is { } map && Transform(actor).MapUid == map
        && !_maps.IsInitialized(map) && _maps.IsPaused(map)
        && _admins.GetAdminData(actor)?.HasFlag(AdminFlags.Mapping) == true;

    public bool CanProgram(EntityUid machine, EntityUid actor) => _kias.IsOnline(machine) || CanMapEdit(machine, actor);

    public EntityUid? Card(EntityUid machine, string slot) => _slots.TryGetSlot(machine, slot, out var itemSlot) ? itemSlot.Item : null;

    public int Inserted(EntityUid rack)
    {
        var count = 0;
        for (var i = 0; i < KiasControllerRackComponent.SlotCount; i++)
            if (Card(rack, KiasControllerRackComponent.SlotId(i)) is { } card && HasComp<KiasControllerCardComponent>(card)) count++;
        return count;
    }

    public void RefreshLoad(Entity<KiasControllerRackComponent> rack)
    {
        rack.Comp.CurrentLoad = Math.Max(0, rack.Comp.BasePowerLoad) + Inserted(rack) * Math.Max(0, rack.Comp.PerControllerLoad);
        Content.Shared.Power.Components.SharedApcPowerReceiverComponent? receiver = null;
        if (_power.ResolveApc(rack, ref receiver)) _power.SetLoad(receiver, rack.Comp.CurrentLoad);
    }

    private void OnRackInit(Entity<KiasControllerRackComponent> ent, ref MapInitEvent args) => RefreshLoad(ent);
    private void OnRackInsert(Entity<KiasControllerRackComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        RefreshLoad(ent);
        EntityManager.System<KiasControllerRuntimeSystem>().ContainerChanged(ent);
        EntityManager.System<KiasControllerUiSystem>().RefreshRack(ent);
    }
    private void OnRackRemove(Entity<KiasControllerRackComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        RefreshLoad(ent);
        EntityManager.System<KiasControllerRuntimeSystem>().ContainerChanged(ent, args.Entity);
        EntityManager.System<KiasControllerUiSystem>().RefreshRack(ent);
    }
    private void OnRackInsertAttempt(Entity<KiasControllerRackComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.User is { } actor && !CanConfigure(ent, actor)) args.Cancelled = true;
    }
    private void OnRackEjectAttempt(Entity<KiasControllerRackComponent> ent, ref ItemSlotEjectAttemptEvent args)
    {
        if (args.User is { } actor && !CanConfigure(ent, actor)) args.Cancelled = true;
    }

    private void OnRackUse(Entity<KiasControllerRackComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<KiasControllerCardComponent>(args.Used)) return;
        args.Handled = true;
        if (!CanConfigure(ent, args.User)) { _popup.PopupEntity(Loc.GetString("kias-owner-only"), ent, args.User); return; }
        for (var i = 0; i < KiasControllerRackComponent.SlotCount; i++)
        {
            var id = KiasControllerRackComponent.SlotId(i);
            if (Card(ent, id) != null) continue;
            if (_slots.TryInsertEmpty((ent.Owner, Comp<ItemSlotsComponent>(ent)), args.Used, args.User)) return;
            break;
        }
        _popup.PopupEntity(Loc.GetString("kias-controller-rack-full"), ent, args.User);
    }

    private void OnRackExamine(Entity<KiasControllerRackComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("kias-controller-rack-examine", ("count", Inserted(ent)),
            ("running", EntityManager.System<KiasControllerRuntimeSystem>().RunningCount(ent)),
            ("load", ent.Comp.CurrentLoad), ("status", Loc.GetString(_kias.IsOnline(ent) ? "kias-status-online" : "kias-status-offline"))));
    }

    private void OnProgrammerInsertAttempt(Entity<KiasControllerProgrammerComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.User is { } actor && !CanConfigure(ent, actor)) args.Cancelled = true;
    }
    private void OnProgrammerEjectAttempt(Entity<KiasControllerProgrammerComponent> ent, ref ItemSlotEjectAttemptEvent args)
    {
        if (ent.Comp.DraftDirty || args.User is { } actor && !CanConfigure(ent, actor))
        {
            args.Cancelled = true;
            if (args.User is { } user && ent.Comp.DraftDirty)
                _popup.PopupEntity(Loc.GetString("kias-controller-dirty-eject"), ent, user);
        }
    }
    private void OnProgrammerRemoveAttempt(Entity<KiasControllerProgrammerComponent> ent, ref ContainerIsRemovingAttemptEvent args)
    {
        if (args.Container.ID == KiasControllerProgrammerComponent.SlotId && ent.Comp.DraftDirty
            && !TerminatingOrDeleted(ent) && !TerminatingOrDeleted(args.EntityUid)) args.Cancel();
    }
    private void OnProgrammerInsert(Entity<KiasControllerProgrammerComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != KiasControllerProgrammerComponent.SlotId || !TryComp<KiasControllerCardComponent>(args.Entity, out var card)) return;
        ent.Comp.Card = args.Entity;
        ent.Comp.Draft = card.Program.Copy();
        ent.Comp.DraftEnabled = card.Enabled;
        ent.Comp.DraftDirty = false;
        ent.Comp.DraftRevision++;
        ent.Comp.Errors.Clear();
        UpdateProgrammerAppearance(ent);
        EntityManager.System<KiasControllerUiSystem>().Refresh(ent);
    }
    private void OnProgrammerRemove(Entity<KiasControllerProgrammerComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != KiasControllerProgrammerComponent.SlotId) return;
        ent.Comp.Card = null; ent.Comp.Draft = null; ent.Comp.DraftDirty = false; ent.Comp.DraftRevision++;
        UpdateProgrammerAppearance(ent);
        EntityManager.System<KiasControllerUiSystem>().Refresh(ent);
    }

    private void OnProgrammerInit(Entity<KiasControllerProgrammerComponent> ent, ref MapInitEvent args) => UpdateProgrammerAppearance(ent);

    private void UpdateProgrammerAppearance(EntityUid uid) => _appearance.SetData(uid,
        KiasVisuals.CardInserted, Card(uid, KiasControllerProgrammerComponent.SlotId) is { } card && HasComp<KiasControllerCardComponent>(card));
}
