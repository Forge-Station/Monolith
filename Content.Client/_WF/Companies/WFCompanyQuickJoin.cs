using System.Linq;
using Content.Client._WF.RoundSetup;
using Content.Client.Lobby;
using Content.Shared._WF.Companies;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.Companies;

/// <summary>One card with every way a company member can get into the round: company crews, hangar ships and joining as their rank.</summary>
public sealed class WFCompanyQuickJoin : BoxContainer
{
    private sealed record Option(string Text, string? ToolTip, Action Pressed);

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IClientPreferencesManager _preferences = default!;

    private readonly bool _embedded;
    private readonly BoxContainer _rows = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4, HorizontalExpand = true, Margin = new Thickness(8) };
    // This control stays visible and empty so it keeps getting frame updates; hidden controls don't.
    private readonly PanelContainer _card = new() { HorizontalExpand = true, StyleClasses = { "WFRoundSetupCard" }, Visible = false };
    private WFCompanyClientSystem? _companies;
    private WFRoundSetupClientSystem? _crews;
    private string _shown = string.Empty;
    private int? _selectedCharacter;
    private float _refreshIn;

    public WFCompanyQuickJoin() : this(false) { }

    /// <param name="embedded">Inside a window that already keeps the state fresh and names the company.</param>
    public WFCompanyQuickJoin(bool embedded)
    {
        IoCManager.InjectDependencies(this);
        _embedded = embedded;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        _card.AddChild(_rows);
        AddChild(_card);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _companies = _entities.System<WFCompanyClientSystem>();
        _crews = _entities.System<WFRoundSetupClientSystem>();
        _companies.Received += OnCompanyState;
        _crews.Received += OnCrewState;
        _selectedCharacter = _preferences.Preferences?.SelectedCharacterIndex;
        Rebuild();
        if (!_embedded)
            Refresh();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        if (_companies != null)
            _companies.Received -= OnCompanyState;
        if (_crews != null)
            _crews.Received -= OnCrewState;
    }

    /// <summary>Switching character changes the whole card, and the server may see the switch after the first refresh.</summary>
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_embedded)
            return;
        var selected = _preferences.Preferences?.SelectedCharacterIndex;
        if (selected != _selectedCharacter)
        {
            _selectedCharacter = selected;
            Refresh();
            _refreshIn = 1f;
            return;
        }
        if (_refreshIn <= 0f)
            return;
        _refreshIn -= args.DeltaSeconds;
        if (_refreshIn <= 0f)
            Refresh();
    }

    private void OnCompanyState(WFCompanyState state) => Rebuild();

    private void OnCrewState(WFRoundSetupState state) => Rebuild();

    private void Refresh()
    {
        _companies?.Send(new WFCompanyRequest(WFCompanyAction.Refresh));
        _crews?.Send(new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
    }

    /// <summary>Rebuilds only when the choices change, so a snapshot can't replace a button mid-click.</summary>
    private void Rebuild()
    {
        var heading = Heading();
        var options = Options();
        var shown = string.Join('\n', options.Select(option => option.Text).Prepend(heading));
        if (shown == _shown)
            return;
        _shown = shown;
        _rows.RemoveAllChildren();
        _card.Visible = options.Count > 0;
        if (options.Count == 0)
            return;
        if (!_embedded && heading.Length > 0)
            _rows.AddChild(new Label { Text = heading, FontColorOverride = _companies?.State?.Own?.Colour ?? Color.White, StyleClasses = { "LabelHeading" } });
        foreach (var option in options)
        {
            var button = new Button { Text = option.Text, HorizontalExpand = true, ToolTip = option.ToolTip, StyleClasses = { "ButtonAccept" } };
            button.OnPressed += _ => option.Pressed();
            _rows.AddChild(button);
        }
    }

    private string Heading() => _companies?.State?.Own is { } own
        ? Loc.GetString("wf-company-creator-member", ("tag", own.Tag), ("company", own.Name), ("rank", Rank(own)))
        : string.Empty;

    private static string Rank(WFCompanyDetail own) =>
        (own.YourProfile == own.OwnerProfile ? own.OwnerRank : own.Ranks.FirstOrDefault(rank => rank.Id == own.YourRank))?.Title ?? string.Empty;

    private List<Option> Options()
    {
        var options = new List<Option>();
        if (_companies?.State is not { Loaded: true } state)
            return options;
        var crews = _crews?.State;
        if (crews?.OwnLobby != null)
            return options;
        if (state.Own is not { } own)
        {
            AddCharacterSwitches(options, state, crews);
            return options;
        }
        var rank = Rank(own);
        foreach (var lobby in crews?.Lobbies ?? Array.Empty<WFRoundSetupLobby>())
        {
            if (lobby.CompanyId != own.Id || !lobby.CanJoinAsRank || !CanJoin(crews!, lobby))
                continue;
            var name = lobby.Title.Length > 0 ? lobby.Title : lobby.HostName;
            options.Add(new Option(Loc.GetString(lobby.Access == WFLobbyAccess.Requests ? "wf-company-quick-request-crew" : "wf-company-quick-join-crew",
                ("crew", name), ("rank", rank)), null, () =>
            {
                _crews?.Send(new WFRoundSetupRequest(WFRoundSetupAction.Join) { LobbyId = lobby.Id, AsRank = true });
                UserInterfaceManager.GetUIController<WFRoundSetupUIController>().OpenCrews();
            }));
        }
        if (crews?.CanJoin == true && (own.YourPermissions & WFCompanyPermissions.Launch) != 0 && own.Members.Length >= WFCompanyPolicy.MinimumFleetMembers)
        {
            foreach (var licence in own.Licences.Where(licence => !licence.Launched))
            {
                var registry = Loc.GetString("wf-company-credits", ("amount", WFCompanyPolicy.RegistryFee(licence.ShipyardPrice, own.LaunchedThisRound)));
                var fee = Loc.GetString("wf-company-credits", ("amount", own.LaunchFee));
                options.Add(new Option(Loc.GetString("wf-company-quick-launch", ("ship", licence.ShipName.Length > 0 ? licence.ShipName : licence.Name)),
                    Loc.GetString("wf-company-hangar-ready", ("registry", registry), ("fee", fee)), () =>
                {
                    _companies?.Send(new WFCompanyRequest(WFCompanyAction.Launch) { CompanyId = own.Id, LicenceId = licence.Id });
                    UserInterfaceManager.GetUIController<WFRoundSetupUIController>().OpenCrews();
                }));
            }
        }
        if (state.CanJoinRound)
        {
            options.Add(new Option(Loc.GetString("wf-company-join-as-rank-company", ("rank", rank), ("company", own.Name)), null, () =>
                _companies?.Send(new WFCompanyRequest(WFCompanyAction.JoinAsRank) { CompanyId = own.Id })));
        }
        return options;
    }

    /// <summary>Offers the player's characters that are in a company, when the selected one isn't.</summary>
    private void AddCharacterSwitches(List<Option> options, WFCompanyState state, WFRoundSetupState? crews)
    {
        if (crews?.CanJoin != true || _preferences.Preferences is not { } preferences)
            return;
        foreach (var character in state.Characters)
        {
            if (!preferences.Characters.TryGetValue(character.Slot, out var profile))
                continue;
            options.Add(new Option(Loc.GetString("wf-company-quick-switch", ("name", profile.Name), ("rank", character.Rank), ("tag", character.Tag),
                ("company", character.Company)), null, () =>
            {
                _preferences.SelectCharacter(character.Slot);
                UserInterfaceManager.GetUIController<LobbyUIController>().ReloadCharacterSetup();
            }));
        }
    }

    /// <summary>Whether a member could get into this crew without an invite or a code.</summary>
    private static bool CanJoin(WFRoundSetupState crews, WFRoundSetupLobby lobby) =>
        (crews.CanJoin || crews.CanJoinLive && lobby.LiveJoin) && lobby.MemberCount < WFRoundSetupPolicy.MaximumMembers &&
        (!lobby.Started || lobby.AllowLateJoin) && lobby.Access is not (WFLobbyAccess.Invite or WFLobbyAccess.Closed or WFLobbyAccess.Code);
}
