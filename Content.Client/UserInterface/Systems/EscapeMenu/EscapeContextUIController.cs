using Content.Client._WF.Cockpit; // WOLFGATE(Cockpit)
using Content.Client.UserInterface.Systems.Info;
using Content.Shared.Input;
using JetBrains.Annotations;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;

namespace Content.Client.UserInterface.Systems.EscapeMenu;

[UsedImplicitly]
public sealed partial class EscapeContextUIController : UIController
{
    [Dependency] private IInputManager _inputManager = default!;

    [Dependency] private CloseRecentWindowUIController _closeRecentWindowUIController = default!;
    [Dependency] private EscapeUIController _escapeUIController = default!;

    public override void Initialize()
    {
        _inputManager.SetInputCommand(ContentKeyFunctions.EscapeContext,
            InputCmdHandler.FromDelegate(_ => CloseWindowOrOpenGameMenu()));
    }

    private void CloseWindowOrOpenGameMenu()
    {
        if (_closeRecentWindowUIController.HasClosableWindow())
        {
            _closeRecentWindowUIController.CloseMostRecentWindow();
        }
        else
        {
            if (UIManager.GetUIController<WFCockpitUIController>().ExitOnEscape()) return; // WOLFGATE(Cockpit): Escape leaves the cockpit before it opens the game menu.
            _escapeUIController.ToggleWindow();
        }
    }
}
