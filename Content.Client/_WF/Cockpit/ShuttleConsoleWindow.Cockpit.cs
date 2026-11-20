using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    private Button? _wfCockpitButton;
    private bool _wfCockpitShipVisible;

    /// <summary>The height of the Enter Cockpit key, half as tall again as a normal console key.</summary>
    public const float WfCockpitButtonHeight = 48;

    /// <summary>
    /// Requests only the summary (hull integrity and fuel, no tile overlays) unless the detailed ship page is showing:
    /// the windowed ship page, or the cockpit's ship page while the cockpit is up.
    /// </summary>
    public bool WfHullOnly => WfCockpitActive ? !_wfCockpitShipVisible : _mode != ShuttleConsoleMode.Ship;

    /// <summary>Requests detailed ship telemetry only while its cockpit page is visible.</summary>
    public void WfCockpitShipVisible(bool visible)
    {
        if (_wfCockpitShipVisible == visible)
            return;
        _wfCockpitShipVisible = visible;
        if (WfCockpitActive)
            WfRequestShipStatus();
    }
    /// <summary>The BUI owner whose piloting session supplies the cockpit.</summary>
    public EntityUid? WfCockpitConsole { get; private set; }
    /// <summary>The console is currently presented around the world view.</summary>
    public bool WfCockpitActive { get; private set; }

    /// <summary>Builds the entry key for the end of the navigation page's left stack: full width and tall, disabled until the pilot can enter.</summary>
    private Button WfCreateCockpitButton()
    {
        _wfCockpitButton = new Button
        {
            Name = "CockpitEnter",
            Text = Loc.GetString("wf-cockpit-enter"),
            MinHeight = WfCockpitButtonHeight,
            HorizontalExpand = true,
        };
        WFInstrumentTheme.Switch(_wfCockpitButton);
        _wfCockpitButton.Disabled = true;
        _wfCockpitButton.ToolTip = Loc.GetString("wf-cockpit-seat-required");
        _wfCockpitButton.OnPressed += _ => UserInterfaceManager.GetUIController<WFCockpitUIController>().Enter(this);
        OnClose += () => UserInterfaceManager.GetUIController<WFCockpitUIController>().Exit(this);
        return _wfCockpitButton;
    }

    /// <summary>Associates the entry switch with the authoritative helm session.</summary>
    public void WfSetCockpitConsole(EntityUid console) => WfCockpitConsole = console;

    /// <summary>Keeps hull telemetry subscribed while the cockpit's permanent gauges use it.</summary>
    public void WfSetCockpitActive(bool active)
    {
        if (active && !WfCockpitActive)
            CameraBar.WfCockpitDefaultView();
        if (!active && WfCockpitActive)
        {
            CameraBar.WfCockpitRestoreView();
            // The windowed SHIP page and the next cockpit must not flash the departed session's last sweep.
            if (_mode != ShuttleConsoleMode.Ship)
                ShipContainer.ClearStatus();
        }
        WfCockpitActive = active;
        CameraBar.Visible = active || NavContainer.Visible;
        // Forced: entering or leaving the cockpit always asks for a fresh sweep, even if the request itself is unchanged.
        WfRequestShipStatus(force: true);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_wfCockpitButton != null)
            _wfCockpitButton.Disabled = !UserInterfaceManager.GetUIController<WFCockpitUIController>().CanEnter(WfCockpitConsole);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            UserInterfaceManager.GetUIController<WFCockpitUIController>().Exit(this);
        base.Dispose(disposing);
    }
}
