using Content.Shared._WF.ShipShields;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    /// <summary>Applies shield-only updates independently of the helm's navigation state.</summary>
    private void WfShieldReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (message is WFShipShieldHelmUpdateMessage update)
            _window?.UpdateShieldShuntSnapshot(update.ShieldShunt);
    }

    private void WfShieldOpen()
    {
        if (_window == null)
            return;
        _window.ShieldEnabledRequested += enabled => SendMessage(new WFShipShieldSetEnabledMessage(enabled));
        _window.ShieldShuntRequested += (direction, concentration, arc) =>
            SendMessage(new WFShipShieldSetShuntMessage(direction, concentration, arc));
    }
}
