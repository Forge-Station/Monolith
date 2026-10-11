using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._WF.CombatConsole;

/// <summary>Displays the ship's real heading on an engraved compass instrument.</summary>
public sealed class WFHeadingInstrument : Control
{
    private static readonly Vector2[] ScrewPoints = { new(10, 10), new(166, 10), new(10, 166), new(166, 166) };
    private readonly Func<double?> _heading;
    private DrawVertexUV2DColor[] _digitalVertices = Array.Empty<DrawVertexUV2DColor>();
    private readonly Font _font;
    private readonly Font _scaleFont;
    private readonly string[] _scaleLabels = new string[12];
    private double? _headingValue;
    private string _headingText = string.Empty;
    private float _sampleElapsed;

    public WFHeadingInstrument(Func<double?> heading)
    {
        _heading = heading;
        MinHeight = 170;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
        var cache = IoCManager.Resolve<IResourceCache>();
        _font = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 22);
        _scaleFont = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf"), 9);
        for (var i = 0; i < _scaleLabels.Length; i++)
            _scaleLabels[i] = (i * 30).ToString();
        _headingValue = ReadHeading();
        Sample();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _headingValue = ReadHeading();
        _sampleElapsed += args.DeltaSeconds;
        if (_sampleElapsed < 0.1f)
            return;
        _sampleElapsed %= 0.1f;
        Sample();
    }

    private double? ReadHeading() => _heading() is { } value && double.IsFinite(value) ? value : null;

    private void Sample()
    {
        _headingText = _headingValue is { } value
            ? Loc.GetString("wf-console-bearing-value", ("heading", $"{(Math.Round((value % 360 + 360) % 360, 1) % 360):000.0}"))
            : Loc.GetString("wf-console-bearing-offline");
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (WFInstrumentTheme.Digital)
        {
            DrawDigital(handle);
            return;
        }
        var skin = WFInstrumentTheme.Skin;
        var (bounds, textBox) = WFGaugeScale.HeadingPanel(PixelWidth, PixelHeight, UIScale);
        var side = bounds.Width;
        var scale = side / 176;
        var origin = bounds.TopLeft;
        WFConsoleMetal.MetalPanel(handle, bounds, UIScale, skin.EdgeSoft);
        foreach (var point in ScrewPoints)
            WFConsoleMetal.Screw(handle, origin + point * scale, 3.4f * scale, skin.TextMuted);

        var center = origin + new Vector2(side / 2);
        var radius = side * 0.435f;
        WFConsoleDigital.Dot(handle, center + new Vector2(0, 2 * scale), radius + 3 * scale,
            Color.Black.WithAlpha(0.75f), ref _digitalVertices);
        WFConsoleDigital.Dot(handle, center, radius, skin.EdgeLight, ref _digitalVertices);
        WFConsoleDigital.Dot(handle, center, radius - 2 * scale, skin.Edge, ref _digitalVertices);
        WFConsoleDigital.Arc(handle, center, radius - scale, scale, MathF.PI, MathF.Tau,
            skin.TextMuted.WithAlpha(0.65f), ref _digitalVertices);
        WFConsoleDigital.Dot(handle, center, radius - 5 * scale, skin.Ink, ref _digitalVertices);
        var face = radius - 7 * scale;
        WFConsoleDigital.Dot(handle, center, face, skin.Glass, ref _digitalVertices);
        WFConsoleDigital.Arc(handle, center, face * 0.52f, scale, 0, MathF.Tau,
            skin.EdgeSoft.WithAlpha(0.5f), ref _digitalVertices);
        for (var i = 0; i < 72; i++)
        {
            var angle = i * MathF.Tau / 72 - MathF.PI / 2;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var major = i % 6 == 0;
            var length = (major ? 8 : i % 3 == 0 ? 5 : 3) * scale;
            handle.DrawLine(center + direction * (face - length), center + direction * (face - scale),
                major ? skin.Text : skin.TextMuted.WithAlpha(0.65f));
        }
        var fontScale = WFGaugeScale.FontScale(scale * 0.92f);
        for (var i = 0; fontScale > 0 && i < _scaleLabels.Length; i++)
        {
            var angle = i * MathF.Tau / 12 - MathF.PI / 2;
            var position = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * face * 0.73f;
            var label = _scaleLabels[i];
            var dimensions = handle.GetDimensions(_scaleFont, label, fontScale);
            handle.DrawString(_scaleFont, position - dimensions / 2, label, fontScale,
                i % 3 == 0 ? skin.Text : skin.TextMuted);
        }
        var heading = _headingValue;
        if (heading is { } degrees)
        {
            var angle = (float) MathHelper.DegreesToRadians(degrees - 90);
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var across = new Vector2(-direction.Y, direction.X);
            var tip = center + direction * face * 0.89f;
            var tail = center - direction * face * 0.19f;
            var shadow = new Vector2(1.5f * scale);
            handle.DrawLine(tail + shadow, tip + shadow, Color.Black.WithAlpha(0.8f));
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList,
                new[] { tail + across * 2 * scale, tip, tail - across * 2 * scale }, skin.Accent);
            handle.DrawLine(center, tip, skin.Caution.WithAlpha(0.6f));
        }
        WFConsoleDigital.Dot(handle, center, 5 * scale, skin.EdgeLight, ref _digitalVertices);
        WFConsoleDigital.Dot(handle, center, 3 * scale, heading == null ? skin.TextMuted : skin.Accent, ref _digitalVertices);
        WFInstrumentGlass.Round(handle, center, face, scale);
        WFInstrumentText.DrawFitted(handle, _font, _headingText, textBox, UIScale, heading == null ? skin.TextMuted : skin.Accent);
    }
    private void DrawDigital(DrawingHandleScreen handle)
    {
        var skin = WFInstrumentTheme.Skin;
        WFConsoleDigital.Panel(handle, new UIBox2(Vector2.Zero, PixelSize), UIScale, skin.Glass, skin.EdgeSoft);
        var radius = MathF.Max(1, MathF.Min(PixelWidth, PixelHeight) / 2f - 3 * UIScale);
        var center = new Vector2(PixelWidth / 2, PixelHeight / 2);
        WFConsoleDigital.Arc(handle, center, radius, UIScale, 0, MathF.Tau, skin.EdgeLight, ref _digitalVertices);
        for (var i = 0; i < 36; i++)
        {
            var angle = i * MathF.Tau / 36 - MathF.PI / 2;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            handle.DrawLine(center + direction * (radius - (i % 9 == 0 ? 8 : 4) * UIScale),
                center + direction * radius, i % 9 == 0 ? skin.Accent : skin.EdgeLight);
        }
        var heading = _headingValue;
        if (heading is { } degrees)
        {
            var angle = (float) MathHelper.DegreesToRadians(degrees - 90);
            WFConsoleDigital.Arc(handle, center, radius - 12 * UIScale, 4 * UIScale, angle - 0.2f, angle + 0.2f, skin.Accent, ref _digitalVertices);
            var tip = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (radius - 5 * UIScale);
            WFConsoleDigital.Dot(handle, tip, 3 * UIScale, skin.Accent, ref _digitalVertices);
        }
        var readout = new Vector2(MathF.Max(1, (radius - 16 * UIScale) * 2), 28 * UIScale);
        WFInstrumentText.DrawFitted(handle, _font, _headingText, UIBox2.FromDimensions(center - readout / 2, readout), UIScale,
            heading == null ? skin.TextMuted : skin.Accent);
        handle.DrawLine(center + new Vector2(-18, 17) * UIScale, center + new Vector2(18, 17) * UIScale, skin.AccentDim);
    }

}
