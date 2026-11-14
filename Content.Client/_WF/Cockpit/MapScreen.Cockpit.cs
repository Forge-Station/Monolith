using System.Numerics;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.Shared.Shuttles.Systems;
using Robust.Client.UserInterface;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class MapScreen
{
    /// <summary>Fits strategic travel and destination selection into the expandable MFD.</summary>
    public Control WfCockpitMap(WFCockpitLease lease)
    {
        lease.Take(this);
        lease.Clear(this);
        Orientation = LayoutOrientation.Vertical;
        MapRadar.WfCockpitInteraction(lease);
        var plot = Scope("wf-console-strategic-scope", lease.Take(MapRadar), Vector2.Zero);
        var details = Column(lease.Take(MapFTLState), new WFGlassGauge("wf-gauge-ftl", () => WFGaugeReading.Number(
            _shuttleEntity == null || _state == FTLState.Invalid ? null : FTLBar.Value * 100, 0, 100, "wf-gauge-unit-percent"), true),
            lease.Take(MapFTLButton), lease.Take(MapAutopilotButton), lease.Take(MapRebuildButton),
            lease.Take(MapBeaconsButton), lease.Take(HyperspaceDestinations));
        // The drive notice sits above the plot, outside the scrolling details, so it can't be scrolled out of sight.
        AddChild(lease.Take(_wfDriveNotice, restoreVisibility: false));
        AddChild(WFCockpitMfdLayout.Split(plot, WFCockpitMfdLayout.Details(details)));
        return this;
    }
}
