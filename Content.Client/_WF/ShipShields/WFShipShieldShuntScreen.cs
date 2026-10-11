using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Shared._WF.ShipShields;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._WF.ShipShields;

/// <summary>Controls live shield allocation bearings at the ship's helm.</summary>
public sealed partial class WFShipShieldShuntScreen : BoxContainer
{
    private readonly FloatSpinBox _direction;
    private readonly FloatSpinBox _arc;
    private readonly Slider _concentration;
    private readonly RichTextLabel _status;
    private readonly RichTextLabel _recovery;
    private readonly RichTextLabel _amount;
    private readonly RichTextLabel _strength;
    private readonly RichTextLabel _outside;
    private readonly ProgressBar _health;
    private readonly Label _previewStatus;
    private readonly BoxContainer _settings;
    private readonly RichTextLabel _draft;
    private readonly RichTextLabel _unprotected;
    private readonly Button _reset;
    private readonly ShieldDial _dial;
    private readonly Button _enabled;
    private readonly Button _stats;
    private WFShipShieldStatsWindow? _statsWindow;
    private float _warningTime;
    private WFShipShieldShuntState? _state;
    private float _helmRotation;
    private bool _updating;
    private bool _pending;
    private bool _queued;
    private float _clock;
    private float _nextSend;
    private float _nextRefresh;
    private float _lastEdit;
    private bool _hasActual;

    /// <summary>Coalesces live allocation requests to at most ten updates per second.</summary>
    public event Action<float, float, float>? AllocationRequested;

    /// <summary>Requests a change to shield deployment.</summary>
    public event Action<bool>? OnSetEnabled;

