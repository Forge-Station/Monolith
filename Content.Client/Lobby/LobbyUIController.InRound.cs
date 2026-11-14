using System.Numerics;
using Content.Client._WF.InRoundCreator;
using Content.Client.Gameplay;

namespace Content.Client.Lobby;

public sealed partial class LobbyUIController
{
    private InRoundCreatorWindow? _inRoundWindow;

    /// <summary>
    /// Opens the creator in a window during a round, so chat stays usable. Edits apply on the next spawn.
    /// </summary>
    public void OpenInRoundCharacterSetup()
    {
        if (_stateManager.CurrentState is not GameplayState || !_preferencesManager.ServerDataLoaded)
            return;

        if (_inRoundWindow is { IsOpen: true })
        {
            _inRoundWindow.MoveToFront();
            return;
        }

        var (characterGui, _) = EnsureGui();
        if (_inRoundWindow == null)
        {
            characterGui.SetInRound();
            var screen = UIManager.RootControl.Size;
            _inRoundWindow = new InRoundCreatorWindow
            {
                Title = Loc.GetString("character-setup-gui-character-setup-label"),
                SetSize = new Vector2(Math.Min(1500f, screen.X - 80f), Math.Min(900f, screen.Y - 80f)),
                CloseRequested = RequestCloseInRound,
            };
            _inRoundWindow.Contents.AddChild(characterGui);
        }

        _inRoundWindow.OpenCentered();
        ReloadCharacterSetup();
    }

    public void OnStateExited(GameplayState state)
    {
        CloseInRound();
    }

    private void RequestCloseInRound()
    {
        if (_profileEditor is { Profile: not null, IsDirty: true })
            OpenSavePanel(CloseInRound);
        else
            CloseInRound();
    }

    private void CloseInRound()
    {
        if (_inRoundWindow == null)
            return;

        _inRoundWindow.CloseRequested = null;
        _inRoundWindow.Dispose();
        _inRoundWindow = null;
        _characterSetup = null;
        _profileEditor = null;
    }
}
