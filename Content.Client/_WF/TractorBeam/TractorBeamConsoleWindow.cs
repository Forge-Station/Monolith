using System.Linq;
using System.Numerics;
using Content.Shared._WF.TractorBeam;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WF.TractorBeam;

public sealed class TractorBeamConsoleWindow : DefaultWindow
{
    public event Action<NetEntity, NetEntity?, bool, bool, float?>? OnCommand;

    private readonly TractorBeamRadarControl _radar;
    private readonly OptionButton _emitters;
    private readonly OptionButton _targets;
    private readonly Label _connection;
    private readonly Label _targetDetails;
    private readonly Label _targetConeStatus;
    private readonly Label _shieldWarning;
    private readonly Label _powerWarning;
    private readonly Label _lockWarning;
    private readonly Label _status;
    private readonly Label _strain;
    private readonly Label _power;
    private readonly ProgressBar _strainBar;
    private readonly Button _lock;
    private readonly Button _setRange;
    private readonly FloatSpinBox _desiredRange;
    private readonly Label _rangeLimits;
    private readonly Label _distance;
    private readonly Button _lockInPlace;
    private readonly Button _release;
    private TractorBeamConsoleBoundUserInterfaceState? _state;
    private NetEntity? _selectedEmitter;
    private NetEntity? _selectedTarget;
    private NetEntity? _rangeEmitter;
    private NetEntity? _rangeTarget;

    public TractorBeamConsoleWindow()
    {
        Title = Loc.GetString("tractor-beam-console-title");
        SetSize = new Vector2(840, 480);
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        Contents.AddChild(root);
        root.AddChild(_connection = new Label());
        var body = new BoxContainer { HorizontalExpand = true, VerticalExpand = true, SeparationOverride = 12 };
        root.AddChild(body);
        body.AddChild(_radar = new TractorBeamRadarControl());
        var controls = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            MinWidth = 340,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };
        body.AddChild(controls);
        var emitterRow = new BoxContainer { SeparationOverride = 8 };
        controls.AddChild(emitterRow);
        emitterRow.AddChild(new Label { Text = Loc.GetString("tractor-beam-console-emitter"), MinWidth = 90 });
        emitterRow.AddChild(_emitters = new OptionButton { Name = "Emitters", HorizontalExpand = true, MaxWidth = 300, RectClipContent = true });
        var targetRow = new BoxContainer { SeparationOverride = 8 };
        controls.AddChild(targetRow);
        targetRow.AddChild(new Label { Text = Loc.GetString("tractor-beam-console-target"), MinWidth = 90 });
        targetRow.AddChild(_targets = new OptionButton { Name = "Targets", HorizontalExpand = true, MaxWidth = 300, RectClipContent = true });
        controls.AddChild(_status = new Label { ClipText = true });
        controls.AddChild(_lockWarning = new Label { ClipText = true });
        controls.AddChild(_targetConeStatus = new Label { ClipText = true });
        controls.AddChild(_shieldWarning = new Label
        {
            Name = "ShieldWarning",
            Text = Loc.GetString("tractor-beam-target-shielded"),
            FontColorOverride = Color.Red,
        });
        controls.AddChild(_powerWarning = new Label
        {
            Name = "PowerWarning",
            Text = Loc.GetString("tractor-beam-console-power-shortfall"),
            FontColorOverride = Color.Red,
        });

