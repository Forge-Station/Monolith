using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._WF.CombatConsole;

/// <summary>Draws original painted metal, machined edges and fasteners at the current UI scale.</summary>
public static class WFConsoleMetal
{
    /// <summary>Draws a raised or recessed metal lip without changing the control's content bounds.</summary>
    public static void Bevel(DrawingHandleScreen handle, UIBox2 box, float scale, Color fill, Color edge, bool inset = false)
    {
        if (box.Width <= 0 || box.Height <= 0)
            return;
        handle.DrawRect(box, fill);
        var width = MathF.Min(scale, MathF.Min(box.Width, box.Height) / 4);
        var light = Color.InterpolateBetween(edge, WFInstrumentTheme.Cream, 0.2f);
        var shadow = Color.InterpolateBetween(fill, WFInstrumentTheme.Ink, 0.7f);
        handle.DrawRect(box, shadow, false);
        var a = box.TopLeft + Vector2.One * width;
        var b = box.BottomRight - Vector2.One * width;
        handle.DrawLine(a, new Vector2(b.X, a.Y), inset ? shadow : light);
        handle.DrawLine(a, new Vector2(a.X, b.Y), inset ? shadow : edge);
        handle.DrawLine(new Vector2(a.X, b.Y), b, inset ? light : shadow);
        handle.DrawLine(new Vector2(b.X, a.Y), b, inset ? edge : shadow);
    }

    /// <summary>Adds low-contrast brushing and deterministic edge wear to a painted instrument face.</summary>
    public static void MetalPanel(DrawingHandleScreen handle, UIBox2 box, float scale, Color fill)
    {
        Bevel(handle, box, scale, fill, WFInstrumentTheme.Skin.EdgeLight);
        if (box.Width <= 6 * scale || box.Height <= 6 * scale)
            return;
        var inner = new UIBox2(box.TopLeft + new Vector2(2 * scale), box.BottomRight - new Vector2(2 * scale));
        var bands = Math.Clamp((int) (inner.Height / (2 * scale)), 8, 64);
        for (var i = 0; i < bands; i++)
        {
            var top = inner.Top + inner.Height * i / bands;
            var bottom = inner.Top + inner.Height * (i + 1) / bands;
            var depth = (i + 0.5f) / bands;
            handle.DrawRect(new UIBox2(inner.Left, top, inner.Right, bottom),
                Color.White.WithAlpha(0.012f * (1 - depth) * (1 - depth)));
        }
        var strokes = Math.Clamp((int) (inner.Height / (5 * scale)), 4, 36);
        for (var i = 0; i < strokes; i++)
        {
            var y = inner.Top + inner.Height * (i + 0.5f) / strokes;
            var start = inner.Left + inner.Width * ((i * 37 % 23) / 31f);
            var length = MathF.Min(inner.Width * (0.08f + (i % 4) * 0.035f), inner.Right - start);
            handle.DrawLine(new Vector2(start, y), new Vector2(start + length, y),
                i % 3 == 0 ? Color.White.WithAlpha(0.012f) : Color.Black.WithAlpha(0.06f));
        }
        var wear = WFInstrumentTheme.Cream.WithAlpha(0.16f);
        var scratch = MathF.Min(12 * scale, inner.Width / 8);
        handle.DrawLine(new Vector2(inner.Left + scratch, inner.Top), new Vector2(inner.Left + scratch * 2, inner.Top), wear);
        handle.DrawLine(new Vector2(inner.Right - scratch * 2, inner.Bottom), new Vector2(inner.Right - scratch, inner.Bottom), wear.WithAlpha(0.07f));
    }

    /// <summary>Draws a recessed slotted fastener with a lit metal shoulder.</summary>
    public static void Screw(DrawingHandleScreen handle, Vector2 center, float radius, Color metal)
    {
        if (radius <= 0)
            return;
        handle.DrawCircle(center, radius, WFInstrumentTheme.Ink);
        handle.DrawCircle(center - new Vector2(radius * 0.1f), radius * 0.73f, metal);
        handle.DrawLine(center + new Vector2(-0.4f, 0.35f) * radius,
            center + new Vector2(0.4f, -0.35f) * radius, WFInstrumentTheme.Ink);
        handle.DrawLine(center + new Vector2(-0.45f, -0.38f) * radius,
            center + new Vector2(0.05f, -0.61f) * radius, WFInstrumentTheme.Cream.WithAlpha(0.3f));
    }
}
