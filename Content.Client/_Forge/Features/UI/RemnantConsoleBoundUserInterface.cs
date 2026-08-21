using Content.Shared._Forge.Features;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Features.UI;

[UsedImplicitly]
public sealed class RemnantConsoleBoundUserInterface : BoundUserInterface
{
    private RemnantConsoleWindow? _window;

    public RemnantConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<RemnantConsoleWindow>();
        _window.OnButtonPressed += index => SendMessage(new RemnantConsolePressButtonMessage(index));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is RemnantConsoleUiState uiState)
            _window?.UpdateButtons(uiState.ButtonLabels);
    }
}
