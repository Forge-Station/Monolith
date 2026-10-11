using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._WF.CombatConsole;

/// <summary>
/// A telemetry sample with explicit scale limits, exact text and its current warning tint. Digits (padded to the
/// scale's width) and Unit are the same text in two pieces; both are empty for text that is not a measurement.
/// </summary>
public readonly record struct WFGaugeReading(double? Value, double Minimum, double Maximum, string Text, Color? Tint = null,
    string Digits = "", string Unit = "")
{
    /// <summary>Formats a measured quantity without inventing a reading when its source is unavailable.</summary>
    public static WFGaugeReading Number(double? value, double minimum, double maximum, string unit, int decimals = 0, Color? tint = null)
    {
        if (value is not { } number || !double.IsFinite(number))
            return new(value, minimum, maximum, Loc.GetString("wf-gauge-offline"), tint);
        var unitText = Loc.GetString(unit);
        return new(value, minimum, maximum, Loc.GetString("wf-gauge-value", ("value", WFGaugeScale.Fixed(number, decimals)),
            ("unit", unitText)), tint, WFGaugeScale.Digits(number, minimum, maximum, decimals), unitText);
    }
}

/// <summary>A damped needle, engraved scale and exact readout beneath a reflective glass lens.</summary>
public sealed class WFGlassGauge : Control
{
    private readonly Func<WFGaugeReading> _read;
    private readonly string _caption;
    private DrawVertexUV2DColor[] _digitalVertices = Array.Empty<DrawVertexUV2DColor>();
    private readonly Font _small;
    private readonly Font _digits;
    private readonly Font _dialDigits;
    private readonly Font _stripDigits;
    private WFGaugeReading _reading;
    private float? _needle;
    private float? _target;
    private float _sampleElapsed;
    private string _minimumText = string.Empty;
    private string _maximumText = string.Empty;
    private string _middleText = string.Empty;
    private readonly string _offline = Loc.GetString("wf-gauge-offline");

    /// <summary>Uses a horizontal scale with a marked midpoint for linear telemetry.</summary>
    public bool Strip { get; }

    /// <summary>Fits the caption, exact reading and linear scale into a 44-unit cockpit strip.</summary>
    public bool CompactStrip { get; set; }

    /// <summary>Reserves space beside a round dial caption for adjacent instruments.</summary>
    public float CaptionInset { get; set; } = 2;

    /// <summary>Exposes the actual reading independently of visual needle damping.</summary>
    public WFGaugeReading Reading => _read();

    /// <summary>Creates a passive instrument bound to a typed telemetry source.</summary>
    public WFGlassGauge(string caption, Func<WFGaugeReading> read, bool strip = false)
    {
        _caption = Loc.GetString(caption);
        _read = read;
        Strip = strip;
        MinWidth = 104;
        SetHeight = strip ? 64 : 142;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
        var cache = IoCManager.Resolve<IResourceCache>();
        var regular = cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf");
        var bold = cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf");
        _small = new VectorFont(regular, 9);
        _digits = new VectorFont(bold, 22);
        _dialDigits = new VectorFont(regular, 12);
        _stripDigits = new VectorFont(bold, WFInstrumentText.StripDigitSize);
        Sample();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _sampleElapsed += args.DeltaSeconds;
        if (_sampleElapsed >= 0.1f)
        {
            _sampleElapsed %= 0.1f;
            Sample();
        }
        _needle = _target == null ? null : _needle == null ? _target :
            _needle + (_target - _needle) * MathF.Min(1, args.DeltaSeconds * 12);
    }

