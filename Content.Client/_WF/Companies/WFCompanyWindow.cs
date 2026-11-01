using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.Companies;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.Companies;

/// <summary>Creates, joins and runs a player company for the player's current character.</summary>
public sealed partial class WFCompanyWindow : DefaultWindow
{
    [Dependency] private IEntityManager _entities = default!;

    private readonly WFCompanyClientSystem _system;
    private readonly RichTextLabel _message = new() { HorizontalExpand = true, Visible = false };
    private readonly Label _character = new();
    private readonly TabContainer _tabs = new() { VerticalExpand = true, HorizontalExpand = true };
    private WFCompanyState? _state;
    private bool _deferred;
    private bool _wantCreate;

    public WFCompanyWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Text("title");
        MinSize = new Vector2(560, 420);
        SetSize = new Vector2(860, 680);
        _system = _entities.System<WFCompanyClientSystem>();
        var layout = Column(8);
        layout.Margin = new Thickness(8);
        layout.AddChild(_character);
        layout.AddChild(_message);
        layout.AddChild(_tabs);
        Contents.AddChild(layout);
        _system.Received += Receive;
        _system.LogosChanged += OnLogos;
        OnClose += () =>
        {
            _system.Received -= Receive;
            _system.LogosChanged -= OnLogos;
        };
        if (_system.State is { } state)
            Receive(state);
        Send(new WFCompanyRequest(WFCompanyAction.Refresh));
    }

    /// <summary>Switches to the Create tab, now or once the state arrives.</summary>
    public void ShowCreate()
    {
        _wantCreate = true;
        Render();
    }

    private void Receive(WFCompanyState state)
    {
        if (state.Message != null)
        {
            _message.SetMessage(FormattedMessage.FromUnformatted(state.Message));
            _message.Visible = true;
        }
        _state = state;
        if (IsEditing())
        {
            _deferred = true;
            return;
        }
        Render();
    }

    private void OnLogos()
    {
        if (IsEditing())
            _deferred = true;
        else
            Render();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_deferred && !IsEditing())
            Render();
    }

    /// <summary>A snapshot never rebuilds the page under a field being typed in.</summary>
    private bool IsEditing()
    {
        for (var focused = UserInterfaceManager.KeyboardFocused; focused != null; focused = focused.Parent)
        {
            if (focused == this)
                return true;
        }
        return false;
    }

    private void Render()
    {
        _deferred = false;
        if (_state is not { } state)
            return;
        var tab = _tabs.CurrentTab;
        var previousCount = _tabs.ChildCount;
        _tabs.RemoveAllChildren();
        if (!state.Loaded)
        {
            _character.Text = Text("loading");
            return;
        }
        _character.Text = state.CharacterName == null ? Text("no-character") : Text("acting-as", ("name", state.CharacterName));
        AddTab("tab-companies", BuildCompanies(state));
        if (state.Own is { } own)
        {
            AddTab("tab-overview", BuildOverview(state, own));
            AddTab("tab-members", BuildMembers(own));
            AddTab("tab-ranks", BuildRanks(own));
            AddTab("tab-hangar", BuildHangar(own));
            AddTab("tab-ledger", BuildLedger(own));
            AddTab("tab-settings", BuildSettings(own));
        }
        else if (state.CharacterName != null)
            AddTab("tab-create", BuildCreate(state));
        if (_tabs.ChildCount == previousCount && tab < _tabs.ChildCount)
            _tabs.CurrentTab = tab;
        if (_wantCreate)
        {
            _wantCreate = false;
            if (state.Own == null && state.CharacterName != null)
                _tabs.CurrentTab = 1;
        }
    }

    private void AddTab(string key, Control content)
    {
        var scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
        content.Margin = new Thickness(6);
        scroll.AddChild(content);
        _tabs.AddChild(scroll);
        TabContainer.SetTabTitle(scroll, Text(key));
    }

    private void Send(WFCompanyRequest request)
    {
        _message.Visible = false;
        _system.Send(request);
    }

    private void Send(WFCompanyAction action, int company, int? target = null) =>
        Send(new WFCompanyRequest(action) { CompanyId = company, Target = target });

    private static string Text(string key, params (string, object)[] args) => Loc.GetString($"wf-company-{key}", args);

    private static BoxContainer Column(int separation) =>
        new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = separation, HorizontalExpand = true };

    private static BoxContainer Row(params Control[] controls)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6, HorizontalExpand = true };
        foreach (var control in controls)
            row.AddChild(control);
        return row;
    }

    private static Label Heading(string key, params (string, object)[] args) =>
        new() { Text = Text(key, args), StyleClasses = { "LabelHeading" } };

    private static RichTextLabel Paragraph(string text)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromUnformatted(text));
        return label;
    }

    private static Button Button(string key, Action pressed, bool disabled = false)
    {
        var button = new Button { Text = Text(key), Disabled = disabled };
        button.OnPressed += _ => pressed();
        return button;
    }

    /// <summary>A two-press button for actions that are hard to undo.</summary>
    private static ConfirmButton Confirm(string key, Action pressed)
    {
        var button = new ConfirmButton { Text = Text(key), ConfirmationText = Text("confirm") };
        button.AddStyleClass("ButtonColorRed");
        button.OnPressed += _ => pressed();
        return button;
    }

    private static PanelContainer Card(Control content)
    {
        content.Margin = new Thickness(10);
        return new PanelContainer { HorizontalExpand = true, StyleClasses = { "WFRoundSetupCard" }, Children = { content } };
    }

    /// <summary>Accepts whole numbers only.</summary>
    private static LineEdit Number(string placeholder, int? value = null) => new()
    {
        PlaceHolder = placeholder, MinWidth = 110, Text = value?.ToString() ?? string.Empty,
        IsValid = text => text.Length <= 9 && text.All(char.IsDigit),
    };

    private static int ParseNumber(LineEdit edit) => int.TryParse(edit.Text, out var value) ? value : 0;

    private static string Credits(long amount) => Loc.GetString("wf-company-credits", ("amount", amount));
}
