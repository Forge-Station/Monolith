#nullable enable

using Content.Client._WF.CombatConsole;
using Content.Shared._WF.Shuttles;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.Cockpit;

/// <summary>Pairs the ship's normalized fuel reserve with an independent low-fuel warning lens.</summary>
public sealed class WFCockpitFuelBank : BoxContainer
{
    private readonly Func<ShipFuelSummary?> _read;
    private readonly WFGlassGauge _gauge;
    private readonly WFCockpitFuelLamp _lamp;
    private float _poll;
    private int? _sources;
    private int? _unknown;
    private bool _initialized;

    /// <summary>Preserves missing and incomplete telemetry rather than displaying a false empty tank.</summary>
    public ShipFuelSummary? Reading => _read();

    /// <summary>Reads a ship's fuel summary; the prefix names the bank and its parts, so two banks in one window stay distinguishable.</summary>
    public WFCockpitFuelBank(Func<ShipFuelSummary?> read, string prefix = "Cockpit")
    {
        _read = read;
        Name = prefix + "FuelBank";
        HorizontalExpand = true;
        SeparationOverride = 6;
        SetHeight = 44;
        _gauge = new WFGlassGauge("wf-cockpit-fuel", GaugeReading, true)
        {
            Name = prefix + "Fuel",
            CompactStrip = true,
            SetHeight = 44,
            MouseFilter = MouseFilterMode.Pass,
        };
        _lamp = new WFCockpitFuelLamp(() => Reading) { Name = prefix + "FuelLow" };
        AddChild(_gauge);
        AddChild(_lamp);
        RefreshTooltip();
    }

    private WFGaugeReading GaugeReading()
    {
        var reading = Reading;
        return WFGaugeReading.Number(reading is { Available: true } fuel ? fuel.Fraction * 100 : null,
            0, 100, "wf-gauge-unit-percent", decimals: 1,
            tint: reading is { Low: true } ? WFInstrumentTheme.Skin.Caution : WFInstrumentTheme.Green);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _poll += args.DeltaSeconds;
        if (_poll < 0.1f)
            return;
        _poll %= 0.1f;
        RefreshTooltip();
    }

    private void RefreshTooltip()
    {
        var reading = Reading;
        if (_initialized && _sources == reading?.Sources && _unknown == reading?.UnknownSources)
            return;
        _initialized = true;
        _sources = reading?.Sources;
        _unknown = reading?.UnknownSources;
        var status = reading switch
        {
            null => Loc.GetString("wf-cockpit-fuel-no-signal"),
            { Sources: 0 } => Loc.GetString("wf-cockpit-fuel-no-sources"),
            { UnknownSources: > 0 } fuel => Loc.GetString("wf-cockpit-fuel-incomplete",
                ("unknown", fuel.UnknownSources), ("sources", fuel.Sources)),
            { } fuel => Loc.GetString("wf-cockpit-fuel-sources", ("sources", fuel.Sources)),
        };
        ToolTip = status + "\n" + Loc.GetString("wf-cockpit-fuel-help") + "\n" +
            Loc.GetString("wf-cockpit-fuel-low-help", ("low", MathF.Round(ShipFuelSummary.LowThreshold * 100)),
                ("critical", MathF.Round(ShipFuelSummary.CriticalThreshold * 100)));
        _gauge.ToolTip = ToolTip;
        _lamp.ToolTip = ToolTip;
    }
}
