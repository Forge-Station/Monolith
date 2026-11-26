using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class DockingScreen
{
    /// <summary>Places docking interlocks and ports beside a full-height approach display.</summary>
    public void WfRefitInstruments()
    {
        var approach = Scope("wf-console-docking-scope", DockingControl);
        var locks = Panel("wf-console-interlocks", Column(Row(FTLLockEnabledButton, FTLLockDisabledButton), UndockAllButton));
        var ports = Panel("wf-console-docking-ports", Scroll(DockPorts), true);
        var side = Column(locks, ports);
        side.MinWidth = side.MaxWidth = 260;
        side.VerticalExpand = true;
        DisposeAllChildren();
        SeparationOverride = 8;
        AddChild(approach);
        AddChild(side);
    }
}
