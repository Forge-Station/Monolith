using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.Contracts;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.Contracts;

/// <summary>The contract board window: offers, accepted, history and standing.</summary>
public sealed partial class WFContractBoardWindow : FancyWindow
{
    [Dependency] private IGameTiming _timing = default!;

    public enum Tab
    {
        Offers,
        Accepted,
        History,
        Standing,
    }

    public event Action<int>? OnAccept;
    public event Action<int>? OnAbandon;

    private readonly Dictionary<Tab, Button> _tabs = new();
    private readonly BoxContainer _rail;
    private readonly BoxContainer _list;
    private readonly BoxContainer _listColumn;
    private readonly WFContractDetails _details;
    private readonly ScrollContainer _standingScroll;
    private readonly WFContractStandingList _standing;
    private readonly Label _status;
    private readonly List<WFContractRow> _rows = new();

    private WFContractBoardState? _state;
    private Tab _tab = Tab.Offers;
    private WFContractCategory? _category;
    private int? _selected;
    private int? _confirm;
    private TimeSpan _nextTick;

    public WFContractBoardWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("wf-contract-board-title");
        SetSize = new Vector2(980, 620);
        MinSize = new Vector2(760, 480);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(6) };
        ContentsContainer.AddChild(root);

        var tabs = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var tab in Enum.GetValues<Tab>())
        {
            var button = new Button { ToggleMode = true, MinWidth = 120 };
            button.OnPressed += _ => SetTab(tab);
            _tabs[tab] = button;
            tabs.AddChild(button);
        }

        _status = new Label { HorizontalExpand = true, HorizontalAlignment = HAlignment.Right, FontColorOverride = WFContractStyle.Dim };
        tabs.AddChild(_status);
        root.AddChild(tabs);

        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, VerticalExpand = true };
        root.AddChild(body);

        _rail = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, MinWidth = 150, Margin = new Thickness(0, 0, 6, 0) };
        body.AddChild(_rail);

        var listScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _list = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        listScroll.AddChild(_list);
        _listColumn = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SizeFlagsStretchRatio = 1.2f,
            Margin = new Thickness(0, 0, 6, 0),
        };
        _listColumn.AddChild(listScroll);
        body.AddChild(_listColumn);

        _details = new WFContractDetails(_timing) { SizeFlagsStretchRatio = 1f };
        _details.OnAction += OnDetailsAction;
        body.AddChild(_details);

        _standing = new WFContractStandingList();
        _standingScroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false, Visible = false };
        _standingScroll.AddChild(_standing);
        body.AddChild(_standingScroll);

        SetTab(Tab.Offers);
    }

    public void UpdateState(WFContractBoardState state)
    {
        _state = state;
        _tabs[Tab.Offers].Text = Loc.GetString("wf-contract-tab-offers", ("count", state.Offers.Count));
        _tabs[Tab.Accepted].Text = Loc.GetString("wf-contract-tab-accepted", ("count", state.Active.Count), ("max", state.MaxActive));
        _tabs[Tab.History].Text = Loc.GetString("wf-contract-tab-history");
        _tabs[Tab.Standing].Text = Loc.GetString("wf-contract-tab-standing");
        _status.Text = !state.CanHold
            ? Loc.GetString("wf-contract-board-no-character")
            : state.Owed > 0
                ? Loc.GetString("wf-contract-board-owed", ("pay", state.Owed))
                : Loc.GetString("wf-contract-board-slots", ("crew", state.CrewSize), ("slots", state.MaxActive),
                    ("next", state.NextSlotAt));
        _standing.Update(state.Standing);
        Rebuild();
    }

    private void SetTab(Tab tab)
    {
        _tab = tab;
        foreach (var (key, button) in _tabs)
        {
            button.Pressed = key == tab;
        }

        _selected = null;
        var standing = tab == Tab.Standing;
        _standingScroll.Visible = standing;
        _listColumn.Visible = !standing;
        _details.Visible = !standing;
        _rail.Visible = tab == Tab.Offers;
        Rebuild();
    }

    private List<WFContractView> Shown()
    {
        if (_state == null)
            return new List<WFContractView>();

        return _tab switch
        {
            Tab.Offers => _state.Offers.Where(c => _category == null || c.Category == _category)
                .OrderBy(c => c.Locked != null)
                .ThenBy(c => c.Tier)
                .ThenByDescending(c => c.Reward)
                .ToList(),
            Tab.Accepted => _state.Active,
            Tab.History => _state.History,
            _ => new List<WFContractView>(),
        };
    }

    private void Rebuild()
    {
        if (_state == null)
            return;

        BuildRail();
        _list.RemoveAllChildren();
        _rows.Clear();
        var shown = Shown();
        if (shown.Count == 0)
        {
            _list.AddChild(new Label
            {
                Text = Loc.GetString(_tab switch
                {
                    Tab.Offers when _category == WFContractCategory.Planetary => "wf-contract-empty-planetary",
                    Tab.Offers => "wf-contract-empty-offers",
                    Tab.Accepted => "wf-contract-empty-accepted",
                    _ => "wf-contract-empty-history",
                }),
                FontColorOverride = WFContractStyle.Dim,
                Margin = new Thickness(4),
            });
        }

        foreach (var contract in shown)
        {
            var row = new WFContractRow(contract, _timing);
            row.OnPressed += _ => Select(contract.Id);
            _rows.Add(row);
            _list.AddChild(row);
        }

        if (_selected is not { } selected || shown.All(c => c.Id != selected))
            _selected = shown.FirstOrDefault()?.Id;

        ShowSelected();
    }

    /// <summary>The category rail: every category with its offer count; planetary is listed though nothing uses it yet.</summary>
    private void BuildRail()
    {
        _rail.RemoveAllChildren();
        if (_state == null)
            return;

        AddRailButton(null, Loc.GetString("wf-contract-category-all"), _state.Offers.Count);
        foreach (var category in Enum.GetValues<WFContractCategory>())
        {
            AddRailButton(category, WFContractStyle.Category(category), _state.Offers.Count(c => c.Category == category));
        }
    }

    private void AddRailButton(WFContractCategory? category, string name, int count)
    {
        var button = new Button
        {
            Text = Loc.GetString("wf-contract-rail-entry", ("name", name), ("count", count)),
            ToggleMode = true,
            Pressed = _category == category,
            Margin = new Thickness(0, 0, 0, 2),
        };
        button.Label.HorizontalAlignment = HAlignment.Left;
        if (count == 0 && category != null)
            button.ModulateSelfOverride = WFContractStyle.Dim;

        button.OnPressed += _ =>
        {
            _category = category;
            _selected = null;
            Rebuild();
        };
        _rail.AddChild(button);
    }

    private void Select(int id)
    {
        _selected = id;
        _confirm = null;
        ShowSelected();
    }

    private void ShowSelected()
    {
        foreach (var row in _rows)
        {
            row.SetSelected(row.Contract.Id == _selected);
        }

        var contract = _rows.FirstOrDefault(r => r.Contract.Id == _selected)?.Contract;
        if (contract == null)
        {
            _details.Clear();
            return;
        }

        switch (_tab)
        {
            case Tab.Offers:
                _details.Show(contract, Loc.GetString("wf-contract-action-accept", ("deposit", contract.Deposit)), contract.Locked);
                break;
            case Tab.Accepted:
                _details.Show(contract, !contract.Mine
                    ? null
                    : Loc.GetString(_confirm == contract.Id ? "wf-contract-action-abandon-confirm" : "wf-contract-action-abandon",
                        ("deposit", contract.Deposit)));
                break;
            default:
                _details.Show(contract, null);
                break;
        }
    }

    private void OnDetailsAction(WFContractView contract)
    {
        if (_tab == Tab.Offers)
        {
            OnAccept?.Invoke(contract.Id);
            return;
        }

        if (_tab != Tab.Accepted)
            return;

        // Abandoning loses the deposit, so it takes a second press.
        if (_confirm != contract.Id)
        {
            _confirm = contract.Id;
            ShowSelected();
            return;
        }

        _confirm = null;
        OnAbandon?.Invoke(contract.Id);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_timing.RealTime < _nextTick)
            return;

        _nextTick = _timing.RealTime + TimeSpan.FromSeconds(1);
        foreach (var row in _rows)
        {
            row.UpdateTime();
        }

        _details.UpdateTime();
    }
}
