using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._WF.SectorControl;

/// <summary>Draws a <see cref="WFSectorMapView"/> into a shuttle control or the sector map, which pass the matrix from map space to their pixels.</summary>
public sealed class WFSectorOverlay
{
    private const float FillAlpha = 0.10f;
    private const float EdgeAlpha = 0.8f;

    /// <summary>The neutral colour of a cell being retaken: it belongs to no faction.</summary>
    public static readonly Color RetakeColor = Color.FromHex("#dfe6ea");

    /// <summary>The dim neutral colour of a freed cell that is clearing.</summary>
    public static readonly Color ClearingColor = Color.FromHex("#8a949c");

    /// <summary>The cell width on screen above which held cells print their callsign.</summary>
    private const float LabelPixels = 60f;

    private Vector2[] _buffer = new Vector2[256];

    /// <summary>Draws map-space vertices moved to pixels through a buffer reused between calls.</summary>
    public void Draw(DrawingHandleScreen handle, DrawPrimitiveTopology topology, Vector2[] source, in Matrix3x2 worldToView, Color color)
    {
        if (_buffer.Length < source.Length)
            _buffer = new Vector2[Math.Max(source.Length, _buffer.Length * 2)];

        for (var i = 0; i < source.Length; i++)
        {
            _buffer[i] = Vector2.Transform(source[i], worldToView);
        }

        handle.DrawPrimitives(topology, new ReadOnlySpan<Vector2>(_buffer, 0, source.Length), color);
    }

    /// <summary>Fills every held cell in its faction's colour.</summary>
    public void DrawFills(DrawingHandleScreen handle, WFSectorMapView view, in Matrix3x2 worldToView)
    {
        for (var i = 0; i < view.Fills.Length; i++)
        {
            if (view.Fills[i].Length == 0)
                continue;

            Draw(handle, DrawPrimitiveTopology.TriangleList, view.Fills[i], worldToView, view.Colors[i].WithAlpha(FillAlpha));
        }
    }

    /// <summary>Draws the edges between a faction's cells and everything else.</summary>
    public void DrawBorders(DrawingHandleScreen handle, WFSectorMapView view, in Matrix3x2 worldToView)
    {
        for (var i = 0; i < view.Edges.Length; i++)
        {
            if (view.Edges[i].Length == 0)
                continue;

            Draw(handle, DrawPrimitiveTopology.LineList, view.Edges[i], worldToView, view.Colors[i].WithAlpha(EdgeAlpha));
        }
    }

    /// <summary>Pulses and outlines contested cells and counts down to their deadline.</summary>
    public void DrawContests(DrawingHandleScreen handle, WFSectorClientSystem system, WFSectorMapView view, in Matrix3x2 worldToView, Font font, float scale, UIBox2 bounds)
    {
        var pulse = system.Pulse;
        var now = system.Now;
        foreach (var contest in view.Contests)
        {
            Draw(handle, DrawPrimitiveTopology.TriangleList, contest.Fill, worldToView, contest.Color.WithAlpha(0.06f + 0.14f * pulse));
            Draw(handle, DrawPrimitiveTopology.LineList, contest.Outline, worldToView, contest.Color.WithAlpha(0.5f + 0.5f * pulse));

            var text = FormatRemaining(contest.Remaining(now));
            DrawCentred(handle, font, scale, Vector2.Transform(contest.Centre, worldToView), text, contest.Color, bounds);
        }
    }

    /// <summary>Pulses and outlines cells being retaken in a neutral colour and counts down to when they are freed.</summary>
    public void DrawRetakes(DrawingHandleScreen handle, WFSectorClientSystem system, WFSectorMapView view, in Matrix3x2 worldToView, Font font, float scale, UIBox2 bounds)
    {
        var pulse = system.Pulse;
        var now = system.Now;
        foreach (var retake in view.Retakes)
        {
            Draw(handle, DrawPrimitiveTopology.TriangleList, retake.Fill, worldToView, RetakeColor.WithAlpha(0.04f + 0.10f * pulse));
            Draw(handle, DrawPrimitiveTopology.LineList, retake.Outline, worldToView, RetakeColor.WithAlpha(0.4f + 0.5f * pulse));

            var text = Loc.GetString("wf-sector-map-retake-label", ("time", FormatRemaining(retake.Remaining(now))));
            DrawCentred(handle, font, scale, Vector2.Transform(retake.Centre, worldToView), text, RetakeColor, bounds);
        }
    }

    /// <summary>Outlines freed cells in a dim neutral colour with a countdown to when drones return.</summary>
    public void DrawClearing(DrawingHandleScreen handle, WFSectorClientSystem system, WFSectorMapView view, in Matrix3x2 worldToView, Font font, float scale, UIBox2 bounds)
    {
        var now = system.Now;
        foreach (var clearing in view.Clearing)
        {
            Draw(handle, DrawPrimitiveTopology.LineList, clearing.Outline, worldToView, ClearingColor.WithAlpha(0.45f));
            var text = Loc.GetString("wf-sector-map-clearing-label", ("time", FormatRemaining(clearing.Remaining(now))));
            DrawCentred(handle, font, scale, Vector2.Transform(clearing.Centre, worldToView), text, ClearingColor, bounds);
        }
    }

