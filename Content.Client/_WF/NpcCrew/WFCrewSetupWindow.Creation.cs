using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Roles;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.NpcCrew;

public sealed partial class WFCrewSetupWindow
{
    private readonly OptionButton _grid = new() { Filterable = true };
    private readonly OptionButton _vessel = new() { Filterable = true };
    private readonly LineEdit _count = new() { Text = "2" };
    private readonly LineEdit _group = new() { Text = "crew" };
    private readonly CheckBox _captain = new();
    private readonly BoxContainer _roster = Column();
    private readonly Label _rosterSummary = new();
    private readonly RichTextLabel _review = new();
    private readonly List<Row> _rows = new();
    private readonly List<BoxContainer> _steps = new();
    private readonly List<Button> _stepButtons = new();
    private readonly List<string> _roles = new();
    private readonly List<string> _loadouts = new();
    private readonly List<string> _vessels = new();
    private Button _next = default!;
    private Button _back = default!;
    private NetEntity? _creationGrid;
    private int _step;

    private void BuildCreation()
    {
        _roles.AddRange(_prototypes.EnumeratePrototypes<WFCrewRolePrototype>().OrderBy(role => role.Order).Select(role => role.ID));
        _loadouts.Add(string.Empty);
        _loadouts.AddRange(_prototypes.EnumeratePrototypes<StartingGearPrototype>().Select(gear => gear.ID).Order());
        _vessels.AddRange(_prototypes.EnumeratePrototypes<VesselPrototype>().Where(vessel => !vessel.Abstract).OrderBy(vessel => vessel.Name).Select(vessel => vessel.ID));
        for (var index = 0; index < _vessels.Count; index++)
        {
            var vessel = _prototypes.Index<VesselPrototype>(_vessels[index]);
            _vessel.AddItem($"{vessel.Name} ({vessel.ID})", index);
        }
        SelectOnClick(_vessel);
        SelectOnClick(_grid);
        _grid.OnItemSelected += args =>
        {
            _creationGrid = args.Id >= 0 && args.Id < _grids.Count ? _grids[args.Id].Id : null;
            _contextVersion++;
            _rows.Clear();
            _roster.RemoveAllChildren();
            UpdateRosterSummary();
        };

        var navigation = new BoxContainer { SeparationOverride = 4 };
        _createView.AddChild(navigation);
        foreach (var key in new[] { "step-ship", "step-roster", "step-rules", "step-review" })
        {
            var at = _steps.Count;
            var button = Button(key, () => { if (CanLeaveStep()) ShowStep(at); });
            button.ToggleMode = true;
            button.HorizontalExpand = true;
            _stepButtons.Add(button);
            navigation.AddChild(button);
            _steps.Add(Column(12));
        }
        var pages = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true };
        foreach (var page in _steps)
            pages.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, ReserveScrollbarSpace = true, Children = { page } });
        _createView.AddChild(pages);

        var ship = _steps[0];
        ship.AddChild(Heading("choose-ship"));
        ship.AddChild(Help("ship-help"));
        ship.AddChild(Line("ship", _grid));
        ship.AddChild(Line("group", _group));
        _group.IsValid = text => text.Length <= WFCrewLimits.MaxGroup;
        _group.OnTextChanged += _ => UpdateCreationButtons();
        ship.AddChild(Help("group-help"));
        var spawn = Column();
        spawn.AddChild(Heading("need-ship"));
        spawn.AddChild(Help("vessel-help"));
        spawn.AddChild(_vessel);
        spawn.AddChild(Button("spawn-vessel", () =>
        {
            if (_vessel.SelectedId < 0 || _vessel.SelectedId >= _vessels.Count)
                return;
            Request(new WFCrewSetupRequest { Action = WFCrewSetupAction.SpawnVessel, Vessel = _vessels[_vessel.SelectedId] });
        }));
        ship.AddChild(Card(spawn));

        var roster = _steps[1];
        roster.AddChild(Heading("assign-posts"));
        roster.AddChild(Help("planner-help"));
        _captain.Text = Text("captain");
        roster.AddChild(Line("deckhands", _count, _captain));
        roster.AddChild(Buttons(Button("plan", PlanCrew), Button("add", () => { _contextVersion++; AddRow(new WFCrewSetupPost()); })));
        roster.AddChild(_rosterSummary);
        roster.AddChild(_roster);
        roster.AddChild(Button("preview", () => SendCreation(WFCrewSetupAction.Preview)));

        _createSettings = new SettingsForm(_prototypes);
        _steps[2].AddChild(Heading("choose-rules"));
        _steps[2].AddChild(Help("rules-intro"));
        _steps[2].AddChild(_createSettings.Body);
        _steps[3].AddChild(Heading("ready-to-spawn"));
        _steps[3].AddChild(Card(_review));
        _steps[3].AddChild(Help("spawn-help"));
        _steps[3].AddChild(Button("preview", () => SendCreation(WFCrewSetupAction.Preview)));

        _back = Button("back", () => ShowStep(_step - 1));
        _next = Button("next", () =>
        {
            if (!CanLeaveStep())
                return;
            if (_step == 3)
                SendCreation(WFCrewSetupAction.Spawn);
            else
            {
                if (_step == 0 && _rows.Count == 0)
                    PlanCrew();
                ShowStep(_step + 1);
            }
        });
        _next.HorizontalExpand = true;
        _createView.AddChild(Buttons(_back, _next));
    }

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, 3);
        for (var index = 0; index < _steps.Count; index++)
        {
            _steps[index].Parent!.Visible = index == _step;
            _stepButtons[index].Pressed = index == _step;
        }
        if (_step == 3)
        {
            var settings = _createSettings.Read(_group.Text.Trim());
            var roles = string.Join(", ", _rows.GroupBy(row => _roles[row.Role.SelectedId])
                .Select(group => Text("role-count", ("count", group.Count()), ("role", Loc.GetString(_prototypes.Index<WFCrewRolePrototype>(group.Key).Title)))));
            Plain(_review, Text("review-summary", ("ship", _creationGrid is { } grid ? ShipName(grid) : Text("select-ship")),
                ("group", settings.Group), ("count", _rows.Count), ("roles", roles), ("faction", settings.Faction),
                ("boarding", Text($"response-{settings.BoardingResponse.ToString().ToLowerInvariant()}")),
                ("docking", Text($"response-{settings.DockingResponse.ToString().ToLowerInvariant()}"))));
        }
        UpdateCreationButtons();
    }

    private bool CanLeaveStep()
    {
        if (_creationGrid == null || string.IsNullOrWhiteSpace(_group.Text) || _group.Text.Trim().Length > WFCrewLimits.MaxGroup)
        {
            Plain(_status, Text("choose-ship-group"));
            return false;
        }
        return _step == 0 || _rows.Count > 0;
    }

    private void UpdateCreationButtons()
    {
        if (_next == null)
            return;
        _back.Disabled = _step == 0;
        _next.Text = Text(_step == 3 ? "spawn" : "next");
        var valid = _creationGrid != null && !string.IsNullOrWhiteSpace(_group.Text) && _group.Text.Trim().Length <= WFCrewLimits.MaxGroup;
        _next.Disabled = !valid || _step > 0 && _rows.Count == 0;
        for (var index = 1; index < _stepButtons.Count; index++)
            _stepButtons[index].Disabled = !valid || index > 1 && _rows.Count == 0;
    }

    private void RefreshCreationGrid()
    {
        RefreshGridChoices(_grid, "select-ship", _creationGrid, skipLarge: true);
        if (_creationGrid != null && _grid.SelectedId == -1)
        {
            _creationGrid = null;
            if (_creating)
                _contextVersion++;
            _rows.Clear();
            _roster.RemoveAllChildren();
            UpdateRosterSummary();
        }
        UpdateCreationButtons();
    }

    private void PlanCrew()
    {
        if (_creationGrid == null)
            return;
        if (!int.TryParse(_count.Text, out var count) || count is < 0 or > 32)
        {
            Plain(_status, Text("bad-count"));
            return;
        }
        _contextVersion++;
        Request(new WFCrewSetupRequest { Action = WFCrewSetupAction.Plan, Grid = _creationGrid, Deckhands = count, Captain = _captain.Pressed });
    }

    private void SendCreation(WFCrewSetupAction action, Row? row = null)
    {
        if (_creationGrid == null || !CanLeaveStep())
            return;
        var posts = row == null ? _rows.Select(ReadPost).ToList() : new List<WFCrewSetupPost> { ReadPost(row) };
        if (posts.Count == 0 || posts.Any(post => !InBounds(post.Position)))
        {
            Plain(_status, Text("bad-posts"));
            return;
        }
        if (action == WFCrewSetupAction.Spawn && _liveCrews.Any(crew => crew.Grid == _creationGrid && crew.Group == _group.Text.Trim()))
        {
            Plain(_status, Text("duplicate-group"));
            return;
        }
        var mission = _createSettings.Read(_group.Text.Trim());
        if (!ValidateNavigation(mission))
            return;
        Request(new WFCrewSetupRequest { Action = action, Grid = _creationGrid, Posts = posts, Mission = mission });
    }

    private void AddRow(WFCrewSetupPost post)
    {
        if (_rows.Count >= 64)
            return;
        var row = new Row();
        Fill(row.Role, _roles, post.Role);
        for (var index = 0; index < _roles.Count; index++)
            row.Role.SetItemText(index, Loc.GetString(_prototypes.Index<WFCrewRolePrototype>(_roles[index]).Title));
        Fill(row.Loadout, _loadouts, post.Loadout);
        row.X.Text = post.Position.X.ToString(CultureInfo.InvariantCulture);
        row.Y.Text = post.Position.Y.ToString(CultureInfo.InvariantCulture);
        row.Engagement.AddItem(Text("role-default"), 0);
        row.Engagement.AddItem(Text("onsight"), 1);
        row.Engagement.AddItem(Text("attacked"), 2);
        row.Engagement.SelectId(post.Engagement is { } engagement ? (int) engagement + 1 : 0);
        SelectOnClick(row.Engagement);
        var details = Column();
        details.Visible = false;
        details.AddChild(Line("equipment", row.Loadout));
        details.AddChild(Line("engagement", row.Engagement));
        details.AddChild(Line("post-x", row.X));
        details.AddChild(Line("post-y", row.Y));
        details.AddChild(Help("post-help"));
        details.AddChild(Button("teleport", () => SendCreation(WFCrewSetupAction.Teleport, row)));
        var header = Buttons(row.Role, Button("post-options", () => details.Visible = !details.Visible), Button("remove", () =>
        {
            _contextVersion++;
            _rows.Remove(row);
            _roster.RemoveChild(row.Box);
            UpdateRosterSummary();
        }));
        row.Role.HorizontalExpand = true;
        var content = Column();
        content.AddChild(header);
        content.AddChild(details);
        row.Box.AddChild(Card(content));
        foreach (var choice in new[] { row.Role, row.Loadout, row.Engagement })
            choice.OnItemSelected += _ => _contextVersion++;
        row.X.OnTextChanged += _ => _contextVersion++;
        row.Y.OnTextChanged += _ => _contextVersion++;
        _rows.Add(row);
        _roster.AddChild(row.Box);
        UpdateRosterSummary();
    }

    private void UpdateRosterSummary()
    {
        _rosterSummary.Text = Text("roster-count", ("count", _rows.Count));
        UpdateCreationButtons();
    }

    private WFCrewSetupPost ReadPost(Row row) => new()
    {
        Role = _roles[row.Role.SelectedId], Loadout = _loadouts[row.Loadout.SelectedId],
        Engagement = row.Engagement.SelectedId == 0 ? null : (WFCrewEngagement) (row.Engagement.SelectedId - 1),
        Position = new Vector2(Number(row.X), Number(row.Y)),
    };

    private sealed class Row
    {
        public readonly BoxContainer Box = Column();
        public readonly OptionButton Role = new();
        public readonly OptionButton Loadout = new();
        public readonly OptionButton Engagement = new();
        public readonly LineEdit X = new();
        public readonly LineEdit Y = new();
    }
}
