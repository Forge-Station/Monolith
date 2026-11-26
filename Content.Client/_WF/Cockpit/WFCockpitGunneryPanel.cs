using System.Linq;
using Content.Client._Mono.FireControl.UI;
using Content.Client._WF.CombatConsole;
using Content.Shared._Mono.FireControl;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Cockpit;

/// <summary>Hosts the existing weapon controls without opening a separate gunnery window.</summary>
public sealed class WFCockpitGunneryPanel : Control
{
    private readonly FireControlWindow _window = new();
    private readonly WFCockpitLease _lease = new();
    private readonly WFThreatAnnunciator _annunciator = new();
    private bool _released;

    /// <summary>Forwards the existing fire-control commands through the active helm session.</summary>
    public event Action<BoundUserInterfaceMessage>? Command;

    /// <summary>Returns the offensive selection across the whole list, excluding countermeasure launchers.</summary>
    public List<NetEntity> SelectedWeapons => _window.WeaponsList
        .Where(pair => pair.Value.Pressed && _window.WfAvailableWeapon(pair.Key)).Select(pair => pair.Key).ToList();

    /// <summary>Whether any offensive weapon is selected, without building the list.</summary>
    public bool HasSelectedWeapons
    {
        get
        {
            foreach (var (uid, button) in _window.WeaponsList)
            {
                if (button.Pressed && _window.WfAvailableWeapon(uid))
                    return true;
            }
            return false;
        }
    }

    /// <summary>Whether this offensive weapon is selected, wherever it sits in the list.</summary>
    public bool IsSelected(NetEntity weapon) =>
        _window.WeaponsList.TryGetValue(weapon, out var button) && button.Pressed && _window.WfAvailableWeapon(weapon);

    public WFCockpitGunneryPanel()
    {
        HorizontalExpand = VerticalExpand = true;
        _window.CombatMessage += Forward;
        _window.OnServerRefresh += Refresh;
        AddChild(_window.WfBuildCockpitGunnery(_lease));
        WFInstrumentTheme.Install(this);
    }

    /// <summary>Applies a linked console snapshot, or clears stale controls when the link is lost.</summary>
    public void UpdateState(FireControlConsoleBoundInterfaceState? state)
    {
        if (state == null)
        {
            ClearSelection();
            state = new FireControlConsoleBoundInterfaceState(false, Array.Empty<FireControllableEntry>(),
                new NavInterfaceState(250, null, null, new(), default));
        }
        UpdateStatus(state);
    }

    /// <summary>Updates the same supply, grouping and countermeasure logic as the full console.</summary>
    public void UpdateStatus(FireControlConsoleBoundInterfaceState state)
    {
        _window.UpdateStatus(state);
        _window.WfUpdateCockpitGunnery();
        if (_annunciator.Update(state.Combat.Threats, state.Connected))
            IoCManager.Resolve<IEntityManager>().System<WFConsoleAudio>().Warn();
    }

    /// <summary>Drops the previous console's targeting selection when the accessible console changes.</summary>
    public void ClearSelection()
    {
        foreach (var button in _window.WeaponsList.Values)
            button.Pressed = false;
        _window.OnWeaponSelectionChanged?.Invoke();
    }

    private void Forward(BoundUserInterfaceMessage message) => Command?.Invoke(message);
    private void Refresh() => Forward(new FireControlConsoleRefreshServerMessage());

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_released)
        {
            _released = true;
            _window.CombatMessage -= Forward;
            _window.OnServerRefresh -= Refresh;
            _lease.Restore();
            _window.Dispose();
        }
        base.Dispose(disposing);
    }
}