        // Keep the commands above the scrollable telemetry. Neither long ship names nor a
        // short screen should push capture controls below the bottom edge of the window.
        var buttons = new BoxContainer { SeparationOverride = 8 };
        controls.AddChild(buttons);
        buttons.AddChild(_lock = new Button { Name = "HoldTarget", Text = Loc.GetString("tractor-beam-console-lock"), HorizontalExpand = true });
        buttons.AddChild(_lockInPlace = new Button { Name = "LockInPlace", Text = Loc.GetString("tractor-beam-console-lock-in-place"), HorizontalExpand = true });
        buttons.AddChild(_release = new Button { Name = "Release", Text = Loc.GetString("tractor-beam-console-release"), HorizontalExpand = true });
        var rangeControls = new BoxContainer { SeparationOverride = 8 };
        controls.AddChild(rangeControls);
        rangeControls.AddChild(new Label { Text = Loc.GetString("tractor-beam-console-desired-range") });
        rangeControls.AddChild(_desiredRange = new FloatSpinBox(1f, 1)
        {
            Name = "DesiredRange",
            HorizontalExpand = true,
            IsValid = value => float.IsFinite(value) && value > 0,
        });
        rangeControls.AddChild(_setRange = new Button { Name = "SetRange", Text = Loc.GetString("tractor-beam-console-set-range") });
        controls.AddChild(_rangeLimits = new Label { ClipText = true });
        controls.AddChild(_distance = new Label { ClipText = true });
        controls.AddChild(_strain = new Label());
        controls.AddChild(_strainBar = new ProgressBar { MinValue = 0, MaxValue = 1, MinHeight = 16 });
        controls.AddChild(_power = new Label { ClipText = true });
        var telemetryScroll = new ScrollContainer { Name = "TelemetryScroll", HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
        controls.AddChild(telemetryScroll);
        var telemetry = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6, HorizontalExpand = true };
        telemetryScroll.AddChild(telemetry);
        telemetry.AddChild(_targetDetails = new Label { ClipText = true });