    /// <summary>Creates a helm-relative bearing dial, allocation slider and protected-arc controls.</summary>
    public WFShipShieldShuntScreen()
    {
        HorizontalExpand = true;
        VerticalExpand = true;
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 12;
        Margin = new Thickness(12);
        var header = Column(6);
        var heading = new BoxContainer { SeparationOverride = 12 };
        heading.AddChild(new Label { Text = Loc.GetString("wf-shield-helm-heading"), HorizontalExpand = true });
        heading.AddChild(_stats = new Button { Text = Loc.GetString("wf-shield-stats-title"), MinHeight = 36, Disabled = true });
        _stats.OnPressed += _ =>
        {
            if (_state?.Stats is not { } stats)
                return;
            if (_wfCockpitShowStats != null)
            {
                _wfCockpitShowStats(stats);
                return;
            }
            _statsWindow ??= new WFShipShieldStatsWindow(_dial!.GlassFace);
            _statsWindow.UpdateStats(stats);
            _statsWindow.OpenCentered();
        };
        heading.AddChild(_enabled = new Button { MinHeight = 36, Disabled = true });
        _enabled.OnPressed += _ =>
        {
            if (_state is { Available: true })
                OnSetEnabled?.Invoke(!_state.Enabled);
        };
        header.AddChild(heading);
        header.AddChild(_status = new RichTextLabel());
        header.AddChild(_health = new ProgressBar
        {
            MinValue = 0f, MaxValue = 1f, MinHeight = 22,
            BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#1B303C") },
            ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#58D6EC") },
        });
        header.AddChild(_recovery = new RichTextLabel
        {
            Modulate = Color.FromHex("#FFCC80"),
            ToolTip = Loc.GetString("wf-shield-recovery-help"),
        });
        AddChild(Card(header));

        var preview = Column(8);
        preview.AddChild(_previewStatus = new Label { HorizontalAlignment = HAlignment.Center });
        preview.AddChild(new Label { Text = Loc.GetString("wf-shield-helm-forward"), HorizontalAlignment = HAlignment.Center });
        preview.AddChild(_dial = new ShieldDial());
        preview.AddChild(new Label { Text = Loc.GetString("wf-shield-helm-aft"), HorizontalAlignment = HAlignment.Center });
        preview.AddChild(new RichTextLabel { Text = Loc.GetString("wf-shield-helm-click-bearing") });
        _settings = new ShieldSettings { SeparationOverride = 12 };
        AddChild(new ShieldColumns(Card(preview), _settings));
        _direction = new FloatSpinBox(1f, 1)
        {
            Value = 0f, IsValid = value => float.IsFinite(value) && value >= 0f && value < 360f,
            HorizontalExpand = true,
        };
        _arc = new FloatSpinBox(5f, 0)
        {
            Value = 90f, IsValid = value => float.IsFinite(value) && value >= 30f && value <= 360f,
            HorizontalExpand = true,
        };
        var direction = Column(8);
        direction.AddChild(Field("wf-shield-helm-bearing", _direction));
        var bearings = new BoxContainer { SeparationOverride = 4 };
        foreach (var (key, bearing) in new[] { ("fore", 0f), ("starboard", 90f), ("aft", 180f), ("port", 270f) })
        {
            var button = QuickButton($"wf-shield-helm-preset-{key}");
            button.OnPressed += _ => { _direction.Value = bearing; Edited(); };
            bearings.AddChild(button);
        }
        direction.AddChild(bearings);
        direction.AddChild(Field("wf-shield-helm-arc", _arc));
        direction.AddChild(new RichTextLabel { Text = Loc.GetString("wf-shield-helm-arc-help") });
        _settings.AddChild(Card(direction));

        var power = Column(8);
        power.AddChild(_amount = new RichTextLabel());
        power.AddChild(_concentration = new Slider
        {
            MinValue = 0f, MaxValue = 100f, Rounded = true, MinHeight = 28, HorizontalExpand = true,
        });
        var amounts = new BoxContainer { SeparationOverride = 4 };
        foreach (var (key, amount) in new[] { ("balanced", 0f), ("half", 50f), ("all", 100f) })
        {
            var button = QuickButton($"wf-shield-helm-power-{key}");
            button.OnPressed += _ => { _concentration.Value = amount; Edited(); };
            amounts.AddChild(button);
        }
        power.AddChild(amounts);
        power.AddChild(new RichTextLabel { Text = Loc.GetString("wf-shield-helm-power-help") });
        _settings.AddChild(Card(power));

        var result = Column(8);
        result.AddChild(new Label { Text = Loc.GetString("wf-shield-helm-result") });
        result.AddChild(_strength = new RichTextLabel());
        result.AddChild(_outside = new RichTextLabel());
        result.AddChild(_unprotected = new RichTextLabel());
        _unprotected.SetMessage(Loc.GetString("wf-shield-helm-unprotected"), Color.Orange);
        _settings.AddChild(Card(result));
        var footer = Column(8);
        footer.AddChild(_draft = new RichTextLabel());
        var actions = new BoxContainer { SeparationOverride = 8 };
        actions.AddChild(_reset = new Button { Text = Loc.GetString("wf-shield-helm-reset"), Disabled = true, MinHeight = 38 });
        footer.AddChild(actions);
        AddChild(Card(footer));
        _direction.OnValueChanged += _ => Edited();
        _arc.OnValueChanged += _ => Edited();
        _concentration.OnValueChanged += _ => Edited();
        _dial.BearingRequested += degrees =>
        {
            _direction.Value = MathF.Round(degrees, 1) % 360f;
            Edited();
        };
        _reset.OnPressed += _ => ResetAllocation();
        UpdateState(null, 0f);
    }

    private static BoxContainer Column(int spacing) => new()
    {
        Orientation = LayoutOrientation.Vertical, SeparationOverride = spacing, HorizontalExpand = true,
    };

