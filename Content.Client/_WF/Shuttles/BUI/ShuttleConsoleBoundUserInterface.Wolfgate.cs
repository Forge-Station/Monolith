using Content.Shared._WF.ShipPa;
using Content.Shared._WF.Shuttles;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private void WfOpen()
    {
        if (_window == null)
            return;

        _window.WfSetCockpitConsole(Owner); // WOLFGATE(Cockpit): bind fullscreen entry to this helm session.
        WfCockpitGunneryOpen(); // WOLFGATE(Cockpit): route the optional weapon bank through the helm.
        _window.ShipStatusActiveChanged += (active, overlays) =>
            SendMessage(new ShipStatusRequestMessage(active, overlays, _window.WfHullOnly));
        _window.WfRequestShipStatus(force: true); // The window opens on the navigation page, whose fuel gauge needs the summary.

        _window.ShipCodeRequested += code => SendMessage(new ShipAlertCodeRequestMessage(code));
        _window.ShipGeneralQuartersRequested += active => SendMessage(new ShipGeneralQuartersRequestMessage(active));
        _window.ShipAnnounceRequested += text => SendMessage(new ShipPaAnnounceRequestMessage(text));
        _window.ShipSoundRequested += url => SendMessage(new ShipPaInternetSoundRequestMessage(url));
        _window.ShipSoundStopRequested += () => SendMessage(new ShipPaInternetSoundStopMessage());
        _window.ShipPlaylistRequested += (action, id) => SendMessage(new ShipPaPlaylistRequestMessage(action, id));
        _window.ShipCollisionAlertRequested += enabled => SendMessage(new CollisionWarningToggleMessage(enabled));
        // WOLFGATE(Cockpit): preserve camera requests in the same input order as cockpit entry and exit.
        _window.ShipCameraRequested += (view, zoom, lowLight) => SendPredictedMessage(new ShuttleCameraSetMessage(view, zoom, lowLight));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        WfCockpitGunneryReceive(message); // WOLFGATE(Cockpit): refresh the linked weapon bank independently.
        WfShieldReceiveMessage(message); // WOLFGATE(ShipShields): update shield controls without refreshing navigation.
        WfHailReceiveMessage(message); // WOLFGATE(Hailing): ships in range and why a hail stopped

        // WOLFGATE(Cockpit): refresh the status lamp without changing the active MFD.
        if (message is Content.Shared._WF.Cockpit.WFCockpitAutopilotUpdateMessage autopilot)
            _window?.WfUpdateCockpitAutopilot(autopilot.Active);

        if (message is ShipStatusMessage status)
            _window?.UpdateShipStatus(status);
    }
}
