using System.Linq;
using Content.Client.UserInterface.Fragments;
using Content.Shared._WF.Contracts;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.Contracts;

/// <summary>The Contracts PDA app: the holder's accepted contracts, history and standing. Offers are taken at the board.</summary>
public sealed partial class ContractsUi : UIFragment
{
    private ContractsUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new ContractsUiFragment();
        _fragment.OnAbandon += id => userInterface.SendMessage(new CartridgeUiMessage(new WFContractCartridgeAbandonMessage(id)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is WFContractCartridgeUiState cast)
            _fragment?.UpdateState(cast.State);
    }
}

/// <summary>Three tabs over a list, and the selected contract's details below it.</summary>
public sealed partial class ContractsUiFragment : BoxContainer
{
    [Dependency] private IGameTiming _timing = default!;

    public event Action<int>? OnAbandon;

    private readonly Button _accepted;
    private readonly Button _history;
    private readonly Button _standingTab;
    private readonly Label _status;
    private readonly BoxContainer _list;
    private readonly ScrollContainer _listScroll;
    private readonly WFContractDetails _details;
    private readonly ScrollContainer _standingScroll;
    private readonly WFContractStandingList _standing;
    private readonly List<WFContractRow> _rows = new();

    private WFContractBoardState? _state;
    private int _tab;
    private int? _selected;
    private int? _confirm;
    private TimeSpan _nextTick;

    public ContractsUiFragment()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(4);

        var tabs = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        _accepted = Tab(0);
        _history = Tab(1);
        _standingTab = Tab(2);
        tabs.AddChild(_accepted);
        tabs.AddChild(_history);
        tabs.AddChild(_standingTab);
        AddChild(tabs);

        _status = new Label { FontColorOverride = WFContractStyle.Dim, Margin = new Thickness(0, 2, 0, 4), ClipText = true };
        AddChild(_status);

        _listScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, SizeFlagsStretchRatio = 0.8f };
        _list = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        _listScroll.AddChild(_list);
        AddChild(_listScroll);

        _details = new WFContractDetails(_timing) { SizeFlagsStretchRatio = 1.2f, Margin = new Thickness(0, 4, 0, 0) };
        _details.OnAction += OnDetailsAction;
        AddChild(_details);

        _standing = new WFContractStandingList();
        _standingScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Visible = false };
        _standingScroll.AddChild(_standing);
        AddChild(_standingScroll);
        SetTab(0);
    }

    private Button Tab(int index)
    {
        var button = new Button { ToggleMode = true, HorizontalExpand = true };
        button.OnPressed += _ => SetTab(index);
        return button;
    }

    public void UpdateState(WFContractBoardState state)
    {
        _state = state;
        _accepted.Text = Loc.GetString("wf-contract-tab-accepted", ("count", state.Active.Count), ("max", state.MaxActive));
        _history.Text = Loc.GetString("wf-contract-tab-history");
        _standingTab.Text = Loc.GetString("wf-contract-tab-standing");
        _status.Text = state.Owed > 0
            ? Loc.GetString("wf-contract-board-owed", ("pay", state.Owed))
            : Loc.GetString("wf-contract-pda-hint", ("offers", state.Offers.Count), ("crew", state.CrewSize),
                ("slots", state.MaxActive), ("next", state.NextSlotAt));
        _standing.Update(state.Standing);
        Rebuild();
    }

    private void SetTab(int tab)
    {
        _tab = tab;
        _accepted.Pressed = tab == 0;
        _history.Pressed = tab == 1;
        _standingTab.Pressed = tab == 2;
        _selected = null;
        _listScroll.Visible = tab != 2;
        _details.Visible = tab != 2;
        _standingScroll.Visible = tab == 2;
        Rebuild();
    }

    private void Rebuild()
    {
        _list.RemoveAllChildren();
        _rows.Clear();
        if (_state == null)
            return;

        var shown = _tab == 0 ? _state.Active : _state.History;
        if (shown.Count == 0)
        {
            _list.AddChild(new Label
            {
                Text = Loc.GetString(_tab == 0 ? "wf-contract-empty-accepted" : "wf-contract-empty-history"),
                FontColorOverride = WFContractStyle.Dim,
            });
        }

        foreach (var contract in shown)
        {
            var row = new WFContractRow(contract, _timing);
            row.OnPressed += _ =>
            {
                _selected = contract.Id;
                _confirm = null;
                ShowSelected();
            };
            _rows.Add(row);
            _list.AddChild(row);
        }

        if (_selected is not { } selected || shown.All(c => c.Id != selected))
            _selected = shown.FirstOrDefault()?.Id;

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

        _details.Show(contract, _tab != 0 || !contract.Mine
            ? null
            : Loc.GetString(_confirm == contract.Id ? "wf-contract-action-abandon-confirm" : "wf-contract-action-abandon",
                ("deposit", contract.Deposit)));
    }

    private void OnDetailsAction(WFContractView contract)
    {
        if (_tab != 0)
            return;

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
