using System.Numerics;
using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._WF.RoundSetup;

/// <summary>Opens crew setup from the lobby, gameplay toolbar and game menu.</summary>
public sealed class WFRoundSetupUIController : UIController, IOnStateExited<LobbyState>, IOnStateExited<GameplayState>
{
    private WFRoundSetupWindow? _window;
    private MenuButton? CrewSetupButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.CrewSetupButton;

    public override void Initialize()
    {
        base.Initialize();
        var gameplay = UIManager.GetUIController<GameplayStateLoadController>();
        gameplay.OnScreenLoad += LoadButton;
        gameplay.OnScreenUnload += UnloadButton;
    }

    private void LoadButton()
    {
        if (CrewSetupButton is not { } button)
            return;
        // MenuButton needs a non-null key even for mouse-only actions.
        button.BoundKey = string.Empty;
        foreach (var child in button.ButtonRoot.Children)
        {
            if (child is TextureRect icon)
                icon.TextureScale = Vector2.One;
        }
        button.Pressed = _window is { IsOpen: true };
        button.OnPressed += ToggleFromToolbar;
    }

    private void UnloadButton()
    {
        if (CrewSetupButton is { } button)
            button.OnPressed -= ToggleFromToolbar;
    }

    private void ToggleFromToolbar(BaseButton.ButtonEventArgs args)
    {
        if (_window is { IsOpen: true })
            _window.Close();
        else
            OpenCrews();
    }

    private void SetButtonPressed(bool pressed)
    {
        if (CrewSetupButton is { } button)
            button.Pressed = pressed;
    }

    /// <summary>Opens the browser or brings the existing setup window forward.</summary>
    public void OpenWindow()
    {
        if (_window is { IsOpen: true })
        {
            _window.MoveToFront();
            return;
        }

        var window = new WFRoundSetupWindow();
        _window = window;
        window.OnClose += () =>
        {
            if (_window == window)
                _window = null;
            SetButtonPressed(false);
        };
        window.FitTo(UIManager.WindowRoot.Size);
        window.OpenCentered();
        SetButtonPressed(true);
    }

    /// <summary>Starts with normal joining or player lobbies from the primary Join action.</summary>
    public void OpenJoin()
    {
        OpenWindow();
        _window?.ShowJoin();
    }

    /// <summary>Opens crew browsing or the player's existing crew only when requested.</summary>
    public void OpenCrews()
    {
        OpenWindow();
        _window?.ShowCrews();
    }

    /// <summary>Opens the scenario chooser from the main lobby's setup action.</summary>
    public void OpenCreate()
    {
        OpenWindow();
        _window?.OpenCreate();
    }

    /// <summary>Opens a selected lobby's details while preserving explicit admission choices.</summary>
    public void OpenLobby(int lobbyId)
    {
        OpenWindow();
        _window?.ShowLobby(lobbyId);
    }

    public void OnStateExited(LobbyState state) => _window?.Close();

    public void OnStateExited(GameplayState state) => _window?.Close();
}
