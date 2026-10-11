using System.Numerics;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.Shared._CE.ZLevels.Core.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Physics.Components;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class NavScreen
{
    /// <summary>Builds compact passive instruments while retaining the helm's existing data sources.</summary>
    public (Control Instruments, Control Translation) WfCockpitInstruments(WFCockpitLease lease)
    {
        var heading = new WFHeadingInstrument(() =>
            _entManager.TryGetComponent(_shuttleEntity, out TransformComponent? transform)
                ? (-_xformSystem.GetWorldRotation(transform)).Reduced().FlipPositive().Degrees : null)
            { Name = "CockpitHeading", MinHeight = 106, SetHeight = 106 };
        var velocity = new WFVelocityVectorInstrument(() =>
            _entManager.TryGetComponent(_shuttleEntity, out PhysicsComponent? physics) &&
            _entManager.TryGetComponent(_shuttleEntity, out TransformComponent? transform)
                ? WFCockpitVelocityReading.FromWorld(physics.LinearVelocity, _xformSystem.GetWorldRotation(transform)) : null)
            { Name = "CockpitVelocity" };
        var yaw = new WFGlassGauge("wf-gauge-yaw", () => WfMotionReading(2))
            { Name = "CockpitYaw", MinWidth = 88, SetHeight = 100, CaptionInset = 8 };
        WFCockpitInstrumentSizing.Bind(heading, lease, 176);
        WFCockpitInstrumentSizing.Bind(velocity, lease, 160);
        WFCockpitInstrumentSizing.Bind(yaw, lease, 160);
        var forward = new WFGlassGauge("wf-gauge-forward", () => WfMotionReading(1), true)
            { Name = "CockpitForward", SetHeight = 44, CompactStrip = true };
        var lateral = new WFGlassGauge("wf-gauge-lateral", () => WfMotionReading(0), true)
            { Name = "CockpitLateral", SetHeight = 44, CompactStrip = true };
        var coordinates = new WFCockpitCoordinates(WfCockpitPosition) { Name = "CockpitCoordinates" };
        var altitude = new WFGlassGauge("wf-gauge-altitude", () =>
        {
            double? value = GridAltitude.Visible && _shuttleEntity is { } shuttle ? _zLevels.GetAbsoluteAltitude(shuttle) : null;
            var limit = WFGaugeScale.Ceiling(Math.Abs(value ?? 0), 10);
            return WFGaugeReading.Number(value, -limit, limit, "wf-gauge-unit-altitude", 2);
        }, true) { Name = "CockpitAltitude", SetHeight = 44, CompactStrip = true, Visible = GridAltitude.Visible };
        var climb = new WFGlassGauge("wf-gauge-climb", () => WFGaugeReading.Number(
            GridVerticalVelocity.Visible && _entManager.TryGetComponent(_shuttleEntity, out CEZPhysicsComponent? physics)
                ? physics.Velocity : null, -10, 10, "wf-gauge-unit-climb", 2), true)
            { Name = "CockpitClimb", SetHeight = 44, CompactStrip = true, Visible = GridVerticalVelocity.Visible };
        var vertical = Row(altitude, climb);
        vertical.Visible = GridAltitude.Visible;
        Action<Control> updateAltitude = control => altitude.Visible = vertical.Visible = control.Visible;
        Action<Control> updateClimb = control => climb.Visible = control.Visible;
        GridAltitude.OnVisibilityChanged += updateAltitude;
        GridVerticalVelocity.OnVisibilityChanged += updateClimb;
        lease.Remember(() =>
        {
            GridAltitude.OnVisibilityChanged -= updateAltitude;
            GridVerticalVelocity.OnVisibilityChanged -= updateClimb;
        });
        var motion = Row(velocity, yaw);
        motion.Name = "CockpitMotionDials";
        motion.SeparationOverride = 12;
        var instruments = Column(heading, motion);
        instruments.SeparationOverride = 3;
        return (instruments, Column(Row(forward, coordinates, lateral), vertical));
    }

    /// <summary>The ship's centre of mass in world coordinates, which is the position the helm lists.</summary>
    private Vector2? WfCockpitPosition()
    {
        if (!_entManager.TryGetComponent(_shuttleEntity, out TransformComponent? transform))
            return null;
        return _entManager.TryGetComponent(_shuttleEntity, out PhysicsComponent? body)
            ? Vector2.Transform(body.LocalCenter, _xformSystem.GetWorldMatrix(transform))
            : _xformSystem.GetWorldPosition(transform);
    }

    /// <summary>Refreshes contextual flight data when the original navigation panel is hidden.</summary>
    public void WfCockpitRefresh()
    {
        if (_entManager.TryGetComponent(_shuttleEntity, out TransformComponent? transform))
        {
            UpdateAltitude(transform);
            var position = WfCockpitPosition() ?? Vector2.Zero;
            GridPosition.Text = Loc.GetString("shuttle-console-position-value", ("X", $"{position.X:0.0}"), ("Y", $"{position.Y:0.0}"));
        }
        else
            GridAltitude.Visible = GridVerticalVelocity.Visible = GridTravelState.Visible = false;
    }

    /// <summary>Moves propulsion controls into the cockpit's permanent lower bank.</summary>
    public Control WfCockpitFlight(WFCockpitLease lease)
    {
        return Column(lease.Take(DampenerModeButtons, restoreVisibility: false), lease.Take(WfOrbitButton));
    }

    /// <summary>Moves the live radar into the navigation MFD.</summary>
    public Control WfCockpitRadar(WFCockpitLease lease)
    {
        var radar = lease.Take(NavRadar);
        radar.WfCockpitInteraction(lease);
        radar.VerticalExpand = true;
        return ScopeLayers(radar, Vector2.Zero);
    }

    /// <summary>Keeps sensor configuration and wired auxiliary controls available without another window.</summary>
    public Control WfCockpitSystems(WFCockpitLease lease)
    {
        var speedLabel = (Label) MaximumShuttleSpeedBox.GetChild(0);
        var caption = speedLabel.Text;
        lease.Remember(() => speedLabel.Text = caption);
        speedLabel.Text = Loc.GetString("wf-cockpit-speed-limit");
        var dimension = NetworkPortsBox.LimitedDimension;
        var rows = NetworkPortsBox.Rows;
        var columns = NetworkPortsBox.Columns;
        lease.Remember(() =>
        {
            if (dimension == Dimension.Row)
                NetworkPortsBox.Rows = rows;
            else
                NetworkPortsBox.Columns = columns;
        });
        NetworkPortsBox.Columns = 2;
        return Column(new WFGlassReadout(Column(lease.Take(ShuttleDesignation), lease.Take(GridPosition))),
            lease.Take(MaximumShuttleSpeedBox), lease.Take(IFFToggle), lease.Take(IFFDetailedToggle), lease.Take(DockToggle), lease.Take(IffSearchBox),
            lease.Take(MaximumIFFDistanceBox), lease.Take(NetworkPortsBox));
    }
}
