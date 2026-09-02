// Forge-Change-full
using Content.Shared._Forge.Contractors;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Contractors.UI;

[UsedImplicitly]
public sealed class PassportConsoleBoundUserInterface : BoundUserInterface
{
    private PassportConsoleWindow? _window;

    public PassportConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<PassportConsoleWindow>();
        _window.OnRecordSelected += id => SendMessage(new PassportConsoleSelectMessage(id));
        _window.OnPrintPressed += id => SendMessage(new PassportConsolePrintMessage(id));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is PassportConsoleBoundUserInterfaceState consoleState)
            _window?.UpdateState(consoleState);
    }
}