    /// <summary>Samples formatted telemetry at ten hertz while the needle continues animating every frame.</summary>
    private void Sample()
    {
        var next = _read();
        if (_minimumText.Length == 0 || next.Minimum != _reading.Minimum || next.Maximum != _reading.Maximum)
        {
            _minimumText = next.Minimum.ToString("0.#");
            _maximumText = next.Maximum.ToString("0.#");
            _middleText = ((next.Minimum + next.Maximum) / 2).ToString("0.#");
        }
        _reading = next;
        _target = WFGaugeScale.Fraction(next.Value, next.Minimum, next.Maximum);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var color = _reading.Tint ?? WFInstrumentTheme.Accent;
        if (_target == null)
            color = WFInstrumentTheme.Muted;
        if (Strip)
        {
            DrawStrip(handle, color);
            return;
        }
        if (WFInstrumentTheme.Digital)
        {
            DrawDigital(handle, color);
            return;
        }
        var layout = WFGaugeScale.RoundDial(PixelWidth, PixelHeight, UIScale);
        var radius = layout.Radius;
        var center = layout.Center;
        handle.DrawCircle(center + new Vector2(0, 2 * UIScale), radius, Color.Black.WithAlpha(0.8f));
        handle.DrawCircle(center, radius, WFInstrumentTheme.Skin.EdgeLight);
        handle.DrawCircle(center, radius - 2 * UIScale, WFInstrumentTheme.Skin.EdgeSoft);
        handle.DrawCircle(center, radius - 4 * UIScale, WFInstrumentTheme.Skin.TextMuted);
        handle.DrawCircle(center, radius - 5 * UIScale, WFInstrumentTheme.Skin.Ink);
        var face = MathF.Max(1, radius - 8 * UIScale);
        handle.DrawCircle(center, face, WFInstrumentTheme.Skin.Glass);
        for (var i = 0; i <= 30; i++)
        {
            var angle = (135 + i * 9) * MathF.PI / 180;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            handle.DrawLine(center + direction * (face - (i % 5 == 0 ? 8 : 4) * UIScale),
                center + direction * face, i % 5 == 0 ? WFInstrumentTheme.Cream : WFInstrumentTheme.Muted);
        }
        Text(handle, _small, _middleText,
            center + new Vector2(0, -face * 0.65f + 3 * UIScale), face * 0.65f, WFInstrumentTheme.Muted);
        if (_needle is { } needle)
        {
            var angle = (135 + needle * 270) * MathF.PI / 180;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var side = new Vector2(-direction.Y, direction.X);
            handle.DrawLine(center + new Vector2(2 * UIScale) - direction * face * 0.14f,
                center + new Vector2(2 * UIScale) + direction * face * 0.84f, Color.Black);
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList,
                new[] { center - direction * face * 0.16f + side * 1.8f * UIScale,
                    center + direction * face * 0.88f, center - direction * face * 0.16f - side * 1.8f * UIScale }, color);
        }
        handle.DrawCircle(center, 4 * UIScale, WFInstrumentTheme.Skin.TextMuted);
        handle.DrawCircle(center, 2 * UIScale, WFInstrumentTheme.Skin.Edge);
        var minimum = WFGaugeScale.RoundLabelBounds(face, false).Translated(center);
        var maximum = WFGaugeScale.RoundLabelBounds(face, true).Translated(center);
        var readoutHeight = MathF.Min(18 * UIScale, face * 0.34f);
        var readoutTop = MathF.Min(center.Y + face * 0.17f, minimum.Top - 3 * UIScale - readoutHeight);
        var readout = UIBox2.FromDimensions(new Vector2(center.X - face * 0.73f, readoutTop), new Vector2(face * 1.46f, readoutHeight));
        handle.DrawRect(readout, WFInstrumentTheme.Skin.Ink);
        WFInstrumentText.DrawFitted(handle, _dialDigits, _reading.Text,
            new UIBox2(readout.TopLeft + new Vector2(UIScale), readout.BottomRight - new Vector2(UIScale)), UIScale, color);
        TextInBox(handle, _small, _minimumText, minimum, WFInstrumentTheme.Muted);
        TextInBox(handle, _small, _maximumText, maximum, WFInstrumentTheme.Muted);
        WFInstrumentGlass.Round(handle, center, face + 2 * UIScale, UIScale);
        Text(handle, _small, _caption, new Vector2(center.X, layout.CaptionBaseline), PixelWidth - 2 * CaptionInset * UIScale, WFInstrumentTheme.Cream);
    }

    /// <summary>Draws the reading as a figure with a smaller unit, or its whole text when it is not a measurement.</summary>
    private void Readout(DrawingHandleScreen handle, UIBox2 box, Color color)
    {
        if (_reading.Digits.Length > 0)
            WFInstrumentText.DrawReading(handle, _stripDigits, _reading.Digits, _reading.Unit, box, UIScale, color,
                WFInstrumentTheme.Skin.TextMuted);
        else
            WFInstrumentText.DrawFitted(handle, _stripDigits, _reading.Text, box, UIScale, color);
    }

    /// <summary>Draws a linear scale with its caption and exact reading above it, in both skins and both heights.</summary>
    private void DrawStrip(DrawingHandleScreen handle, Color color)
    {
        var skin = WFInstrumentTheme.Skin;
        var digital = WFInstrumentTheme.Digital;
        var bounds = PixelSizeBox;
        if (digital)
            WFConsoleDigital.Panel(handle, bounds, UIScale, skin.Glass, skin.EdgeSoft);
        else
            handle.DrawRect(bounds, skin.Glass);

        var compact = CompactStrip;
        var content = (compact ? 44 : 64) * UIScale;
        var y = WFGaugeScale.StripOffset(PixelHeight, content);
        var inset = 4 * UIScale;
        var header = UIBox2.FromDimensions(new Vector2(inset, y + 2 * UIScale),
            new Vector2(MathF.Max(0, PixelWidth - 2 * inset), (compact ? 20 : 22) * UIScale));
        var captionWidth = MathF.Min(header.Width * 0.4f, handle.GetDimensions(_small, _caption, UIScale).X + 8 * UIScale);
        TextInBox(handle, _small, _caption, UIBox2.FromDimensions(header.TopLeft, new Vector2(captionWidth, header.Height)),
            digital ? skin.TextMuted : skin.Text);
        // Keeps the figures clear of the strip's right edge and of the caption.
        Readout(handle, new UIBox2(header.Left + captionWidth + 4 * UIScale, header.Top + 2 * UIScale,
            header.Right - 3 * UIScale, header.Bottom - 2 * UIScale), color);

        var left = 10 * UIScale;
        var width = MathF.Max(0, PixelWidth - 2 * left);
        var top = y + (compact ? 24 : 27) * UIScale;
        var tall = (compact ? 7 : 14) * UIScale;
        if (digital)
        {
            var zero = WFGaugeScale.Fraction(0, _reading.Minimum, _reading.Maximum) ?? 0;
            for (var i = 0; i < 40; i++)
            {
                var fraction = (i + 0.5f) / 40;
                var active = _needle is { } needle && fraction >= MathF.Min(zero, needle) && fraction <= MathF.Max(zero, needle);
                handle.DrawRect(UIBox2.FromDimensions(new Vector2(left + width * i / 40, top + tall * 0.125f),
                    new Vector2(MathF.Max(1, width / 40 - 2 * UIScale), tall * 0.75f)), active ? color : skin.GlassLight);
            }
            if (_reading.Minimum < 0 && _reading.Maximum > 0)
            {
                var x = left + width * zero;
                handle.DrawLine(new Vector2(x, top - UIScale), new Vector2(x, top + tall), skin.Text);
            }
            if (_needle is { } position)
            {
                var x = left + width * position;
                handle.DrawLine(new Vector2(x, top - 2 * UIScale), new Vector2(x, top + tall), color);
            }
        }
        else
        {
            for (var i = 0; i <= 20; i++)
            {
                var x = left + width * i / 20;
                handle.DrawLine(new Vector2(x, top),
                    new Vector2(x, top + tall * (i == 10 ? 1 : i % 5 == 0 ? 0.75f : 0.35f)), skin.TextMuted);
            }
            if (_needle is { } position)
            {
                var x = left + width * position;
                var half = tall * 0.28f + UIScale;
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList,
                    new[] { new Vector2(x, top - UIScale), new Vector2(x - half, top + tall),
                        new Vector2(x + half, top + tall) }, color);
            }
        }

        var labelsTop = top + tall + 2 * UIScale;
        var labelsBottom = y + content - 3 * UIScale;
        TextInBox(handle, _small, _minimumText, new UIBox2(inset, labelsTop, PixelWidth * 0.32f, labelsBottom), skin.TextMuted, true);
        TextInBox(handle, _small, _middleText, new UIBox2(PixelWidth * 0.34f, labelsTop, PixelWidth * 0.66f, labelsBottom),
            digital ? skin.TextMuted : skin.Text, true);
        TextInBox(handle, _small, _maximumText, new UIBox2(PixelWidth * 0.68f, labelsTop, PixelWidth - inset, labelsBottom), skin.TextMuted, true);
        if (!digital)
            WFInstrumentGlass.Window(handle, bounds, UIScale);
    }

    /// <summary>Draws an arc dial whose figure and unit sit in the ring and whose caption sits just beneath it.</summary>
    private void DrawDigital(DrawingHandleScreen handle, Color color)
    {
        var skin = WFInstrumentTheme.Skin;
        WFConsoleDigital.Panel(handle, PixelSizeBox, UIScale, skin.Glass, skin.EdgeSoft);
        var layout = WFGaugeScale.RoundDial(PixelWidth, PixelHeight, UIScale);
        var radius = layout.Radius;
        var center = layout.Center;
        var start = MathF.PI * 0.75f;
        var sweep = MathF.PI * 1.5f;
        WFConsoleDigital.Arc(handle, center, radius, UIScale, start, start + sweep, skin.EdgeLight, ref _digitalVertices);
        WFConsoleDigital.Arc(handle, center, radius - 5 * UIScale, 4 * UIScale, start, start + sweep, skin.GlassLight, ref _digitalVertices);
        if (_needle is { } value)
        {
            var zero = WFGaugeScale.Fraction(0, _reading.Minimum, _reading.Maximum) ?? 0;
            WFConsoleDigital.Arc(handle, center, radius - 5 * UIScale, 4 * UIScale,
                start + MathF.Min(zero, value) * sweep, start + MathF.Max(zero, value) * sweep, color, ref _digitalVertices);
            var direction = new Vector2(MathF.Cos(start + value * sweep), MathF.Sin(start + value * sweep));
            WFConsoleDigital.Dot(handle, center + direction * (radius - 7 * UIScale), 3 * UIScale, color, ref _digitalVertices);
        }
        var (figure, unit) = WFGaugeScale.RingReadout(center, radius, UIScale);
        if (_reading.Digits.Length > 0)
        {
            WFInstrumentText.DrawFitted(handle, _digits, _reading.Digits, figure, UIScale, color);
            WFInstrumentText.DrawFitted(handle, _digits, _reading.Unit, unit, UIScale * WFInstrumentText.TagScale, skin.TextMuted);
        }
        else
            WFInstrumentText.DrawFitted(handle, _digits, _reading.Text, UIBox2.FromDimensions(figure.TopLeft,
                new Vector2(figure.Width, unit.Bottom - figure.Top)), UIScale, color);
        Text(handle, _small, _minimumText, center + new Vector2(-radius * 0.57f, radius * 0.86f), radius * 0.8f, skin.TextMuted);
        Text(handle, _small, _maximumText, center + new Vector2(radius * 0.57f, radius * 0.86f), radius * 0.8f, skin.TextMuted);
        Text(handle, _small, _caption, new Vector2(center.X, layout.CaptionBaseline), PixelWidth - 2 * CaptionInset * UIScale, skin.Text);
    }

    /// <summary>Fits the complete text height and width inside its reserved dial area; figures may fill their band.</summary>
    private void TextInBox(DrawingHandleScreen handle, Font font, string text, UIBox2 bounds, Color color, bool digits = false)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        var scale = digits
            ? WFInstrumentText.FitDigits(handle, font, text, UIScale, bounds.Width, bounds.Height)
            : WFInstrumentText.FitScale(handle, font, text, UIScale, bounds.Width, bounds.Height);
        if (scale == 0)
            return;
        var drawn = handle.GetDimensions(font, text, scale).X;
        var position = bounds.Center - new Vector2(drawn / 2, font.GetHeight(scale) / 2f);
        handle.DrawString(font, position, text, scale, color);
    }

    private void Text(DrawingHandleScreen handle, Font font, string? text, Vector2 position, float width, Color color)
    {
        text ??= _offline;
        var scale = WFInstrumentText.FitScale(handle, font, text, UIScale, width);
        if (scale == 0)
            return;
        var drawn = handle.GetDimensions(font, text, scale).X;
        handle.DrawString(font, position - new Vector2(drawn / 2, font.GetAscent(scale)), text, scale, color);
    }
}
