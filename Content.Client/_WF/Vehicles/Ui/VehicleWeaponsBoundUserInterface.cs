using System;
using System.Numerics;
using Content.Shared._WF.Vehicles.Compat;
using Content.Shared._WF.Vehicles.Weapons;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Vehicles.Ui;

/// <summary>Opens the weapons window on a weapons seat and redraws it from the seat's replicated state.</summary>
public sealed class VehicleWeaponsBoundUserInterface : BoundUserInterface, IRefreshableBui
{
    private VehicleWeaponsMenu? _menu;

    public VehicleWeaponsBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new VehicleWeaponsMenu();
        _menu.OnClose += Close;
        _menu.Title = string.Empty;

        _menu.OnSelect += mountedEntity => SendMessage(new VehicleWeaponsSelectMessage(mountedEntity));
        _menu.OnToggleStabilization += enabled => SendMessage(new VehicleWeaponsStabilizationMessage(enabled));
        if (!EntMan.System<WFVehicleHudSystem>().TryEmbedMenu(_menu, Loc.GetString("wf-vehicle-tab-weapons")))
            _menu.OpenCenteredAt(new Vector2(0.7f, 0.05f));
        Refresh();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        if (_menu == null)
            return;

        _menu.OnClose -= Close;

        if (_menu.IsOpen)
            _menu.Close();

        _menu = null;
    }

    /// <summary>Redraws the window from the seat's current state.</summary>
    public void Refresh()
    {
        if (_menu is not { IsOpen: true })
            return;

        if (!EntMan.TryGetComponent(Owner, out VehicleWeaponsSeatComponent? seat))
            return;

        var weaponsState = seat.Ui;

        _menu?.Update(
            weaponsState.Vehicle,
            weaponsState.Hardpoints,
            weaponsState.CanToggleStabilization,
            weaponsState.StabilizationEnabled);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is VehicleWeaponsCooldownFeedbackMessage cooldown)
            _menu?.FlashCooldownFeedback(cooldown.RemainingSeconds);
    }
}
