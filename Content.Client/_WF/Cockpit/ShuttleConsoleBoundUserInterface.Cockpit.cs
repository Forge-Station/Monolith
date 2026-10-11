using Content.Client._WF.Cockpit;
using Content.Shared._WF.Cockpit;
using Robust.Client.UserInterface;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    /// <summary>Lets the cockpit controller enter by itself for this helm if the option is on and the pilot is already buckled.</summary>
    private void WfCockpitAutoEnter()
    {
        if (_window != null)
            IoCManager.Resolve<IUserInterfaceManager>().GetUIController<WFCockpitUIController>().AutoEnterOnOpen(_window);
    }

    private void WfCockpitGunneryOpen()
    {
        if (_window != null)
            // Mode changes and their first command must retain order within the same input tick.
            // A detached player has no input tick, so the session end sent by teardown is dropped like SendMessage would.
            _window.WfCockpitGunneryCommand += message =>
            {
                if (PlayerManager.LocalEntity != null)
                    SendPredictedMessage(message);
            };
    }

    private void WfCockpitGunneryReceive(BoundUserInterfaceMessage message)
    {
        if (message is WFCockpitGunneryStateMessage state)
            _window?.WfReceiveCockpitGunnery(state);
    }
}
