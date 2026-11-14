using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Shared._WF.AsteroidMining;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.AsteroidMining;

/// <summary>Drawing and controls shared by the mining laser panel, the cockpit's mining page and its asteroid scanner.</summary>
public static class WFMiningGauges
{
    private static readonly float[] PowerSteps = { -0.1f, -0.01f, 0.01f, 0.1f };

    /// <summary>A row of buttons that step the laser power down or up by ten and by one percent.</summary>
    public static BoxContainer PowerStepButtons(Action<float> step)
    {
        var row = WFInstrumentTheme.Row();
        row.SeparationOverride = 2;
        foreach (var amount in PowerSteps)
        {
            var button = new Button
            {
                Text = Loc.GetString(amount > 0 ? "wf-mining-gunnery-power-up" : "wf-mining-gunnery-power-down",
                    ("amount", (int) MathF.Round(MathF.Abs(amount) * 100))),
                HorizontalExpand = true,
            };
            button.AddStyleClass("WfCompact");
            button.OnPressed += _ => step(amount);
            row.AddChild(button);
        }

        return row;
    }

    /// <summary>Writes a rock's ores onto reused labels, adding one only when a rock has more ores than any before it.</summary>
    public static void ShowOres(Control parent, List<Label> labels, WFMiningBeamOreEntry[] ores)
    {
        for (var i = labels.Count; i < ores.Length; i++)
        {
            var label = WFInstrumentTheme.Label("wf-mining-gunnery-idle");
            label.ClipText = true;
            label.HorizontalExpand = true;
            parent.AddChild(label);
            labels.Add(label);
        }

        for (var i = 0; i < labels.Count; i++)
        {
            var label = labels[i];
            label.Visible = i < ores.Length;
            if (!label.Visible)
                continue;

            var ore = ores[i];
            label.FontColorOverride = ore.Color;
            label.Text = Loc.GetString("wf-mining-gunnery-ore", ("name", ore.Name), ("percent", (int) MathF.Round(ore.Fraction * 100)));
        }
    }

    /// <summary>The colour of the heat scale at a heat: cold, the cracking window, the hot band past it, then overcharge.</summary>
    public static Color Band(float heat, float low, float high)
    {
        var skin = WFInstrumentTheme.Skin;
        if (heat >= WFAsteroidHeat.OverchargeLine)
            return skin.Danger;
        if (heat > high)
            return skin.Caution;
        return heat >= low ? skin.Good : skin.TextMuted;
    }

    /// <summary>
    /// The heat scale as a ring of arcs from <paramref name="start"/> over <paramref name="sweep"/> radians, which reach
    /// <see cref="WFAsteroidHeat.MeterScale"/>: cold, the window, the hot band and the overcharge zone.
    /// </summary>
    public static void HeatArc(DrawingHandleScreen handle, Vector2 center, float radius, float width, float start, float sweep,
        float low, float high, float alpha, bool flash, ref DrawVertexUV2DColor[] vertices)
    {
        var skin = WFInstrumentTheme.Skin;
        Segment(0f, low, skin.GlassLight, ref vertices);
        Segment(low, high, skin.Good, ref vertices);
        Segment(high, WFAsteroidHeat.OverchargeLine, skin.Caution.WithAlpha(0.8f), ref vertices);
        Segment(WFAsteroidHeat.OverchargeLine, WFAsteroidHeat.MeterScale, skin.Danger.WithAlpha(flash ? 1f : 0.7f), ref vertices);

        void Segment(float from, float to, Color color, ref DrawVertexUV2DColor[] buffer)
        {
            from = Math.Clamp(from, 0f, WFAsteroidHeat.MeterScale);
            to = Math.Clamp(to, 0f, WFAsteroidHeat.MeterScale);
            if (to <= from)
                return;
            WFConsoleDigital.Arc(handle, center, radius, width, start + sweep * from / WFAsteroidHeat.MeterScale,
                start + sweep * to / WFAsteroidHeat.MeterScale, color.WithAlpha(color.A * alpha), ref buffer);
        }
    }

    /// <summary>The angle, on a scale from <paramref name="start"/> over <paramref name="sweep"/> radians, of a heat.</summary>
    public static float HeatAngle(float heat, float start, float sweep)
    {
        return start + sweep * Math.Clamp(heat / WFAsteroidHeat.MeterScale, 0f, 1f);
    }

