using System.Numerics;
using Content.Client._WF.Cockpit;
using Robust.Client.UserInterface;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class DockingScreen
{
    /// <summary>Keeps docking, port interlocks and undocking in the cockpit MFD.</summary>
    public Control WfCockpitDock(WFCockpitLease lease)
    {
        lease.Take(this);
        lease.Clear(this);
        Orientation = LayoutOrientation.Vertical;
        DockingControl.WfCockpitInteraction(lease);
        var actions = DockingControl.WfCockpitDockActions(lease);
        var plot = Scope("wf-console-docking-scope", lease.Take(DockingControl), Vector2.Zero);
        var recenter = Button("wf-cockpit-dock-recenter");
        recenter.Name = "CockpitDockRecenter";
        recenter.OnPressed += _ => DockingControl.Offset = Vector2.Zero;
        var details = Column(recenter, actions, Row(lease.Take(FTLLockEnabledButton), lease.Take(FTLLockDisabledButton)),
            lease.Take(UndockAllButton), lease.Take(DockPorts));
        AddChild(WFCockpitMfdLayout.Split(plot, WFCockpitMfdLayout.Details(details), 0.8f));
        return this;
    }
}
