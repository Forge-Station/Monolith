using System.Numerics;
using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Shared._WF.Planets;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Network;

namespace Content.Client._WF.Planets;

/// <summary>
/// Keeps the server's world-forming state: a bar in the lobby while worlds form or the round start is held for them,
/// and a small one in the round for a player on a world still forming.
/// </summary>
public sealed class WFWorldFormingClientSystem : EntitySystem
{
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IStateManager _states = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    /// <summary>Distance from the top of the screen to the in-round bar.</summary>
    private const float HudTop = 64f;

    private WFWorldFormingBar _hud = default!;
    private float _untilRefresh;

    /// <summary>The latest state from the server; empty when nothing is forming.</summary>
    public WFWorldFormingEvent State { get; private set; } = new();

    /// <summary>Raised whenever a new state arrives.</summary>
    public event Action<WFWorldFormingEvent>? Changed;

    /// <summary>Whether the in-round bar is showing.</summary>
    public bool HudVisible => _hud.Visible;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
        SubscribeNetworkEvent<WFWorldFormingEvent>(OnState);
        _states.OnStateChanged += OnStateChanged;
        _net.Disconnect += OnDisconnect;

        _hud = new WFWorldFormingBar(compact: true) { Visible = false };
        _ui.PopupRoot.AddChild(_hud);
        LayoutContainer.SetAnchorPreset(_hud, LayoutContainer.LayoutPreset.TopLeft);
    }

    public override void Shutdown()
    {
        _states.OnStateChanged -= OnStateChanged;
        _net.Disconnect -= OnDisconnect;
        _hud.Orphan();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _untilRefresh -= frameTime;
        if (_untilRefresh > 0)
            return;

        _untilRefresh = 0.25f;
        RefreshHud();
    }

    private void OnState(WFWorldFormingEvent args)
    {
        State = args;
        Changed?.Invoke(args);
        RefreshHud();
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs args)
    {
        OnState(new WFWorldFormingEvent());
    }

    /// <summary>Puts the bar under the lobby's start-time line each time the lobby opens.</summary>
    private void OnStateChanged(StateChangedEventArgs args)
    {
        if (args.NewState is not LobbyState { Lobby: { } lobby })
            return;

        foreach (var child in lobby.HeaderContainer.Children)
        {
            if (child is WFWorldFormingBar)
                return;
        }

        var bar = new WFWorldFormingBar(compact: false) { Margin = new Thickness(0, 6, 0, 0) };
        lobby.HeaderContainer.AddChild(bar);
        RefreshHud();
    }

    /// <summary>The forming world the local player is on or over, if any.</summary>
    public WFWorldFormingEntry? LocalWorld()
    {
        if (_player.LocalEntity is not { } local || Transform(local).MapUid is not { } map)
            return null;

        var net = GetNetEntity(map);
        foreach (var world in State.Worlds)
        {
            if (world.Maps.Contains(net))
                return world;
        }

        return null;
    }

    private void RefreshHud()
    {
        var world = _states.CurrentState is GameplayState ? LocalWorld() : null;
        if (world == null)
        {
            _hud.Visible = false;
            return;
        }

        _hud.ShowWorld(world);
        var width = MathF.Min(WFWorldFormingBar.HudWidth, MathF.Max(0, _ui.PopupRoot.Width - 24));
        _hud.SetWidth = width;
        LayoutContainer.SetPosition(_hud, new Vector2(MathF.Max(0, (_ui.PopupRoot.Width - width) / 2), HudTop));
        _hud.Visible = true;
    }
}
