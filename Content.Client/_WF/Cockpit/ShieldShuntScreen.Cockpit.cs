using System.Numerics;
using System.Linq;
using Content.Client._WF.CombatConsole;
using Content.Shared._WF.ShipShields;
using Content.Client._WF.Cockpit;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using InstrumentTheme = Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.ShipShields;

public sealed partial class WFShipShieldShuntScreen
{
    private Action<WFShipShieldGeneratorStats>? _wfCockpitShowStats;
    private bool _wfCockpitCompact;
    /// <summary>Retains the live shield dial, deployment and power slider in the permanent HUD.</summary>
    public Control WfCockpitShield(WFCockpitLease lease)
    {
        lease.Take(this);
        lease.Clear(this);
        Visible = true;
        var dial = lease.Take(_dial);
        dial.SetSize = new Vector2(96);
        dial.HorizontalExpand = false;
        var allocation = lease.Take(_amount);
        allocation.Visible = true;
        var arc = lease.Take(_arc);
        var originalName = arc.Name;
        arc.Name = "CockpitShieldArc";
        _wfCockpitCompact = true;
        lease.Remember(() =>
        {
            arc.Name = originalName;
            _wfCockpitCompact = false;
        });
        var controls = InstrumentTheme.Column(lease.Take(_enabled),
            InstrumentTheme.Row(InstrumentTheme.Label("wf-cockpit-shield-arc"), arc),
            allocation, lease.Take(_concentration), lease.Take(_health));
        controls.HorizontalExpand = true;
        // The thin shield panel is sized from this column, so it fixes the narrowest usable power slider.
        controls.MinWidth = 150;
        _health.MinHeight = 12;
        AddChild(InstrumentTheme.Row(dial, controls));
        return this;
    }

    /// <summary>Exposes detailed allocation, arc, recovery and generator statistics in the MFD.</summary>
    public Control WfCockpitShieldDetails(WFCockpitLease lease)
    {
        var telemetry = InstrumentTheme.Column();
        telemetry.HorizontalExpand = true;
        var dials = new GridContainer { Columns = 2, HorizontalExpand = true };
        foreach (var gauge in WFCockpitLease.Descendants(_settings).OfType<WFGlassGauge>().ToArray())
        {
            lease.Take(gauge);
            gauge.MinWidth = 88;
            if (gauge.Strip)
            {
                gauge.SetHeight = 64;
                telemetry.AddChild(gauge);
            }
            else
            {
                WFCockpitInstrumentSizing.Bind(gauge, lease, 160);
                dials.AddChild(gauge);
            }
        }
        telemetry.AddChild(dials);
        var stats = new WFShipShieldStatsWindow();
        var specifications = stats.Contents.GetChild(0).GetChild(0);
        specifications.Parent!.RemoveChild(specifications);
        specifications.Visible = false;
        _wfCockpitShowStats = data =>
        {
            stats.UpdateStats(data);
            specifications.Visible = !specifications.Visible;
        };
        lease.Remember(() =>
        {
            _wfCockpitShowStats = null;
            stats.Dispose();
        });
        return WFCockpitMfdLayout.Details(InstrumentTheme.Column(telemetry, InstrumentTheme.Label("wf-console-shield-vector"),
            InstrumentTheme.Label("wf-shield-helm-bearing"), lease.Take(_direction),
            lease.Take(_stats), lease.Take(_status), lease.Take(_recovery, restoreVisibility: false),
            lease.Take(_draft), lease.Take(_reset), lease.Take(_unprotected, restoreVisibility: false), specifications));
    }
}
