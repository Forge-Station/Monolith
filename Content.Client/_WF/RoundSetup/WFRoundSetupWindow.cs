using System.Linq;
using System.Numerics;
using Content.Client.Lobby;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Roles;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WF.RoundSetup;

/// <summary>Browses starting scenarios and manages a player's group, job and admission settings.</summary>
public sealed partial class WFRoundSetupWindow : DefaultWindow
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IClientPreferencesManager _preferences = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly WFRoundSetupClientSystem _system;
    private readonly BoxContainer _body = Column(12);
    private readonly RichTextLabel _message = new() { HorizontalExpand = true, Visible = false };
    private readonly Dictionary<int, string> _enteredCodes = new();
    private WFRoundSetupState? _state;
    private Robust.Shared.Network.NetUserId? _inviteTarget;
    private int? _settingsLobby;
    private bool _settingsDirty;
    private WFLobbyAccess _draftAccess;
    private bool _draftLateJoin;
    private string _draftTitle = string.Empty;
    private WFCrewFaction _newFaction = WFCrewFaction.TSF;
    private string _newTitle = string.Empty;
    private string _newDescription = string.Empty;
    private string _draftDescription = string.Empty;
    private bool _confirmingJoin;
    private bool _refreshAfterRequest;
    private bool _deferredRefresh;

    /// <summary>Builds the browser and subscribes while its window is open.</summary>
    public WFRoundSetupWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Text("title");
        MinSize = new Vector2(480, 360);
        SetSize = new Vector2(1120, 780);
        _system = _entities.System<WFRoundSetupClientSystem>();

        InitializeLayout();

        _system.Received += Receive;
        _preferences.OnServerDataLoaded += PreferencesLoaded;
        OnClose += () =>
        {
            _system.Received -= Receive;
            _preferences.OnServerDataLoaded -= PreferencesLoaded;
        };
        if (_system.State is { } state)
            Receive(state);
        else
            _body.AddChild(Description("loading"));
        Send(WFRoundSetupAction.Refresh);
    }

    /// <summary>Keeps the setup browser within the viewport.</summary>
    public void FitTo(Vector2 viewport)
    {
        var available = Vector2.Max(Vector2.One, viewport - new Vector2(32));
        MinSize = Vector2.Min(new Vector2(480, 360), available);
        SetSize = Vector2.Min(new Vector2(1120, 780), available);
        _compact = SetSize.X < 820;
        _lastLayoutWidth = SetSize.X;
        _layoutRefreshPending = false;
        Redraw();
    }

    private void PreferencesLoaded()
    {
        if (_state == null)
            return;
        var state = _state;
        _refreshAfterRequest = true;
        Receive(state);
    }

    /// <summary>Updates the browser from the recipient-specific server snapshot.</summary>
    private void Receive(WFRoundSetupState state)
    {
        if (_confirmingJoin && state.JoinConfirmed)
        {
            _confirmingJoin = false;
            Close();
            return;
        }
        if (state.Message != null)
            _confirmingJoin = false;
        ReceiveShipName(state);
        if (_pendingPage is { } page)
        {
            var own = state.Lobbies.FirstOrDefault(lobby => lobby.Id == state.OwnLobby);
            var me = own?.Members.FirstOrDefault(member => member.UserId == _players.LocalSession?.UserId);
            if (state.Message != null)
                _pendingPage = null;
            else if (me?.Title == _personalTitle.Trim())
            {
                _personalTitleDirty = false;
                _pendingPage = null;
                _lobbyPage = page;
            }
        }
        var previous = _state;
        _state = state;
        if (previous?.OwnLobby != state.OwnLobby)
        {
            _roleDrafts.Clear();
            _shipNameDirty = false;
            _pendingShipPage = null;
            _personalTitleDirty = false;
            _pendingPage = null;
            _saveName = string.Empty;
            _settingsDirty = false;
            _inviteTarget = null;
            _pageScroll.VScroll = 0;
            _editingRole = null;
            if (previous != null && state.OwnLobby != null)
            {
                _view = SetupView.MyLobby;
                _lobbyPage = LobbyPage.Overview;
            }
        }
        if (!string.IsNullOrEmpty(state.Message))
        {
            _message.SetMessage(FormattedMessage.FromUnformatted(state.Message));
            _message.ToolTip = state.Message;
            _message.Visible = true;
        }
        var refresh = _refreshAfterRequest;
        _refreshAfterRequest = false;
        if (!refresh && previous != null && SameState(previous, state))
            return;
        if (previous?.OwnLobby == state.OwnLobby && IsEditingControls())
        {
            _deferredRefresh = true;
            return;
        }
        RenderState(state);
    }

    /// <summary>Displays a lobby with the admission action its host permits.</summary>
    private Control LobbyCard(WFRoundSetupState state, WFRoundSetupLobby lobby, bool invitation)
    {
        var content = Column(12);
        content.AddChild(CrewIdentity(lobby));
        if (lobby.CompanyTag.Length > 0)
            content.AddChild(PlainDescription(Text("company-crew", ("tag", lobby.CompanyTag), ("company", lobby.CompanyName))));
        if (!string.IsNullOrEmpty(lobby.Description))
            content.AddChild(PlainDescription(lobby.Description));
        content.AddChild(PlainDescription(Text("lobby-info", ("count", lobby.MemberCount), ("access", AccessName(lobby.Access)),
            ("status", Text(lobby.Started ? "status-started" : "status-waiting")))));

        content.AddChild(Heading("members"));
        if (lobby.Started)
            content.AddChild(CrewHealthSummary(lobby));
        foreach (var crewmate in lobby.Roster)
        {
            var status = MemberStatusKey(crewmate.Spawned, crewmate.Ready, crewmate.Status);
            var role = string.IsNullOrEmpty(crewmate.Title) ? JobName(crewmate.Job) : crewmate.Title;
            content.AddChild(Row(WFRoundSetupCrewRow.JobIcon(crewmate.Job, crewmate.RankId),
                PlainDescription(Text("public-member", ("name", crewmate.Name), ("role", role), ("status", Text(status))))));
        }
        content.AddChild(WFScenarioDifficultyLabel.Create(lobby.Scenario));
        content.AddChild(Description(ScenarioDescription(lobby.Scenario)));
        if (WFRoundSetupPolicy.IsShip(lobby.Scenario) || lobby.Started)
        {
            content.AddChild(VesselSummary(state, lobby));
            if (!lobby.Started)
                content.AddChild(Description(ShipStartKey(lobby.ShipStart)));
        }
        var live = !state.CanJoin && state.CanJoinLive && lobby.LiveJoin;
        var available = (state.CanJoin || live) && state.OwnLobby == null && lobby.MemberCount < WFRoundSetupPolicy.MaximumMembers &&
                        (!lobby.Started || lobby.AllowLateJoin);
        // Members of the crew's company join as their rank, so openings aren't theirs to pick.
        var joinsAsRank = lobby.CanJoinAsRank && lobby.CompanyId != null && lobby.CompanyId == state.OwnCompany && lobby.Scenario != WFRoundScenario.FactionShip;
        var hasRoles = BuildBrowseRoles(content, lobby, available && !joinsAsRank);
        available &= hasRoles || joinsAsRank;
        if (available && live)
            content.AddChild(Description("live-join-help"));
        var disabledReason = !state.CanJoin && !live ? state.CanJoinLive ? "live-unavailable" : "disabled-join" : state.OwnLobby != null ? "disabled-own-crew" :
            lobby.MemberCount >= WFRoundSetupPolicy.MaximumMembers ? "disabled-full" :
            lobby.Started && !lobby.AllowLateJoin ? "latejoin-closed" : "no-roles";
        if (available && NeedsJoinWarning(lobby))
            content.AddChild(JoinWarning());
        var companyMember = lobby.CompanyId != null && lobby.CompanyId == state.OwnCompany;
        if (!invitation && lobby.CanJoinAsRank && state.CompanyRank != null &&
            state.OwnLobby == null && (state.CanJoin || live) && lobby.Access is not (WFLobbyAccess.Invite or WFLobbyAccess.Closed) &&
            (lobby.Access != WFLobbyAccess.CompanyOnly || companyMember))
        {
            var asRank = new Button { Text = Text("join-as-rank", ("rank", state.CompanyRank)), MinHeight = 40, HorizontalExpand = true, StyleClasses = { "ButtonAccept" } };
            asRank.OnPressed += _ => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.Join)
                { LobbyId = lobby.Id, AsRank = true, Code = _enteredCodes.GetValueOrDefault(lobby.Id) });
            content.AddChild(asRank);
        }
        if (invitation)
        {
            content.AddChild(FlowRow(JoinButton(lobby, "accept-invite", () => JoinLobby(WFRoundSetupAction.AcceptInvite, lobby.Id), !available, disabledReason),
                Button("decline", () => Send(WFRoundSetupAction.DeclineInvite, lobby.Id))));
        }
        else if (joinsAsRank && lobby.Access is not (WFLobbyAccess.Code or WFLobbyAccess.Invite or WFLobbyAccess.Closed))
        {
            // The join as rank button above is the way in.
        }
        else if (lobby.Access == WFLobbyAccess.Code)
        {
            var code = new LineEdit
            {
                PlaceHolder = Text("code-placeholder"),
                Text = _enteredCodes.GetValueOrDefault(lobby.Id, string.Empty),
                HorizontalExpand = true,
                Editable = available,
                ToolTip = available ? null : Text(disabledReason),
            };
            code.OnTextChanged += _ => _enteredCodes[lobby.Id] = code.Text;
            content.AddChild(FlowRow(code, JoinButton(lobby, "join-code", () => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.Join)
            {
                LobbyId = lobby.Id,
                Code = code.Text.Trim(),
                RoleId = _browsedRoles.GetValueOrDefault(lobby.Id),
            }), !available, disabledReason)));
        }
        else if (lobby.Access is WFLobbyAccess.Public or WFLobbyAccess.Requests or WFLobbyAccess.CompanyFirst || lobby.Access == WFLobbyAccess.CompanyOnly && companyMember)
        {
            var asks = lobby.Access == WFLobbyAccess.Requests || lobby.Access == WFLobbyAccess.CompanyFirst && !companyMember;
            content.AddChild(JoinButton(lobby, asks ? "request-join" : "join",
                () => JoinLobby(WFRoundSetupAction.Join, lobby.Id), !available, disabledReason));
        }
        else
            content.AddChild(Description(lobby.Access switch
            {
                WFLobbyAccess.Invite => "invite-only",
                WFLobbyAccess.CompanyOnly => "company-only-help",
                _ => "closed-help",
            }));

        if (lobby.Started && !lobby.AllowLateJoin)
            content.AddChild(Description("latejoin-closed"));
        return Panel(content);
    }

    /// <summary>Offers admission settings, join approvals and direct invitations to the host.</summary>
    private void BuildHostSettings(WFRoundSetupLobby lobby, WFRoundSetupState state)
    {
        if (_settingsLobby != lobby.Id || !_settingsDirty)
        {
            _settingsLobby = lobby.Id;
            _settingsDirty = false;
            _draftAccess = lobby.Access;
            _draftLateJoin = lobby.AllowLateJoin;
            _draftTitle = lobby.Title;
            _draftDescription = lobby.Description;
        }
        BuildInvitations(lobby, state);
        BuildRecruiting(lobby);
        BuildCrewCompany(lobby, state);
        var settings = Column(6);
        settings.AddChild(Heading("crew-title"));
        var title = new LineEdit { Text = _draftTitle, PlaceHolder = Text("crew-title-placeholder"), HorizontalExpand = true };
        title.OnTextChanged += _ => { _draftTitle = title.Text; _settingsDirty = true; };
        settings.AddChild(title);
        title.ToolTip = Text("crew-title-help");
        settings.AddChild(DescriptionEditor(_draftDescription, value => { _draftDescription = value; _settingsDirty = true; }));
        settings.AddChild(Heading("admission"));
        var access = new WFRoundSetupOptionButton { HorizontalExpand = true };
        if (lobby.CompanyId == null && WFRoundSetupPolicy.IsCompanyAccess(_draftAccess))
            _draftAccess = WFLobbyAccess.Requests;
        foreach (var option in Enum.GetValues<WFLobbyAccess>().Where(option => lobby.CompanyId != null || !WFRoundSetupPolicy.IsCompanyAccess(option)))
            access.AddItem(AccessName(option), (int) option);
        access.SelectId((int) _draftAccess);
        access.OnItemSelected += args =>
        {
            access.SelectId(args.Id);
            _draftAccess = (WFLobbyAccess) args.Id;
            _settingsDirty = true;
        };
        var lateJoin = new CheckBox { Text = Text("allow-latejoin"), Pressed = _draftLateJoin };
        lateJoin.OnToggled += args =>
        {
            _draftLateJoin = args.Pressed;
            _settingsDirty = true;
        };
        settings.AddChild(Description("admission-help"));
        settings.AddChild(access);
        settings.AddChild(lateJoin);

        if (lobby.Started || !WFRoundSetupPolicy.IsShip(lobby.Scenario))
        {
            var saveSettings = Button("save-settings", () =>
            {
                _settingsDirty = false;
                SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.Configure)
                {
                    LobbyId = lobby.Id,
                    Access = _draftAccess,
                    AllowLateJoin = _draftLateJoin,
                    Title = _draftTitle,
                    Description = _draftDescription,
                });
            });
            CheckDescription(saveSettings, () => _draftDescription);
            settings.AddChild(saveSettings);
        }
        if (lobby.Code != null)
        {
            var code = new LineEdit { Text = lobby.Code, Editable = false, HorizontalExpand = true };
            settings.AddChild(Heading("share-code"));
            settings.AddChild(FlowRow(code,
                Button("rotate-code", () => Send(WFRoundSetupAction.RegenerateCode, lobby.Id))));
        }
        _body.AddChild(Panel(settings));
        if (!WFRoundSetupPolicy.IsFormed(lobby.Scenario))
            BuildSaveSetup(lobby, state);

        if (lobby.Requests.Length > 0)
        {
            _body.AddChild(Heading("requests"));
            foreach (var request in lobby.Requests)
            {
                var role = lobby.Roles.FirstOrDefault(choice => choice.Id == request.RoleId);
                var label = Text("request-role", ("name", request.Name),
                    ("role", role == null ? Text("job-unselected") : RoleName(role)));
                _body.AddChild(PlainDescription(label));
                _body.AddChild(FlowRow(
                    Button("accept", () => SendTarget(WFRoundSetupAction.AcceptRequest, lobby.Id, request.UserId)),
                    Button("reject", () => SendTarget(WFRoundSetupAction.RejectRequest, lobby.Id, request.UserId))));
            }
        }
    }

    /// <summary>Lets an unspawned member choose their own character, job and readiness.</summary>
    private void BuildMemberSetup(WFRoundSetupState state, WFRoundSetupLobby lobby, WFRoundSetupMember member)
    {
        _body.AddChild(Heading("your-character"));
        if (_preferences.Preferences is { } preferences)
        {
            var characters = new WFRoundSetupOptionButton { HorizontalExpand = true, Disabled = member.Ready, ToolTip = Text("unready-edit") };
            foreach (var (slot, profile) in preferences.Characters.OrderBy(pair => pair.Key))
                characters.AddItem(profile.Name, slot);
            characters.SelectId(preferences.SelectedCharacterIndex);
            characters.OnItemSelected += args =>
            {
                characters.SelectId(args.Id);
                _preferences.SelectCharacter(args.Id);
                SetReady(lobby.Id, false);
                // After the rate limit and the server's handling of the switch, so both states describe the new character.
                Robust.Shared.Timing.Timer.Spawn(300, () =>
                {
                    Send(WFRoundSetupAction.Refresh);
                    _entities.System<Companies.WFCompanyClientSystem>().Send(new Shared._WF.Companies.WFCompanyRequest(Shared._WF.Companies.WFCompanyAction.Refresh));
                });
                UserInterfaceManager.GetUIController<LobbyUIController>().ReloadCharacterSetup();
            };
            _body.AddChild(characters);
        }

        if (member.RankLocked)
        {
            // Their job is their company rank; a half-typed personal title no longer applies.
            _personalTitleDirty = false;
            _body.AddChild(Heading("personal-job"));
            _body.AddChild(Row(WFRoundSetupCrewRow.JobIcon(member.Job, member.RankId), PlainDescription(Text("company-rank-locked", ("rank", member.Title), ("job", JobName(member.Job))))));
            BuildJobLoadout(lobby, member);
            return;
        }
        if (lobby.Host == member.UserId || member.RoleId == null)
            BuildPersonalJobSetup(state, lobby, member);
        else if (lobby.Host != member.UserId)
            BuildMemberRoles(lobby, member);
        BuildJobLoadout(lobby, member);
        if (lobby.Host == member.UserId)
            BuildPersonalTitle(lobby, member);
    }

    /// <summary>Keeps unchanged periodic snapshots from interrupting text entry and open selectors.</summary>
    private static bool SameState(WFRoundSetupState first, WFRoundSetupState second)
    {
        return first.Payment == second.Payment && first.CrewShips.SequenceEqual(second.CrewShips) && first.PendingShip == second.PendingShip &&
               first.InRound == second.InRound && first.CanJoin == second.CanJoin && first.CanJoinLive == second.CanJoinLive && first.OwnLobby == second.OwnLobby &&
               first.RecruitingStations.SequenceEqual(second.RecruitingStations) && first.OwnCompany == second.OwnCompany &&
               first.FieldCompanies.SequenceEqual(second.FieldCompanies) && first.CompanyRank == second.CompanyRank &&
               first.CrashlandedAvailable == second.CrashlandedAvailable && first.DoomedAvailable == second.DoomedAvailable && first.Factions.SequenceEqual(second.Factions) && first.HostBalance == second.HostBalance &&
               SameSavedSetups(first.SavedSetups, second.SavedSetups) && first.Vessels.SequenceEqual(second.Vessels) &&
               first.Jobs.SequenceEqual(second.Jobs) && first.RoleJobs.SequenceEqual(second.RoleJobs) && first.InviteTargets.SequenceEqual(second.InviteTargets) &&
               first.Lobbies.Length == second.Lobbies.Length && first.Invitations.Length == second.Invitations.Length &&
               first.Lobbies.Zip(second.Lobbies).All(pair => SameLobby(pair.First, pair.Second)) &&
               first.Invitations.Zip(second.Invitations).All(pair => SameLobby(pair.First, pair.Second));
    }

    private static bool SameLobby(WFRoundSetupLobby first, WFRoundSetupLobby second)
    {
        return first.Id == second.Id && first.Host == second.Host && first.HostJob == second.HostJob && first.HostRankId == second.HostRankId && first.HostTitle == second.HostTitle && Equals(first.Portrait, second.Portrait) && first.HostName == second.HostName && first.Title == second.Title && first.Description == second.Description &&
               first.Scenario == second.Scenario && first.Faction == second.Faction && first.ShipName == second.ShipName && first.ShipStart == second.ShipStart && first.Access == second.Access && first.AllowLateJoin == second.AllowLateJoin &&
               first.Started == second.Started && first.MemberCount == second.MemberCount && first.Code == second.Code &&
               first.Recruiting == second.Recruiting && first.RecruitingOn == second.RecruitingOn && first.CompanyId == second.CompanyId &&
               first.CompanyName == second.CompanyName && first.CompanyTag == second.CompanyTag && first.CompanyColour == second.CompanyColour && first.LiveJoin == second.LiveJoin && first.NextPosting == second.NextPosting && first.CanJoinAsRank == second.CanJoinAsRank &&
               first.CurrentShip == second.CurrentShip && first.ShipCondition == second.ShipCondition && first.Vessel == second.Vessel && first.Roles.SequenceEqual(second.Roles) &&
               first.Roster.SequenceEqual(second.Roster) && first.Members.SequenceEqual(second.Members) && first.Requests.SequenceEqual(second.Requests);
    }

    private void SendRequest(WFRoundSetupRequest request)
    {
        _message.Visible = false;
        _refreshAfterRequest |= request.Action != WFRoundSetupAction.Refresh;
        _system.Send(request);
    }

    private void SetReady(int lobby, bool ready)
    {
        SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SetReady) { LobbyId = lobby, Ready = ready });
    }

    private void Send(WFRoundSetupAction action, int lobby = 0)
    {
        SendRequest(new WFRoundSetupRequest(action) { LobbyId = lobby });
    }

    private void SendTarget(WFRoundSetupAction action, int lobby, Robust.Shared.Network.NetUserId target)
    {
        SendRequest(new WFRoundSetupRequest(action) { LobbyId = lobby, Target = target });
    }

    private string JobName(string? id)
    {
        return id != null && _prototypes.TryIndex<JobPrototype>(id, out var prototype)
            ? prototype.LocalizedName : Text("job-unselected");
    }

    private static string LobbyTitle(WFRoundSetupLobby lobby) => string.IsNullOrEmpty(lobby.Title)
        ? Text("lobby-title", ("host", lobby.Portrait?.Name ?? lobby.HostName), ("scenario", ScenarioName(lobby.Scenario))) : lobby.Title;

    private static string ScenarioName(WFRoundScenario scenario) => Text(scenario switch
    {
        WFRoundScenario.Crashlanded => "scenario-crash",
        WFRoundScenario.Ship => "scenario-ship",
        WFRoundScenario.FactionShip => "scenario-faction",
        WFRoundScenario.Doomed => "scenario-doomed",
        WFRoundScenario.Formed => "scenario-formed",
        _ => "scenario-normal",
    });

    private static string ScenarioDescription(WFRoundScenario scenario) => scenario switch
    {
        WFRoundScenario.Crashlanded => "crash-description",
        WFRoundScenario.Ship => "ship-description",
        WFRoundScenario.FactionShip => "faction-description",
        WFRoundScenario.Doomed => "doomed-description",
        WFRoundScenario.Formed => "formed-description",
        _ => "normal-description",
    };

    private static string AccessName(WFLobbyAccess access) => Text($"access-{access.ToString().ToLowerInvariant()}");

    private static string Text(string key, params (string, object)[] args) => Loc.GetString($"wf-round-setup-{key}", args);

    private static BoxContainer Column(int separation) => new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = separation, HorizontalExpand = true };

    private static BoxContainer Row(params Control[] controls)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8, HorizontalExpand = true };
        foreach (var control in controls)
            row.AddChild(control);
        return row;
    }

    private static RichTextLabel Heading(string key, params (string, object)[] args)
    {
        var heading = PlainDescription(Text(key, args));
        heading.StyleClasses.Add("WFRoundSetupHeading");
        return heading;
    }

    private static RichTextLabel Description(string key) => PlainDescription(Text(key));

    private static RichTextLabel PlainDescription(string text)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromUnformatted(text));
        return label;
    }

    private static Button Button(string key, Action pressed, bool disabled = false, string? disabledReason = null)
    {
        var button = new Button { Text = Text(key), Disabled = disabled, MinHeight = 40, HorizontalExpand = true,
            ToolTip = disabled ? Text(disabledReason ?? "disabled-join") : null };
        if (key == "leave")
            button.AddStyleClass("ButtonColorRed");
        button.Label.Visible = false;
        var caption = PlainDescription(button.Text);
        caption.MouseFilter = MouseFilterMode.Ignore;
        button.AddChild(caption);
        button.OnPressed += _ => pressed();
        return button;
    }

    private static PanelContainer Panel(Control content)
    {
        content.Margin = new Thickness(14);
        return new PanelContainer
        {
            HorizontalExpand = true,
            StyleClasses = { "WFRoundSetupCard" },
            Children = { content },
        };
    }
}
