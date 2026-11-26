using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._WF.CombatConsole;

/// <summary>Draws a common warning-lamp lens in the selected mechanical or digital instrument style.</summary>
public static class WFConsoleAnnunciator
{
    /// <summary>Returns the legend inset after drawing the inactive or illuminated lens.</summary>
    public static UIBox2 Face(DrawingHandleScreen handle, UIBox2 box, float scale, Color color, bool lit)
    {
        var skin = WFInstrumentTheme.Skin;
        var inner = new UIBox2(box.TopLeft + new Vector2(4 * scale), box.BottomRight - new Vector2(4 * scale));
        if (WFInstrumentTheme.Digital)
        {
            WFConsoleDigital.Panel(handle, box, scale, skin.Ink, lit ? color : skin.EdgeSoft);
            handle.DrawRect(inner, Color.InterpolateBetween(skin.Glass, color, lit ? 0.14f : 0.015f));
            if (lit)
            {
                handle.DrawRect(new UIBox2(inner.Left, inner.Top, inner.Left + 2 * scale, inner.Bottom), color);
                handle.DrawRect(new UIBox2(inner.Right - 2 * scale, inner.Top, inner.Right, inner.Bottom), color);
                handle.DrawLine(new Vector2(inner.Left + 6 * scale, inner.Bottom - 2 * scale),
                    new Vector2(inner.Right - 6 * scale, inner.Bottom - 2 * scale), color.WithAlpha(0.65f));
            }
        }
        else
        {
            WFConsoleMetal.Bevel(handle, box, scale, skin.EdgeSoft, skin.EdgeLight);
            WFConsoleMetal.Bevel(handle, inner, scale,
                Color.InterpolateBetween(skin.Ink, color, lit ? 0.78f : 0.055f), skin.EdgeLight, inset: true);
            if (lit)
            {
                var glow = new UIBox2(inner.TopLeft + new Vector2(3 * scale), inner.BottomRight - new Vector2(3 * scale));
                handle.DrawRect(glow, color.WithAlpha(0.34f));
            }
            for (var i = 1; i < 8; i++)
            {
                var x = inner.Left + inner.Width * i / 8;
                handle.DrawLine(new Vector2(x, inner.Top + 2 * scale), new Vector2(x, inner.Bottom - 2 * scale),
                    skin.Ink.WithAlpha(lit ? 0.09f : 0.2f));
            }
            WFInstrumentGlass.Window(handle, inner, scale);
        }
        return inner;
    }
}
