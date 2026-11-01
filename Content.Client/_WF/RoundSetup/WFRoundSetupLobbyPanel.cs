using System.Linq;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.RoundSetup;

/// <summary>Shows compact live crew rows within the lobby menu’s existing scroll area.</summary>
public sealed partial class WFRoundSetupLobbyPanel : PanelContainer
{
    [Dependency] private IEntityManager _entities = default!;

    private WFRoundSetupClientSystem? _system;
    private readonly BoxContainer _cards;
    private readonly RichTextLabel _empty;
    private WFRoundSetupLobby[] _shown = Array.Empty<WFRoundSetupLobby>();
    private int[] _invitations = Array.Empty<int>();
    private bool _listening;

    /// <summary>Builds compact crew rows below the lobby menu.</summary>
    public WFRoundSetupLobbyPanel()
    {
        IoCManager.InjectDependencies(this);
        HorizontalExpand = true;
        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        AddChild(content);
        content.AddChild(new Companies.WFCompanyQuickJoin());
        content.AddChild(new Label { Text = Text("title"), StyleClasses = { "LabelSubText" } });
        _empty = Wrapped(Text("loading"));
        _empty.ToolTip = Text("empty-help");
        content.AddChild(_empty);
        _cards = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2,
            HorizontalExpand = true, Visible = false,
        };
        content.AddChild(_cards);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        SetListening(VisibleInTree);
    }

    protected override void ExitedTree()
    {
        SetListening(false);
        base.ExitedTree();
    }

    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);
        SetListening(VisibleInTree);
        if (newVisible)
            Relayout();
    }

    /// <summary>Re-measures the whole chain so a menu sized before the rows arrived doesn't stack them.</summary>
    private void Relayout()
    {
        for (Control? control = _cards; control != null; control = control.Parent)
            control.InvalidateMeasure();
    }

    /// <summary>Follows snapshots only while this screen is visible, including after character setup closes.</summary>
    private void SetListening(bool listening)
    {
        if (_listening == listening)
            return;
        if (!listening)
        {
            if (_system != null)
                _system.Received -= Receive;
            _system = null;
            _listening = false;
            return;
        }
        _system = _entities.System<WFRoundSetupClientSystem>();
        _system.Received += Receive;
        _listening = true;
        if (_system.State is { } state)
            Receive(state);
        Refresh();
    }

    /// <summary>Registers this viewer for the server's live lobby snapshots.</summary>
    private void Refresh() => _system?.Send(new WFRoundSetupRequest(WFRoundSetupAction.Refresh));

    /// <summary>Updates public cards without rebuilding unchanged snapshots or disturbing scrolling.</summary>
    private void Receive(WFRoundSetupState state)
    {
        var listings = state.Lobbies.OrderByDescending(lobby => lobby.Id == state.OwnLobby)
            .ThenByDescending(lobby => state.OwnCompany != null && lobby.CompanyId == state.OwnCompany)
            .ThenByDescending(lobby => lobby.Recruiting).ThenBy(lobby => lobby.Started).ThenBy(lobby => lobby.Id).ToArray();
        var invitations = state.Invitations.Select(lobby => lobby.Id).ToArray();
        _empty.SetMessage(FormattedMessage.FromUnformatted(Text("empty")));
        _empty.Visible = listings.Length == 0;
        _cards.Visible = listings.Length != 0;
        if (_shown.Length == listings.Length && _shown.Zip(listings).All(pair => WFRoundSetupCrewRow.SameListing(pair.First, pair.Second)) &&
            _invitations.SequenceEqual(invitations))
            return;
        _shown = listings;
        _invitations = invitations;
        _cards.RemoveAllChildren();
        foreach (var lobby in listings)
            _cards.AddChild(new WFRoundSetupCrewRow(lobby, invitations.Contains(lobby.Id),
                () => UserInterfaceManager.GetUIController<WFRoundSetupUIController>().OpenLobby(lobby.Id)));
        Relayout();
    }

    private static RichTextLabel Wrapped(string text)
    {
        var label = new RichTextLabel { HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore };
        label.SetMessage(FormattedMessage.FromUnformatted(text));
        return label;
    }

    private static string Text(string key, params (string, object)[] args) => Loc.GetString($"wf-round-setup-panel-{key}", args);
}
