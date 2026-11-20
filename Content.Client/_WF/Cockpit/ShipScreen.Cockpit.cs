using System.Linq;
using System.Numerics;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Shuttles.UI;

public sealed partial class ShipScreen
{
    /// <summary>Keeps hull integrity below the flight dials.</summary>
    public Control WfCockpitHull() => new WFGlassGauge("wf-cockpit-hull", () => WFGaugeReading.Number(
        _wfHull?.HullIntegrity * 100, 0, 100, "wf-gauge-unit-percent", decimals: 1,
        tint: _wfHull?.HullIntegrity < 0.5f ? Red : Green), true) { Name = "CockpitHull", SetHeight = 44, CompactStrip = true };

    /// <summary>Provides an independent reserve strip that stays fixed below the scrolling dials.</summary>
    public Control WfCockpitFuel() => new WFCockpitFuelBank(() => _wfHull?.Fuel);

    /// <summary>Uses extra MFD width for telemetry and overlays beside the live hull display.</summary>
    public Control WfCockpitShip(WFCockpitLease lease)
    {
        var departments = WFCockpitLease.Descendants(this).OfType<CheckBox>().Single(toggle => toggle.Name == "DepartmentToggle");
        var gauges = WFCockpitLease.Descendants(this).OfType<WFGlassGauge>().ToArray();
        lease.Take(this);
        lease.Clear(this);
        // The helm centres these captions on its keys; the MFD keeps them left-aligned.
        foreach (var toggle in new[] { DamageToggle, FireToggle, PressureToggle, PowerToggle, departments })
        {
            toggle.TextAlign = Robust.Client.UserInterface.Controls.Label.AlignMode.Left;
            lease.Remember(() => toggle.TextAlign = Robust.Client.UserInterface.Controls.Label.AlignMode.Center);
        }
        var telemetry = new GridContainer { Name = "CockpitShipTelemetry", Columns = 2, HorizontalExpand = true };
        foreach (var gauge in gauges)
        {
            lease.Take(gauge);
            gauge.MinWidth = 88;
            WFCockpitInstrumentSizing.Bind(gauge, lease, 160);
            telemetry.AddChild(gauge);
        }
        Orientation = LayoutOrientation.Vertical;
        ShipView.WfCockpitInteraction(lease);
        var plot = Scope("wf-console-hull-scope", lease.Take(ShipView), Vector2.Zero);
        var overlays = new GridContainer { Name = "CockpitShipOverlays", Columns = 4, HorizontalExpand = true };
        foreach (var control in new Control[] { DamageToggle, FireToggle, PressureToggle, PowerToggle })
            overlays.AddChild(lease.Take(control));
        var mapControls = Row(lease.Take(departments), lease.Take(FitButton));
        mapControls.Name = "CockpitShipMapControls";
        var details = Column(new WFGlassReadout(lease.Take(ShipNameLabel)), telemetry, overlays, mapControls, lease.Take(TruncatedLabel, restoreVisibility: false));
        AddChild(new WFCockpitShipLayout(plot, WFCockpitMfdLayout.Details(details)));
        return this;
    }

    /// <summary>Moves announcements and alarm controls into their own MFD page.</summary>
    public Control WfCockpitAnnouncements(WFCockpitLease lease) => WFCockpitMfdLayout.Details(lease.Take(AlarmPanel));
}
