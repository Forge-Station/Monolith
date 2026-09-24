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
        SubscribeLocalEvent<ClothingBatteryAlertComponent, ComponentShutdown>(OnShutdown);
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
        ClearWornAlert(ent, args.Wearer);
    }

    private void OnShutdown(Entity<ClothingBatteryAlertComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Wearer is { } wearer)
            ClearWornAlert(ent, wearer);
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
        if (ent.Comp.Wearer is not { } wearer || TerminatingOrDeleted(wearer))
            return;

        if (!_powerCell.TryGetBatteryFromSlot(ent, out var battery))
        {
            _alerts.ClearAlert(wearer, ent.Comp.BatteryAlert);
            _alerts.ShowAlert(wearer, ent.Comp.NoBatteryAlert);
            return;
        }

        var minSeverity = _alerts.GetMinSeverity(ent.Comp.BatteryAlert);
        var maxSeverity = _alerts.GetMaxSeverity(ent.Comp.BatteryAlert);
        var chargePercent = minSeverity;

        if (battery.MaxCharge > 0f && maxSeverity >= minSeverity)
        {
            chargePercent = (short) Math.Clamp(
                MathF.Round(battery.CurrentCharge / battery.MaxCharge * maxSeverity),
                (float) minSeverity,
                (float) maxSeverity);
        }

        // Empty icon only when the cell cannot pay the draw rate.
        if (chargePercent == minSeverity && chargePercent < maxSeverity && _powerCell.HasDrawCharge(ent))
            chargePercent++;

        _alerts.ClearAlert(wearer, ent.Comp.NoBatteryAlert);
        _alerts.ShowAlert(wearer, ent.Comp.BatteryAlert, chargePercent);
    }

    private void ClearWornAlert(Entity<ClothingBatteryAlertComponent> ent, EntityUid wearer)
    {
        if (!TerminatingOrDeleted(wearer))
            ClearAlerts(ent, wearer);

        if (ent.Comp.Wearer == wearer)
            ent.Comp.Wearer = null;
    }

    private void ClearAlerts(Entity<ClothingBatteryAlertComponent> ent, EntityUid wearer)
    {
        _alerts.ClearAlert(wearer, ent.Comp.BatteryAlert);
        _alerts.ClearAlert(wearer, ent.Comp.NoBatteryAlert);
    }
}
