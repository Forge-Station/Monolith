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
    public event Action<string>? ShipSoundRequested;
    public event Action? ShipSoundStopRequested;

    /// <summary>
    /// The pilot switched the collision warning on or off.
    /// </summary>
    public event Action<bool>? ShipCollisionAlertRequested;

    private void WfInitialize()
    {
        ShipContainer.CodeRequested += code => ShipCodeRequested?.Invoke(code);
        ShipContainer.GeneralQuartersRequested += active => ShipGeneralQuartersRequested?.Invoke(active);
        ShipContainer.AnnounceRequested += text => ShipAnnounceRequested?.Invoke(text);
        ShipContainer.SoundRequested += url => ShipSoundRequested?.Invoke(url);
        ShipContainer.SoundStopRequested += () => ShipSoundStopRequested?.Invoke();
        ShipContainer.CollisionAlertRequested += enabled => ShipCollisionAlertRequested?.Invoke(enabled);

        // Flipping an overlay changes what the server needs to send, so re-request with the new mask.
        ShipContainer.OverlaysChanged += () =>
        {
            if (ShipContainer.Visible)
                ShipStatusActiveChanged?.Invoke(true, ShipContainer.Overlays);
        };
    }

    private void WfSetShipMode(bool active)
    {
        if (ShipContainer.Visible == active)
            return;

        ShipContainer.Visible = active;

        if (!active)
            ShipContainer.ClearStatus();

        ShipStatusActiveChanged?.Invoke(active, ShipContainer.Overlays);
    }

    public void UpdateShipStatus(ShipStatusMessage message)
    {
        ShipContainer.UpdateStatus(message);
    }

    private void WfUpdateTractorCapture(string[] sources)
    {
        CaptureBanner.SetSources(sources);
    }
}
