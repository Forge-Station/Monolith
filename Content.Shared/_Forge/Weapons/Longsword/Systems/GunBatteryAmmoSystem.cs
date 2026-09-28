using System;
using System.Diagnostics.CodeAnalysis;
using Content.Shared._Forge.Weapons.Longsword.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.PowerCell.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._Forge.Weapons.Longsword.Systems;

public sealed class GunBatteryAmmoSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedBatterySystem _battery = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunBatteryAmmoComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<GunBatteryAmmoComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<GunBatteryAmmoComponent, GunMuzzleFlashAttemptEvent>(OnMuzzleFlashAttempted);
        SubscribeLocalEvent<GunBatteryAmmoComponent, AmmoShotEvent>(OnAmmoShot);
    }

    private bool TryGetBattery(EntityUid uid, out EntityUid batteryUid, [NotNullWhen(true)] out BatteryComponent? battery)
    {
        batteryUid = default;
        battery = null;

        if (!TryComp<PowerCellSlotComponent>(uid, out var slotComp))
            return false;

        if (!_itemSlots.TryGetSlot(uid, slotComp.CellSlotId, out var slot) || slot.Item is not { } item)
            return false;

        if (!TryComp(item, out battery))
            return false;

        batteryUid = item;
        return true;
    }

    private bool HasEnoughCharge(EntityUid uid, float cost, out string? failMessage)
    {
        failMessage = null;

        if (!TryGetBattery(uid, out _, out var battery))
        {
            failMessage = "gun-battery-missing";
            return false;
        }

        if (battery.CurrentCharge < cost)
        {
            failMessage = "gun-battery-no-charge";
            return false;
        }

        return true;
    }

    private void OnShotAttempted(Entity<GunBatteryAmmoComponent> ent, ref ShotAttemptedEvent args)
    {
        if (args.Cancelled)
            return;

        if (!HasEnoughCharge(ent.Owner, ent.Comp.FireCost, out _))
        {
            args.Cancel();
        }
    }

    private void OnAttemptShoot(Entity<GunBatteryAmmoComponent> ent, ref AttemptShootEvent args)
    {
        if (args.Cancelled)
            return;

        if (!HasEnoughCharge(ent.Owner, ent.Comp.FireCost, out var failMessage))
        {
            args.Cancelled = true;
            args.Message = Loc.GetString(failMessage ?? ent.Comp.NoChargePopup);
        }
    }

    private void OnMuzzleFlashAttempted(Entity<GunBatteryAmmoComponent> ent, ref GunMuzzleFlashAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!HasEnoughCharge(ent.Owner, ent.Comp.FireCost, out _))
        {
            args.Cancelled = true;
        }
    }

    private void OnAmmoShot(EntityUid uid, GunBatteryAmmoComponent component, AmmoShotEvent args)
    {
        var count = Math.Max(args.FiredProjectiles.Count, 1);
        var cost = component.FireCost * count;

        if (!TryGetBattery(uid, out var batteryUid, out var battery))
            return;

        _battery.TryUseCharge(batteryUid, cost);

        if (battery.CurrentCharge < component.FireCost)
        {
            _popup.PopupEntity(Loc.GetString("gun-battery-destroyed"), uid);
            EntityManager.DeleteEntity(batteryUid);
        }
    }
}