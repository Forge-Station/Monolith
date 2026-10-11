using Content.Shared._WF.Cockpit;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    /// <summary>Sends cockpit gunnery requests through this helm's bound interface.</summary>
    public event Action<BoundUserInterfaceMessage>? WfCockpitGunneryCommand;

    /// <summary>Delivers the server's current reachable gunnery console to the active cockpit.</summary>
    public event Action<WFCockpitGunneryStateMessage>? WfCockpitGunneryUpdated;

    /// <summary>Forwards a cockpit command without opening a second console window.</summary>
    public void WfSendCockpitGunnery(BoundUserInterfaceMessage message) => WfCockpitGunneryCommand?.Invoke(message);

    /// <summary>Updates only the optional gun bank without replacing navigation state.</summary>
    public void WfReceiveCockpitGunnery(WFCockpitGunneryStateMessage message) => WfCockpitGunneryUpdated?.Invoke(message);
}