    /// <summary>Hatches disputed cells with diagonal lines in their faction's colour and pulses their borders.</summary>
    public void DrawDisputes(DrawingHandleScreen handle, WFSectorClientSystem system, WFSectorMapView view, in Matrix3x2 worldToView)
    {
        var pulse = system.Pulse;
        foreach (var dispute in view.Disputed)
        {
            Draw(handle, DrawPrimitiveTopology.LineList, dispute.Hatch, worldToView, dispute.Color.WithAlpha(0.35f));
            Draw(handle, DrawPrimitiveTopology.LineList, dispute.Outline, worldToView, dispute.Color.WithAlpha(0.4f + 0.6f * pulse));
        }
    }

    /// <summary>A contest countdown as m:ss, or h:mm:ss from an hour up, never below zero.</summary>
    public static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        return remaining.TotalHours >= 1 ? remaining.ToString(@"h\:mm\:ss") : remaining.ToString(@"m\:ss");
    }

    /// <summary>Prints the callsign of each held cell once a cell is wide enough on screen to hold one.</summary>
    public void DrawCallsigns(DrawingHandleScreen handle, WFSectorMapView view, in Matrix3x2 worldToView, Font font, float scale, float minimapScale, UIBox2 bounds)
    {
        DrawCallsigns(handle, view.Labels, view.CellSize, worldToView, font, scale, minimapScale, bounds);
    }

    /// <summary>Prints each label at its cell's centre once a cell is wide enough on screen to hold one.</summary>
    public void DrawCallsigns(DrawingHandleScreen handle, List<WFSectorLabelView> labels, float cellSize, in Matrix3x2 worldToView, Font font, float scale, float minimapScale, UIBox2 bounds)
    {
        // Flat to flat, in pixels.
        var width = cellSize * MathF.Sqrt(3f) * minimapScale;
        if (width < LabelPixels * scale)
            return;

        foreach (var label in labels)
        {
            DrawCentred(handle, font, scale * 0.85f, Vector2.Transform(label.Centre, worldToView), label.Text, label.Color.WithAlpha(0.7f), bounds);
        }
    }

    /// <summary>Lists the legend lines, each after a swatch of its faction's colour, from a corner.</summary>
    public void DrawLegend(DrawingHandleScreen handle, WFSectorClientSystem system, Font font, float scale)
    {
        var legend = new List<WFSectorLegendView>(system.Legend);
        if (system.TryGetAnyMap(out var map) && map.Disputed.Count > 0)
            legend.Add(new WFSectorLegendView(Color.White, Loc.GetString("wf-sector-map-legend-disputed")));

        if (system.TryGetAnyMap(out var retaking) && retaking.Retakes.Count > 0)
            legend.Add(new WFSectorLegendView(RetakeColor, Loc.GetString("wf-sector-map-legend-retaking")));

        if (system.TryGetAnyMap(out var freed) && freed.Clearing.Count > 0)
            legend.Add(new WFSectorLegendView(ClearingColor, Loc.GetString("wf-sector-map-legend-clearing")));

        if (legend.Count == 0)
            return;

        var margin = 5f * scale;
        var width = 0f;
        var lineHeight = 0f;
        foreach (var line in legend)
        {
            var size = handle.GetDimensions(font, line.Text, scale);
            width = MathF.Max(width, size.X);
            lineHeight = MathF.Max(lineHeight, size.Y);
        }

        var swatch = lineHeight * 0.6f;
        var gap = 4f * scale;
        var top = margin;
        var panel = UIBox2.FromDimensions(new Vector2(margin, top), new Vector2(width + swatch + gap * 3f, lineHeight * legend.Count + gap * 2f));
        handle.DrawRect(panel, Color.Black.WithAlpha(0.55f));
        var y = top + gap;
        foreach (var line in legend)
        {
            var x = margin + gap;
            var mid = y + lineHeight / 2f;
            handle.DrawRect(UIBox2.FromDimensions(new Vector2(x, mid - swatch / 2f), new Vector2(swatch, swatch)), line.Color);
            handle.DrawString(font, new Vector2(x + swatch + gap, y), line.Text, scale, line.Color);
            y += lineHeight;
        }
    }

    /// <summary>Prints text centred on a point with a dark shadow, unless the point is off the control.</summary>
    private static void DrawCentred(DrawingHandleScreen handle, Font font, float scale, Vector2 point, string text, Color color, UIBox2 bounds)
    {
        if (point.X < bounds.Left || point.X > bounds.Right || point.Y < bounds.Top || point.Y > bounds.Bottom)
            return;

        var size = handle.GetDimensions(font, text, scale);
        var position = point - size / 2f;
        handle.DrawString(font, position + new Vector2(1f, 1f), text, scale, Color.Black.WithAlpha(0.8f));
        handle.DrawString(font, position, text, scale, color);
    }
}
