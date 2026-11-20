using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.Cockpit;

/// <summary>A compact bank of live status lamps with full descriptions on hover.</summary>
public sealed class WFCockpitStatusLights : GridContainer
{
    /// <summary>Creates two columns of lamps bound to the existing flight telemetry.</summary>
    public WFCockpitStatusLights(params (string Caption, Func<WFCockpitStatusReading> Read)[] readings)
    {
        Name = "CockpitStatus";
        Columns = 2;
        HSeparationOverride = 4;
        VSeparationOverride = 3;
        HorizontalExpand = true;
        foreach (var (caption, read) in readings)
            AddChild(new WFCockpitStatusLamp(caption, read));
    }
}

/// <summary>A theme-aware indicator whose state comes directly from a live telemetry reader.</summary>
public sealed class WFCockpitStatusLamp : Control
{
    private readonly string _caption;
    private readonly Func<WFCockpitStatusReading> _read;
    private string? _detail;
    private string? _fontSkin;
    private Font? _font;
    private DrawVertexUV2DColor[] _vertices = Array.Empty<DrawVertexUV2DColor>();

    /// <summary>Exposes the current source reading independently of drawing.</summary>
    public WFCockpitStatusReading Reading => _read();

    /// <summary>Creates a small indicator without copying or predicting its authoritative source.</summary>
    public WFCockpitStatusLamp(string caption, Func<WFCockpitStatusReading> read)
    {
        _caption = Loc.GetString(caption);
        _read = read;
        Name = caption;
        HorizontalExpand = true;
        SetHeight = 21;
        MinWidth = 64;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Pass;
        UpdateTooltip();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        var detail = Reading.Detail;
        if (_detail == detail)
            return;
        _detail = detail;
        ToolTip = Loc.GetString(detail);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var skin = WFInstrumentTheme.Skin;
        if (_fontSkin != skin.Id)
        {
            _font = IoCManager.Resolve<IResourceCache>().GetFont(skin.MenuFonts, 10);
            _fontSkin = skin.Id;
        }
        var reading = Reading;
        var lit = reading.State is WFCockpitLampState.Good or WFCockpitLampState.Active or WFCockpitLampState.Caution
            or WFCockpitLampState.Fault;
        var color = reading.State switch
        {
            WFCockpitLampState.Good => skin.Good,
            WFCockpitLampState.Active => skin.Accent,
            WFCockpitLampState.Caution => skin.Caution,
            WFCockpitLampState.Fault => skin.Danger,
            _ => skin.EdgeLight,
        };
        if (WFInstrumentTheme.Digital)
            DrawDigital(handle, color, lit);
        else
            DrawAnalog(handle, color, lit);

        var textColor = reading.State == WFCockpitLampState.Unavailable ? skin.TextDisabled :
            lit ? WFInstrumentTheme.Digital ? color : skin.Text : skin.TextMuted;
        var left = 24 * UIScale;
        var scale = WFInstrumentText.FitScale(handle, _font!, _caption, UIScale,
            PixelWidth - left - 5 * UIScale, PixelHeight - 2 * UIScale);
        if (scale == 0)
            return;
        var position = new Vector2(left, (PixelHeight - _font!.GetHeight(scale)) / 2f);
        if (!WFInstrumentTheme.Digital)
            handle.DrawString(_font, position + new Vector2(0, UIScale), _caption, scale, skin.Ink);
        handle.DrawString(_font, position, _caption, scale, textColor);
    }

