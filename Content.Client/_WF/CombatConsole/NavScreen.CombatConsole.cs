using Robust.Client.UserInterface.Controls;
using Robust.Shared.Physics.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.Shared._WF.Shuttles;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class NavScreen
{
    /// <summary>Housing padding for the side banks, tight enough that each column fits the page without scrolling.</summary>
    private const int WfPanelPadding = 8;

    /// <summary>Space between a bank's title and its controls, and between the controls, in the side banks.</summary>
    private const int WfPanelGap = 4;

    /// <summary>The ship's latest fuel reserves, or null while the helm has no telemetry; the console window keeps it current.</summary>
    public ShipFuelSummary? WfFuel { get; set; }

    /// <summary>Arranges flight telemetry, radar and navigation switches in separate instrument banks; the cockpit key ends the left stack.</summary>
    public void WfRefitInstruments(Button? cockpit = null)
    {
        ReadonlyDisplay.Margin = new Thickness(0);
        NavDisplayLabel.MinHeight = 0;
        NavDisplayLabel.VerticalExpand = false;
        NavDisplayLabel.HorizontalExpand = true;
        NavDisplayLabel.HorizontalAlignment = HAlignment.Stretch;
        NavDisplayLabel.ClipText = true;
        ReadonlyDisplay.Visible = false;
        var speed = new WFGlassGauge("wf-gauge-speed", () => WfMotionReading(-1));
        var yaw = new WFGlassGauge("wf-gauge-yaw", () => WfMotionReading(2));
        var lateral = new WFGlassGauge("wf-gauge-lateral", () => WfMotionReading(0), true) { SetHeight = 44, CompactStrip = true };
        var forward = new WFGlassGauge("wf-gauge-forward", () => WfMotionReading(1), true) { SetHeight = 44, CompactStrip = true };
        var altitude = new WFGlassGauge("wf-gauge-altitude", () =>
        {
            double? value = GridAltitude.Visible && _entManager.TryGetComponent(_shuttleEntity, out TransformComponent? _) && _shuttleEntity is { } shuttle ? _zLevels.GetAbsoluteAltitude(shuttle) : null;
            var limit = WFGaugeScale.Ceiling(Math.Abs(value ?? 0), 10);
            return WFGaugeReading.Number(value, -limit, limit, "wf-gauge-unit-altitude", 2);
        }, true) { SetHeight = 44, CompactStrip = true };
        var climb = new WFGlassGauge("wf-gauge-climb", () =>
        {
            double? value = GridVerticalVelocity.Visible && _entManager.TryGetComponent(_shuttleEntity, out CEZPhysicsComponent? physics)
                ? physics.Velocity : null;
            var limit = WFGaugeScale.Ceiling(Math.Abs(value ?? 0), 10);
            return WFGaugeReading.Number(value, -limit, limit, "wf-gauge-unit-climb", 2);
        }, true) { SetHeight = 44, CompactStrip = true };
        altitude.Name = "WfAltitudeGauge";
        climb.Name = "WfClimbGauge";
        altitude.Visible = GridAltitude.Visible;
        climb.Visible = GridVerticalVelocity.Visible;
        var vertical = Row(altitude, climb);
        vertical.Visible = altitude.Visible || climb.Visible;
        GridAltitude.OnVisibilityChanged += control =>
        {
            altitude.Visible = control.Visible;
            vertical.Visible = altitude.Visible || climb.Visible;
        };
        GridVerticalVelocity.OnVisibilityChanged += control =>
        {
            climb.Visible = control.Visible;
            vertical.Visible = altitude.Visible || climb.Visible;
        };
        var travel = new WFGlassReadout(GridTravelState) { Visible = GridTravelState.Visible };
        GridTravelState.OnVisibilityChanged += control => travel.Visible = control.Visible;
        ShuttleDesignation.ClipText = GridPosition.ClipText = GridTravelState.ClipText = true;
        var identity = Column(NavDisplayLabel, ShuttleDesignation);
        identity.SeparationOverride = 2;
        var position = Label("wf-console-coordinates", Muted);
        position.Margin = new Thickness(0);
        var coordinateBody = Column(position, GridPosition);
        coordinateBody.SeparationOverride = 0;
        var coordinates = new WFGlassReadout(coordinateBody) { Name = "WfCoordinates" };
        var fuel = new WFCockpitFuelBank(() => WfFuel, "Nav");
        var instruments = Column(new WFGlassReadout(identity), Row(speed, yaw), forward, coordinates, lateral,
            vertical, travel, fuel, ReadonlyDisplay);
        instruments.SeparationOverride = WfPanelGap;
        var telemetry = Panel("wf-console-telemetry", instruments, padding: WfPanelPadding, gap: 2);
        MaximumShuttleSpeedBox.Margin = IffSearchBox.Margin = MaximumIFFDistanceBox.Margin = new Thickness(0);
        var motionControls = Column(DampenerModeButtons, MaximumShuttleSpeedBox, WfOrbitButton);
        motionControls.SeparationOverride = WfPanelGap;
        var motion = Panel("wf-console-motion", motionControls, padding: WfPanelPadding, gap: 2);
        var gyro = Panel("wf-console-bearing", new WFHeadingInstrument(() =>
            _entManager.TryGetComponent(_shuttleEntity, out TransformComponent? transform)
                ? (-_xformSystem.GetWorldRotation(transform)).Reduced().FlipPositive().Degrees
                : null) { MinHeight = 148 }, padding: WfPanelPadding, gap: 2);
        var left = cockpit == null ? Column(gyro, telemetry) : Column(gyro, telemetry, cockpit);
        left.Name = "WfNavLeftStack";
        left.SeparationOverride = WfPanelGap;
        left.MinWidth = left.MaxWidth = 280;

        foreach (var control in ReadonlyDisplay.Children)
        {
            if (control is Label { HorizontalExpand: true } value)
                value.ClipText = true;
        }
        var rangeInput = MaximumIFFDistanceValue.GetChild(0).GetChild(1);
        rangeInput.MinWidth = 72;
        rangeInput.HorizontalExpand = false;
        var sensorControls = Column(IFFToggle, IFFDetailedToggle, DockToggle, IffSearchBox, MaximumIFFDistanceBox);
        sensorControls.SeparationOverride = WfPanelGap;
        var sensors = Panel("wf-console-sensors", sensorControls, padding: WfPanelPadding, gap: 2);
        NetworkPortsBox.Columns = 2;
        NetworkPortsBox.Rows = 4;
        NetworkPortsBox.HorizontalAlignment = HAlignment.Stretch;
        NetworkPortsBox.Margin = new Thickness(0);
        foreach (var control in NetworkPortsBox.Children)
        {
            control.SetWidth = 110;
            control.HorizontalExpand = true;
        }
        var right = Column(motion, sensors, Panel("wf-console-auxiliary", NetworkPortsBox, padding: WfPanelPadding, gap: 2));
        right.SeparationOverride = WfPanelGap;
        right.MinWidth = right.MaxWidth = 260;
        var radar = Scope("wf-console-navigation-scope", NavRadar);
        var leftScroll = Scroll(left);
        leftScroll.HorizontalExpand = false;
        leftScroll.SetWidth = 280;
        var rightScroll = Scroll(right);
        rightScroll.HorizontalExpand = false;
        rightScroll.SetWidth = 260;
        DisposeAllChildren();
        SeparationOverride = 8;
        AddChild(leftScroll);
        AddChild(radar);
        AddChild(rightScroll);
    }

    private WFGaugeReading WfMotionReading(int axis)
    {
        double? value = null;
        if (_entManager.TryGetComponent(_shuttleEntity, out PhysicsComponent? physics) &&
            _entManager.TryGetComponent(_shuttleEntity, out TransformComponent? transform))
        {
            var velocity = (-_xformSystem.GetWorldRotation(transform)).RotateVec(physics.LinearVelocity);
            value = axis switch { -1 => velocity.Length(), 0 => velocity.X, 1 => velocity.Y,
                _ => -MathHelper.RadiansToDegrees(physics.AngularVelocity) };
        }
        var limit = WFGaugeScale.Ceiling(Math.Abs(value ?? 0), axis == 2 ? 90 : 100);
        return WFGaugeReading.Number(value, axis == -1 ? 0 : -limit, limit,
            axis == 2 ? "wf-gauge-unit-turn" : "wf-gauge-unit-speed", 1);
    }
}
