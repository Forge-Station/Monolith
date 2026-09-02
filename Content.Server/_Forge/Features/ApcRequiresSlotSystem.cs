using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Forge.Features.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Robust.Shared.Containers;

namespace Content.Server._Forge.Features;

public sealed class ApcRequiresSlotSystem : SharedApcRequiresSlotSystem
{
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ApcRequiresSlotComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ApcRequiresSlotComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<ApcRequiresSlotComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<ApcRequiresSlotComponent, ApcToggleMainBreakerAttemptEvent>(OnToggleAttempt);
        SubscribeLocalEvent<ApcRequiresSlotComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(EntityUid uid, ApcRequiresSlotComponent component, MapInitEvent args)
    {
        RefreshPower(uid, component);
    }

    private void OnInserted(EntityUid uid, ApcRequiresSlotComponent component, EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != ApcRequiresSlotComponent.BatterySlotId)
            return;

        RefreshPower(uid, component);
    }

    private void OnRemoved(EntityUid uid, ApcRequiresSlotComponent component, EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != ApcRequiresSlotComponent.BatterySlotId)
            return;

        RefreshPower(uid, component);
    }

    private void OnToggleAttempt(EntityUid uid, ApcRequiresSlotComponent component, ref ApcToggleMainBreakerAttemptEvent args)
    {
        if (HasRequiredItem(uid, component))
            return;

        args.Cancelled = true;
        _popup.PopupEntity(Loc.GetString("remnant-apc-missing-battery"), uid, PopupType.Medium);
    }

    private void OnExamined(EntityUid uid, ApcRequiresSlotComponent component, ExaminedEvent args)
    {
        args.PushMarkup(HasRequiredItem(uid, component)
            ? Loc.GetString("remnant-apc-examine-inserted")
            : Loc.GetString("remnant-apc-examine-empty"));
    }

    private bool HasRequiredItem(EntityUid uid, ApcRequiresSlotComponent component)
    {
        return _slots.GetItemOrNull(uid, ApcRequiresSlotComponent.BatterySlotId) != null;
    }

    private void RefreshPower(EntityUid uid, ApcRequiresSlotComponent component)
    {
        var hasItem = HasRequiredItem(uid, component);

        if (TryComp<ApcComponent>(uid, out var apc))
        {
            apc.MainBreakerEnabled = hasItem;
            apc.NeedStateUpdate = true;
        }

        if (TryComp<PowerNetworkBatteryComponent>(uid, out var battery))
            battery.CanDischarge = hasItem;
    }
}
