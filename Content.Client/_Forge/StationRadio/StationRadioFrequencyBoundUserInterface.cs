using Content.Shared._Forge.StationRadio;
using JetBrains.Annotations;
using Robust.Client.GameObjects;

namespace Content.Client._Forge.StationRadio;

[UsedImplicitly]
public sealed class StationRadioFrequencyBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private StationRadioFrequencyMenu? _menu;

    public StationRadioFrequencyBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new StationRadioFrequencyMenu();
        _menu.OnFrequencyChanged += frequency =>
        {
            if (int.TryParse(frequency.Trim(), out var intFrequency))
                SendMessage(new SelectStationRadioFrequencyMessage(intFrequency));
            else
                SendMessage(new SelectStationRadioFrequencyMessage(-1));
        };
        _menu.OnReceiverToggled += () => SendMessage(new ToggleStationRadioReceiverMessage());
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Close();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not StationRadioFrequencyBoundUIState msg)
            return;

        _menu?.Update(msg);
    }
}
