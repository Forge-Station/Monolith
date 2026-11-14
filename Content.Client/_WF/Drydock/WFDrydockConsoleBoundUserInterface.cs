using Content.Shared._WF.Drydock;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Drydock;

/// <summary>
/// Client UI for the drydock berth console.
/// </summary>
public sealed class WFDrydockConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WFDrydockConsoleWindow? _window;

    public WFDrydockConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WFDrydockConsoleWindow>();
        _window.OnRepairPressed += OnRepairPressed;
        _window.OnBreakPressed += () => SendMessage(new WFDrydockBreakMessage());
        _window.OnUpgradePressed += OnUpgradePressed;
        _window.OnCancelBookingPressed += OnCancelBookingPressed;
        _window.OnBerthLightsPressed += OnBerthLightsPressed;
    }

    private void OnBerthLightsPressed()
    {
        SendMessage(new WFDrydockToggleBerthLightsMessage());
    }

    private void OnRepairPressed()
    {
        SendMessage(new WFDrydockRepairMessage());
    }

    private void OnUpgradePressed(string upgrade)
    {
        SendMessage(new WFDrydockUpgradeMessage(upgrade));
    }

    private void OnCancelBookingPressed()
    {
        SendMessage(new WFDrydockCancelBookingMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not WFDrydockConsoleState castState)
            return;

        _window?.UpdateState(castState);
    }
}
