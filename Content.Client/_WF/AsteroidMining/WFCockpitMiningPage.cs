using System.Linq;
using System.Numerics;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.ShipAccess;
using Content.Shared._WF.AsteroidMining;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.AsteroidMining;

/// <summary>
/// The cockpit's mining page: the ship's mining laser power on a lever, the rock under the lasers on a heat dial with
/// lamps for its bands and figures for its window, crack and yield, and what the rock holds. It shows the same scan as the gunnery console's mining panel, through
/// the helm's gunnery link, and its selector only shows while the linked ship has mining lasers.
/// </summary>
public sealed partial class WFCockpitMiningPage : BoxContainer, IWFCockpitOptionalPage
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>How long a new power shows before the server's value, in case the server never applies it.</summary>
    private static readonly TimeSpan PendingTimeout = TimeSpan.FromSeconds(1);

    /// <summary>A dragged lever sends at most this often, and its last setting when it stops.</summary>
    private static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(0.1);

    private readonly Label _status;
    private readonly WFMiningAnnunciator _lamps;
    private readonly WFMiningPowerLever _lever;
    private readonly WFMiningHeatDial _dial;
    private readonly WFMiningFigures _figures;
    private readonly Control _scan;
    private readonly Label _rock;
    private readonly Label _veins;
    private readonly WFMiningCompositionBar _bar;
    private readonly BoxContainer _ores;
    private readonly List<TargetLine> _oreLines = new();
    private readonly TargetLine _inert;
    private readonly TargetLine _resistance;
    private readonly Label _idle;
    private WFMiningBeamGunneryState _state;
    private float? _pending;
    private TimeSpan _pendingUntil;
    private float? _unsent;
    private TimeSpan _nextSend;

    /// <summary>A power the pilot set, for the helm to send to the linked gunnery console.</summary>
    public event Action<float>? PowerRequested;

    public bool Available => _state.Lasers > 0;

    public WFCockpitMiningPage()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = VerticalExpand = true;

        _status = Label("wf-mining-gunnery-idle", Muted);
        _status.ClipText = true;
        _status.HorizontalExpand = true;
        _status.Align = Robust.Client.UserInterface.Controls.Label.AlignMode.Right;
        var caption = Label("wf-mining-gunnery-title", Accent);
        _lamps = new WFMiningAnnunciator();

        _lever = new WFMiningPowerLever();
        _lever.Changed += SetPower;
        _dial = new WFMiningHeatDial();
        var instruments = Row(_lever, _dial);
        instruments.SeparationOverride = 6;
        instruments.MinHeight = 190;
        _figures = new WFMiningFigures();

        var steps = WFMiningGauges.PowerStepButtons(step =>
            SetPower(MathF.Round(Math.Clamp((_pending ?? _state.Power) + step, 0f, 1f), 2)));

        _rock = Label("wf-mining-gunnery-idle", Cream);
        _rock.ClipText = true;
        _rock.HorizontalExpand = true;
        _veins = Label("wf-mining-gunnery-idle", Muted);
        _bar = new WFMiningCompositionBar();
        _ores = Column();
        _ores.SeparationOverride = 0;
        _inert = new TargetLine(true);
        _inert.Name.Text = Loc.GetString("wf-mining-scan-inert");
        _resistance = new TargetLine(false);
        _resistance.Name.Text = Loc.GetString("wf-mining-scan-resistance");
        var table = Column(_ores, _inert.Row, _resistance.Row);
        table.SeparationOverride = 0;
        var scan = Column(Row(_rock, _veins), _bar, table);
        scan.SeparationOverride = 2;
        _scan = Panel("wf-mining-page-target", scan);
        _scan.Visible = false;
        _idle = Label("wf-mining-gunnery-idle", Muted);
        _idle.ToolTip = Loc.GetString("wf-mining-gunnery-idle-help");
        _idle.MouseFilter = MouseFilterMode.Pass;
        var hint = new WFWrappedLabel
        {
            Text = Loc.GetString("wf-mining-page-hint"),
            FontOverride = _idle.FontOverride,
            FontColorOverride = Muted,
            Margin = _idle.Margin,
            HorizontalExpand = true,
            ToolTip = Loc.GetString("wf-mining-page-hint-help"),
            MouseFilter = MouseFilterMode.Pass,
        };

        var body = Column(Row(caption, _status), _lamps, instruments, _figures, steps, _scan, _idle, hint);
        body.SeparationOverride = 6;
        AddChild(Scroll(body));
    }

    /// <summary>Applies the server's scan; a power the pilot just set keeps showing until the server agrees or a second passes.</summary>
    public void UpdateState(WFMiningBeamGunneryState state)
    {
        _state = state;
        if (_unsent == null && _pending is { } pending &&
            (MathF.Abs(pending - state.Power) < 0.005f || _timing.RealTime > _pendingUntil))
            _pending = null;
        Refresh();
    }

    private void SetPower(float power)
    {
        if (!Available)
            return;
        _pending = power;
        _unsent = power;
        _pendingUntil = _timing.RealTime + PendingTimeout;
        Refresh();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        var now = _timing.RealTime;
        if (_unsent is { } power && now >= _nextSend)
        {
            _unsent = null;
            _nextSend = now + SendInterval;
            _pendingUntil = now + PendingTimeout;
            PowerRequested?.Invoke(power);
        }
        else if (_unsent == null && _pending != null && now > _pendingUntil)
        {
            // The server never confirmed the setting, and an unchanged scan isn't resent.
            _pending = null;
            Refresh();
        }
    }

    private void Refresh()
    {
        var state = _state;
        if (state.Lasers == 0)
            return;

        _lever.Power = _pending ?? state.Power;
        var overloaded = state.Cooldown > 0f;
        _status.Text = overloaded
            ? Loc.GetString("wf-mining-gunnery-cooldown", ("seconds", (int) MathF.Ceiling(state.Cooldown)))
            : Loc.GetString("wf-mining-gunnery-lasers", ("count", state.Lasers));
        _status.FontColorOverride = overloaded ? Red : Muted;

        var target = state.Target;
        _dial.SetTarget(target);
        _figures.SetTarget(target);
        _idle.Visible = target == null;
        _scan.Visible = target != null;
        if (target is not { } rock)
        {
            _lamps.Set(false, false, false, overloaded);
            return;
        }

        _lamps.Set(rock.Charge >= rock.WindowLow && rock.Charge <= rock.WindowHigh,
            rock.Charge > rock.WindowHigh && rock.Charge < WFAsteroidHeat.OverchargeLine, rock.Overcharge > 0f, overloaded);
        _rock.Text = Loc.GetString("wf-mining-page-rock", ("name", rock.Name));
        _veins.Text = Loc.GetString("wf-mining-scan-veins", ("mass", (int) MathF.Round(rock.Mass)));
        _bar.Ores = rock.Ores;
        // Reuse the ore lines between scans, adding only when a rock has more ores than any before it.
        for (var i = _oreLines.Count; i < rock.Ores.Length; i++)
        {
            var line = new TargetLine(true);
            _ores.AddChild(line.Row);
            _oreLines.Add(line);
        }

        for (var i = 0; i < _oreLines.Count; i++)
        {
            var line = _oreLines[i];
            line.Row.Visible = i < rock.Ores.Length;
            if (!line.Row.Visible)
                continue;

            var ore = rock.Ores[i];
            line.Name.Text = Loc.GetString("wf-mining-scan-ore", ("name", ore.Name));
            line.Show(ore.Fraction, ore.Color, Cream);
        }

        _inert.Show(1f - rock.Ores.Sum(o => o.Fraction), Muted, Muted);
        _resistance.Show(rock.Resistance, Cream, Muted);
    }

    /// <summary>One line of the target table: a colour swatch for ores, a name, and a percentage against the right edge.</summary>
    private sealed class TargetLine
    {
        public readonly BoxContainer Row;
        public readonly Label Name;
        private readonly StyleBoxFlat? _swatch;
        private readonly Label _value;

        public TargetLine(bool swatch)
        {
            Name = Label("wf-mining-gunnery-idle");
            Name.ClipText = true;
            Name.HorizontalExpand = true;
            _value = Label("wf-mining-gunnery-idle");
            Row = WFInstrumentTheme.Row();
            if (swatch)
            {
                _swatch = new StyleBoxFlat();
                Row.AddChild(new PanelContainer
                {
                    PanelOverride = _swatch,
                    SetSize = new Vector2(8, 8),
                    Margin = new Thickness(2, 0, 0, 0),
                    VerticalAlignment = VAlignment.Center,
                });
            }

            Row.AddChild(Name);
            Row.AddChild(_value);
        }

        /// <summary>Shows a fraction as a percentage in <paramref name="color"/>, which also fills the swatch.</summary>
        public void Show(float fraction, Color color, Color nameColor)
        {
            Name.FontColorOverride = nameColor;
            _value.Text = Loc.GetString("wf-mining-scan-percent", ("percent", (int) MathF.Round(fraction * 100)));
            _value.FontColorOverride = color;
            if (_swatch != null)
                _swatch.BackgroundColor = color;
        }
    }
}

/// <summary>A rock's make-up as one bar, each ore in its colour by share and plain rock after them.</summary>
public sealed class WFMiningCompositionBar : Control
{
    public WFMiningBeamOreEntry[] Ores = Array.Empty<WFMiningBeamOreEntry>();

    public WFMiningCompositionBar()
    {
        MinSize = new Vector2(0, 14);
        HorizontalExpand = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        WFMiningGauges.Composition(handle, PixelSizeBox, Ores, UIScale);
    }
}
