using System.Linq;
using System.Numerics;
using Content.Client.Gameplay;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Systems.Alerts.Widgets;
using Content.Shared._WF.RoundSetup;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.RoundSetup;

/// <summary>Maintains a personal leader compass and non-modal captain approval cards.</summary>
public sealed partial class WFCrewHudSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IStateManager _states = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFRoundSetupClientSystem _setup = default!;

    private WFCrewLocatorHud _locator = default!;
    private PanelContainer _request = default!;
    private TimeSpan _lastSignal;
    private Button? _acceptRequest;
    private (int, NetUserId, int?)? _shownRequest;
    private readonly HashSet<(int, NetUserId, int?)> _dismissed = new();

    /// <summary>The latest navigation signal, intended only for the local player's current body.</summary>
    public WFCrewSignalEvent? Signal { get; private set; }

    /// <summary>Whether the leader indicator is currently displayed.</summary>
    public bool LocatorVisible => _locator.Visible;

    /// <summary>Whether a captain has an outstanding approval card.</summary>
    public bool RequestVisible => _request.Visible;

    /// <summary>The approval card's height, so other cards can stack above it.</summary>
    public float RequestHeight => _request.Height;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
        _locator = new WFCrewLocatorHud { Visible = false };
        _request = new PanelContainer { Name = "CrewJoinRequest", SetWidth = 320, Visible = false,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#142630F2"), BorderColor = Color.FromHex("#F1B85A"),
                BorderThickness = new Thickness(2), ContentMarginLeftOverride = 12, ContentMarginRightOverride = 12,
                ContentMarginTopOverride = 10, ContentMarginBottomOverride = 10,
            } };
        _ui.PopupRoot.AddChild(_request);
        LayoutContainer.SetAnchorPreset(_request, LayoutContainer.LayoutPreset.TopLeft);
        SubscribeNetworkEvent<WFCrewSignalEvent>(OnSignal);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRestart);
        _setup.Received += OnState;
        _net.Disconnect += OnDisconnect;
    }

    public override void Shutdown()
    {
        _setup.Received -= OnState;
        _net.Disconnect -= OnDisconnect;
        _locator.Orphan();
        _request.Orphan();
        base.Shutdown();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var width = _ui.PopupRoot.Width;
        _request.SetWidth = MathF.Min(320, MathF.Max(0, width - 24));
        LayoutContainer.SetPosition(_request, new Vector2(MathF.Max(12, width - _request.SetWidth - 16),
            MathF.Max(12, _ui.PopupRoot.Height - _request.Height - 100)));
        _locator.Visible = Signal is { Body: not null } signal && _timing.RealTime - _lastSignal < TimeSpan.FromSeconds(2) &&
            _states.CurrentState is GameplayState && _players.LocalEntity is { } body && GetNetEntity(body) == signal.Body;
        PlaceLocator();
        if (!_locator.Visible || Signal is not { } current || _players.LocalEntity is not { } player)
            return;
        Vector2? direction = null;
        if (current.Offset is { } offset)
        {
            offset += current.Origin - _transform.GetWorldPosition(player);
            var rotated = _eye.CurrentEye.Rotation.RotateVec(offset);
            direction = new Vector2(rotated.X, -rotated.Y);
        }
        _locator.Refresh(current, direction);
    }

    /// <summary>Keeps the locator as the last entry of the alerts column while it is shown.</summary>
    private void PlaceLocator()
    {
        var column = _locator.Visible ? _ui.GetActiveUIWidgetOrNull<AlertsUI>()?.AlertContainer : null;
        if (column == null)
        {
            _locator.Orphan();
            return;
        }
        if (_locator.Parent != column)
        {
            _locator.Orphan();
            column.AddChild(_locator);
        }
        else if (_locator.GetPositionInParent() != column.ChildCount - 1)
            _locator.SetPositionInParent(column.ChildCount - 1);
    }

    private void OnSignal(WFCrewSignalEvent signal)
    {
        Signal = signal;
        _lastSignal = _timing.RealTime;
        if (signal.Body == null) _locator.Visible = false;
    }

    /// <summary>Shows one request at a time, preserving focus and leaving all other controls usable.</summary>
    private void OnState(WFRoundSetupState state)
    {
        var own = state.Lobbies.FirstOrDefault(lobby => lobby.Id == state.OwnLobby && lobby.Host == _players.LocalSession?.UserId);
        var pending = own?.Requests ?? Array.Empty<WFRoundSetupPlayer>();
        _dismissed.RemoveWhere(key => own == null || key.Item1 != own.Id || !pending.Any(p => p.UserId == key.Item2 && p.RoleId == key.Item3));
        var next = pending.FirstOrDefault(p => !_dismissed.Contains((own!.Id, p.UserId, p.RoleId)));
        if (own == null || next == null)
        {
            _shownRequest = null;
            _request.Visible = false;
            return;
        }
        var key = (own.Id, next.UserId, next.RoleId);
        var role = own.Roles.FirstOrDefault(r => r.Id == next.RoleId);
        // A request with role -1 is a company member joining as their rank, which takes no opening.
        var asRank = next.RoleId == -1;
        var canAccept = WFRoundSetupPolicy.TakesRequests(own.Access) && own.MemberCount < WFRoundSetupPolicy.MaximumMembers &&
            (!own.Started || own.AllowLateJoin) && (asRank || role != null && role.Occupied < role.Capacity);
        if (_shownRequest == key)
        {
            SetAcceptAvailability(canAccept);
            return;
        }
        _shownRequest = key;
        _request.RemoveAllChildren();
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        column.AddChild(new Label { Text = Loc.GetString("wf-round-setup-request-popup-title"), Modulate = Color.FromHex("#F1B85A") });
        var copy = new RichTextLabel();
        copy.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("wf-round-setup-request-popup-body", ("name", next.Name),
            ("role", asRank ? Loc.GetString("wf-round-setup-request-as-rank") : role == null ? Loc.GetString("wf-round-setup-job-unselected") : string.IsNullOrEmpty(role.Name)
                ? _prototypes.Index<JobPrototype>(role.Job).LocalizedName : role.Name))));
        column.AddChild(copy);
        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        foreach (var (label, action) in new[] { ("accept", WFRoundSetupAction.AcceptRequest), ("reject", WFRoundSetupAction.RejectRequest) })
        {
            var button = new Button { Text = Loc.GetString("wf-round-setup-request-popup-" + label), HorizontalExpand = true };
            button.AddStyleClass(action == WFRoundSetupAction.AcceptRequest ? "ButtonAccept" : StyleBase.ButtonCaution);
            if (action == WFRoundSetupAction.AcceptRequest)
                _acceptRequest = button;
            button.OnPressed += _ => _setup.Send(new WFRoundSetupRequest(action) { LobbyId = own.Id, Target = next.UserId, RoleId = next.RoleId });
            buttons.AddChild(button);
        }
        var later = new Button { Text = Loc.GetString("wf-round-setup-request-popup-later"), HorizontalExpand = true,
            ToolTip = Loc.GetString("wf-round-setup-request-popup-later-help") };
        later.OnPressed += _ => { _dismissed.Add(key); _shownRequest = null; OnState(_setup.State ?? state); };
        buttons.AddChild(later);
        column.AddChild(buttons);
        _request.AddChild(column);
        _request.Visible = true;
        SetAcceptAvailability(canAccept);
    }

    private void SetAcceptAvailability(bool available)
    {
        if (_acceptRequest == null) return;
        _acceptRequest.Disabled = !available;
        _acceptRequest.ToolTip = available ? null : Loc.GetString("wf-round-setup-request-popup-unavailable");
    }

    private void OnRestart(RoundRestartCleanupEvent args) => Clear();
    private void OnDisconnect(object? sender, NetDisconnectedArgs args) => Clear();

    private void Clear()
    {
        Signal = null;
        _locator.Visible = false;
        _request.Visible = false;
        _shownRequest = null;
        _dismissed.Clear();
    }
}
