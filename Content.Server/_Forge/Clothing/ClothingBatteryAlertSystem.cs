using Content.Server.PowerCell;
using Content.Shared._Forge.Clothing;
using Content.Shared.Alert;
using Content.Shared.Clothing;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;

namespace Content.Server._Forge.Clothing;

/// <summary>
/// Updates borg-style battery alerts for clothing with an internal or slotted power cell.
/// </summary>
public sealed partial class ClothingBatteryAlertSystem : EntitySystem
{
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private PowerCellSystem _powerCell = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClothingBatteryAlertComponent, ClothingGotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<ClothingBatteryAlertComponent, ClothingGotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<ClothingBatteryAlertComponent, PowerCellChangedEvent>(OnPowerCellChanged);
        SubscribeLocalEvent<ClothingBatteryAlertComponent, PowerCellSlotEmptyEvent>(OnPowerCellSlotEmpty);
    }

    private void OnEquipped(Entity<ClothingBatteryAlertComponent> ent, ref ClothingGotEquippedEvent args)
    {
        ent.Comp.Wearer = args.Wearer;
        UpdateAlert(ent);
    }

    private void OnUnequipped(Entity<ClothingBatteryAlertComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        ClearAlerts(ent, args.Wearer);
        ent.Comp.Wearer = null;
    }

    private void OnPowerCellChanged(Entity<ClothingBatteryAlertComponent> ent, ref PowerCellChangedEvent args)
    {
        UpdateAlert(ent);
    }

    private void OnPowerCellSlotEmpty(Entity<ClothingBatteryAlertComponent> ent, ref PowerCellSlotEmptyEvent args)
    {
        UpdateAlert(ent);
    }

    private void UpdateAlert(Entity<ClothingBatteryAlertComponent> ent)
    {
        if (ent.Comp.Wearer is not { } wearer)
            return;

        if (!_powerCell.TryGetBatteryFromSlot(ent, out var battery))
        {
            _alerts.ClearAlert(wearer, ent.Comp.BatteryAlert);
            _alerts.ShowAlert(wearer, ent.Comp.NoBatteryAlert);
            return;
        }

        var chargePercent = (short) MathF.Round(battery.CurrentCharge / battery.MaxCharge * 10f);

        if (chargePercent == 0 && _powerCell.HasDrawCharge(ent))
            chargePercent = 1;

        _alerts.ClearAlert(wearer, ent.Comp.NoBatteryAlert);
        _alerts.ShowAlert(wearer, ent.Comp.BatteryAlert, chargePercent);
    }

    private void ClearAlerts(Entity<ClothingBatteryAlertComponent> ent, EntityUid wearer)
    {
        _alerts.ClearAlert(wearer, ent.Comp.BatteryAlert);
        _alerts.ClearAlert(wearer, ent.Comp.NoBatteryAlert);
    }
}
