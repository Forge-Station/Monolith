using Content.Shared._Forge.Company;
using JetBrains.Annotations;
using Robust.Client.GameObjects;

namespace Content.Client._Forge.Company;

[UsedImplicitly]
public sealed class CompanyDoorAccessConsoleBoundUserInterface : BoundUserInterface
{
    private CompanyDoorAccessConsoleWindow? _window;

    public CompanyDoorAccessConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = new CompanyDoorAccessConsoleWindow();
        _window.OnClose += Close;
        _window.OnSetMode += (doors, mode) => SendMessage(new CompanyDoorAccessConsoleSetMessage(doors, mode));
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is CompanyDoorAccessConsoleBoundUserInterfaceState cast)
            _window?.UpdateState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
        _window?.Orphan();
        _window = null;
    }
}
