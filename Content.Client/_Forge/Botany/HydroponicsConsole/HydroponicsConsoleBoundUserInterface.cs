using Content.Shared._Forge.Botany.HydroponicsConsole;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Botany.HydroponicsConsole;

[UsedImplicitly]
public sealed class HydroponicsConsoleBoundUserInterface : BoundUserInterface
{
    private HydroponicsConsoleWindow? _window;

    public HydroponicsConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<HydroponicsConsoleWindow>();
        _window.Title = Loc.GetString("hydroponics-console-title");
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is HydroponicsConsoleBoundUserInterfaceState cast)
            _window?.Populate(cast);
    }
}
