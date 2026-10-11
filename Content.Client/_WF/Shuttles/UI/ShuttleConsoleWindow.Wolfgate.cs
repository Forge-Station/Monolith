using Content.Shared._WF.ShipPa;
using Content.Shared._WF.Shuttles;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    /// <summary>
    /// Raised when the whole-ship view comes on or off screen, so the console can stop asking the
    /// server for hull telemetry nobody is looking at.
    /// </summary>
    public event Action<bool, ShipOverlays>? ShipStatusActiveChanged;

    /// <summary>
    /// The pilot picked a situation code on the PA panel.
    /// </summary>
    public event Action<string>? ShipCodeRequested;

    /// <summary>
    /// The pilot sounded or secured general quarters.
    /// </summary>
    public event Action<bool>? ShipGeneralQuartersRequested;

    /// <summary>
    /// The pilot wants a line read out over the PA.
    /// </summary>
    public event Action<string>? ShipAnnounceRequested;

    /// <summary>
    /// The pilot wants a sound played over the PA.
    /// </summary>
    public event Action<string>? ShipSoundRequested;

    /// <summary>
    /// The pilot stopped the PA sound.
    /// </summary>
    public event Action? ShipSoundStopRequested;

    /// <summary>
    /// The pilot skipped, removed or cleared PA playlist entries.
    /// </summary>
    public event Action<ShipPaPlaylistAction, int>? ShipPlaylistRequested;

    /// <summary>
    /// The pilot switched the collision warning on or off.
    /// </summary>
    public event Action<bool>? ShipCollisionAlertRequested;

    /// <summary>
    /// The pilot picked a hull camera view, zoom or low-light setting.
    /// </summary>
    public event Action<ShuttleCameraView, float, bool>? ShipCameraRequested;

    /// <summary>The last hull telemetry request sent, so an unchanged one is not repeated.</summary>
    private (bool Active, ShipOverlays Overlays, bool HullOnly)? _wfStatusRequest;

    /// <summary>Whether any page on screen reads hull telemetry: the ship page, the navigation fuel gauge or the cockpit.</summary>
    private bool WfStatusWanted => WfCockpitActive || _mode is ShuttleConsoleMode.Ship or ShuttleConsoleMode.Nav;

    private void WfInitialize()
    {
        ShipContainer.CodeRequested += code => ShipCodeRequested?.Invoke(code);
        ShipContainer.GeneralQuartersRequested += active => ShipGeneralQuartersRequested?.Invoke(active);
        ShipContainer.AnnounceRequested += text => ShipAnnounceRequested?.Invoke(text);
        ShipContainer.SoundRequested += url => ShipSoundRequested?.Invoke(url);
        ShipContainer.SoundStopRequested += () => ShipSoundStopRequested?.Invoke();
        ShipContainer.PlaylistRequested += (action, id) => ShipPlaylistRequested?.Invoke(action, id);
        ShipContainer.CollisionAlertRequested += enabled => ShipCollisionAlertRequested?.Invoke(enabled);
        CameraBar.CameraRequested += (view, zoom, lowLight) => ShipCameraRequested?.Invoke(view, zoom, lowLight);

        // Flipping an overlay changes what the server needs to send, so re-request with the new mask.
        ShipContainer.OverlaysChanged += () =>
        {
            if (ShipContainer.Visible || WfCockpitActive)
                WfRequestShipStatus();
        };

        // The navigation page's fuel gauge reads the summary, so its request follows that page on and off screen.
        NavContainer.OnVisibilityChanged += _ => WfRequestShipStatus();
    }

    private void WfSetShipMode(bool active)
    {
        if (ShipContainer.Visible == active)
            return;

        ShipContainer.Visible = active;

        if (!active && !WfCockpitActive)
            ShipContainer.ClearStatus();

        WfRequestShipStatus();
    }

    /// <summary>
    /// Tells the server whether this window wants hull telemetry and how much of it: everything for the ship page,
    /// the summary alone for the navigation fuel gauge. Nothing is sent when the request has not changed, unless forced.
    /// </summary>
    public void WfRequestShipStatus(bool force = false)
    {
        var wanted = WfStatusWanted;
        if (!wanted)
            NavContainer.WfFuel = null;
        if (ShipStatusActiveChanged is not { } changed)
            return;
        (bool Active, ShipOverlays Overlays, bool HullOnly) request = (wanted, ShipContainer.Overlays, wanted && WfHullOnly);
        if (!force && _wfStatusRequest is { } last && last == request)
            return;
        _wfStatusRequest = request;
        changed(wanted, ShipContainer.Overlays);
    }

    /// <summary>Feeds fresh hull telemetry to the pages showing it; a reply arriving after they have gone is dropped.</summary>
    public void UpdateShipStatus(ShipStatusMessage message)
    {
        if (!WfStatusWanted)
            return;
        NavContainer.WfFuel = message.Summary.Fuel;
        if (WfCockpitActive || _mode == ShuttleConsoleMode.Ship)
            ShipContainer.UpdateStatus(message);
    }
}