    /// <summary>Sets a ribbed glass lens into a recessed, machined metal socket.</summary>
    private void DrawAnalog(DrawingHandleScreen handle, Color color, bool lit)
    {
        var skin = WFInstrumentTheme.Skin;
        WFConsoleMetal.Bevel(handle, PixelSizeBox, UIScale, skin.GlassRaised, skin.EdgeSoft, inset: true);
        var center = new Vector2(11 * UIScale, PixelHeight / 2f);
        WFConsoleDigital.Dot(handle, center + new Vector2(0, UIScale), 8 * UIScale, skin.Ink, ref _vertices);
        WFConsoleDigital.Dot(handle, center, 7.4f * UIScale, skin.EdgeLight, ref _vertices);
        WFConsoleDigital.Dot(handle, center + new Vector2(0, 0.5f * UIScale), 6.3f * UIScale, skin.Ink, ref _vertices);
        WFConsoleDigital.Arc(handle, center, 7.2f * UIScale, UIScale,
            MathF.PI * 1.08f, MathF.PI * 1.83f, skin.TextMuted.WithAlpha(0.65f), ref _vertices);

        var lens = Color.InterpolateBetween(skin.Ink, lit ? color : skin.EdgeLight, lit ? 0.64f : 0.16f);
        WFConsoleDigital.Dot(handle, center, 5.3f * UIScale, lens, ref _vertices);
        WFConsoleDigital.Dot(handle, center + new Vector2(0, 0.6f * UIScale), 4.2f * UIScale,
            lit ? color.WithAlpha(0.85f) : skin.GlassRaised, ref _vertices);
        if (lit)
            WFConsoleDigital.Dot(handle, center + new Vector2(0, 0.9f * UIScale), 2.7f * UIScale,
                Color.InterpolateBetween(color, skin.Text, 0.25f), ref _vertices);
        for (var i = -1; i <= 1; i++)
        {
            var y = center.Y + i * 2 * UIScale;
            handle.DrawLine(new Vector2(center.X - 3.6f * UIScale, y), new Vector2(center.X + 3.6f * UIScale, y),
                skin.Ink.WithAlpha(lit ? 0.16f : 0.25f));
        }
        WFConsoleDigital.Arc(handle, center, 4.2f * UIScale, 0.7f * UIScale,
            MathF.PI * 1.13f, MathF.PI * 1.74f, skin.Text.WithAlpha(lit ? 0.65f : 0.18f), ref _vertices);
        if (PixelWidth > 92 * UIScale)
            WFConsoleMetal.Screw(handle, new Vector2(PixelWidth - 6 * UIScale, center.Y), 1.8f * UIScale, skin.EdgeLight);
    }

    /// <summary>Uses a luminous light guide and thin traces in a flush digital annunciator.</summary>
    private void DrawDigital(DrawingHandleScreen handle, Color color, bool lit)
    {
        var skin = WFInstrumentTheme.Skin;
        handle.DrawRect(PixelSizeBox, skin.Ink);
        var bounds = new UIBox2(Vector2.One * UIScale, PixelSize - Vector2.One * UIScale);
        handle.DrawRect(bounds, Color.InterpolateBetween(skin.Glass, color, lit ? 0.06f : 0));
        var cut = 4 * UIScale;
        var trace = lit ? color.WithAlpha(0.42f) : skin.EdgeSoft;
        handle.DrawLine(bounds.TopLeft, new Vector2(bounds.Right - cut, bounds.Top), trace);
        handle.DrawLine(new Vector2(bounds.Right - cut, bounds.Top), new Vector2(bounds.Right, bounds.Top + cut), trace);
        handle.DrawLine(new Vector2(bounds.Right, bounds.Top + cut), bounds.BottomRight, skin.EdgeSoft);
        handle.DrawLine(bounds.BottomRight, new Vector2(bounds.Left + cut, bounds.Bottom), skin.EdgeSoft);
        handle.DrawLine(new Vector2(bounds.Left + cut, bounds.Bottom), new Vector2(bounds.Left, bounds.Bottom - cut), skin.EdgeSoft);

        var center = new Vector2(11 * UIScale, PixelHeight / 2f);
        if (lit)
        {
            LightGuide(handle, center, 14 * UIScale, 8 * UIScale, color.WithAlpha(0.055f));
            LightGuide(handle, center, 12 * UIScale, 5 * UIScale, color.WithAlpha(0.15f));
        }
        LightGuide(handle, center, 10 * UIScale, 3 * UIScale, lit ? color : skin.EdgeLight);
        if (lit)
            handle.DrawLine(center - new Vector2(3 * UIScale, 0), center + new Vector2(3 * UIScale, 0),
                Color.InterpolateBetween(color, skin.Text, 0.65f));
        var underline = PixelHeight - 3 * UIScale;
        handle.DrawLine(new Vector2(24 * UIScale, underline), new Vector2(PixelWidth - 7 * UIScale, underline),
            lit ? color.WithAlpha(0.16f) : skin.EdgeSoft.WithAlpha(0.45f));
    }

    private void LightGuide(DrawingHandleScreen handle, Vector2 center, float width, float height, Color color)
    {
        var radius = height / 2;
        var half = new Vector2((width - height) / 2, 0);
        handle.DrawRect(new UIBox2(center - half - new Vector2(0, radius), center + half + new Vector2(0, radius)), color);
        WFConsoleDigital.Dot(handle, center - half, radius, color, ref _vertices);
        WFConsoleDigital.Dot(handle, center + half, radius, color, ref _vertices);
    }
}
