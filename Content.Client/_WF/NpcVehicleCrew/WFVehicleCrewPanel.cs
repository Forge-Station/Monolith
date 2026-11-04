using System.Linq;
using System.Numerics;
using Content.Shared._WF.NpcVehicleCrew;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.NpcVehicleCrew;

/// <summary>
/// The admin's NPC vehicle crew panel: pick a crew, its orders and how many, spawn them beside you; then order, rehome,
/// visit or delete the ones already out.
/// </summary>
[UsedImplicitly]
public sealed class WFVehicleCrewPanel : DefaultWindow
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    /// <summary>The orders a crew can be given; Retreat and Bail are reached, not ordered.</summary>
    private static readonly WFVehicleBehaviour[] Orders =
    {
        WFVehicleBehaviour.Hold, WFVehicleBehaviour.Patrol, WFVehicleBehaviour.Hunt, WFVehicleBehaviour.Escort,
        WFVehicleBehaviour.Flee,
    };

    private static readonly int[] Distances = { 4, 8, 12, 16, 24, 32 };

    private readonly WFVehicleCrewPanelSystem _system;
    private readonly LineEdit _search = new() { HorizontalExpand = true };
    private readonly BoxContainer _roster = Column(2);
    private readonly Label _crewName = new() { StyleClasses = { "LabelHeading" } };
    private readonly RichTextLabel _details = new();
    private readonly OptionButton _orders = new();
    private readonly OptionButton _count = new();
    private readonly OptionButton _distance = new();
    private readonly Button _spawn = new();
    private readonly BoxContainer _live = Column(6);
    private readonly Label _status = new();
    private readonly Label _none = new();
    private readonly Dictionary<NetEntity, (Control Row, Label Label, bool Bailed)> _rows = new();
    private readonly List<(Button Button, WFVehicleCrewPrototype Crew)> _rosterButtons = new();
    private WFVehicleCrewPrototype? _selected;

    public WFVehicleCrewPanel()
    {
        IoCManager.InjectDependencies(this);
        _system = _entities.System<WFVehicleCrewPanelSystem>();
        Title = Loc.GetString("wf-vehiclecrew-panel-title");
        MinSize = new Vector2(980, 520);
        SetSize = new Vector2(1100, 600);

        var body = Column(8);
        Contents.AddChild(body);
        var split = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 14, VerticalExpand = true };
        body.AddChild(split);

        // The roster.
        var left = Column(6);
        left.SetWidth = 220;
        left.AddChild(Heading("wf-vehiclecrew-panel-roster"));
        _search.PlaceHolder = Loc.GetString("wf-vehiclecrew-panel-search");
        _search.OnTextChanged += _ => FilterRoster();
        left.AddChild(_search);
        left.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _roster } });
        split.AddChild(left);

        // What to spawn.
        var middle = Column(8);
        middle.SetWidth = 300;
        middle.AddChild(Heading("wf-vehiclecrew-panel-setup"));
        middle.AddChild(_crewName);
        middle.AddChild(_details);
        middle.AddChild(Field("wf-vehiclecrew-panel-orders", _orders));
        middle.AddChild(Field("wf-vehiclecrew-panel-count", _count));
        middle.AddChild(Field("wf-vehiclecrew-panel-distance", _distance));
        _spawn.Text = Loc.GetString("wf-vehiclecrew-panel-spawn");
        _spawn.OnPressed += _ => Spawn();
        middle.AddChild(_spawn);
        split.AddChild(middle);

        // What's out there.
        var right = Column(6);
        right.HorizontalExpand = true;
        var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        var liveHeading = Heading("wf-vehiclecrew-panel-live");
        liveHeading.HorizontalExpand = true;
        header.AddChild(liveHeading);
        var clear = new Button { Text = Loc.GetString("wf-vehiclecrew-panel-clear-all"), StyleClasses = { "Caution" } };
        clear.OnPressed += _ => _system.Send(new WFVehicleCrewPanelRequest { Action = WFVehicleCrewPanelAction.ClearAll });
        header.AddChild(clear);
        right.AddChild(header);
        right.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _live } });
        _none.Text = Loc.GetString("wf-vehiclecrew-panel-none");
        _live.AddChild(_none);
        split.AddChild(right);

        body.AddChild(_status);

        for (var i = 1; i <= 8; i++)
            _count.AddItem(i.ToString(), i);
        foreach (var distance in Distances)
            _distance.AddItem(distance.ToString(), distance);
        _distance.SelectId(8);
        _count.OnItemSelected += args => _count.SelectId(args.Id);
        _distance.OnItemSelected += args => _distance.SelectId(args.Id);
        _orders.OnItemSelected += args => _orders.SelectId(args.Id);

        BuildRoster();
        Select(null);
        RenderLive(new List<WFVehicleCrewPanelEntry>());

        _system.Received += Receive;
        OnClose += () => _system.Received -= Receive;
        _system.Send(new WFVehicleCrewPanelRequest { Action = WFVehicleCrewPanelAction.List });
    }

    private void BuildRoster()
    {
        foreach (var crew in _prototypes.EnumeratePrototypes<WFVehicleCrewPrototype>().OrderBy(CrewName))
        {
            var button = new Button { Text = CrewName(crew), ToggleMode = true, HorizontalExpand = true };
            button.Label.ClipText = true;
            button.OnPressed += _ => Select(crew);
            _roster.AddChild(button);
            _rosterButtons.Add((button, crew));
        }
    }

    private void FilterRoster()
    {
        var search = _search.Text.Trim();
        foreach (var (button, crew) in _rosterButtons)
            button.Visible = search.Length == 0 || CrewName(crew).Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void Select(WFVehicleCrewPrototype? crew)
    {
        _selected = crew;
        foreach (var (button, rosterCrew) in _rosterButtons)
            button.Pressed = rosterCrew == crew;

        _spawn.Disabled = crew == null;
        _orders.Clear();
        if (crew == null)
        {
            _crewName.Text = string.Empty;
            _details.SetMessage(Loc.GetString("wf-vehiclecrew-panel-pick"));
            return;
        }

        _crewName.Text = CrewName(crew);
        var vehicle = _prototypes.TryIndex(crew.Vehicle, out var vehicleProto) ? vehicleProto.Name : crew.Vehicle.Id;
        var seats = 1 + (crew.Gunner != null ? 1 : 0) + crew.Troopers.Count;
        _details.SetMessage(crew.Gunner == null
            ? Loc.GetString("wf-vehiclecrew-panel-unarmed", ("vehicle", vehicle), ("faction", crew.Faction.Id), ("seats", seats))
            : Loc.GetString("wf-vehiclecrew-panel-details", ("vehicle", vehicle), ("faction", crew.Faction.Id), ("seats", seats),
                ("skill", crew.Skill.ToString()), ("range", crew.EngageRange)));

        // Id -1 keeps the crew's own orders.
        _orders.AddItem(Loc.GetString("wf-vehiclecrew-panel-orders-default", ("behaviour", BehaviourName(crew.Behaviour))), -1);
        foreach (var order in Orders)
            _orders.AddItem(BehaviourName(order), (int) order);
        _orders.SelectId(-1);
    }

    private void Spawn()
    {
        if (_selected == null)
            return;
        _system.Send(new WFVehicleCrewPanelRequest
        {
            Action = WFVehicleCrewPanelAction.Spawn,
            Crew = _selected.ID,
            Behaviour = _orders.SelectedId < 0 ? null : (WFVehicleBehaviour) _orders.SelectedId,
            Count = _count.SelectedId,
            Distance = _distance.SelectedId,
        });
    }

    private void Receive(WFVehicleCrewPanelState state)
    {
        RenderLive(state.Crews);
        if (state.Message is { Length: > 0 } message)
        {
            _status.Text = message;
            _status.FontColorOverride = state.Error ? Color.FromHex("#ff6060") : null;
        }
    }

    /// <summary>
    /// Updates each crew's row in place and adds or drops rows only as crews come and go, so a refresh never closes an
    /// orders list someone has open.
    /// </summary>
    private void RenderLive(List<WFVehicleCrewPanelEntry> crews)
    {
        var current = crews.ToDictionary(crew => crew.Vehicle);
        foreach (var vehicle in _rows.Keys.Where(vehicle => !current.TryGetValue(vehicle, out var crew) || crew.Bailed != _rows[vehicle].Bailed).ToList())
        {
            _live.RemoveChild(_rows[vehicle].Row);
            _rows.Remove(vehicle);
        }

        _none.Visible = crews.Count == 0;
        foreach (var crew in crews.OrderBy(entry => entry.Name))
        {
            if (!_rows.TryGetValue(crew.Vehicle, out var row))
            {
                row = LiveRow(crew);
                _rows[crew.Vehicle] = row;
                _live.AddChild(row.Row);
            }
            row.Label.Text = RowText(crew);
        }
    }

    private static string RowText(WFVehicleCrewPanelEntry crew)
    {
        return crew.Bailed
            ? Loc.GetString("wf-vehiclecrew-panel-row-bailed", ("name", crew.Name), ("crew", crew.Crew), ("alive", crew.Alive),
                ("total", crew.Total), ("location", crew.Location))
            : Loc.GetString("wf-vehiclecrew-panel-row", ("name", crew.Name), ("crew", crew.Crew),
                ("behaviour", BehaviourName(crew.Behaviour)), ("alive", crew.Alive), ("total", crew.Total),
                ("integrity", (int) MathF.Round(crew.Integrity * 100f)), ("location", crew.Location));
    }

    private (Control Row, Label Label, bool Bailed) LiveRow(WFVehicleCrewPanelEntry crew)
    {
        var row = Column(2);
        var label = new Label { ClipText = true };
        row.AddChild(label);

        var actions = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        if (!crew.Bailed)
        {
            var order = new OptionButton { ToolTip = Loc.GetString("wf-vehiclecrew-panel-escort-tooltip") };
            order.AddItem(Loc.GetString("wf-vehiclecrew-panel-order"), -1);
            foreach (var behaviour in Orders)
                order.AddItem(BehaviourName(behaviour), (int) behaviour);
            order.SelectId(-1);
            order.OnItemSelected += args =>
            {
                if (args.Id < 0)
                    return;
                order.SelectId(args.Id);
                Act(WFVehicleCrewPanelAction.Order, crew.Vehicle, (WFVehicleBehaviour) args.Id);
            };
            actions.AddChild(order);

            var home = new Button { Text = Loc.GetString("wf-vehiclecrew-panel-home"), ToolTip = Loc.GetString("wf-vehiclecrew-panel-home-tooltip") };
            home.OnPressed += _ => Act(WFVehicleCrewPanelAction.HomeHere, crew.Vehicle);
            actions.AddChild(home);
        }

        var goTo = new Button { Text = Loc.GetString("wf-vehiclecrew-panel-goto") };
        goTo.OnPressed += _ => Act(WFVehicleCrewPanelAction.GoTo, crew.Vehicle);
        actions.AddChild(goTo);

        var delete = new Button { Text = Loc.GetString("wf-vehiclecrew-panel-delete"), StyleClasses = { "Caution" } };
        delete.OnPressed += _ => Act(WFVehicleCrewPanelAction.Delete, crew.Vehicle);
        actions.AddChild(delete);
        row.AddChild(actions);
        return (row, label, crew.Bailed);
    }

    private void Act(WFVehicleCrewPanelAction action, NetEntity vehicle, WFVehicleBehaviour? behaviour = null)
    {
        _system.Send(new WFVehicleCrewPanelRequest { Action = action, Vehicle = vehicle, Behaviour = behaviour });
    }

    private static string CrewName(WFVehicleCrewPrototype crew)
    {
        return crew.Name is { } name ? Loc.GetString(name) : crew.ID;
    }

    private static string BehaviourName(WFVehicleBehaviour behaviour)
    {
        return Loc.GetString($"wf-vehiclecrew-behaviour-{behaviour.ToString().ToLowerInvariant()}");
    }

    private static BoxContainer Column(int separation)
    {
        return new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = separation };
    }

    private static Label Heading(string key)
    {
        return new Label { Text = Loc.GetString(key), StyleClasses = { "LabelHeading" } };
    }

    private static BoxContainer Field(string key, Control control)
    {
        var field = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        field.AddChild(new Label { Text = Loc.GetString(key), MinWidth = 110 });
        control.HorizontalExpand = true;
        field.AddChild(control);
        return field;
    }
}
