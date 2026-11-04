using System;
using Content.Shared._WF.Vehicles.Compat;
using Content.Shared._WF.Vehicles.Weapons;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Vehicles.Ui;

/// <summary>Opens the ammo loader window and feeds it the loader's state.</summary>
public sealed class VehicleAmmoLoaderBoundUserInterface : BoundUserInterface, IRefreshableBui
{
    private VehicleAmmoLoaderMenu? _menu;

    public VehicleAmmoLoaderBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new VehicleAmmoLoaderMenu();
        _menu.OnClose += Close;

        if (EntMan.TryGetComponent(Owner, out MetaDataComponent? metadata))
            _menu.Title = metadata.EntityName;

        _menu.OnSlotSelected += (slotPath, ammoSlot, action) =>
            SendMessage(new VehicleAmmoLoaderSelectMessage(slotPath, ammoSlot, action));
        if (!EntMan.System<WFVehicleHudSystem>().TryEmbedMenu(_menu, Loc.GetString("wf-vehicle-tab-ammunition")))
            _menu.OpenCentered();
        Refresh();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        if (_menu != null)
            _menu.OnClose -= Close;

        if (_menu is { Disposed: false })
            _menu.Orphan();

        _menu = null;
    }

    /// <summary>Redraws the window from the loader's current state.</summary>
    public void Refresh()
    {
        if (_menu is not { IsOpen: true })
            return;

        if (!EntMan.TryGetComponent(Owner, out VehicleAmmoLoaderComponent? loader))
            return;

        var ammoState = loader.Ui;
        _menu?.Update(ammoState.Hardpoints, ammoState.AmmoAmount, ammoState.AmmoMax, ammoState.AmmoPrototype);
    }
}