        _emitters.OnItemSelected += args =>
        {
            if (_state == null || args.Id < 0 || args.Id >= _state.Emitters.Length)
                return;
            _selectedEmitter = _state.Emitters[args.Id].Entity;
            _emitters.SelectId(args.Id);
            RestoreCapturedTarget();
            RefreshDetails();
        };
        _targets.OnItemSelected += args =>
        {
            if (_state == null || args.Id < 0 || args.Id > _state.Targets.Length)
                return;
            _selectedTarget = args.Id == 0 ? null : _state.Targets[args.Id - 1].Entity;
            _targets.SelectId(args.Id);
            RefreshDetails();
        };
        _radar.OnTargetSelected += entity =>
        {
            _selectedTarget = entity;
            if (_state != null)
                _targets.SelectId(Array.FindIndex(_state.Targets, entry => entry.Entity == entity) + 1);
            RefreshDetails();
        };
        _lock.OnPressed += _ =>
        {
            if (_selectedEmitter is { } emitter && _selectedTarget is { } target)
                OnCommand?.Invoke(emitter, target, false, false, null);
        };
        _desiredRange.OnValueChanged += _ => RefreshDetails();
        _setRange.OnPressed += _ =>
        {
            if (_selectedEmitter is { } emitter && _selectedTarget is { } target)
                OnCommand?.Invoke(emitter, target, true, false, _desiredRange.Value);
        };
        _lockInPlace.OnPressed += _ =>
        {
            if (_selectedEmitter is { } emitter && _selectedTarget is { } target)
                OnCommand?.Invoke(emitter, target, false, true, null);
        };
        _release.OnPressed += _ =>
        {
            if (_selectedEmitter is { } emitter)
                OnCommand?.Invoke(emitter, null, false, false, null);
        };
        RefreshDetails();
    }

    public void UpdateState(TractorBeamConsoleBoundUserInterfaceState state)
    {
        // Keep open dropdowns intact during routine power and contact-position updates.
        var emittersChanged = _state == null || !_state.Emitters.Select(entry => (entry.Entity, entry.Name))
            .SequenceEqual(state.Emitters.Select(entry => (entry.Entity, entry.Name)));
        var targetsChanged = _state == null || !_state.Targets.Select(entry => (entry.Entity, entry.Name))
            .SequenceEqual(state.Targets.Select(entry => (entry.Entity, entry.Name)));
        _state = state;
        if (emittersChanged)
        {
            _emitters.Clear();
            for (var i = 0; i < state.Emitters.Length; i++)
                _emitters.AddItem($"{i + 1}. {state.Emitters[i].Name}", i);
            if (state.Emitters.Length == 0)
            {
                _emitters.AddItem(Loc.GetString("tractor-beam-console-no-emitters"), 0);
                _emitters.SelectId(0);
            }
        }
        var previousEmitter = _selectedEmitter;
        if (!state.Emitters.Any(entry => entry.Entity == _selectedEmitter))
            _selectedEmitter = state.Emitters.Length > 0 ? state.Emitters[0].Entity : null;
        if (_selectedEmitter is { } emitter)
            _emitters.SelectId(Array.FindIndex(state.Emitters, entry => entry.Entity == emitter));
        _emitters.Disabled = state.Emitters.Length == 0;

        if (targetsChanged)
        {
            _targets.Clear();
            _targets.AddItem(Loc.GetString("tractor-beam-console-no-target"), 0);
            for (var i = 0; i < state.Targets.Length; i++)
                _targets.AddItem(state.Targets[i].Name, i + 1);
        }
        var lostTarget = _selectedTarget != null && !state.Targets.Any(entry => entry.Entity == _selectedTarget);
        if (lostTarget)
            _selectedTarget = null;
        if (previousEmitter != _selectedEmitter || lostTarget)
            RestoreCapturedTarget();
        _targets.SelectId(Array.FindIndex(state.Targets, entry => entry.Entity == _selectedTarget) + 1);
        _radar.Targets = state.Targets;
        _radar.Range = state.Range;
        RefreshDetails();
    }

    private void RestoreCapturedTarget()
    {
        if (_state == null)
            return;

        var captured = _state.Emitters.FirstOrDefault(entry => entry.Entity == _selectedEmitter).Target;
        if (captured != null && _state.Targets.Any(entry => entry.Entity == captured))
            _selectedTarget = captured;
        _targets.SelectId(Array.FindIndex(_state.Targets, entry => entry.Entity == _selectedTarget) + 1);
    }

    private void RefreshDetails()
    {
        var emitter = _state?.Emitters.FirstOrDefault(entry => entry.Entity == _selectedEmitter);
        var target = _state?.Targets.FirstOrDefault(entry => entry.Entity == _selectedTarget);
        var hasEmitter = _selectedEmitter != null;
        var connected = _state?.Connected == true;
        var locked = hasEmitter ? emitter?.Target : null;
        var strain = hasEmitter ? Math.Clamp(emitter?.Strain ?? 0, 0, 1) : 0;
        _connection.Visible = !connected;
        _connection.Text = Loc.GetString(connected ? "tractor-beam-console-connected" : "tractor-beam-console-disconnected");
        _status.Text = Loc.GetString(!hasEmitter ? "tractor-beam-console-no-emitters" :
            emitter?.Powered != true ? "tractor-beam-console-unpowered" :
            locked != null ? (emitter?.LockedInPlace == true ? "tractor-beam-console-locked-in-place" :
                emitter?.Pulling == true ? "tractor-beam-console-pulling" : "tractor-beam-console-locked") :
            "tractor-beam-console-idle");
        if (locked is { } lockedEntity)
        {
            if (emitter?.Pulling == true && emitter?.DesiredRange is { } pullingRange)
                _status.Text = Loc.GetString("tractor-beam-console-pulling-range", ("range", pullingRange.ToString("N1")));
            var lockedName = _state?.Targets.FirstOrDefault(entry => entry.Entity == lockedEntity).Name;
            _status.Text += $": {lockedName ?? Loc.GetString("tractor-beam-console-unknown-target")}";
        }
        _status.ToolTip = _status.Text;
        if (emitter is { CooldownRemaining: > 0 } cooling)
        {
            _status.Text = Loc.GetString("tractor-beam-console-cooldown", ("seconds", MathF.Ceiling(cooling.CooldownRemaining)));
            _status.ToolTip = _status.Text;
        }
        _lockWarning.Visible = false;
        if (emitter is { } lockedDish && locked is { } activeTarget && _state != null)
        {
            var lockedContact = _state.Targets.FirstOrDefault(entry => entry.Entity == activeTarget);
            if (lockedContact.Entity == activeTarget)
            {
                var offset = lockedContact.Position - lockedDish.Position;
                var inside = TractorBeamOperatingCone.Contains(offset, lockedDish.Direction, lockedDish.Range, lockedDish.ConeHalfAngle);
                var nearEdge = TractorBeamOperatingCone.IsNearEdge(offset, lockedDish.Direction, lockedDish.Range, lockedDish.ConeHalfAngle);
                _lockWarning.Visible = !inside || nearEdge;
                _lockWarning.Text = Loc.GetString(inside ? "tractor-beam-console-lock-edge" : "tractor-beam-console-lock-outside");
                _lockWarning.FontColorOverride = inside ? Color.Orange : Color.Red;
            }
        }
        _radar.ToolTip = Loc.GetString("tractor-beam-console-range", ("range", (emitter?.Range ?? 0).ToString("N0")),
            ("angle", ((emitter?.ConeHalfAngle ?? 0) * 360 / MathF.PI).ToString("N0")));
        _targetDetails.Visible = _selectedTarget != null;
        _targetDetails.Text = _selectedTarget == null ? Loc.GetString("tractor-beam-console-select-target") :
            Loc.GetString("tractor-beam-console-target-details", ("distance", ((target?.Position - emitter?.Position)?.Length() ?? 0).ToString("N0")),
                ("mass", (target?.Mass ?? 0).ToString("N0")));
        var insideCone = emitter is { } selectedDish && target is { } selectedContact && _selectedTarget != null &&
            TractorBeamOperatingCone.Contains(selectedContact.Position - selectedDish.Position, selectedDish.Direction,
                selectedDish.Range, selectedDish.ConeHalfAngle);
        var targetNearEdge = insideCone && emitter is { } edgeDish && target is { } edgeContact &&
            TractorBeamOperatingCone.IsNearEdge(edgeContact.Position - edgeDish.Position, edgeDish.Direction,
                edgeDish.Range, edgeDish.ConeHalfAngle);
        _targetConeStatus.Visible = hasEmitter && _selectedTarget != null && locked != _selectedTarget && (!insideCone || targetNearEdge);
        _targetConeStatus.Text = Loc.GetString(!insideCone ? "tractor-beam-console-target-outside" :
            targetNearEdge ? "tractor-beam-console-target-edge" : "tractor-beam-console-target-inside");
        _targetConeStatus.FontColorOverride = !insideCone ? Color.Red : targetNearEdge ? Color.Orange : Color.LightGreen;
        _strain.Text = Loc.GetString("tractor-beam-console-strain", ("strain", (strain * 100).ToString("N0")));
        _strainBar.Value = strain;
        _power.Text = Loc.GetString("tractor-beam-console-power", ("received", ((emitter?.ReceivedPower ?? 0) / 1000).ToString("N1")),
            ("requested", ((emitter?.RequestedPower ?? 0) / 1000).ToString("N1")));
        // Allow the same small allocation lag tolerated by the pin control.
        var powerShortfall = hasEmitter && emitter is { } supplied &&
            supplied.RequestedPower > 0 && supplied.ReceivedPower * 1.01f < supplied.RequestedPower;
        // Keep the warning row measured so power fluctuations never move the controls.
        _powerWarning.FontColorOverride = powerShortfall ? Color.Red : Color.Transparent;
        _power.FontColorOverride = powerShortfall ? Color.Red : null;
        var canEngage = connected && hasEmitter && emitter?.Powered == true && insideCone && emitter?.CooldownRemaining <= 0;
        var shieldsBlockAcquisition = _selectedTarget != null && locked != _selectedTarget && target?.Shielded == true;
        _shieldWarning.Visible = hasEmitter && shieldsBlockAcquisition;
        _lock.ToolTip = shieldsBlockAcquisition ? _shieldWarning.Text : null;
        _lock.Disabled = !canEngage || shieldsBlockAcquisition ||
            (locked == _selectedTarget && emitter?.Pulling != true && emitter?.LockedInPlace != true);
        var minimumRange = emitter?.MinimumDistance ?? 0;
        var maximumRange = MathF.Min(emitter?.Range ?? 0, MathF.Min(emitter?.CurrentDistance ?? 0, emitter?.HoldDistance ?? 0));
        if (_rangeEmitter != _selectedEmitter || _rangeTarget != locked)
        {
            _rangeEmitter = _selectedEmitter;
            _rangeTarget = locked;
            // Initialize on changing dishes/captures, then preserve what the operator is typing
            // while live telemetry continues to update.
            if (locked != null)
                _desiredRange.Value = MathF.Max(MathF.Ceiling(minimumRange * 10) / 10,
                    MathF.Floor((emitter?.DesiredRange ?? maximumRange - 5f) * 10) / 10);
        }
        _distance.Visible = locked != null;
        _distance.Text = Loc.GetString("tractor-beam-console-current-range",
            ("current", (emitter?.CurrentDistance ?? 0).ToString("N1")),
            ("ordered", (emitter?.DesiredRange ?? emitter?.HoldDistance ?? 0).ToString("N1")));
        _rangeLimits.Visible = locked != null;
        _rangeLimits.Text = locked == null ? Loc.GetString("tractor-beam-console-range-needs-lock") :
            Loc.GetString("tractor-beam-console-range-limits", ("minimum", (MathF.Ceiling(minimumRange * 10) / 10).ToString("N1")),
                ("maximum", (MathF.Floor(maximumRange * 10) / 10).ToString("N1")));
        _setRange.Disabled = !canEngage || emitter?.Active != true || locked != _selectedTarget ||
            !float.IsFinite(_desiredRange.Value) || _desiredRange.Value < minimumRange || _desiredRange.Value > maximumRange;
        _lockInPlace.Disabled = !canEngage || locked != _selectedTarget || emitter?.CanLockInPlace != true || emitter?.LockedInPlace == true;
        var pinReason = !connected || !hasEmitter ? "tractor-beam-console-pin-connect" :
            locked == null ? "tractor-beam-console-pin-capture" :
            locked != _selectedTarget ? "tractor-beam-console-pin-select-capture" :
            !insideCone ? "tractor-beam-console-pin-coverage" :
            emitter?.PinStatus switch
            {
                TractorBeamPinStatus.AlreadyPinned => "tractor-beam-console-pin-active",
                TractorBeamPinStatus.StopSource => "tractor-beam-console-pin-stop-source",
                TractorBeamPinStatus.StopTarget => "tractor-beam-console-pin-stop-target",
                TractorBeamPinStatus.InsufficientPower => "tractor-beam-console-pin-power",
                TractorBeamPinStatus.WaitingForBeam => "tractor-beam-console-pin-waiting",
                TractorBeamPinStatus.Ready => "tractor-beam-console-pin-ready",
                _ => "tractor-beam-console-pin-capture",
            };
        _lockInPlace.ToolTip = Loc.GetString(pinReason);
        var rangeReason = !connected || !hasEmitter ? "tractor-beam-console-pin-connect" :
            locked == null ? "tractor-beam-console-range-needs-lock" :
            locked != _selectedTarget ? "tractor-beam-console-range-select-capture" :
            !insideCone ? "tractor-beam-console-pin-coverage" :
            emitter?.Powered != true || emitter?.Active != true ? "tractor-beam-console-range-power" :
            maximumRange < minimumRange ? "tractor-beam-console-range-no-space" :
            !float.IsFinite(_desiredRange.Value) || _desiredRange.Value < minimumRange || _desiredRange.Value > maximumRange ?
                "tractor-beam-console-range-invalid" : null;
        _setRange.ToolTip = rangeReason == null ? _rangeLimits.Text : Loc.GetString(rangeReason);
        _desiredRange.ToolTip = _setRange.ToolTip;
        _rangeLimits.ToolTip = _rangeLimits.Text;
        _release.Disabled = locked == null;
        _radar.Selected = _selectedTarget;
        _radar.Locked = locked;
        _radar.Emitter = hasEmitter ? emitter : null;
    }
}
