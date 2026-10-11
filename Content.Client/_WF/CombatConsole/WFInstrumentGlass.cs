using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.CombatConsole;

/// <summary>Draws reflections and curved rim highlights over instrument faces without handling input.</summary>
public static class WFInstrumentGlass
{
    private const int RoundQuads = 19;
    private static readonly DrawVertexUV2DColor[] RoundVertices = new DrawVertexUV2DColor[RoundQuads * 6];
    private static readonly DrawVertexUV2DColor[] WindowVertices = new DrawVertexUV2DColor[4];

    /// <summary>Adds a convex lens reflection contained within a circular dial.</summary>
    public static void Round(DrawingHandleScreen handle, Vector2 center, float radius, float scale)
    {
        var index = 0;
        for (var i = 0; i < 18; i++)
        {
            var y1 = -0.94f + i * 0.031f;
            var y2 = y1 + 0.032f;
            var w1 = MathF.Sqrt(1 - y1 * y1) * 0.94f;
            var w2 = MathF.Sqrt(1 - y2 * y2) * 0.94f;
            index = Quad(handle, index, center + new Vector2(-w1, y1) * radius, center + new Vector2(w1, y1) * radius,
                center + new Vector2(w2, y2) * radius, center + new Vector2(-w2, y2) * radius,
                Color.White.WithAlpha(0.032f - i * 0.0011f));
        }
        index = Quad(handle, index, center + new Vector2(-0.65f, -0.55f) * radius, center + new Vector2(-0.36f, -0.83f) * radius,
            center + new Vector2(0.68f, 0.37f) * radius, center + new Vector2(0.46f, 0.68f) * radius,
            WFInstrumentTheme.Cream.WithAlpha(0.012f));
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
            new ReadOnlySpan<DrawVertexUV2DColor>(RoundVertices, 0, index));
        for (var i = 0; i < 28; i++)
        {
            var start = MathF.PI * (1.08f + i * 0.022f);
            var end = start + MathF.PI * 0.022f;
            handle.DrawLine(center + new Vector2(MathF.Cos(start), MathF.Sin(start)) * (radius - scale),
                center + new Vector2(MathF.Cos(end), MathF.Sin(end)) * (radius - scale),
                WFInstrumentTheme.Cream.WithAlpha(0.45f));
        }
    }

    /// <summary>Adds reflected light and an inset edge to a rectangular glass window.</summary>
    public static void Window(DrawingHandleScreen handle, UIBox2 box, float scale)
    {
        if (WFInstrumentTheme.Digital)
        {
            handle.DrawLine(box.TopLeft, new Vector2(box.Right, box.Top), WFInstrumentTheme.Skin.EdgeLight.WithAlpha(0.5f));
            return;
        }
        var top = box.TopLeft;
        var bottom = box.Top + box.Height * 0.45f;
        var light = Color.FromSrgb(WFInstrumentTheme.Cream.WithAlpha(0.025f) * handle.Modulate);
        var clear = light.WithAlpha(0);
        // Interpolate one continuous surface so reflection strips cannot overlap into bright seams.
        WindowVertices[0] = new DrawVertexUV2DColor(top, light);
        WindowVertices[1] = new DrawVertexUV2DColor(new Vector2(box.Right, box.Top), light);
        WindowVertices[2] = new DrawVertexUV2DColor(new Vector2(box.Left, bottom), clear);
        WindowVertices[3] = new DrawVertexUV2DColor(new Vector2(box.Right, bottom), clear);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, Texture.White, WindowVertices);
        handle.DrawLine(top + new Vector2(scale, scale), new Vector2(box.Right - scale, box.Top + scale),
            WFInstrumentTheme.Cream.WithAlpha(0.38f));
        handle.DrawLine(new Vector2(box.Left, box.Bottom - scale), new Vector2(box.Right, box.Bottom - scale),
            Color.Black.WithAlpha(0.8f));
        handle.DrawRect(box, WFInstrumentTheme.Skin.EdgeLight.WithAlpha(0.6f), false);
    }

    /// <summary>Appends one flat quad to the shared reflection buffer, tinted like a single-colour primitive.</summary>
    private static int Quad(DrawingHandleScreen handle, int index, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        var tint = Color.FromSrgb(color * handle.Modulate);
        RoundVertices[index++] = new DrawVertexUV2DColor(a, tint);
        RoundVertices[index++] = new DrawVertexUV2DColor(b, tint);
        RoundVertices[index++] = new DrawVertexUV2DColor(c, tint);
        RoundVertices[index++] = new DrawVertexUV2DColor(a, tint);
        RoundVertices[index++] = new DrawVertexUV2DColor(c, tint);
        RoundVertices[index++] = new DrawVertexUV2DColor(d, tint);
        return index;
    }
}

/// <summary>Retains a bound text or status control inside a recessed glass display.</summary>
public sealed class WFGlassReadout : PanelContainer
{
    /// <summary>Rehouses a bound control, optionally covering a live bar with engraved graduations.</summary>
    public WFGlassReadout(Control content, bool meter = false)
    {
        HorizontalExpand = true;
        PanelOverride = new WFConsoleFrameStyleBox(7);
        AddChild(WFInstrumentTheme.Detach(content));
        if (meter)
            AddChild(new WFGlassMeterLens());
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        WFInstrumentGlass.Window(handle, new UIBox2(Vector2.Zero, PixelSize), UIScale);
    }
}

/// <summary>A transparent graduated cover for a live integrity bar.</summary>
public sealed class WFGlassMeterLens : Control
{
    /// <summary>Leaves pointer input to the underlying control.</summary>
    public WFGlassMeterLens() => MouseFilter = MouseFilterMode.Ignore;

    protected override void Draw(DrawingHandleScreen handle)
    {
        WFInstrumentGlass.Window(handle, new UIBox2(Vector2.Zero, PixelSize), UIScale);
        for (var i = 1; i < 20; i++)
        {
            var x = PixelWidth * i / 20f;
            var length = (i % 5 == 0 ? 7 : 3) * UIScale;
            handle.DrawLine(new Vector2(x, 0), new Vector2(x, length), Color.White.WithAlpha(0.45f));
            handle.DrawLine(new Vector2(x, PixelHeight), new Vector2(x, PixelHeight - length), Color.Black.WithAlpha(0.4f));
        }
    }
}
