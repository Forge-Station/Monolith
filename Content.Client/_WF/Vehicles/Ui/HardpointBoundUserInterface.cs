using System;
using Content.Shared._WF.Vehicles.Compat;
using Content.Shared._WF.Vehicles.Hardpoint;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Vehicles.Ui;

/// <summary>Opens the hardpoint window and keeps it drawn from the vehicle's replicated hardpoint state.</summary>
public sealed class HardpointBoundUserInterface : BoundUserInterface, IRefreshableBui
{
    private HardpointMenu? _menu;

    /// <summary>The open window, if any.</summary>
    public HardpointMenu? Menu => _menu;

    public HardpointBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new HardpointMenu();
        _menu.OnClose += Close;
        _menu.VehicleEntity = Owner;

        if (EntMan.TryGetComponent(Owner, out MetaDataComponent? metadata))
            _menu.Title = metadata.EntityName;

        _menu.OnRemove += slotId => SendMessage(new HardpointRemoveMessage(slotId));
        if (!EntMan.System<WFVehicleHudSystem>().TryEmbedMenu(_menu, Loc.GetString("wf-vehicle-tab-parts")))
            _menu.OpenCentered();
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

    /// <summary>Redraws the window from the vehicle's current state.</summary>
    public void Refresh()
    {
        if (_menu is not { IsOpen: true })
            return;

        if (!EntMan.TryGetComponent(Owner, out HardpointSlotsComponent? hardpoints))
            return;

        var hardpointState = hardpoints.Ui;
        _menu.Update(
            hardpointState.Hardpoints,
            hardpointState.FrameIntegrity,
            hardpointState.FrameMaxIntegrity,
            hardpointState.HasFrameIntegrity,
            hardpointState.Error);
    }
}
