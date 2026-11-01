using System.Linq;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    private enum SetupView : byte { Browse, Create, Saved, MyLobby }
    private enum LobbyPage : byte { Overview, Ship, Admission, Roles }

    private readonly ScenarioColumns _navigation = new() { HorizontalExpand = true };
    private readonly BoxContainer _shipPage = Column(8);
    private readonly BoxContainer _footer = Column(8);
    private readonly ScrollContainer _pageScroll = new() { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
    private SetupView _view;
    private LobbyPage _lobbyPage;
    private int? _focusedLobby;
    private bool _pendingLobbyFocus;
    private bool _initialViewResolved;
    private bool _manualNavigation;
    private bool _compact;
    private bool _rendering;
    private float _lastLayoutWidth = 1120;
    private bool _layoutRefreshPending;

    /// <summary>Opens the player's crew or the crew browser.</summary>
    public void ShowJoin()
    {
        _manualNavigation = true;
        _pendingLobbyFocus = false;
        Navigate(_state?.OwnLobby == null ? SetupView.Browse : SetupView.MyLobby);
    }

    /// <summary>Opens available crews or the player's current setup.</summary>
    public void ShowCrews() => Navigate(_state?.OwnLobby == null ? SetupView.Browse : SetupView.MyLobby);

    /// <summary>Opens crew details without changing membership.</summary>
    public void ShowLobby(int lobbyId)
    {
        _manualNavigation = true;
        _focusedLobby = lobbyId;
        _pendingLobbyFocus = true;
        _view = _state?.OwnLobby == lobbyId ? SetupView.MyLobby : SetupView.Browse;
        _lobbyPage = LobbyPage.Overview;
        _pageScroll.VScroll = 0;
        Redraw();
    }

    /// <summary>Shows scenarios before an explicit creation action.</summary>
    public void OpenCreate() => Navigate(SetupView.Create);

    /// <summary>Opens the scenario selection screen.</summary>
    public void ShowScenarios() => OpenCreate();

    private void InitializeLayout()
    {
        _navigation.Name = "RoundSetupNavigation";
        _shipPage.Name = "RoundSetupShipPage";
        _shipPage.VerticalExpand = true;
        _shipPage.HorizontalExpand = true;
        _shipPage.Visible = false;
        _body.Name = "RoundSetupPage";
        _footer.Name = "RoundSetupFooter";
        _pageScroll.Name = "RoundSetupPageScroll";
        var layout = Column(12);
        layout.Margin = new Thickness(8);
        Contents.AddChild(new PanelContainer
        {
            StyleClasses = { "WFRoundSetupSurface" },
            Children = { layout },
        });
        var company = new Button { Text = Text("company-open"), HorizontalAlignment = HAlignment.Right, ToolTip = Text("company-open-help") };
        company.OnPressed += _ => _entities.System<Content.Client._WF.Companies.WFCompanyClientSystem>().OpenWindow();
        layout.AddChild(company);
        layout.AddChild(_navigation);
        layout.AddChild(_message);
        _pageScroll.AddChild(_body);
        layout.AddChild(_pageScroll);
        layout.AddChild(_shipPage);
        layout.AddChild(_footer);
    }

    protected override void Resized()
    {
        base.Resized();
        var compact = Width < 820;
        if (_compact == compact && Math.Abs(_lastLayoutWidth - Width) < 1)
            return;
        _compact = compact;
        _lastLayoutWidth = Width;
        _layoutRefreshPending = true;
    }

    private void Redraw()
    {
        if (_state != null && !_rendering)
            RenderState(_state);
    }

    private void Navigate(SetupView view)
    {
        _manualNavigation = true;
        _view = view;
        _pendingLobbyFocus = false;
        _pageScroll.VScroll = 0;
        Redraw();
    }

    private void NavigateLobby(LobbyPage page)
    {
        if (_lobbyPage == LobbyPage.Overview && _personalTitleDirty && _state?.OwnLobby is { } id)
        {
            if (!WFRoundSetupPolicy.TryNormalizeRoleName(_personalTitle, out var title))
                return;
            _personalTitle = title;
            _pendingPage = page;
            SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SetTitle) { LobbyId = id, RoleName = _personalTitle });
            return;
        }
        if (_lobbyPage == LobbyPage.Ship && _shipNameDirty && _state?.OwnLobby is { } shipLobby)
        {
            SaveShipName(shipLobby, page);
            return;
        }
        _lobbyPage = page;
        _pageScroll.VScroll = 0;
        Redraw();
    }

    /// <summary>Rebuilds the active task while retaining local form drafts.</summary>
    private void RenderState(WFRoundSetupState state)
    {
        if (_rendering)
            return;
        _deferredRefresh = false;
        _rendering = true;
        try
        {
            var own = state.Lobbies.FirstOrDefault(lobby => lobby.Id == state.OwnLobby);
            if (!_initialViewResolved)
            {
                _initialViewResolved = true;
                if (!_manualNavigation)
                    _view = own == null ? SetupView.Browse : SetupView.MyLobby;
            }
            if (_pendingLobbyFocus)
            {
                _pendingLobbyFocus = false;
                _view = own?.Id == _focusedLobby ? SetupView.MyLobby : SetupView.Browse;
                _lobbyPage = LobbyPage.Overview;
            }
            if (_view == SetupView.MyLobby && own == null)
                _view = SetupView.Browse;
            if (own != null && (_view != SetupView.Browse || _focusedLobby == null || _focusedLobby == own.Id))
                _view = SetupView.MyLobby;
            _descriptionChecks.Clear();
            _body.RemoveAllChildren();
            _navigation.RemoveAllChildren();
            _shipPage.RemoveAllChildren();
            _shipPage.Visible = false;
            _pageScroll.Visible = true;
            _pageScroll.VScrollEnabled = true;
            _body.SeparationOverride = 12;
            Title = own != null && _view == SetupView.MyLobby ? LobbyTitle(own) : Text("title");
            _footer.RemoveAllChildren();
            if (own == null)
            {
                _navigation.AddChild(NavigationButton("flow-browse", () => Navigate(SetupView.Browse), _view == SetupView.Browse));
                _navigation.AddChild(NavigationButton("flow-create", OpenCreate, _view is SetupView.Create or SetupView.Saved, !state.CanJoin && !state.CanJoinLive));
            }
            switch (_view)
            {
                case SetupView.Create: BuildScenarios(state); break;
                case SetupView.Saved: BuildSavedSetups(state); break;
                case SetupView.MyLobby when own != null: BuildOwnLobby(state, own); break;
                default: BuildBrowser(state); break;
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private static Button NavigationButton(string key, Action action, bool selected, bool disabled = false)
    {
        var button = Button(key, action, disabled);
        button.ToggleMode = true;
        button.Pressed = selected;
        button.HorizontalExpand = true;
        return button;
    }

    private Container FlowRow(params Control[] controls)
    {
        Container row = _compact ? Column(8) : new ScenarioColumns { HorizontalExpand = true };
        foreach (var control in controls)
            row.AddChild(control);
        return row;
    }

    /// <summary>Lists crews first, then opens a focused briefing and role selection.</summary>
    private void BuildBrowser(WFRoundSetupState state)
    {
        _body.AddChild(Heading("browse"));
        var lobbies = state.Lobbies.Where(lobby => lobby.Id != state.OwnLobby)
            .OrderByDescending(lobby => state.Invitations.Any(invitation => invitation.Id == lobby.Id))
            .ThenByDescending(lobby => state.OwnCompany != null && lobby.CompanyId == state.OwnCompany)
            .ThenByDescending(lobby => lobby.Recruiting).ThenBy(lobby => lobby.Started).ToArray();
        var selected = lobbies.FirstOrDefault(lobby => lobby.Id == _focusedLobby);
        if (selected != null)
        {
            _body.AddChild(Button(state.OwnLobby == null ? "back-lobbies" : "flow-my-lobby", () => { _focusedLobby = null; Redraw(); }));
            _body.AddChild(LobbyCard(state, selected, state.Invitations.Any(invitation => invitation.Id == selected.Id)));
        }
        else
        {
            _focusedLobby = null;
            _body.AddChild(new Companies.WFCompanyQuickJoin(true));
            _body.AddChild(Description(lobbies.Length == 0 ? "empty" : "browse-help"));
            foreach (var lobby in lobbies)
                _body.AddChild(LobbyListing(lobby, state.Invitations.Any(invitation => invitation.Id == lobby.Id)));
        }
        _footer.AddChild(Button("refresh", () => Send(WFRoundSetupAction.Refresh)));
    }

    private Control LobbyListing(WFRoundSetupLobby lobby, bool invited) =>
        new WFRoundSetupCrewRow(lobby, invited, () => ShowLobby(lobby.Id));

    /// <summary>Shows full-width scenario briefs with one explicit create action per scenario.</summary>
    private void BuildScenarios(WFRoundSetupState state)
    {
        if (!state.CanJoin && state.CanJoinLive)
        {
            BuildFormedScenario();
            return;
        }
        var load = Button("load-saved-scenario", () => { _selectedSavedId = null; Navigate(SetupView.Saved); },
            state.SavedSetups.Length == 0, "saved-empty-help");
        load.Name = "RoundSetupLoadSaved";
        _body.AddChild(load);
        _body.AddChild(Heading("choose-scenario"));
        _body.AddChild(Description("create-help"));
        _body.AddChild(Heading("crew-title"));
        var crewName = new LineEdit { Text = _newTitle, PlaceHolder = Text("crew-title-placeholder"), HorizontalExpand = true };
        crewName.OnTextChanged += _ => _newTitle = crewName.Text;
        _body.AddChild(crewName);
        crewName.ToolTip = Text("crew-title-help");
        _body.AddChild(DescriptionEditor(_newDescription, value => _newDescription = value));
        BuildCreateCompany(state);
        foreach (var scenario in new[] { WFRoundScenario.Ship, WFRoundScenario.Crashlanded, WFRoundScenario.FactionShip, WFRoundScenario.Doomed })
        {
            var card = Column(8);
            var title = PlainDescription(ScenarioName(scenario));
            title.StyleClasses.Add("WFRoundSetupHeading");
            card.AddChild(title);
            card.AddChild(WFScenarioDifficultyLabel.Create(scenario));
            card.AddChild(Description(ScenarioDescription(scenario)));
            if (scenario == WFRoundScenario.FactionShip)
            {
                var faction = new WFRoundSetupOptionButton { Name = "RoundSetupFaction", HorizontalExpand = true };
                foreach (var option in WFFactionCrewPolicy.All)
                    faction.AddItem(Text("faction-" + option.ToString().ToLowerInvariant()), (int) option);
                faction.SelectId((int) _newFaction);
                faction.OnItemSelected += args => { _newFaction = (WFCrewFaction) args.Id; Redraw(); };
                card.AddChild(faction);
            }
            var unavailable = scenario == WFRoundScenario.Crashlanded && !state.CrashlandedAvailable ||
                scenario == WFRoundScenario.Doomed && !state.DoomedAvailable ||
                scenario == WFRoundScenario.FactionShip && !state.Factions.Contains(_newFaction);
            var unavailableReason = scenario switch
            {
                WFRoundScenario.FactionShip => "faction-unavailable",
                WFRoundScenario.Doomed => "doomed-unavailable",
                _ => "crash-unavailable",
            };
            if (unavailable)
                card.AddChild(Description(unavailableReason));
            var create = Button("create", () => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.Create)
            {
                CompanyId = CreateCompany(state),
                Scenario = scenario,
                Faction = scenario == WFRoundScenario.FactionShip ? _newFaction : WFCrewFaction.None,
                Title = _newTitle,
                Description = _newDescription,
            }), unavailable || !state.CanJoin);
            create.ToolTip = create.Disabled ? Text(unavailable ? unavailableReason : "disabled-join") : null;
            CheckDescription(create, () => _newDescription);
            create.Name = "RoundSetupCreate" + scenario;
            card.AddChild(create);
            _body.AddChild(Panel(card));
        }
    }

    /// <summary>Separates personal preparation from the host's crew and access tools.</summary>
    private void BuildOwnLobby(WFRoundSetupState state, WFRoundSetupLobby lobby)
    {
        var user = _players.LocalSession?.UserId;
        var host = lobby.Host == user;
        var member = lobby.Members.FirstOrDefault(player => player.UserId == user);
        var pages = new List<(LobbyPage Page, string Label)> { (LobbyPage.Overview, "flow-overview") };
        if (WFRoundSetupPolicy.IsShip(lobby.Scenario) || lobby.Started)
            pages.Add((LobbyPage.Ship, "ship"));
        pages.Add((LobbyPage.Roles, "crew-section"));
        if (host)
            pages.Add((LobbyPage.Admission, "admission"));
        if (!pages.Any(page => page.Page == _lobbyPage))
            _lobbyPage = LobbyPage.Overview;
        var sections = new ScenarioColumns { Name = "RoundSetupSections", HorizontalExpand = true };
        foreach (var (page, label) in pages)
            sections.AddChild(NavigationButton(label, () => NavigateLobby(page), _lobbyPage == page));
        var navigation = Column(8);
        navigation.AddChild(sections);
        if (state.PendingShip is { } pending && pending.Owner == user && !host)
            navigation.AddChild(ShipApproval(lobby, pending));
        _navigation.AddChild(navigation);
        if (_lobbyPage == LobbyPage.Ship)
        {
            _pageScroll.Visible = false;
            _shipPage.Visible = true;
        }
        else
        {
            _body.AddChild(PlainDescription(Text("lobby-info", ("count", lobby.MemberCount), ("access", AccessName(lobby.Access)),
                ("status", Text(lobby.Started ? "status-started" : "status-waiting")))));
        }
        switch (_lobbyPage)
        {
            case LobbyPage.Ship: BuildShipSettings(state, lobby, host); break;
            case LobbyPage.Roles:
                if (!string.IsNullOrEmpty(lobby.Description))
                    _body.AddChild(PlainDescription(lobby.Description));
                BuildContributions(state, lobby, host);
                BuildCrewRoster(lobby, host);
                if (host)
                    BuildRoleEditor(state, lobby);
                break;
            case LobbyPage.Admission: BuildHostSettings(lobby, state); break;
            default:
                if (host && WFRoundSetupPolicy.IsShip(lobby.Scenario) && member is { Spawned: false })
                {
                    _body.SeparationOverride = 6;
                    _pageScroll.VScrollEnabled = false;
                    if (!lobby.Started)
                        BuildScenarioSelector(state, lobby);
                    BuildCrewCompanyRow(lobby, state);
                    BuildMemberSetup(state, lobby, member);
                    break;
                }
                _body.AddChild(CrewIdentity(lobby));
                if (host && !lobby.Started)
                    BuildScenarioSelector(state, lobby);
                if (host)
                    BuildCrewCompanyRow(lobby, state);
                _body.AddChild(WFScenarioDifficultyLabel.Create(lobby.Scenario));
                _body.AddChild(Description(ScenarioDescription(lobby.Scenario)));
                if (member is { Spawned: false } && state.CanJoin)
                    BuildMemberSetup(state, lobby, member);
                else
                    _body.AddChild(Description("already-playing"));
                if (WFRoundSetupPolicy.IsShip(lobby.Scenario) || lobby.Started)
                {
                    _body.AddChild(VesselSummary(state, lobby));
                    if (!lobby.Started)
                        _body.AddChild(Description(ShipStartKey(lobby.ShipStart)));
                    _body.AddChild(Button("ship", () => NavigateLobby(LobbyPage.Ship)));
                }
                _body.AddChild(Description(state.InRound ? "ready-round-help" : "ready-pregame-help"));
                break;
        }
        BuildMemberFooter(state, lobby, member);
    }

    /// <summary>Changes the starting scenario without discarding the existing crew or their roles.</summary>
    private void BuildScenarioSelector(WFRoundSetupState state, WFRoundSetupLobby lobby)
    {
        var selector = new WFRoundSetupOptionButton { Name = "RoundSetupCrewType", HorizontalExpand = true };
        selector.AddItem(ScenarioName(WFRoundScenario.Ship), (int) WFRoundScenario.Ship);
        selector.AddItem(ScenarioName(WFRoundScenario.Crashlanded), (int) WFRoundScenario.Crashlanded);
        if (!state.CrashlandedAvailable)
            selector.DisableItem(1, Text("crash-unavailable"));
        selector.AddItem(ScenarioName(WFRoundScenario.Doomed), (int) WFRoundScenario.Doomed);
        if (!state.DoomedAvailable)
            selector.DisableItem(2, Text("doomed-unavailable"));
        foreach (var faction in WFFactionCrewPolicy.All)
        {
            selector.AddItem(Text("faction-start", ("faction", Text("faction-" + faction.ToString().ToLowerInvariant()))), 10 + (int) faction);
            if (!state.Factions.Contains(faction))
                selector.DisableItem(selector.ItemCount - 1, Text("faction-unavailable"));
        }
        selector.SelectId(lobby.Scenario == WFRoundScenario.FactionShip ? 10 + (int) lobby.Faction : (int) lobby.Scenario);
        selector.OnItemSelected += args => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.ChangeScenario)
        {
            LobbyId = lobby.Id,
            Scenario = args.Id > 10 ? WFRoundScenario.FactionShip : (WFRoundScenario) args.Id,
            Faction = args.Id > 10 ? (WFCrewFaction) (args.Id - 10) : WFCrewFaction.None,
        });
        _body.AddChild(selector);
        selector.ToolTip = Text("crew-type-help");
    }

    private void BuildCrewRoster(WFRoundSetupLobby lobby, bool host)
    {
        _body.AddChild(Heading("members"));
        if (lobby.Started)
            _body.AddChild(CrewHealthSummary(lobby));
        foreach (var player in lobby.Members)
        {
            var status = !player.Connected && !player.Spawned ? Text("member-disconnected")
                : !player.Connected ? Text("member-disconnected-status", ("status", Text(MemberStatusKey(true, player.Ready, player.Status))))
                : Text(MemberStatusKey(player.Spawned, player.Ready, player.Status));
            var content = Column(6);
            content.AddChild(Row(WFRoundSetupCrewRow.JobIcon(player.Job, player.RankId), PlainDescription(player.UserId == lobby.Host && lobby.Portrait != null ? lobby.Portrait.Name : player.Name)));
            content.AddChild(PlainDescription(Text("member-info", ("job", MemberRoleName(lobby, player)), ("status", status))));
            if (host && player.UserId != lobby.Host)
            {
                var actions = FlowRow(Button("transfer", () => SendTarget(WFRoundSetupAction.TransferHost, lobby.Id, player.UserId)));
                if (!player.Spawned || player.Live)
                    actions.AddChild(Button("kick", () => SendTarget(WFRoundSetupAction.Kick, lobby.Id, player.UserId)));
                content.AddChild(actions);
            }
            _body.AddChild(Panel(content));
        }
    }

    /// <summary>Keeps readiness, purchase consent and launch actions outside the scrolling page.</summary>
    private void BuildMemberFooter(WFRoundSetupState state, WFRoundSetupLobby lobby, WFRoundSetupMember? member)
    {
        if (member is not { Spawned: false } || !state.CanJoin)
        {
            _footer.AddChild(Button("leave", () => Send(WFRoundSetupAction.Leave, lobby.Id)));
            return;
        }
        var purchase = WFRoundSetupPolicy.IsShip(lobby.Scenario) && lobby.Host == member.UserId && !lobby.Started;
        var vessel = state.Vessels.FirstOrDefault(choice => choice.Id == lobby.Vessel);
        var eligible = member.RoleId == null
            ? state.Jobs.Any(job => job.Id == member.Job && (member.Station == null || job.Station == member.Station))
            : lobby.Roles.Any(role => role.Id == member.RoleId && role.CanSelect);
        var payment = state.Payment;
        var canPurchase = payment == null || payment.Price > 0 && payment.Allocated == payment.Price &&
            (payment.Contribution == 0 || state.HostBalance is { } balance && balance >= payment.Contribution);
        var paymentReason = payment is { Price: > 0 } && payment.Allocated != payment.Price ? "payment-total-error" :
            lobby.Vessel == null ? "ship-unselected" : state.HostBalance == null ? "ship-bank-help" : "ship-insufficient-help";
        if (purchase && !member.Ready && _lobbyPage != LobbyPage.Admission)
        {
            var nextPage = _lobbyPage == LobbyPage.Overview ? LobbyPage.Ship :
                _lobbyPage == LobbyPage.Ship ? LobbyPage.Roles : LobbyPage.Admission;
            var reason = _lobbyPage == LobbyPage.Overview && !eligible ? "ready-needs-job" :
                _lobbyPage == LobbyPage.Ship && vessel == null ? "ship-unselected" :
                _lobbyPage == LobbyPage.Roles && _editingRole != null ? "finish-role-edit" : null;
            var next = Button("next", () => NavigateLobby(nextPage), reason != null, reason);
            next.Name = "RoundSetupNext";
            next.AddStyleClass("ButtonAccept");
            _footer.AddChild(Row(next, Button("leave", () => Send(WFRoundSetupAction.Leave, lobby.Id))));
            return;
        }
        var content = Column(8);
        content.AddChild(Description(!eligible ? "ready-needs-job" : !canPurchase
            ? paymentReason
            : member.Ready ? state.InRound ? "join-waiting" : "ready-confirmed" : "ready-prompt"));
        if (payment is { Price: > 0 })
        {
            content.AddChild(PlainDescription(Text("payment-share", ("amount", Price(payment.Contribution)), ("total", Price(payment.Price)))));
            content.AddChild(Description("payment-consent-help"));
            if (member.Ready && !payment.AllApproved)
                content.AddChild(Description("payment-waiting"));
        }
        if (purchase && member.Ready && state.InRound && payment is { AllApproved: true })
        {
            var launch = Button("payment-launch", () =>
            {
                _confirmingJoin = true;
                Send(WFRoundSetupAction.Launch, lobby.Id);
            });
            launch.AddStyleClass("ButtonAccept");
            content.AddChild(launch);
        }
        var caption = member.Ready ? state.InRound ? "cancel-join" : "change-setup"
            : payment is { Contribution: > 0 } ? "payment-approve" : purchase ? "finish-ready" : state.InRound ? "join-round" : "ready";
        var ready = Button(caption, () =>
        {
            if (member.Ready)
                SetReady(lobby.Id, false);
            else
            {
                _confirmingJoin = true;
                SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.ConfirmJoin)
                {
                    LobbyId = lobby.Id,
                    PaymentRevision = payment?.Revision,
                    Title = lobby.Host == member.UserId && _settingsDirty ? _draftTitle : null,
                    Description = lobby.Host == member.UserId && _settingsDirty ? _draftDescription : null,
                    Access = _draftAccess,
                    AllowLateJoin = _draftLateJoin,
                    RoleName = lobby.Host == member.UserId && _personalTitleDirty ? _personalTitle : null,
                });
            }
        }, !member.Ready && (!eligible || !canPurchase), !eligible ? "ready-needs-job" :
            paymentReason);
        if (!member.Ready && lobby.Host == member.UserId && _lobbyPage == LobbyPage.Admission)
            CheckDescription(ready, () => _draftDescription);
        ready.Name = "RoundSetupReady";
        ready.AddStyleClass(member.Ready ? "ButtonColorGreen" : "ButtonAccept");
        var actions = new ScenarioColumns { HorizontalExpand = true };
        actions.AddChild(ready);
        actions.AddChild(Button("leave", () => Send(WFRoundSetupAction.Leave, lobby.Id)));
        content.AddChild(actions);
        _footer.AddChild(Panel(content));
    }
}
