using System.Numerics;
using Content.Client.Resources;
using Content.Client._WF.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.CombatConsole;

/// <summary>Displays a weapon name and exact supply above a quiet ammunition strip.</summary>
public sealed class WFWeaponRow : Control
{
    private const float BarHeight = 2;
    private const int NameSize = 10;
    private const int SupplySize = 8;
    private readonly Button _button;
    private readonly Func<WFGaugeReading> _read;
    private WolfgateSkin? _skin;
    private Font? _nameFont;
    private Font? _supplyFont;
    private WFGaugeReading _reading;
    private float _sampleElapsed;

    /// <summary>Returns the authoritative supply without visual smoothing.</summary>
    public WFGaugeReading Reading => _read();
    /// <summary>Readable weapon name; the button tooltip retains its full text when clipped.</summary>
    public Label NameLabel { get; }
    /// <summary>Exact ammunition, energy or replenishment state.</summary>
    public Label SupplyLabel { get; }

    public WFWeaponRow(Button button, Func<WFGaugeReading> read)
    {
        _button = button;
        _read = read;
        HorizontalExpand = VerticalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
        NameLabel = new Label { Name = "WfWeaponName", ClipText = true, MouseFilter = MouseFilterMode.Ignore };
        SupplyLabel = new Label { Name = "WfWeaponSupply", ClipText = true, MouseFilter = MouseFilterMode.Ignore };
        AddChild(NameLabel);
        AddChild(SupplyLabel);
        Refresh();
    }

    /// <summary>Refreshes labels without replacing the selected button or its input handlers.</summary>
    public void Refresh() => Refresh(Reading);

    /// <summary>Refreshes the labels from a supply reading the caller has already taken.</summary>
    public void Refresh(WFGaugeReading reading)
    {
        _reading = reading;
        var skin = WFInstrumentTheme.Skin;
        if (_skin != skin)
        {
            var cache = IoCManager.Resolve<IResourceCache>();
            NameLabel.FontOverride = _nameFont = cache.GetFont(skin.MenuFonts, NameSize);
            SupplyLabel.FontOverride = _supplyFont = cache.GetFont(skin.MonoFonts, SupplySize);
            _skin = skin;
        }
        if (NameLabel.Text != _button.Text)
            NameLabel.Text = _button.Text;
        if (SupplyLabel.Text != reading.Text)
            SupplyLabel.Text = reading.Text;
        NameLabel.FontColorOverride = _button.Disabled ? skin.TextMuted : skin.Text;
        SupplyLabel.FontColorOverride = reading.Tint == skin.Danger ? skin.Danger : skin.TextMuted;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var line = new Vector2(availableSize.X, float.PositiveInfinity);
        NameLabel.Measure(line);
        SupplyLabel.Measure(line);
        return new Vector2(0, LineHeight(NameLabel, _nameFont) + LineHeight(SupplyLabel, _supplyFont) + BarHeight);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var name = LineHeight(NameLabel, _nameFont);
        NameLabel.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(finalSize.X, name)));
        SupplyLabel.Arrange(UIBox2.FromDimensions(new Vector2(0, name), new Vector2(finalSize.X, LineHeight(SupplyLabel, _supplyFont))));
        return finalSize;
    }

    /// <summary>A label's height, never less than one line of its font so an empty name keeps its row.</summary>
    private float LineHeight(Label label, Font? font) =>
        Math.Max(label.DesiredSize.Y, font == null ? 0 : font.GetHeight(UIScale) / UIScale);

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _sampleElapsed += args.DeltaSeconds;
        if (_sampleElapsed < 0.1f && _skin == WFInstrumentTheme.Skin)
            return;
        _sampleElapsed = 0;
        Refresh();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var skin = WFInstrumentTheme.Skin;
        var bar = new UIBox2(0, Math.Max(0, PixelHeight - BarHeight * UIScale), PixelWidth, PixelHeight);
        handle.DrawRect(bar, skin.Ink);
        var reading = _reading;
        if (WFGaugeScale.Fraction(reading.Value, reading.Minimum, reading.Maximum) is not { } fraction)
            return;
        var color = reading.Tint ?? skin.Accent;
        handle.DrawRect(new UIBox2(bar.Left, bar.Top, bar.Left + bar.Width * fraction, bar.Bottom),
            color.WithAlpha(WFInstrumentTheme.Digital ? 0.85f : 0.65f));
    }
}

/// <summary>Separates weapon rows with a selection rail instead of nested mechanical card borders.</summary>
public sealed class WFWeaponRowStyleBox : StyleBox
{
    private readonly Button _button;

    public WFWeaponRowStyleBox(Button button)
    {
        _button = button;
        SetContentMarginOverride(Margin.Horizontal, 5);
        SetContentMarginOverride(Margin.Vertical, 0);
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var skin = WFInstrumentTheme.Skin;
        var selected = _button.Pressed;
        var fill = _button.Disabled ? skin.ButtonDisabled : _button.IsHovered ? skin.ButtonHovered :
            selected ? Color.InterpolateBetween(skin.GlassRaised, skin.Accent, 0.13f) : skin.GlassRaised;
        handle.DrawRect(box, fill);
        handle.DrawLine(new Vector2(box.Left, box.Bottom - uiScale), box.BottomRight - Vector2.UnitY * uiScale,
            selected ? skin.Accent.WithAlpha(0.45f) : skin.EdgeSoft);
        if (selected)
            handle.DrawRect(new UIBox2(box.Left, box.Top + 2 * uiScale, box.Left + 2 * uiScale, box.Bottom - 2 * uiScale), skin.Accent);
    }
}
