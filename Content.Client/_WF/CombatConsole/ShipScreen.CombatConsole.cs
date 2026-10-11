using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Client._WF.CombatConsole;
using Content.Shared._WF.Shuttles;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Shuttles.UI;

public sealed partial class ShipScreen
{
    /// <summary>Shares of the page width taken by the hull plot, the telemetry bank and the announcement column.</summary>
    private const float WfPlotShare = 0.43f;
    private const float WfStatusShare = 0.27f;
    private const float WfSideShare = 0.3f;

    private ShipStatusSummary? _wfHull;
    private int? _wfTileCount;
    /// <summary>Gives the hull plot the widest column, with telemetry beside it and the announcements and overlay toggles stacked at the right.</summary>
    public void WfRefitInstruments()
    {
        var mapControls = ShipView.WfDetachInstrumentToolbar();
        var departments = (CheckBox) mapControls.GetChild(0);
        var overlayButtons = new[] { DamageToggle, FireToggle, PressureToggle, PowerToggle, departments };
        foreach (var button in overlayButtons)
        {
            button.Margin = new Thickness(0);
            button.AddStyleClass("WfCompact");
            button.HorizontalExpand = true;
            button.TextAlign = Robust.Client.UserInterface.Controls.Label.AlignMode.Center;
        }
        FitButton.AddStyleClass("WfCompact");
        FitButton.Margin = new Thickness(0);
        FitButton.HorizontalExpand = true;
        FitButton.MinHeight = 32;
        var toggles = Column(WfToggleRow(DamageToggle, FireToggle), WfToggleRow(PressureToggle, PowerToggle),
            WfToggleRow(departments, FitButton));
        toggles.SeparationOverride = 4;
        toggles.HorizontalExpand = true;
        var overlays = Panel("wf-console-hull-overlays", toggles, false, 8, 4);
        overlays.Name = "WfHullControls";
        var plot = Column(Scope("wf-console-hull-scope", ShipView, new Vector2(208, 150)), mapControls, NoDataLabel);
        plot.Name = "WfHullPlot";
        plot.HorizontalExpand = plot.VerticalExpand = true;
        plot.SizeFlagsStretchRatio = WfPlotShare;
        NoDataLabel.HorizontalAlignment = HAlignment.Center;
        NoDataLabel.VerticalExpand = false;
        var telemetry = ShipNameLabel.Parent!;
        telemetry.Margin = new Thickness(0);
        foreach (var control in telemetry.Children)
        {
            if (control is Label { HorizontalExpand: true } value)
                value.ClipText = true;
        }
        telemetry.Visible = false;
        var gauges = new GridContainer { Columns = 2, HorizontalExpand = true, VerticalExpand = true };
        foreach (var gauge in new[]
        {
            new WFGlassGauge("wf-gauge-hull", () => WFGaugeReading.Number(_wfHull?.WorstIntegrity * 100, 0, 100,
                "wf-gauge-unit-percent", tint: _wfHull?.WorstIntegrity < 0.5f ? Red : Green)),
            new WFGlassGauge("wf-gauge-tiles", () => WFGaugeReading.Number(_wfTileCount, 0,
                WFGaugeScale.Ceiling(_wfTileCount ?? 0, 100), "wf-gauge-unit-tiles")),
            WfHullCount("wf-gauge-damaged", () => _wfHull?.DamagedTiles),
            WfHullCount("wf-gauge-fire", () => _wfHull?.FireTiles),
            WfHullCount("wf-gauge-vented", () => _wfHull?.VentedTiles),
            WfHullCount("wf-gauge-power-loss", () => _wfHull?.UnpoweredTiles),
        })
        {
            gauge.SetHeight = float.NaN;
            gauge.MinHeight = 110;
            gauge.VerticalExpand = true;
            gauges.AddChild(gauge);
        }
        var hull = Column(new WFGlassReadout(ShipNameLabel), TruncatedLabel, gauges, telemetry);
        hull.VerticalExpand = true;
        var status = Panel("wf-console-hull-status", hull, true);
        status.Name = "WfHullStatus";
        status.SizeFlagsStretchRatio = WfStatusShare;
        AlarmPanel.WfRefitInstruments();
        var announcements = Panel("wf-console-hull-announcements", AlarmPanel, true, 8, 4);
        announcements.Name = "WfHullAnnouncements";
        var side = Column(announcements, overlays);
        side.Name = "WfHullSide";
        side.HorizontalExpand = side.VerticalExpand = true;
        side.SizeFlagsStretchRatio = WfSideShare;
        var workspace = Row(plot, status, side);
        workspace.VerticalExpand = true;
        workspace.SeparationOverride = 8;
        DisposeAllChildren();
        Orientation = LayoutOrientation.Vertical;
        VerticalExpand = true;
        SeparationOverride = 8;
        AddChild(workspace);
    }

    /// <summary>Pairs two controls on one row of the overlay panel.</summary>
    private static BoxContainer WfToggleRow(Control left, Control right)
    {
        var row = Row(left, right);
        row.SeparationOverride = 4;
        return row;
    }

    private WFGlassGauge WfHullCount(string caption, Func<int?> read) => new(caption, () =>
    {
        var count = read();
        return WFGaugeReading.Number(count, 0, Math.Max(1, _wfTileCount ?? WFGaugeScale.Ceiling(count ?? 0)),
            "wf-gauge-unit-tiles", tint: count > 0 ? Red : Green);
    });
}
