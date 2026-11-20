using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client._WF.NpcCrew;
using Content.Shared._WF.Encounters;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WF.Encounters;

/// <summary>
/// The admin encounter window. The first tab lists the running encounters beside the one picked, with its ships and an
/// order panel to have one of them do something; the others start an encounter and set the scheduler.
/// </summary>
[UsedImplicitly]
public sealed partial class WFEncounterWindow : DefaultWindow
{
    [Dependency] private IEntityManager _entities = default!;

    private readonly WFEncounterClientSystem _system;
    private readonly Label _status = new() { ClipText = true };

    // Encounters tab.
    private readonly LineEdit _search = new() { HorizontalExpand = true };
    private readonly CheckBox _showResolved = new();
    private readonly BoxContainer _list = Column(2);
    private readonly BoxContainer _detail = Column(6);
    private readonly Label _emptyDetail = new();
    private NetEntity? _selected;
    private string _listShown = string.Empty;
    private string _detailShown = string.Empty;

    // Order panel, kept across refreshes so a choice is not lost while the state updates.
    private readonly BoxContainer _orderPanel = Column(4);
    private readonly OptionButton _orderShip = new() { MinWidth = 140 };
    private readonly OptionButton _order = new() { MinWidth = 150 };
    private readonly OptionButton _orderTarget = new() { HorizontalExpand = true };
    private readonly Label _orderHint = new() { ClipText = true };
    private readonly List<string> _orderShipKeys = new();
    private readonly List<WFEncounterAdminOrder> _orders = new();
    private readonly List<NetEntity> _targetGrids = new();

    // Start tab.
    private readonly LineEdit _startSearch = new() { HorizontalExpand = true };
    private readonly OptionButton _prototype = new() { HorizontalExpand = true };
    private readonly LineEdit _distance = new() { Text = "300", MinWidth = 70 };
    private readonly CheckBox _keep = new() { Pressed = true };
    private readonly OptionButton _tier = new();
    private readonly List<string> _prototypeIds = new();
    private readonly List<WFEncounterAdminPrototype> _prototypes = new();

    // Scheduler tab.
    private readonly CheckBox _enabled = new();
    private readonly CheckBox _paused = new();
    private readonly LineEdit _intervalMin = new() { MinWidth = 70 };
    private readonly LineEdit _intervalMax = new() { MinWidth = 70 };
    private readonly LineEdit _maxActive = new() { MinWidth = 70 };
    private readonly Label _next = new();
    private readonly OptionButton _preset = new();
    private readonly Label _budget = new();
    private readonly List<string> _presetIds = new();
    private bool _schedulerLoaded;

    private WFEncounterAdminState? _last;

    public WFEncounterWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("wf-encounter-admin-title");
        MinSize = new Vector2(820, 480);
        SetSize = new Vector2(1060, 700);
        _system = _entities.System<WFEncounterClientSystem>();

        var tabs = new TabContainer { VerticalExpand = true };
        tabs.AddChild(EncountersTab());
        tabs.AddChild(StartTab());
        tabs.AddChild(SchedulerTab());
        TabContainer.SetTabTitle(tabs.GetChild(0), Text("tab-encounters"));
        TabContainer.SetTabTitle(tabs.GetChild(1), Text("tab-start"));
        TabContainer.SetTabTitle(tabs.GetChild(2), Text("tab-scheduler"));

        var body = Column(6);
        body.AddChild(tabs);
        body.AddChild(_status);
        Contents.AddChild(body);

