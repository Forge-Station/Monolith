using System.Numerics;
using Content.Shared._WF.ShipShields;
using Content.Client._WF.CombatConsole;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using InstrumentTheme = Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.ShipShields;

public sealed partial class WFShipShieldShuntScreen
{
    private const float WfCoverageDialHeight = 116f;

    /// <summary>Houses the live shield selector, allocation controls and status in the helm's instrument deck, with the bearing dial centred.</summary>
    public void WfRefitInstruments()
    {
        Margin = new Thickness(0);
        SeparationOverride = 6;
        var bearing = _direction.Parent!.Parent!;
        var power = _concentration.Parent!;
        var presets = bearing.GetChild(1);
        TagBearingButtons(_direction);
        TagBearingButtons(presets);
        foreach (var child in presets.Children)
        {
            if (child is Button button)
                button.OnPressed += _ => PlayBearingDetent();
        }
        _direction.OnValueChanged += _ => PlayBearingDetent();
        _dial.BearingRequested += _ => PlayBearingDetent();
        var directionPanel = InstrumentTheme.Panel("wf-console-shield-vector",
            InstrumentTheme.Column(InstrumentTheme.Row(_direction.Parent!, _arc.Parent!), presets), false, 8, 4);
        var amounts = power.GetChild(2);
        var powerGauge = new WFGlassGauge("wf-gauge-shield-power", () => WFGaugeReading.Number(
            _state is { Available: true } ? _concentration.Value : null, 0, 100, "wf-gauge-unit-percent"), true);
        _amount.Visible = false;
        var powerPanel = InstrumentTheme.Panel("wf-console-shield-power",
            InstrumentTheme.Column(powerGauge, _amount, _concentration, amounts), false, 8, 4);
        _arc.ToolTip = Loc.GetString("wf-shield-helm-arc-help");
        _concentration.ToolTip = Loc.GetString("wf-shield-helm-power-help");
        _dial.ToolTip = Loc.GetString("wf-shield-helm-click-bearing");
        _dial.GlassFace = true;
        var actual = new WFGlassGauge("wf-gauge-shield-actual", () => WFGaugeReading.Number(
            _state is { Available: true } ? _dial.Concentration * 100 : null, 0, 100, "wf-gauge-unit-percent"));
        var sector = new WFGlassGauge("wf-gauge-shield-sector", () => WFGaugeReading.Number(
            _state is { Available: true } ? MathHelper.RadiansToDegrees(_dial.Arc) : null, 0, 360, "wf-gauge-unit-degrees"));
        var strength = new WFGlassGauge("wf-gauge-shield-strength", () => WFGaugeReading.Number(
            _state is { Available: true } ? WFShipShieldShuntMath.StrengthMultiplier(
                new Vector2(MathF.Cos(_dial.Direction), MathF.Sin(_dial.Direction)), Vector2.Zero,
                _dial.Direction, _dial.Concentration, _dial.Arc) * 100 : null,
            0, Math.Max(100, MathF.Tau / Math.Max(0.01f, _dial.Arc) * 100), "wf-gauge-unit-percent"));
        var outside = new WFGlassGauge("wf-gauge-shield-outside", () => WFGaugeReading.Number(
            _state is { Available: true } ? (_dial.Arc >= MathF.Tau - 0.001f ? 1 : 1 - _dial.Concentration) * 100 : null,
            0, 100, "wf-gauge-unit-percent", tint: _unprotected.Visible ? InstrumentTheme.Red : InstrumentTheme.Accent));
        _strength.Visible = _outside.Visible = false;
        // The side columns sit level with a centred dial, so the dials are shorter than the default 142.
        foreach (var gauge in new[] { actual, sector, strength, outside })
            gauge.SetHeight = WfCoverageDialHeight;
        var resultPanel = InstrumentTheme.Panel("wf-console-shield-coverage",
            InstrumentTheme.Column(new WFGlassReadout(_previewStatus), InstrumentTheme.Row(actual, sector),
                InstrumentTheme.Row(strength, outside), _strength, _outside, new WFGlassReadout(_unprotected)), false, 8, 4);
        _settings.DisposeAllChildren();
        _settings.SeparationOverride = 6;
        _settings.AddChild(directionPanel);
        _settings.AddChild(powerPanel);
        _settings.AddChild(resultPanel);
        InstrumentTheme.Detach(_settings);

        _status.HorizontalExpand = true;
        _enabled.MinWidth = 160;
        _stats.MinWidth = 160;
        var header = new Content.Client._WF.CombatConsole.WFInstrumentPanel(8) { HorizontalExpand = true };
        header.AddChild(InstrumentTheme.Column(InstrumentTheme.Row(new WFGlassReadout(_status), _stats, _enabled), new WFGlassReadout(_health, true), _recovery));
        var scope = InstrumentTheme.Scope("wf-console-shield-scope", _dial, Vector2.Zero);
        // The dial takes the middle of the page, with the sector and power banks to its left and coverage to its right.
        var body = new ShieldColumns(scope, _settings, true);
        _draft.HorizontalExpand = true;
        _reset.MinWidth = 170;
        var footer = new Content.Client._WF.CombatConsole.WFInstrumentPanel(8) { HorizontalExpand = true };
        footer.AddChild(InstrumentTheme.Row(new WFGlassReadout(_draft), _reset));
        DisposeAllChildren();
        AddChild(header);
        AddChild(body);
        AddChild(footer);
        InstrumentTheme.Apply(this);
        UpdateHealthAppearance();
    }

    private void PlayBearingDetent()
    {
        if (!_updating && VisibleInTree && _state is { Available: true })
            IoCManager.Resolve<IEntityManager>().System<WFConsoleAudio>().TurnBearing();
    }

    private static void TagBearingButtons(Control control)
    {
        if (control is BaseButton)
            control.AddStyleClass("WfBearingDetent");
        foreach (var child in control.Children)
            TagBearingButtons(child);
    }
}
