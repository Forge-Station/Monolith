using System.Linq;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._WF.SectorControl;
using Content.Shared.Input;
using Robust.Client.GameObjects;
using Robust.Client.Input;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._WF.SectorControl;

/// <summary>Opens and closes the sector map window from the top-bar button, its key, the game menu and wf_sectormap; it closes when the player leaves the game.</summary>
public sealed class WFSectorMapUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IInputManager _input = default!;

    private static readonly SpriteSpecifier.Rsi ButtonIcon = new(new ResPath("/Textures/_WF/SectorControl/menu.rsi"), "sectormap");

    private WFSectorMapWindow? _window;

    private MenuButton? SectorMapButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.SectorMapButton;

    /// <summary>The open window, if any.</summary>
    public WFSectorMapWindow? Window => _window;

    public override void Initialize()
    {
        base.Initialize();
        RepairKeyBinding();
    }

    /// <summary>
    /// Once per client, gives the sector map key back its default (Shift+N) if a saved file left it unbound or on the
    /// old plain N. A key the player has set themselves is left alone. Returns whether it changed anything.
    /// </summary>
    public bool RepairKeyBinding()
    {
        if (_cfg.GetCVar(SectorControlCVars.MapKeyRepaired))
            return false;

        _cfg.SetCVar(SectorControlCVars.MapKeyRepaired, true);
        _cfg.SaveToFile();

        var bindings = _input.GetKeyBindings(ContentKeyFunctions.OpenSectorMap);
        var stale = bindings.Count == 0
            || bindings.All(b => b.BaseKey == Keyboard.Key.N && b.Mod1 == Keyboard.Key.Unknown);
        if (!stale || !_input.IsKeyFunctionModified(ContentKeyFunctions.OpenSectorMap))
            return false;

        _input.ResetBindingsFor(ContentKeyFunctions.OpenSectorMap);
        _input.SaveToUserData();
        return true;
    }

    public void OnStateEntered(GameplayState state)
    {
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenSectorMap, InputCmdHandler.FromDelegate(_ =>
            {
                UIManager.ClickSound();
                ToggleWindow();
            }))
            .Register<WFSectorMapUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        CommandBinds.Unregister<WFSectorMapUIController>();
        _window?.Close();
    }

    /// <summary>Wires the top-bar button: its icon, its pressed state and its click.</summary>
    public void LoadButton()
    {
        if (SectorMapButton is not { } button)
            return;

        button.Icon = EntityManager.System<SpriteSystem>().Frame0(ButtonIcon);
        button.Pressed = _window != null;
        button.OnPressed += ButtonPressed;
    }

    public void UnloadButton()
    {
        if (SectorMapButton is { } button)
            button.OnPressed -= ButtonPressed;
    }

    private void ButtonPressed(ButtonEventArgs args)
    {
        ToggleWindow();
    }

    /// <summary>Opens the sector map, or closes it if it is open.</summary>
    public void ToggleWindow()
    {
        if (_window != null)
        {
            _window.Close();
            return;
        }

        OpenWindow();
    }

    /// <summary>Opens the sector map, or brings it to the front if it is open.</summary>
    public void OpenWindow()
    {
        if (_window != null)
        {
            _window.MoveToFront();
            return;
        }

        var window = new WFSectorMapWindow();
        _window = window;
        window.OnClose += () =>
        {
            if (_window == window)
                _window = null;

            SetButtonPressed(_window != null);
        };
        window.FitTo(UIManager.WindowRoot.Size);
        window.OpenCentered();
        SetButtonPressed(true);
    }

    private void SetButtonPressed(bool pressed)
    {
        if (SectorMapButton is { } button)
            button.Pressed = pressed;
    }
}