        _system.Received += Receive;
        OnClose += () => _system.Received -= Receive;
        Send(WFEncounterAdminAction.Refresh);
    }

    private Control EncountersTab()
    {
        _search.PlaceHolder = Text("search-hint");
        _search.OnTextChanged += _ => Redraw();
        _showResolved.Text = Text("show-resolved");
        _showResolved.OnToggled += _ => Redraw();

        var left = Column(6);
        left.MinWidth = 340;
        left.AddChild(Row(_search));
        left.AddChild(_showResolved);
        left.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _list } });

        _emptyDetail.Text = Text("pick");
        var right = Column(6);
        right.HorizontalExpand = true;
        right.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _detail } });
        right.AddChild(OrderPanel());

        var split = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 12,
            VerticalExpand = true,
            Margin = new Thickness(4),
        };
        split.AddChild(left);
        split.AddChild(right);
        return split;
    }

    private Control OrderPanel()
    {
        _orderPanel.AddChild(Heading("order"));
        _orderShip.OnItemSelected += args =>
        {
            _orderShip.SelectId(args.Id);
            FillOrders();
        };
        _order.OnItemSelected += args =>
        {
            _order.SelectId(args.Id);
            UpdateOrderTarget();
        };
        _orderTarget.OnItemSelected += args => _orderTarget.SelectId(args.Id);
        _orderPanel.AddChild(Row(new Label { Text = Text("order-ship") }, _orderShip, new Label { Text = Text("order-do") }, _order));
        _orderPanel.AddChild(Row(new Label { Text = Text("order-target") }, _orderTarget, Button("order-go", SendOrder)));
        _orderPanel.AddChild(_orderHint);
        _orderPanel.Visible = false;
        return _orderPanel;
    }

    private Control StartTab()
    {
        var box = Column(8);
        box.Margin = new Thickness(4);
        _startSearch.PlaceHolder = Text("start-search-hint");
        _startSearch.OnTextChanged += _ => FillPrototypes();
        _prototype.OnItemSelected += args => _prototype.SelectId(args.Id);
        _keep.Text = Text("keep");
        _tier.AddItem(Text("tier-sector"), 0);
        for (var tier = 0; tier <= WFEncounterThreat.MaxTier; tier++)
        {
            _tier.AddItem(Text("tier", ("tier", tier)), tier + 1);
        }

        _tier.OnItemSelected += args => _tier.SelectId(args.Id);
        box.AddChild(Heading("start"));
        box.AddChild(Row(new Label { Text = Text("search") }, _startSearch));
        box.AddChild(Row(new Label { Text = Text("prototype") }, _prototype));
        box.AddChild(Row(new Label { Text = Text("distance") }, _distance, new Label { Text = Text("tier-label") }, _tier, _keep));
        box.AddChild(Row(Button("spawn", Spawn)));
        return box;
    }

    private Control SchedulerTab()
    {
        var box = Column(8);
        box.Margin = new Thickness(4);
        _enabled.Text = Text("enabled");
        _paused.Text = Text("paused");
        _preset.OnItemSelected += args =>
        {
            _preset.SelectId(args.Id);
            _system.Send(new WFEncounterAdminRequest { Action = WFEncounterAdminAction.Preset, Prototype = _presetIds[args.Id] });
        };
        box.AddChild(Heading("scheduler"));
        box.AddChild(Row(_enabled, _paused, _next));
        box.AddChild(Row(new Label { Text = Text("interval") }, _intervalMin, new Label { Text = Text("to") }, _intervalMax,
            new Label { Text = Text("cap") }, _maxActive));
        box.AddChild(Row(Button("apply", ApplyScheduler), Button("schedule-now", () => Send(WFEncounterAdminAction.Schedule)),
            Button("start-round", () => Send(WFEncounterAdminAction.StartRound))));
        box.AddChild(Row(new Label { Text = Text("preset") }, _preset, _budget));
        return box;
    }

    private static string Text(string key, params (string, object)[] args)
    {
        return Loc.GetString($"wf-encounter-admin-{key}", args);
    }

    private static BoxContainer Column(int separation)
    {
        return new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = separation };
    }

    private static BoxContainer Row(params Control[] controls)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        foreach (var control in controls)
        {
            row.AddChild(control);
        }

        return row;
    }

    private static Label Heading(string key)
    {
        return new Label { Text = Text(key), StyleClasses = { "LabelHeading" } };
    }

    private static Button Button(string key, Action pressed)
    {
        var button = new Button { Text = Text(key) };
        button.OnPressed += _ => pressed();
        return button;
    }

    private void Send(WFEncounterAdminAction action, NetEntity? target = null)
    {
        _system.Send(new WFEncounterAdminRequest { Action = action, Target = target });
    }

    private void Spawn()
    {
        if (_prototypeIds.Count == 0 || !TryNumber(_distance.Text, out var distance))
        {
            _status.Text = Text("bad-input");
            return;
        }

        _system.Send(new WFEncounterAdminRequest
        {
            Action = WFEncounterAdminAction.Spawn,
            Prototype = _prototypeIds[_prototype.SelectedId],
            Distance = distance,
            Pinned = _keep.Pressed,
            Tier = _tier.SelectedId > 0 ? _tier.SelectedId - 1 : null,
        });
    }

    private void ApplyScheduler()
    {
        if (!TryNumber(_intervalMin.Text, out var min) || !TryNumber(_intervalMax.Text, out var max)
            || !int.TryParse(_maxActive.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cap))
        {
            _status.Text = Text("bad-input");
            return;
        }

        _schedulerLoaded = false;
        _system.Send(new WFEncounterAdminRequest
        {
            Action = WFEncounterAdminAction.Scheduler,
            Enabled = _enabled.Pressed,
            Paused = _paused.Pressed,
            IntervalMin = min,
            IntervalMax = max,
            MaxActive = cap,
        });
    }

    private static bool TryNumber(string text, out float value)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
    }

    private void Receive(WFEncounterAdminState state)
    {
        // Settings are only loaded once and after Apply, so a poll never overwrites what the admin is typing.
        if (!_schedulerLoaded)
        {
            _schedulerLoaded = true;
            _enabled.Pressed = state.Enabled;
            _paused.Pressed = state.Paused;
            _intervalMin.Text = state.IntervalMin.ToString("0", CultureInfo.InvariantCulture);
            _intervalMax.Text = state.IntervalMax.ToString("0", CultureInfo.InvariantCulture);
            _maxActive.Text = state.MaxActive.ToString(CultureInfo.InvariantCulture);
        }

        _next.Text = state.NextIn >= 0f ? Text("next", ("time", Duration(state.NextIn))) : Text("next-none");
        _budget.Text = Text("budget", ("cost", state.Cost), ("budget", state.Budget));
        if (_presetIds.Count != state.Presets.Count)
        {
            _presetIds.Clear();
            _preset.Clear();
            foreach (var preset in state.Presets)
            {
                _preset.AddItem(preset.Name, _presetIds.Count);
                _presetIds.Add(preset.Id);
            }
        }

        var current = _presetIds.IndexOf(state.Preset);
        if (current >= 0 && _preset.SelectedId != current)
            _preset.SelectId(current);
        if (_prototypes.Count != state.Prototypes.Count)
        {
            _prototypes.Clear();
            _prototypes.AddRange(state.Prototypes);
            FillPrototypes();
        }

        // A message stays until the next one, so a poll does not wipe what an order said.
        if (state.Message.Length > 0)
            _status.Text = state.Message;

        _last = state;
        Redraw();
    }

    /// <summary>Refills the start list with the prototypes its search lets through, keeping the one chosen where it can.</summary>
    private void FillPrototypes()
    {
        var chosen = _prototypeIds.Count > 0 && _prototype.SelectedId < _prototypeIds.Count ? _prototypeIds[_prototype.SelectedId] : null;
        var needle = _startSearch.Text.Trim();
        _prototypeIds.Clear();
        _prototype.Clear();
        foreach (var prototype in _prototypes)
        {
            if (needle.Length > 0 && !prototype.Id.Contains(needle, StringComparison.OrdinalIgnoreCase))
                continue;

            var label = prototype.Scheduled ? prototype.Id : Text("prototype-manual", ("id", prototype.Id));
            if (prototype.Running)
                label = Text("prototype-running", ("label", label));
            _prototype.AddItem(label, _prototypeIds.Count);
            _prototypeIds.Add(prototype.Id);
        }

        var keep = chosen != null ? _prototypeIds.IndexOf(chosen) : -1;
        if (keep >= 0)
            _prototype.SelectId(keep);
    }

    /// <summary>Whether the search box lets an encounter through: by its name, prototype, state, or a ship's key, name or side.</summary>
    private bool Matches(WFEncounterAdminEntry encounter)
    {
        var needle = _search.Text.Trim();
        if (needle.Length == 0)
            return true;

        bool Has(string text) => text.Contains(needle, StringComparison.OrdinalIgnoreCase);
        return Has(encounter.Name) || Has(encounter.Prototype) || Has(encounter.State)
               || encounter.Ships.Exists(ship => Has(ship.Key) || Has(ship.Name) || Has(ship.Side));
    }

    /// <summary>Redraws the list and the picked encounter, each only when what it shows has changed, so a click is never eaten by a rebuild.</summary>
    private void Redraw()
    {
        if (_last is not { } state)
            return;

        var shown = state.Encounters.Where(encounter => (_showResolved.Pressed || !encounter.Resolved) && Matches(encounter))
            .OrderBy(encounter => encounter.Resolved).ThenBy(encounter => encounter.Name).ToList();
        if (_selected is { } picked && !state.Encounters.Exists(encounter => encounter.Uid == picked))
            _selected = null;

        var listSignature = string.Join("|", shown.Select(encounter =>
            $"{encounter.Uid}{encounter.Name}{encounter.State}{encounter.Threat}{encounter.Ships.Count(ship => ship.Exists)}{encounter.Uid == _selected}"));
        if (listSignature != _listShown)
        {
            _listShown = listSignature;
            _list.RemoveAllChildren();
            if (shown.Count == 0)
                _list.AddChild(new Label { Text = Text(state.Encounters.Count == 0 ? "none" : "none-match") });

            foreach (var encounter in shown)
            {
                _list.AddChild(ListEntry(encounter));
            }
        }

        var selected = state.Encounters.Find(encounter => encounter.Uid == _selected);
        var detailSignature = selected == null ? string.Empty : DetailSignature(selected);
        if (detailSignature != _detailShown || selected == null && _detail.ChildCount == 0)
        {
            _detailShown = detailSignature;
            _detail.RemoveAllChildren();
            if (selected == null)
                _detail.AddChild(_emptyDetail);
            else
                FillDetail(selected);
        }

        FillOrderPanel(selected, state.Targets);
    }

    private Control ListEntry(WFEncounterAdminEntry encounter)
    {
        var uid = encounter.Uid;
        var ships = encounter.Ships.Count(ship => ship.Exists);
        var simulated = encounter.Ships.Count(ship => ship.Exists && ship.Virtual);
        var button = new Button
        {
            Text = Text("list-entry", ("rank", Rank(encounter.Threat)), ("name", encounter.Name), ("ships", ships),
                ("simulated", simulated), ("resolved", encounter.Resolved ? 1 : 0)),
            ToolTip = $"{encounter.Prototype}: {encounter.State}",
            ToggleMode = true,
            Pressed = uid == _selected,
            HorizontalExpand = true,
            ClipText = true,
        };
        button.OnPressed += _ =>
        {
            _selected = uid;
            _listShown = string.Empty;
            Redraw();
        };
        return button;
    }

    /// <summary>A threat rank as text for the list: chevrons, then stars.</summary>
    private static string Rank(int? threat)
    {
        var count = WFEncounterRanks.Count(threat);
        if (count == 0)
            return string.Empty;

        return new string(threat > 3 ? '*' : '^', count) + " ";
    }

    private static string DetailSignature(WFEncounterAdminEntry encounter)
    {
        var text = new StringBuilder();
        text.Append(encounter.Uid).Append(encounter.Name).Append(encounter.State).Append(encounter.Hidden).Append(encounter.Pinned)
            .Append(encounter.Resolved).Append(encounter.Threat).Append((int) (encounter.Age / 60f)).Append((int) (encounter.ExpiresIn / 60f));
        foreach (var ship in encounter.Ships)
        {
            text.Append(ship.Key).Append(ship.Grid).Append(ship.Exists).Append(ship.Disabled).Append(ship.Crew).Append(ship.Activity)
                .Append(ship.Virtual).Append(ship.Building);
        }

        return text.ToString();
    }

    private void FillDetail(WFEncounterAdminEntry encounter)
    {
        var uid = encounter.Uid;
        _detail.AddChild(new Label { Text = encounter.Name, StyleClasses = { "LabelHeading" }, ClipText = true });
        _detail.AddChild(new Label { Text = Text("detail-prototype", ("prototype", encounter.Prototype)), ClipText = true });
        _detail.AddChild(new Label { Text = Text("detail-state", ("state", encounter.State)), ClipText = true });
        var timing = Text("detail-age", ("age", Duration(encounter.Age)));
        if (encounter.ExpiresIn >= 0f && !encounter.Resolved)
            timing += "  " + Text("expires", ("time", Duration(encounter.ExpiresIn)));
        if (encounter.Pinned)
            timing += "  " + Text("detail-pinned");
        if (encounter.Threat is { } threat)
            timing += "  " + Text("detail-threat", ("threat", threat));
        _detail.AddChild(new Label { Text = timing, ClipText = true });

        var actions = Row(Button("teleport", () => Send(WFEncounterAdminAction.Teleport, uid)));
        if (encounter.Hidden && !encounter.Resolved)
            actions.AddChild(Button("reveal", () => Send(WFEncounterAdminAction.Reveal, uid)));
        if (!encounter.Resolved)
            actions.AddChild(Button("resolve", () => Send(WFEncounterAdminAction.Resolve, uid)));
        actions.AddChild(Button("end", () => Send(WFEncounterAdminAction.End, uid)));
        _detail.AddChild(actions);

        _detail.AddChild(Heading("ships"));
        foreach (var ship in encounter.Ships)
        {
            _detail.AddChild(ShipRow(encounter, ship));
        }
    }

    private Control ShipRow(WFEncounterAdminEntry encounter, WFEncounterAdminShip ship)
    {
        var grid = ship.Grid;
        var group = ship.Group;
        var status = !ship.Exists ? Text("ship-status-gone")
            : ship.Building ? Text("ship-status-building")
            : ship.Virtual ? Text("ship-status-simulated")
            : ship.Disabled ? Text("ship-status-disabled")
            : Text("ship-status-real");
        var label = new Label
        {
            Text = ship.Exists
                ? Text("ship-line", ("key", ship.Key), ("name", ship.Name), ("side", ship.Side), ("status", status), ("crew", ship.Crew))
                : Text("ship-gone", ("key", ship.Key), ("side", ship.Side)),
            HorizontalExpand = true,
            ClipText = true,
        };
        var head = Row(label);
        if (ship.Exists)
        {
            head.AddChild(Button("teleport", () => Send(WFEncounterAdminAction.Teleport, grid)));
            if (!ship.Virtual && !ship.Building)
            {
                head.AddChild(Button("crew", () =>
                {
                    var window = new WFCrewSetupWindow();
                    window.SelectCrew(grid, group);
                    window.OpenCentered();
                }));
            }
        }

        var box = Column(0);
        box.AddChild(head);
        if (ship.Exists && ship.Activity.Length > 0)
            box.AddChild(new Label { Text = ship.Activity, ClipText = true, Margin = new Thickness(16, 0, 0, 0), FontColorOverride = Color.Gray });
        return box;
    }

    /// <summary>Keeps the order panel's ships and targets in step with the picked encounter, keeping what is chosen where it can.</summary>
    private void FillOrderPanel(WFEncounterAdminEntry? encounter, List<WFEncounterAdminTarget> targets)
    {
        _orderPanel.Visible = encounter is { Resolved: false };
        if (encounter == null)
            return;

        var keys = encounter.Ships.Where(ship => ship.Exists).Select(ship => ship.Key).ToList();
        if (!keys.SequenceEqual(_orderShipKeys))
        {
            var chosen = SelectedShipKey();
            _orderShipKeys.Clear();
            _orderShip.Clear();
            foreach (var key in keys)
            {
                _orderShip.AddItem(key, _orderShipKeys.Count);
                _orderShipKeys.Add(key);
            }

            if (chosen != null && _orderShipKeys.IndexOf(chosen) is var index and >= 0)
                _orderShip.SelectId(index);
        }

        FillOrders();
        var grids = targets.Select(target => target.Grid).ToList();
        if (!grids.SequenceEqual(_targetGrids))
        {
            var chosen = _targetGrids.Count > 0 && _orderTarget.SelectedId < _targetGrids.Count ? _targetGrids[_orderTarget.SelectedId] : (NetEntity?) null;
            _targetGrids.Clear();
            _orderTarget.Clear();
            foreach (var target in targets)
            {
                var distance = target.Distance >= 0f
                    ? Text("target-distance", ("km", (target.Distance / 1000f).ToString("0.0", CultureInfo.InvariantCulture)))
                    : Text("target-elsewhere");
                _orderTarget.AddItem(Text("target", ("name", target.Name), ("players", target.Players), ("distance", distance)), _targetGrids.Count);
                _targetGrids.Add(target.Grid);
            }

            if (_targetGrids.Count == 0)
                _orderTarget.AddItem(Text("target-none"), 0);
            if (chosen is { } keep && _targetGrids.IndexOf(keep) is var index and >= 0)
                _orderTarget.SelectId(index);
        }

        UpdateOrderTarget();
    }

    private string? SelectedShipKey()
    {
        return _orderShipKeys.Count > 0 && _orderShip.SelectedId < _orderShipKeys.Count ? _orderShipKeys[_orderShip.SelectedId] : null;
    }

    private WFEncounterAdminShip? SelectedShip()
    {
        var encounter = _last?.Encounters.Find(entry => entry.Uid == _selected);
        return SelectedShipKey() is { } key ? encounter?.Ships.Find(ship => ship.Key == key) : null;
    }

    /// <summary>The orders the chosen ship can take: extort for an extorter, board for a boarder, spawn now while simulated.</summary>
    private void FillOrders()
    {
        var ship = SelectedShip();
        var orders = new List<WFEncounterAdminOrder>();
        if (ship is { Extorter: true })
            orders.Add(WFEncounterAdminOrder.Extort);
        if (ship is { Hunter: true })
            orders.Add(WFEncounterAdminOrder.Board);
        orders.Add(WFEncounterAdminOrder.Attack);
        orders.Add(WFEncounterAdminOrder.Follow);
        orders.Add(WFEncounterAdminOrder.ComeToMe);
        orders.Add(WFEncounterAdminOrder.Hold);
        orders.Add(WFEncounterAdminOrder.Resume);
        if (ship is { Virtual: true })
            orders.Add(WFEncounterAdminOrder.SpawnNow);
        orders.Add(WFEncounterAdminOrder.SendAway);
        if (orders.SequenceEqual(_orders))
            return;

        var chosen = _orders.Count > 0 && _order.SelectedId < _orders.Count ? _orders[_order.SelectedId] : (WFEncounterAdminOrder?) null;
        _orders.Clear();
        _order.Clear();
        foreach (var order in orders)
        {
            _order.AddItem(Text($"order-{order.ToString().ToLowerInvariant()}"), _orders.Count);
            _orders.Add(order);
        }

        if (chosen is { } keep && _orders.IndexOf(keep) is var index and >= 0)
            _order.SelectId(index);
        UpdateOrderTarget();
    }

    private WFEncounterAdminOrder? SelectedOrder()
    {
        return _orders.Count > 0 && _order.SelectedId < _orders.Count ? _orders[_order.SelectedId] : null;
    }

    private static bool NeedsTarget(WFEncounterAdminOrder order)
    {
        return order is WFEncounterAdminOrder.Extort or WFEncounterAdminOrder.Board or WFEncounterAdminOrder.Attack
            or WFEncounterAdminOrder.Follow;
    }

    private void UpdateOrderTarget()
    {
        var order = SelectedOrder();
        _orderTarget.Disabled = order is not { } chosen || !NeedsTarget(chosen) || _targetGrids.Count == 0;
        _orderHint.Text = order is { } hint ? Text($"order-{hint.ToString().ToLowerInvariant()}-hint") : string.Empty;
    }

    private void SendOrder()
    {
        if (_selected is not { } encounter || SelectedShipKey() is not { } key || SelectedOrder() is not { } order)
            return;

        NetEntity? target = null;
        if (NeedsTarget(order))
        {
            if (_targetGrids.Count == 0 || _orderTarget.SelectedId >= _targetGrids.Count)
            {
                _status.Text = Text("order-no-target");
                return;
            }

            target = _targetGrids[_orderTarget.SelectedId];
        }

        _system.Send(new WFEncounterAdminRequest
        {
            Action = WFEncounterAdminAction.Order,
            Target = encounter,
            Key = key,
            Order = order,
            OrderTarget = target,
        });
    }

    private static string Duration(float seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}
