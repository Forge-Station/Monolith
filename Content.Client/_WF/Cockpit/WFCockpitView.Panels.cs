using System.Numerics;
using Content.Shared._WF.CCVar;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Cockpit;

public sealed partial class WFCockpitView
{
    /// <summary>Width the crew alert bank is measured at, as when it floats over the world view.</summary>
    private const float AlertBankWidth = 144;

    /// <summary>Widest a docked vote popup is measured; popups ask for 360 to 600.</summary>
    private const float VoteWidth = 600;

    /// <summary>What the shield panel needs around its controls: the panel frame and room for a scroll bar.</summary>
    private const float ShieldChrome = 38;

    /// <summary>The MFD page a cockpit opens on when none is saved.</summary>
    public const string DefaultPage = "wf-cockpit-nav";

    /// <summary>An optional page to open as soon as it has something to show: the saved page, or one that went away while open.</summary>
    private string? _awaitedPage;
    private bool _swapped;
    private bool _expanded;
    private bool _expandMoot;
    private bool _maximised;
    private bool _worldHidden;
    private float _shieldNeed;

    /// <summary>Whether the MFD and the world view have traded places.</summary>
    public bool PanelsSwapped => _swapped;

    /// <summary>Whether the MFD and the world view share the width evenly instead of the normal split.</summary>
    public bool MfdExpanded => _expanded;

    /// <summary>Whether the MFD has taken over the world view's space and the world view is not drawn.</summary>
    public bool MfdMaximised => _maximised;

    /// <summary>Opens on the MFD page and panel arrangement the player last chose in any cockpit.</summary>
    private void LoadLayout()
    {
        var config = IoCManager.Resolve<IConfigurationManager>();
        var page = config.GetCVar(WolfgateCVars.CockpitMfdPage);
        if (!_pageHosts.ContainsKey(page))
            page = DefaultPage;
        else if (!_selectors[page].Visible)
        {
            _awaitedPage = page;
            page = DefaultPage;
        }
        SelectPage(page);
        SetSwapped(config.GetCVar(WolfgateCVars.CockpitMfdSwapped));
        SetExpanded(config.GetCVar(WolfgateCVars.CockpitMfdExpanded));
        SetMaximised(config.GetCVar(WolfgateCVars.CockpitMfdMaximised));
    }

    /// <summary>
    /// Shows an optional page's selector only while it has something to show. A page that goes away while open hands the
    /// MFD back to the default page, and opens again if it returns before the player picks another.
    /// </summary>
    private void SyncOptionalPages()
    {
        foreach (var (key, content) in _pages)
        {
            if (content is not IWFCockpitOptionalPage optional || _selectors[key].Visible == optional.Available)
                continue;
            _selectors[key].Visible = optional.Available;
            if (optional.Available && _awaitedPage == key)
            {
                _awaitedPage = null;
                SelectPage(key);
            }
            else if (!optional.Available && _page == key)
            {
                _awaitedPage = key;
                SelectPage(DefaultPage);
            }
        }
    }

    /// <summary>Saves the MFD page and panel arrangement when the player changes one.</summary>
    private void SaveLayout()
    {
        var config = IoCManager.Resolve<IConfigurationManager>();
        config.SetCVar(WolfgateCVars.CockpitMfdPage, _page);
        config.SetCVar(WolfgateCVars.CockpitMfdSwapped, _swapped);
        config.SetCVar(WolfgateCVars.CockpitMfdExpanded, _expanded);
        config.SetCVar(WolfgateCVars.CockpitMfdMaximised, _maximised);
        config.SaveToFile();
    }

    /// <summary>Creates a compact toggle for the MFD's own controls; console buttons clip their text, so the width is fixed.</summary>
    private static Button PanelButton(string text, string help, string name, float width)
    {
        var button = Button(text, true);
        button.AddStyleClass("WfCompact");
        button.ToolTip = Loc.GetString(help);
        button.HorizontalExpand = false;
        button.Name = name;
        button.SetWidth = width;
        return button;
    }

    /// <summary>Puts the MFD in the large centre area and the world view in the side column, or back.</summary>
    public void SetSwapped(bool swapped)
    {
        if (_restored || _swapped == swapped)
            return;
        _swapped = swapped;
        _swap.Pressed = swapped;
        InvalidateMeasure();
    }

    /// <summary>Shares the width evenly between the world view and the MFD, whichever is in the side column, or returns to the normal split.</summary>
    public void SetExpanded(bool expanded)
    {
        if (_restored || _expanded == expanded)
            return;
        _expanded = expanded;
        _expand.Pressed = expanded;
        _expand.Text = Loc.GetString(expanded ? "wf-cockpit-collapse" : "wf-cockpit-expand");
        _expand.ToolTip = Loc.GetString(expanded ? "wf-cockpit-collapse-help" : "wf-cockpit-expand-help");
        RefreshExpand();
        InvalidateMeasure();
    }

    /// <summary>Notes whether Expand could widen anything at this window size, then updates the button.</summary>
    private void UpdateExpand(Vector2 size, float left)
    {
        _expandMoot = !WFCockpitPanelLayout.CanExpand(size, left);
        RefreshExpand();
    }

    /// <summary>Expand is off while maximised, and in a window too narrow for it to matter unless it is already on, so it can be released.</summary>
    private void RefreshExpand() => _expand.Disabled = _maximised || !_expanded && _expandMoot;

    /// <summary>Gives the MFD the world view's space and stops drawing the world view, or brings the earlier arrangement back.</summary>
    public void SetMaximised(bool maximised)
    {
        if (_restored || _maximised == maximised)
            return;
        _maximised = maximised;
        _worldHidden = maximised;
        _world.Visible = !maximised;
        _maximise.Pressed = maximised;
        _maximise.Text = Loc.GetString(maximised ? "wf-cockpit-restore" : "wf-cockpit-maximise");
        _maximise.ToolTip = Loc.GetString(maximised ? "wf-cockpit-restore-help" : "wf-cockpit-maximise-help");
        _swap.Disabled = maximised;
        RefreshExpand();
        InvalidateMeasure();
    }
}
