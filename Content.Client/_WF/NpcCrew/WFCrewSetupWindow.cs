using Content.Client._WF.Branding;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.NpcCrew;

/// <summary>Guided crew creation and a separate dashboard for live ship operations.</summary>
[UsedImplicitly]
public sealed partial class WFCrewSetupWindow : DefaultWindow
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly WFCrewSetupClientSystem _system;
    private readonly BoxContainer _crewList = Column();
    private readonly LineEdit _crewSearch = new();
    private readonly Label _heading = new() { StyleClasses = { "LabelHeading" } };
    private readonly RichTextLabel _subtitle = new();
    private readonly RichTextLabel _status = new();
    private readonly BoxContainer _createView = Column();
    private readonly BoxContainer _manageView = Column();
    private readonly BoxContainer _emptyView = Column();
    private readonly Dictionary<int, PendingRequest> _requests = new();
    private readonly Dictionary<CrewKey, CrewEdits> _edits = new();
    private List<WFCrewSetupGrid> _grids = new();
    private List<WFCrewSetupCrew> _liveCrews = new();
    private CrewKey? _selectedCrew;
    private CrewKey? _pendingCrew;
    private bool _creating = true;
    private int _contextVersion;
    private TimeSpan _nextGridFetch;
    private SettingsForm _createSettings = default!;
    private SettingsForm _crewSettings = default!;

    public WFCrewSetupWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("wf-crew-setup-title");
        WFWindowBranding.Apply(this);
        MinSize = new Vector2(1100, 600);
        SetSize = new Vector2(1280, 720);
        _system = _entities.System<WFCrewSetupClientSystem>();

        var body = Column(10);
        Contents.AddChild(body);
        var split = new BoxContainer { SeparationOverride = 16, VerticalExpand = true };
        body.AddChild(split);
        var sidebar = Column(10);
        sidebar.SetWidth = 220;
        split.AddChild(sidebar);
        sidebar.AddChild(Button("new-crew", ShowCreation));
        sidebar.AddChild(Heading("active-crews"));
        _crewSearch.PlaceHolder = Text("search-crews");
        _crewSearch.OnTextChanged += _ => RenderCrews();
        sidebar.AddChild(_crewSearch);
        sidebar.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, ReserveScrollbarSpace = true, Children = { _crewList } });
        sidebar.AddChild(Help("auto-refresh"));
        sidebar.AddChild(Button("refresh", () => _system.Send(new WFCrewSetupRequest { Action = WFCrewSetupAction.List })));

        var main = Column(10);
        main.HorizontalExpand = true;
        split.AddChild(main);
        main.AddChild(_heading);
        main.AddChild(_subtitle);
        _createView.VerticalExpand = _manageView.VerticalExpand = true;
        main.AddChild(_createView);
        main.AddChild(_manageView);
        main.AddChild(_emptyView);
        _emptyView.AddChild(Help("crew-gone"));
        _emptyView.AddChild(Button("new-crew", ShowCreation));
        BuildCreation();
        BuildManagement();
        body.AddChild(Card(_status));
        ShowCreation();
        // Subscribe last: a throw above must not leave a half-built window polling the server.
        _system.Received += Receive;
        OnClose += () => _system.Received -= Receive;
        _system.Send(new WFCrewSetupRequest { Action = WFCrewSetupAction.List });
    }

    /// <summary>Selects an existing crew opened through its admin verb.</summary>
    public void SelectCrew(NetEntity grid, string group)
    {
        _pendingCrew = new CrewKey(grid, group);
        TrySelectPendingCrew();
    }

    private void ShowCreation()
    {
        SaveSettings();
        _creating = true;
        _contextVersion++;
        _createView.Visible = true;
        _manageView.Visible = _emptyView.Visible = false;
        _heading.Text = Text("new-crew");
        Plain(_subtitle, Text("create-intro"));
        Plain(_status, Text("create-hint"));
        ShowStep(_step);
        RenderCrews();
    }

    private void SelectLiveCrew(CrewKey key)
    {
        var crew = _liveCrews.FirstOrDefault(item => Key(item) == key);
        if (crew == null)
            return;
        SaveSettings();
        _selectedCrew = key;
        _creating = false;
        _contextVersion++;
        if (!_edits.TryGetValue(key, out var edits))
            _edits[key] = edits = new CrewEdits();
        var draft = edits.Settings;
        if (draft != null && edits.Baseline != null && !SameSettings(edits.Baseline, crew.Settings))
            draft = null;
        edits.Settings = null;
        LoadSettings(edits, crew);
        if (draft != null)
            _crewSettings.Load(draft);
        _createView.Visible = _emptyView.Visible = false;
        _manageView.Visible = true;
        _removeConfirm.Visible = _manualConfirm.Visible = false;
        ResetObjectiveEditor();
        UpdateCrewHeader(crew);
        RefreshTargets();
        RenderQueue();
        RenderCrews();
        Plain(_status, Text("manage-hint"));
    }

    /// <summary>Keeps a draft of the settings form only when it differs from the live settings it was loaded from.</summary>
    private void SaveSettings()
    {
        if (!_creating && _selectedCrew is { } key && _edits.TryGetValue(key, out var edits))
            edits.Settings = SettingsDirty(edits) ? _crewSettings.Read(key.Group) : null;
    }

    /// <summary>Loads live settings into the form and remembers both them and what the form then reads.</summary>
    private void LoadSettings(CrewEdits edits, WFCrewSetupCrew crew)
    {
        _crewSettings.Load(crew.Settings);
        edits.Baseline = crew.Settings;
        edits.Form = _crewSettings.Read(crew.Group);
    }

    private bool SettingsDirty(CrewEdits edits)
    {
        return _selectedCrew is { } key && edits.Form != null && !SameSettings(_crewSettings.Read(key.Group), edits.Form);
    }

    /// <summary>Discards drafts of vanished crews or changed live settings and refreshes an untouched settings form.</summary>
    private void SyncEdits()
    {
        var live = new Dictionary<CrewKey, WFCrewSetupCrew>();
        foreach (var crew in _liveCrews)
            live[Key(crew)] = crew;
        foreach (var (key, edits) in _edits.ToArray())
        {
            if (!live.TryGetValue(key, out var crew))
            {
                _edits.Remove(key);
                continue;
            }
            if (!_creating && _selectedCrew == key)
            {
                if (edits.Baseline == null)
                    edits.Baseline = crew.Settings;
                else if (!SameSettings(edits.Baseline, crew.Settings) && !SettingsDirty(edits))
                    LoadSettings(edits, crew);
            }
            else if (edits.Settings != null && edits.Baseline != null && !SameSettings(edits.Baseline, crew.Settings))
                edits.Settings = null;
        }
    }

    private void UpdateCrewHeader(WFCrewSetupCrew crew)
    {
        _heading.Text = ShipName(crew.Grid);
        Plain(_subtitle, Text("crew-summary", ("group", crew.Group), ("alive", crew.Alive), ("total", crew.Members), ("status", crew.Status)));
    }

    private void RenderCrews()
    {
        var present = _liveCrews.Select(Key).ToHashSet();
        foreach (var key in _crewCards.Keys.Where(key => !present.Contains(key)).ToArray())
        {
            _crewCards[key].Button.Orphan();
            _crewCards.Remove(key);
        }
        var visible = 0;
        foreach (var crew in _liveCrews)
        {
            var ship = ShipName(crew.Grid);
            var key = Key(crew);
            if (!_crewCards.TryGetValue(key, out var card))
            {
                var button = new ContainerButton { ToggleMode = true };
                button.AddStyleClass(ContainerButton.StyleClassButton);
                var content = Column(3);
                content.Margin = new Thickness(8);
                var name = new Label { ClipText = true };
                var detail = new RichTextLabel();
                content.AddChild(name);
                content.AddChild(detail);
                button.AddChild(content);
                button.OnPressed += _ => SelectLiveCrew(key);
                _crewList.AddChild(button);
                _crewCards[key] = card = new CrewCard(button, name, detail);
            }
            card.Button.Visible = ($"{ship} {crew.Group} {crew.Settings.Callsign}").Contains(_crewSearch.Text, StringComparison.OrdinalIgnoreCase);
            card.Button.Pressed = !_creating && _selectedCrew == key;
            card.Name.Text = card.Name.ToolTip = ship;
            Plain(card.Detail, Text("crew-card", ("group", crew.Group), ("alive", crew.Alive), ("total", crew.Members)));
            if (card.Button.Visible)
                visible++;
        }
        if (_crewListEmpty.Parent == null)
            _crewList.AddChild(_crewListEmpty);
        _crewListEmpty.Visible = visible == 0;
        Plain(_crewListEmpty, Text(_liveCrews.Count == 0 ? "no-crews" : "no-matches"));
    }

    private void TrySelectPendingCrew()
    {
        if (_pendingCrew is not { } key || !_liveCrews.Any(crew => Key(crew) == key))
            return;
        _pendingCrew = null;
        SelectLiveCrew(key);
    }

    /// <summary>Routes replies only to their originating editor context; polling never changes identity.</summary>
    private void Receive(WFCrewSetupResponse response)
    {
        PendingRequest? pending = null;
        if (_requests.Remove(response.RequestId, out var found))
            pending = found;
        if (pending is { Action: WFCrewSetupAction.Objectives, Grid: { } savedGrid } saved && response.Message.Length == 0
            && _edits.TryGetValue(new CrewKey(savedGrid, saved.Group), out var savedEdits) && savedEdits.Version == saved.QueueVersion)
        {
            savedEdits.Queue = null;
            if (saved.Context == _contextVersion)
                _editing = null;
        }
        if (pending is { Action: WFCrewSetupAction.Rules or WFCrewSetupAction.Orders, Grid: { } ruleGrid, Settings: { } sent }
            && response.Message.Length == 0 && _edits.TryGetValue(new CrewKey(ruleGrid, pending.Value.Group), out var ruleEdits))
        {
            ruleEdits.Settings = null;
            ruleEdits.Baseline = null;
            ruleEdits.Form = sent;
        }
        if (response.Action == WFCrewSetupAction.List)
        {
            _grids = response.Grids;
            RefreshCreationGrid();
            RefreshTargets();
        }
        _liveCrews = response.Crews;
        SyncEdits();
        RenderCrews();
        RequestMissingGrids();
        if (!_creating && _selectedCrew is { } selected)
        {
            var current = _liveCrews.FirstOrDefault(crew => Key(crew) == selected);
            if (current == null)
            {
                _contextVersion++;
                _manageView.Visible = false;
                _emptyView.Visible = true;
                _selectedCrew = null;
                Plain(_subtitle, string.Empty);
            }
            else
            {
                UpdateCrewHeader(current);
                RenderQueue();
            }
        }
        TrySelectPendingCrew();
        if (pending is not { } request || request.Context != _contextVersion)
            return;
        Plain(_status, response.Message.Length > 0 ? response.Message : Text("saved"));
        if (response.Action == WFCrewSetupAction.Plan && _creating && response.Grid == _creationGrid)
        {
            _rows.Clear();
            _roster.RemoveAllChildren();
            foreach (var post in response.Posts)
                AddRow(post);
            UpdateRosterSummary();
        }
        else if (response.Action == WFCrewSetupAction.SpawnVessel && response.Grid is { } grid && response.Message.Length == 0)
        {
            _creationGrid = grid;
            _contextVersion++;
            _rows.Clear();
            _roster.RemoveAllChildren();
            UpdateRosterSummary();
            _system.Send(new WFCrewSetupRequest { Action = WFCrewSetupAction.List });
        }
        else if (response.Action == WFCrewSetupAction.Spawn && response.Grid is { } ship
                 && _liveCrews.Any(crew => crew.Grid == ship && crew.Group == request.Group))
        {
            SelectLiveCrew(new CrewKey(ship, request.Group));
            Plain(_status, response.Message);
        }
        else if (response.Action == WFCrewSetupAction.Objectives && response.Message.Length == 0
                 && CurrentEdits is { } edits && edits.Version == request.QueueVersion)
        {
            edits.Queue = null;
            _editing = null;
            RenderQueue();
        }
    }

    /// <summary>Crew polls carry no grid names; asks for the grid list when a crew's ship is unknown.</summary>
    private void RequestMissingGrids()
    {
        var now = _timing.RealTime;
        if (now < _nextGridFetch || _liveCrews.All(crew => _grids.Any(grid => grid.Id == crew.Grid)))
            return;
        _nextGridFetch = now + TimeSpan.FromSeconds(2);
        _system.Send(new WFCrewSetupRequest { Action = WFCrewSetupAction.List });
    }

    private void Request(WFCrewSetupRequest request)
    {
        var now = _timing.RealTime;
        foreach (var stale in _requests.Where(item => now - item.Value.Sent > RequestTimeout).Select(item => item.Key).ToArray())
            _requests.Remove(stale);
        if (request.Action != WFCrewSetupAction.Plan
            && _requests.Values.Any(pending => pending.Action == request.Action && pending.Grid == request.Grid && pending.Group == request.Mission.Group))
            return;
        var id = _system.Send(request);
        _requests[id] = new PendingRequest(_contextVersion, CurrentEdits?.Version ?? 0, request.Mission.Group, request.Action, request.Grid,
            now, request.Action is WFCrewSetupAction.Rules or WFCrewSetupAction.Orders ? request.Mission : null);
        Plain(_status, Text("sending"));
    }

    private string ShipName(NetEntity grid) => _grids.FirstOrDefault(item => item.Id == grid)?.Name ?? grid.ToString();
    private static CrewKey Key(WFCrewSetupCrew crew) => new(crew.Grid, crew.Group);
    private WFCrewSetupCrew? CurrentCrew => !_creating && _selectedCrew is { } key ? _liveCrews.FirstOrDefault(crew => Key(crew) == key) : null;
    private CrewEdits? CurrentEdits => !_creating && _selectedCrew is { } key ? _edits.GetValueOrDefault(key) : null;
    private static string Text(string key, params (string, object)[] args) => Loc.GetString($"wf-crew-setup-{key}", args);
    private static BoxContainer Column(int gap = 8) => new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = gap };
    private static Label Heading(string key) => new() { Text = Text(key), StyleClasses = { "LabelHeading" } };
    private static RichTextLabel Help(string key)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        Plain(label, Text(key));
        return label;
    }
    private static void Plain(RichTextLabel label, string text) => label.SetMessage(FormattedMessage.FromUnformatted(text));
    private static PanelContainer Card(Control content) => new()
    {
        PanelOverride = new StyleBoxFlat(Color.FromHex("#18232E")),
        Children = { new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(12), Children = { content } } },
    };
    private static Button Button(string key, Action pressed)
    {
        var button = new Button { Text = Text(key), MinHeight = 30, Name = key };
        button.OnPressed += _ => pressed();
        return button;
    }
    private static BoxContainer Line(string key, params Control[] controls)
    {
        var line = new BoxContainer { SeparationOverride = 8 };
        line.AddChild(new Label { Text = Text(key), MinWidth = 170 });
        foreach (var control in controls)
        {
            control.HorizontalExpand = true;
            line.AddChild(control);
        }
        return line;
    }
    private static BoxContainer Buttons(params Control[] controls)
    {
        var line = new BoxContainer { SeparationOverride = 6 };
        foreach (var control in controls)
            line.AddChild(control);
        return line;
    }
    private static void SelectOnClick(OptionButton button) => button.OnItemSelected += args => button.SelectId(args.Id);
    private static void Fill(OptionButton button, List<string> choices, string selected)
    {
        button.Filterable = true;
        for (var index = 0; index < choices.Count; index++)
            button.AddItem(choices[index].Length > 0 ? choices[index] : Text("default"), index);
        button.TrySelectId(Math.Max(0, choices.IndexOf(selected)));
        SelectOnClick(button);
    }
    private static float Number(LineEdit edit) => float.TryParse(edit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : float.NaN;
    private readonly record struct CrewKey(NetEntity Grid, string Group);
    private readonly record struct PendingRequest(int Context, int QueueVersion, string Group, WFCrewSetupAction Action, NetEntity? Grid,
        TimeSpan Sent, WFCrewMission? Settings);
    private sealed class CrewEdits
    {
        /// <summary>A draft of the settings form, kept only while it differs from <see cref="Form"/>.</summary>
        public WFCrewMission? Settings;
        /// <summary>The live settings the form was last loaded from, or null until the next reply after a save.</summary>
        public WFCrewMission? Baseline;
        /// <summary>What the form read right after loading <see cref="Baseline"/>.</summary>
        public WFCrewMission? Form;
        public List<WFCrewObjective>? Queue;
        public int Version;
    }
}
