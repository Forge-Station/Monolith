using Content.Client.Lobby;
using Content.Client.UserInterface.Systems.Info;
using Robust.Client.State;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Network;

namespace Content.Client._WF.Roadmap;

/// <summary>
/// Opens the roadmap from the lobby links and the game menu, and once per launch in the lobby,
/// after the server's rules decision and any rules popup.
/// </summary>
public sealed partial class RoadmapUIController : UIController, IOnStateEntered<LobbyState>
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private InfoUIController _info = default!;

    private RoadmapWindow? _window;
    private bool _shown;

    /// <summary>Whether this connection's rules decision has arrived; it can land after the lobby.</summary>
    private bool _rulesDecided;

    /// <summary>Whether the roadmap window is open.</summary>
    public bool IsOpen => _window != null;

    public override void Initialize()
    {
        base.Initialize();
        _info.RulesInformationReceived += OnRulesDecided;
        _info.RulesAccepted += TryAutoOpen;
        _net.Disconnect += (_, _) => _rulesDecided = false;
    }

    public void OnStateEntered(LobbyState state)
    {
        TryAutoOpen();
    }

    private void OnRulesDecided()
    {
        _rulesDecided = true;
        TryAutoOpen();
    }

    private void TryAutoOpen()
    {
        if (_shown || !_rulesDecided || _info.RulesPopupOpen || _state.CurrentState is not LobbyState)
            return;

        OpenRoadmap();
    }

    /// <summary>Opens the roadmap, or closes it if it is open.</summary>
    public void ToggleRoadmap()
    {
        if (_window != null)
        {
            _window.Close();
            return;
        }

        OpenRoadmap();
    }

    private void OpenRoadmap()
    {
        _shown = true;
        if (_window != null)
            return;

        _window = new RoadmapWindow();
        _window.OnClose += () => _window = null;
        _window.FitTo(UIManager.WindowRoot.Size);
        _window.OpenCentered();
    }
}