    private static PanelContainer Card(Control content)
    {
        var style = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#101C25"), BorderColor = Color.FromHex("#2B414F"),
            BorderThickness = new Thickness(1),
        };
        style.SetContentMarginOverride(StyleBox.Margin.Horizontal, 12);
        style.SetContentMarginOverride(StyleBox.Margin.Vertical, 12);
        var panel = new PanelContainer { PanelOverride = style, HorizontalExpand = true };
        panel.AddChild(content);
        return panel;
    }

    private static BoxContainer Field(string label, Control input)
    {
        var row = Column(4);
        row.AddChild(new Label { Text = Loc.GetString(label) });
        row.AddChild(input);
        return row;
    }

    private static Button QuickButton(string label) => new()
    {
        Text = Loc.GetString(label), HorizontalExpand = true, MinHeight = 30,
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _statsWindow?.Dispose();
            _statsWindow = null;
        }
        base.Dispose(disposing);
    }

    /// <summary>Accepts current and target allocation without overwriting unacknowledged local edits.</summary>
    public void UpdateState(WFShipShieldShuntState? state, float helmRotation)
    {
        _state = state;
        _stats.Disabled = state?.Stats == null;
        if (state?.Stats is { } stats)
            _statsWindow?.UpdateStats(stats);
        else
            _statsWindow?.Close();
        _helmRotation = helmRotation;
        _dial.Available = state is { Available: true };
        _health.Value = Math.Clamp(state?.Health ?? 0f, 0f, 1f);
        _enabled.Disabled = state is not { Available: true };
        _enabled.Text = Loc.GetString(state is { Enabled: true } ? "wf-shield-helm-disable" : "wf-shield-helm-enable");
        UpdateHealthAppearance();
        _settings.Visible = state is { Available: true };
        _reset.Disabled = state is not { Available: true };
        _concentration.Disabled = state is not { Available: true };
        _status.Text = state is { Active: true }
            ? Loc.GetString("wf-shield-helm-health", ("health", MathF.Round(state.Health * 100f)))
            : state is { Available: true }
                ? Loc.GetString("wf-shield-helm-offline", ("health", MathF.Round(state.Health * 100f)))
                : Loc.GetString("wf-shield-helm-unavailable");
        UpdateRecovery(state);
        var acknowledged = state != null && !_queued &&
            MathF.Abs(MathF.Atan2(MathF.Sin(state.TargetDirectionRadians - DesiredDirection),
                MathF.Cos(state.TargetDirectionRadians - DesiredDirection))) < 0.002f &&
            MathF.Abs(state.TargetConcentration - _concentration.Value / 100f) < 0.002f &&
            MathF.Abs(state.TargetArcRadians - _arc.Value * MathF.PI / 180f) < 0.002f;
        if (acknowledged || _clock - _lastEdit > 1f)
            _pending = false;
        if (state is not { Available: true })
        {
            _queued = false;
            _pending = false;
            _hasActual = false;
        }
        if (!_pending)
        {
            _updating = true;
            _direction.Value = state == null ? 0f : WFShipShieldHelmAngles.Bearing(state.TargetDirectionRadians, helmRotation);
            _arc.Value = state == null ? 90f : state.TargetArcRadians * 180f / MathF.PI;
            _concentration.Value = (state?.TargetConcentration ?? 0f) * 100f;
            _updating = false;
        }
        if (!_hasActual && state is { Available: true })
        {
            _dial.Direction = ActualDirection;
            _dial.Concentration = state.Concentration;
            _dial.Arc = state.ArcRadians;
            _hasActual = true;
        }
        Refresh();
    }

    /// <summary>Shows the server's estimate or the condition preventing automatic recovery.</summary>
    private void UpdateRecovery(WFShipShieldShuntState? state)
    {
        _recovery.Visible = state is { Available: true, Active: false } && state.RecoveryStatus != WFShipShieldRecoveryStatus.None;
        if (!_recovery.Visible || state == null)
            return;
        var waiting = state.RecoveryStatus switch
        {
            WFShipShieldRecoveryStatus.NoPower => "wf-shield-recovery-no-power",
            WFShipShieldRecoveryStatus.Lowered => "wf-shield-recovery-lowered",
            WFShipShieldRecoveryStatus.Disabled => "wf-shield-recovery-disabled",
            WFShipShieldRecoveryStatus.FtlLocked => "wf-shield-recovery-ftl-locked",
            _ => null,
        };
        if (waiting != null || state.RecoverySeconds < 0)
        {
            _recovery.Text = Loc.GetString(waiting ?? "wf-shield-recovery-stalled");
            return;
        }
        var reason = state.RecoveryStatus switch
        {
            WFShipShieldRecoveryStatus.Recharging => "wf-shield-recovery-recharging",
            WFShipShieldRecoveryStatus.Overloaded => "wf-shield-recovery-overloaded",
            WFShipShieldRecoveryStatus.RechargingAndOverloaded => "wf-shield-recovery-both",
            _ => "wf-shield-recovery-ready",
        };
        var time = Loc.GetString("wf-shield-recovery-time", ("minutes", state.RecoverySeconds / 60),
            ("seconds", (state.RecoverySeconds % 60).ToString("D2")));
        _recovery.Text = Loc.GetString("wf-shield-recovery-countdown", ("time", time), ("reason", Loc.GetString(reason)));
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _warningTime = (_warningTime + args.DeltaSeconds) % 1f;
        UpdateHealthAppearance();
        _clock += args.DeltaSeconds;
        if (_queued && _clock >= _nextSend)
            SendAllocation();
        if (_state is { Available: true } state)
        {
            var blend = 1f - MathF.Exp(-args.DeltaSeconds / 0.1f);
            var difference = ActualDirection - _dial.Direction;
            _dial.Direction += MathF.Atan2(MathF.Sin(difference), MathF.Cos(difference)) * blend;
            _dial.Concentration += (state.Concentration - _dial.Concentration) * blend;
            _dial.Arc += (state.ArcRadians - _dial.Arc) * blend;
            if (_clock >= _nextRefresh)
            {
                _nextRefresh = _clock + 0.1f;
                Refresh();
            }
        }
    }

    /// <summary>Keeps the ring and integrity bar on the same health and warning pulse.</summary>
    private void UpdateHealthAppearance()
    {
        var tint = WFShipShieldEffects.HealthColor(_health.Value);
        if (_dial.GlassFace)
        {
            var skin = WFInstrumentTheme.Skin;
            tint = _health.Value < 0.5f ? Color.InterpolateBetween(skin.Danger, skin.Caution, _health.Value * 2) :
                Color.InterpolateBetween(skin.Caution, skin.Good, (_health.Value - 0.5f) * 2);
        }
        if (_state is { Available: true } && _health.Value < 0.1f)
        {
            var pulse = 0.5f + 0.5f * MathF.Cos(_warningTime * MathF.Tau);
            tint = _dial.GlassFace
                ? Color.InterpolateBetween(WFInstrumentTheme.Skin.Ink, WFInstrumentTheme.Red, 0.3f + 0.7f * pulse)
                : Color.InterpolateBetween(new Color(0.4f, 0.02f, 0.03f), new Color(1f, 0.05f, 0.08f), pulse);
        }
        if (_health.ForegroundStyleBoxOverride is StyleBoxFlat fill)
            fill.BackgroundColor = tint;
        if (_health.BackgroundStyleBoxOverride is StyleBoxFlat background)
            background.BackgroundColor = _state is { Available: true } && _health.Value < 0.1f
                ? tint.WithAlpha(0.35f) : _dial.GlassFace ? WFInstrumentTheme.Skin.GlassLight : Color.FromHex("#1B303C");
        _dial.HealthTint = tint;
    }

    private float DesiredDirection => WFShipShieldHelmAngles.GridDirection(_direction.Value, _helmRotation);
    private float ActualDirection => WFShipShieldHelmAngles.GridDirection(
        WFShipShieldHelmAngles.Bearing(_state?.DirectionRadians ?? 0f, _helmRotation), 0f);

    private void Edited()
    {
        if (_updating || _state is not { Available: true })
            return;
        _pending = true;
        _queued = true;
        _lastEdit = _clock;
        Refresh();
    }

    /// <summary>Sets the requested allocation and queues a live network update.</summary>
    public void SetDraft(float bearingDegrees, float concentration, float arcDegrees)
    {
        _updating = true;
        _direction.Value = bearingDegrees;
        _concentration.Value = Math.Clamp(concentration, 0f, 1f) * 100f;
        _arc.Value = arcDegrees;
        _updating = false;
        Edited();
    }

    /// <summary>Requests balanced allocation without waiting for confirmation.</summary>
    public void ResetAllocation() => SetDraft(_direction.Value, 0f, _arc.Value);

    private void SendAllocation()
    {
        if (_state is not { Available: true })
            return;
        _queued = false;
        _nextSend = _clock + 0.1f;
        AllocationRequested?.Invoke(DesiredDirection, _concentration.Value / 100f, _arc.Value * MathF.PI / 180f);
    }

    private void Refresh()
    {
        var arc = _arc.Value * MathF.PI / 180f;
        _dial.TargetDirection = WFShipShieldHelmAngles.GridDirection(_direction.Value, 0f);
        _dial.TargetArc = arc;
        _amount.Text = Loc.GetString(_wfCockpitCompact ? "wf-cockpit-shield-power" : "wf-shield-helm-concentration", ("amount", _concentration.Value));
        var boost = WFShipShieldShuntMath.StrengthMultiplier(new Vector2(MathF.Cos(_dial.Direction), MathF.Sin(_dial.Direction)),
            Vector2.Zero, _dial.Direction, _dial.Concentration, _dial.Arc);
        _strength.SetMessage(Loc.GetString("wf-shield-helm-strength", ("strength", MathF.Round(boost * 100f))), Color.FromHex("#83DCEB"));
        _outside.Text = Loc.GetString("wf-shield-helm-outside",
            ("outside", MathF.Round((_dial.Arc >= MathF.Tau - 0.001f ? 1f : 1f - _dial.Concentration) * 100f)));
        _unprotected.Visible = _state is { Concentration: >= 1f } && _state.ArcRadians < MathF.Tau - 0.00001f;
        var adjusting = _pending || _state is { } state &&
            (MathF.Abs(state.Concentration - state.TargetConcentration) > 0.002f ||
             MathF.Abs(state.ArcRadians - state.TargetArcRadians) > 0.002f ||
             MathF.Abs(MathF.Atan2(MathF.Sin(state.DirectionRadians - state.TargetDirectionRadians),
                 MathF.Cos(state.DirectionRadians - state.TargetDirectionRadians))) > 0.002f);
        _draft.Text = Loc.GetString(adjusting ? "wf-shield-helm-adjusting-help" : "wf-shield-helm-live");
        _previewStatus.Text = Loc.GetString(adjusting ? "wf-shield-helm-adjusting" : "wf-shield-helm-current");
        _previewStatus.Modulate = adjusting ? Color.Orange : Color.FromHex("#83DCEB");
    }

    /// <summary>Stacks the allocation panels, or splits the last one into a second column when given enough width.</summary>
    private sealed class ShieldSettings : BoxContainer
    {
        private const float Gap = 12f;
        /// <summary>Two columns of about 300 units plus the gap.</summary>
        private const float SplitWidth = 612f;

        public ShieldSettings()
        {
            Orientation = LayoutOrientation.Vertical;
            HorizontalExpand = true;
        }

        private float _flankWidth;

        /// <summary>When above zero, the first panels stack at the left and the last takes the right edge, each this wide, leaving the middle free.</summary>
        public float FlankWidth
        {
            get => _flankWidth;
            set
            {
                if (MathF.Abs(_flankWidth - value) < 0.5f)
                    return;
                _flankWidth = value;
                InvalidateMeasure();
            }
        }

        private bool Split(float width) => width >= SplitWidth && float.IsFinite(width) && VisibleCount() > 1;

        private int VisibleCount()
        {
            var count = 0;
            foreach (var child in Children)
            {
                if (child.Visible)
                    count++;
            }
            return count;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (_flankWidth > 0f)
            {
                var (left, right) = Columns(_flankWidth, availableSize.Y);
                return new Vector2(float.IsFinite(availableSize.X) ? availableSize.X : _flankWidth * 2f, MathF.Max(left, right));
            }
            if (!Split(availableSize.X))
                return base.MeasureOverride(availableSize);
            var column = MathF.Max(0f, (availableSize.X - Gap) / 2f);
            var (first, second) = Columns(column, availableSize.Y);
            return new Vector2(column * 2f + Gap, MathF.Max(first, second));
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            if (_flankWidth > 0f)
                return ArrangeFlank(finalSize);
            if (!Split(finalSize.X))
                return base.ArrangeOverride(finalSize);
            var column = MathF.Max(0f, (finalSize.X - Gap) / 2f);
            Columns(column, finalSize.Y);
            var separation = SeparationOverride ?? 0;
            var y = 0f;
            Control? last = null;
            foreach (var child in Children)
            {
                if (child.Visible)
                    last = child;
            }
            foreach (var child in Children)
            {
                if (!child.Visible)
                    continue;
                if (child == last)
                {
                    child.Arrange(UIBox2.FromDimensions(new Vector2(column + Gap, 0f), new Vector2(column, child.DesiredSize.Y)));
                    continue;
                }
                child.Arrange(UIBox2.FromDimensions(new Vector2(0f, y), new Vector2(column, child.DesiredSize.Y)));
                y += child.DesiredSize.Y + separation;
            }
            return finalSize;
        }

        /// <summary>Stacks the first panels at the left edge and the last at the right edge, each centred vertically.</summary>
        private Vector2 ArrangeFlank(Vector2 finalSize)
        {
            var (left, right) = Columns(_flankWidth, finalSize.Y);
            var separation = SeparationOverride ?? 0;
            Control? last = null;
            foreach (var child in Children)
            {
                if (child.Visible)
                    last = child;
            }
            var y = MathF.Max(0f, (finalSize.Y - left) / 2f);
            foreach (var child in Children)
            {
                if (!child.Visible)
                    continue;
                if (child == last)
                {
                    var top = MathF.Max(0f, (finalSize.Y - right) / 2f);
                    child.Arrange(UIBox2.FromDimensions(new Vector2(MathF.Max(0f, finalSize.X - _flankWidth), top),
                        new Vector2(_flankWidth, child.DesiredSize.Y)));
                    continue;
                }
                child.Arrange(UIBox2.FromDimensions(new Vector2(0f, y), new Vector2(_flankWidth, child.DesiredSize.Y)));
                y += child.DesiredSize.Y + separation;
            }
            return finalSize;
        }

        /// <summary>Measures every visible panel at the column width and returns the height of each column.</summary>
        private (float First, float Second) Columns(float column, float height)
        {
            var separation = SeparationOverride ?? 0;
            Control? last = null;
            foreach (var child in Children)
            {
                if (child.Visible)
                    last = child;
            }
            var first = 0f;
            var second = 0f;
            foreach (var child in Children)
            {
                if (!child.Visible)
                    continue;
                child.Measure(new Vector2(column, height));
                if (child == last)
                    second = child.DesiredSize.Y;
                else
                    first += child.DesiredSize.Y + (first > 0f ? separation : 0f);
            }
            return (first, second);
        }
    }

    /// <summary>Reserves both column widths before measuring text and keeps overflow inside each column.</summary>
    private sealed class ShieldColumns : Container
    {
        private const float Gap = 12f;
        private readonly ScrollContainer _preview;
        private readonly ScrollContainer _settings;

        /// <summary>Gives the settings two columns on wide screens, otherwise one narrower column.</summary>
        private static float SettingsWidth(float total) => total >= 1100f
            ? MathF.Min(780f, MathF.Max(0f, total - Gap) * 0.62f)
            : MathF.Min(380f, MathF.Max(0f, total - Gap) * 0.46f);

        /// <summary>Width of each side column when the preview sits in the middle of the page.</summary>
        private static float SideWidth(float total) => MathF.Min(360f, MathF.Max(256f, total * 0.28f));

        private static float MiddleWidth(float total, float side) => MathF.Max(0f, total - side * 2f - Gap * 2f);

        /// <summary>Puts the preview in the middle with the settings flanking it, when the settings support flanking.</summary>
        private readonly ShieldSettings? _flank;

        public ShieldColumns(Control preview, Control settings, bool centred = false)
        {
            HorizontalExpand = true;
            VerticalExpand = true;
            // A centred preview must sit exactly mid-page, so neither side reserves room for a scroll bar.
            _preview = new ScrollContainer { HScrollEnabled = false, ReserveScrollbarSpace = !centred };
            _settings = new ScrollContainer { HScrollEnabled = false, ReserveScrollbarSpace = !centred };
            _preview.AddChild(preview);
            _settings.AddChild(settings);
            _flank = centred ? settings as ShieldSettings : null;
            if (_flank != null)
            {
                // The preview is added last so it sits above the settings' empty middle.
                AddChild(_settings);
                AddChild(_preview);
                return;
            }
            AddChild(_preview);
            AddChild(_settings);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (_flank != null)
            {
                var side = SideWidth(availableSize.X);
                _flank.FlankWidth = side;
                _settings.Measure(availableSize);
                _preview.Measure(new Vector2(MiddleWidth(availableSize.X, side), availableSize.Y));
                return Vector2.Zero;
            }
            var right = SettingsWidth(availableSize.X);
            _preview.Measure(new Vector2(MathF.Max(0f, availableSize.X - right - Gap), availableSize.Y));
            _settings.Measure(new Vector2(right, availableSize.Y));
            // The expandable body takes the space left after the fixed header and status row.
            return Vector2.Zero;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            if (_flank != null)
            {
                var side = SideWidth(finalSize.X);
                _flank.FlankWidth = side;
                _settings.Arrange(UIBox2.FromDimensions(Vector2.Zero, finalSize));
                _preview.Arrange(UIBox2.FromDimensions(new Vector2(side + Gap, 0f), new Vector2(MiddleWidth(finalSize.X, side), finalSize.Y)));
                return finalSize;
            }
            var right = SettingsWidth(finalSize.X);
            var left = MathF.Max(0f, finalSize.X - right - Gap);
            _preview.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(left, finalSize.Y)));
            _settings.Arrange(UIBox2.FromDimensions(new Vector2(left + Gap, 0f), new Vector2(right, finalSize.Y)));
            return finalSize;
        }
    }

    /// <summary>A clockwise helm-relative bearing selector with current protection and a requested bearing marker.</summary>
    private sealed class ShieldDial : Control
    {
        public event Action<float>? BearingRequested;
        public float Direction;
        public float TargetDirection;
        public float TargetArc = MathF.PI / 2f;
        public float Concentration;
        public float Arc = MathF.PI / 2f;
        public bool Available;
        /// <summary>Gives helm instruments a lens while retaining the generator panel layout.</summary>
        public bool GlassFace;
        public Color HealthTint;
        private bool _dragging;
        private DrawVertexUV2DColor[] _digitalVertices = Array.Empty<DrawVertexUV2DColor>();
        private readonly Vector2[] _band = new Vector2[6];

        public ShieldDial()
        {
            MinSize = new Vector2(220f, 220f);
            HorizontalExpand = true;
            VerticalExpand = true;
            MouseFilter = MouseFilterMode.Stop;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            var center = (Vector2) PixelSize / 2f;
            var diameter = MathF.Min(PixelSize.X, PixelSize.Y);
            var radius = MathF.Max(1f, MathF.Min(diameter * 0.40f, diameter / 2f - 28f * UIScale));
            if (GlassFace && WFInstrumentTheme.Digital)
            {
                var skin = WFInstrumentTheme.Skin;
                handle.DrawCircle(center, radius + 27f * UIScale, skin.Glass);
                WFConsoleDigital.Arc(handle, center, radius + 27f * UIScale, UIScale, 0, MathF.Tau, skin.AccentDim, ref _digitalVertices);
                WFConsoleDigital.Arc(handle, center, radius * 0.72f, UIScale, 0, MathF.Tau, skin.Edge, ref _digitalVertices);
            }
            else if (GlassFace)
            {
                var rim = radius + 27f * UIScale;
                handle.DrawCircle(center, rim, WFInstrumentTheme.Skin.EdgeLight);
                handle.DrawCircle(center, rim - 2f * UIScale, WFInstrumentTheme.Skin.EdgeSoft);
                handle.DrawCircle(center, rim - 4f * UIScale, WFInstrumentTheme.Skin.Edge);
                handle.DrawCircle(center, rim - 5f * UIScale, WFInstrumentTheme.Skin.Glass);
            }
            var grid = GlassFace ? WFInstrumentTheme.Skin.EdgeLight : Color.FromHex("#29404F");
            var tint = Available ? HealthTint : Color.FromHex("#52616B");
            handle.DrawCircle(center, radius * 0.55f, grid, false);
            handle.DrawCircle(center, radius + 14f * UIScale, grid, false);
            handle.DrawLine(center - new Vector2(radius, 0), center + new Vector2(radius, 0), grid);
            handle.DrawLine(center - new Vector2(0, radius), center + new Vector2(0, radius), grid);
            for (var tick = 0; tick < 72; tick++)
            {
                var angle = tick * MathF.Tau / 72f;
                handle.DrawLine(Point(angle, radius + 18f * UIScale),
                    Point(angle, radius + (tick % 6 == 0 ? 26f : 21f) * UIScale), grid);
            }
            // Keep segment gaps readable at reduced resolutions and UI scales.
            var segments = Math.Clamp((int) (MathF.Tau * radius / MathF.Max(12f, 12f * UIScale)), 24, 96);
            var step = MathF.Tau / segments;
            var gap = MathF.Min(step * 0.35f, MathF.Max(step * 0.25f, MathF.Max(2f, 3f * UIScale) / MathF.Max(radius, 1f)));
            for (var segment = 0; segment < segments; segment++)
            {
                var angle = segment * step;
                var next = angle + step - gap;
                var point = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var strength = Available ? WFShipShieldShuntMath.StrengthMultiplier(point, Vector2.Zero, Direction, Concentration, Arc) : 0f;
                var width = Math.Clamp(5f + strength * 7f, 5f, 28f) * UIScale;
                _band[0] = Point(angle, radius);
                _band[1] = Point(next, radius);
                _band[2] = Point(angle, radius - width);
                _band[3] = _band[2];
                _band[4] = _band[1];
                _band[5] = Point(next, radius - width);
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _band,
                    strength <= 0f ? Color.FromHex("#49312D") : tint.WithAlpha(Math.Clamp(0.25f + strength * 0.3f, 0.25f, 1f)));
            }
            if (Available)
            {
                if (Arc < MathF.Tau - 0.001f)
                {
                    for (var side = -1; side <= 1; side += 2)
                    {
                        var edge = Direction + side * Arc / 2f;
                        handle.DrawLine(Point(edge, radius * 0.25f), Point(edge, radius), tint.WithAlpha(0.45f));
                    }
                }
                handle.DrawLine(Point(Direction, radius * 0.3f), Point(Direction, radius), tint.WithAlpha(0.65f));
                handle.DrawLine(Point(TargetDirection, radius * 0.65f), Point(TargetDirection, radius + 8f * UIScale), Color.White.WithAlpha(0.6f));
                handle.DrawCircle(Point(TargetDirection, radius + 8f * UIScale), 5f * UIScale, Color.White, false);
                if (TargetArc < MathF.Tau - 0.001f)
                {
                    for (var segment = 0; segment < 48; segment++)
                    {
                        var start = TargetDirection - TargetArc / 2f + TargetArc * segment / 48f;
                        var end = start + TargetArc / 48f;
                        handle.DrawLine(Point(start, radius + 5f * UIScale), Point(end, radius + 5f * UIScale), Color.White.WithAlpha(0.25f));
                    }
                }
            }
            // A fixed bow marker keeps helm-relative bearings readable while the selector moves.
            var bow = center + new Vector2(0f, -24f) * UIScale;
            var port = center + new Vector2(-15f, 18f) * UIScale;
            var aft = center + new Vector2(0f, 10f) * UIScale;
            var starboard = center + new Vector2(15f, 18f) * UIScale;
            handle.DrawLine(bow, port, Color.White);
            handle.DrawLine(port, aft, Color.White);
            handle.DrawLine(aft, starboard, Color.White);
            handle.DrawLine(starboard, bow, Color.White);
            if (GlassFace && !WFInstrumentTheme.Digital)
                WFInstrumentGlass.Round(handle, center, radius + 22f * UIScale, UIScale);
            return;
            Vector2 Point(float angle, float distance) => center + new Vector2(MathF.Cos(angle), -MathF.Sin(angle)) * distance;
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);
            if (!Available || args.Function != EngineKeyFunctions.UIClick)
                return;
            _dragging = true;
            SelectBearing(args.RelativePosition);
            args.Handle();
        }

        protected override void KeyBindUp(GUIBoundKeyEventArgs args)
        {
            base.KeyBindUp(args);
            if (args.Function == EngineKeyFunctions.UIClick)
                _dragging = false;
        }

        protected override void MouseMove(GUIMouseMoveEventArgs args)
        {
            base.MouseMove(args);
            if (_dragging && Available)
                SelectBearing(args.RelativePosition);
        }

        private void SelectBearing(Vector2 position)
        {
            var delta = position - Size / 2f;
            if (delta.LengthSquared() < 100f)
                return;
            var degrees = MathF.Atan2(delta.X, -delta.Y) * 180f / MathF.PI;
            BearingRequested?.Invoke((degrees + 360f) % 360f);
        }
    }
}