    /// <summary>A rock's make-up as one bar: each ore in its colour by share, then plain rock, under graduations.</summary>
    public static void Composition(DrawingHandleScreen handle, UIBox2 box, IReadOnlyList<WFMiningBeamOreEntry> ores, float scale,
        float alpha = 1f)
    {
        if (box.Width <= 0 || box.Height <= 0)
            return;

        var skin = WFInstrumentTheme.Skin;
        handle.DrawRect(box, skin.GlassLight.WithAlpha(0.55f * alpha));
        var x = box.Left;
        for (var i = 0; i < ores.Count; i++)
        {
            var width = box.Width * Math.Clamp(ores[i].Fraction, 0f, 1f);
            var right = MathF.Min(box.Right, x + width);
            handle.DrawRect(new UIBox2(x, box.Top, right, box.Bottom), ores[i].Color.WithAlpha(0.92f * alpha));
            // A sheen along the top of each vein, so neighbouring colours stay apart.
            handle.DrawRect(new UIBox2(x, box.Top, right, box.Top + MathF.Max(1f, box.Height * 0.25f)),
                Color.White.WithAlpha(0.18f * alpha));
            x = right;
            if (i < ores.Count - 1)
                handle.DrawLine(new Vector2(x, box.Top), new Vector2(x, box.Bottom), skin.Ink.WithAlpha(alpha));
        }

        for (var i = 1; i < 10; i++)
        {
            var tick = box.Left + box.Width * i / 10f;
            var length = (i == 5 ? 0.45f : 0.25f) * box.Height;
            handle.DrawLine(new Vector2(tick, box.Bottom), new Vector2(tick, box.Bottom - length), skin.Ink.WithAlpha(0.6f * alpha));
        }

        handle.DrawRect(box, skin.EdgeLight.WithAlpha(0.7f * alpha), false);
        if (!WFInstrumentTheme.Digital && alpha >= 1f)
            WFInstrumentGlass.Window(handle, box, scale);
    }

    /// <summary>Four corner brackets round a point, turned by <paramref name="rotation"/> radians, two pixels thick.</summary>
    public static void Brackets(DrawingHandleScreen handle, Vector2 center, float half, float rotation, Color color, float scale)
    {
        var arm = MathF.Max(4f * scale, half * 0.45f);
        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);
        for (var corner = 0; corner < 4; corner++)
        {
            var sx = corner is 0 or 3 ? -1f : 1f;
            var sy = corner < 2 ? -1f : 1f;
            for (var inset = 0f; inset < 2f; inset++)
            {
                var tip = new Vector2(sx * (half - inset * scale), sy * (half - inset * scale));
                Line(tip, tip - new Vector2(sx * arm, 0f));
                Line(tip, tip - new Vector2(0f, sy * arm));
            }
        }

        void Line(Vector2 from, Vector2 to)
        {
            handle.DrawLine(center + Turn(from), center + Turn(to), color);
        }

        Vector2 Turn(Vector2 point)
        {
            return new Vector2(point.X * cos - point.Y * sin, point.X * sin + point.Y * cos);
        }
    }

    /// <summary>Draws text from its top-left corner, shrunk to fit the width if it must be.</summary>
    public static void TextLeft(DrawingHandleScreen handle, Font font, string text, Vector2 topLeft, float width, float scale,
        Color color)
    {
        var fitted = WFInstrumentText.FitScale(handle, font, text, scale, width);
        if (fitted > 0)
            handle.DrawString(font, topLeft, text, fitted, color);
    }

    /// <summary>Draws text right-aligned against <paramref name="right"/>, shrunk to fit the width if it must be, and returns its width.</summary>
    public static float TextRight(DrawingHandleScreen handle, Font font, string text, float right, float top, float width,
        float scale, Color color)
    {
        var fitted = WFInstrumentText.FitScale(handle, font, text, scale, width);
        if (fitted <= 0)
            return 0f;
        var drawn = handle.GetDimensions(font, text, fitted).X;
        handle.DrawString(font, new Vector2(right - drawn, top), text, fitted, color);
        return drawn;
    }
}
