using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Branding;

/// <summary>The six logo bars, drawn crisp at any UI scale: as the upright mark, or flat as a spectrum rule.</summary>
public sealed class WFBrandBars : Control
{
    /// <summary>Bar colours sampled from logo.png, left to right.</summary>
    public static readonly Color[] Colors =
    {
        Color.FromHex("#56D4DC"),
        Color.FromHex("#7BC86F"),
        Color.FromHex("#E5C25B"),
        Color.FromHex("#E0863F"),
        Color.FromHex("#D95F4C"),
        Color.FromHex("#C06BB4"),
    };

    /// <summary>True for a full-width rule of six equal segments, false for the upright mark.</summary>
    public bool Rule { get; init; }

    /// <summary>Opacity of every bar.</summary>
    public float Alpha { get; set; } = 1f;

    public WFBrandBars()
    {
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = PixelSize;
        if (Rule)
        {
            var segment = size.X / (float) Colors.Length;
            for (var i = 0; i < Colors.Length; i++)
            {
                var left = MathF.Round(i * segment);
                var right = MathF.Round((i + 1) * segment);
                handle.DrawRect(new UIBox2(left, 0, right, size.Y), Colors[i].WithAlpha(Alpha));
            }

            return;
        }

        // Bars and gaps snap to whole pixels so the mark never blurs.
        var gap = MathF.Max(1f, MathF.Round(UIScale));
        var bar = MathF.Max(1f, MathF.Floor((size.X - gap * (Colors.Length - 1)) / Colors.Length));
        var x = MathF.Floor((size.X - (bar * Colors.Length + gap * (Colors.Length - 1))) / 2f);
        for (var i = 0; i < Colors.Length; i++)
        {
            handle.DrawRect(new UIBox2(x, 0, x + bar, size.Y), Colors[i].WithAlpha(Alpha));
            x += bar + gap;
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return Vector2.Zero;
    }
}
