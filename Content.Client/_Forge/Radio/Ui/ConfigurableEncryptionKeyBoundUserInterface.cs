using Content.Shared._Forge.Radio;
using JetBrains.Annotations;
using Robust.Client.GameObjects;

namespace Content.Client._Forge.Radio.Ui;

[UsedImplicitly]
public sealed class ConfigurableEncryptionKeyBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private ConfigurableEncryptionKeyMenu? _menu;

    public ConfigurableEncryptionKeyBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new ConfigurableEncryptionKeyMenu();
        _menu.OnFrequencyChanged += frequency =>
        {
            if (int.TryParse(frequency.Trim(), out var intFrequency))
                SendMessage(new SelectConfigurableEncryptionKeyFrequencyMessage(intFrequency));
            else
                SendMessage(new SelectConfigurableEncryptionKeyFrequencyMessage(-1));
        };

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

        if (state is not ConfigurableEncryptionKeyBoundUIState msg)
            return;

        _menu?.Update(msg);
    }
}
