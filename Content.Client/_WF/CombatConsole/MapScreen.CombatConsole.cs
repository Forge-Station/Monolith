using Content.Client._WF.CombatConsole;
using Content.Shared.Shuttles.Systems;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class MapScreen
{
    /// <summary>Separates the strategic plot, jump controls and destination register.</summary>
    public void WfRefitInstruments()
    {
        var plot = Scope("wf-console-strategic-scope", MapRadar);
        FTLBar.Visible = false;
        var progress = new WFGlassGauge("wf-gauge-ftl", () => WFGaugeReading.Number(
            _shuttleEntity == null || _state == FTLState.Invalid ? null : FTLBar.Value * 100, 0, 100, "wf-gauge-unit-percent"), true);
        var jump = Panel("wf-console-drive", Column(_wfDriveNotice, new WFGlassReadout(MapFTLState), progress, FTLBar,
            MapFTLButton, MapAutopilotButton, MapRebuildButton, MapBeaconsButton));
        var destinations = Panel("wf-console-destinations", Scroll(HyperspaceDestinations), true);
        var side = Column(jump, destinations);
        side.MinWidth = side.MaxWidth = 260;
        side.VerticalExpand = true;
        DisposeAllChildren();
        SeparationOverride = 8;
        AddChild(plot);
        AddChild(side);
    }
}
