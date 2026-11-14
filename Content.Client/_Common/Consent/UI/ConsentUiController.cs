// SPDX-FileCopyrightText: Copyright (c) 2024-2025 Space Wizards Federation
// SPDX-License-Identifier: MIT

using Content.Client.Gameplay;
using Content.Client.Options.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.EscapeMenu;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._Common.Consent; // Forge-Change
using Content.Shared.Input;
using Robust.Client.Input;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Input.Binding;
using Robust.Shared.Prototypes; // Forge-Change
using JetBrains.Annotations;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._Common.Consent.UI;

/// <summary>
/// Wolfgate: the consent preferences live in a Game Options tab rather than their own window, so the HUD
/// button and the keybind both just open Options there.
/// </summary>
[UsedImplicitly]
public sealed partial class ConsentUiController : UIController, IOnStateChanged<GameplayState>
{
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPrototypeManager _prototype = default!; // Forge-Change

    // Forge-Change-Start: every consent toggle went with the anatomy module. While the build defines none there is
    // nothing to consent to, so the HUD button, the keybind and the options tab stay out of the way.
    /// <summary>Whether this build defines any consent toggle at all.</summary>
    public bool HasToggles => _prototype.Count<ConsentTogglePrototype>() > 0;
    // Forge-Change-End

    private MenuButton? ConsentButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.ConsentButton;

    public void OnStateEntered(GameplayState state)
    {
        _input.SetInputCommand(ContentKeyFunctions.OpenConsentWindow,
            InputCmdHandler.FromDelegate(_ => OpenConsentOptions()));
    }

    public void OnStateExited(GameplayState state)
    {
        _input.SetInputCommand(ContentKeyFunctions.OpenConsentWindow, null);
    }

    public void UnloadButton()
    {
        if (ConsentButton == null)
            return;

        ConsentButton.OnPressed -= ConsentButtonPressed;
    }

    public void LoadButton()
    {
        if (ConsentButton == null)
            return;

        ConsentButton.OnPressed += ConsentButtonPressed;
        ConsentButton.Visible = HasToggles; // Forge-Change
    }

    private void ConsentButtonPressed(ButtonEventArgs args)
    {
        OpenConsentOptions();
    }

    private void OpenConsentOptions()
    {
        if (!HasToggles) // Forge-Change
            return;

        UIManager.ClickSound();
        UIManager.GetUIController<OptionsUIController>().OpenWindow(OptionsMenu.ConsentTabIndex);

        // The options window owns its own open state, so the HUD button never stays lit.
        if (ConsentButton is not null)
            ConsentButton.Pressed = false;
    }
}
