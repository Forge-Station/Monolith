using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._WF.DroneRings;

/// <summary>Draws the drone rings of a map on a radar or map screen.</summary>
public static class WFDroneRingDrawing
{
    /// <summary>Ring colours, nearest first; rings past the last share it.</summary>
    private static readonly Color[] Colors =
    {
        Color.FromHex("#e0b040"),
        Color.FromHex("#e08030"),
        Color.FromHex("#e04030"),
        Color.FromHex("#d030a0"),
    };

    private const float DashLength = 8f;
    private const float GapLength = 6f;
    private const int MaxDashes = 2048;

    private static Vector2[] _verts = new Vector2[256];

    /// <summary>
    /// Draws each ring as a dashed circle about <paramref name="centre"/>, named where it comes nearest the middle
    /// of the view. Only the part of a ring that can cross the view is built, so a ring many kilometres wide stays
    /// round when a radar shows a few hundred metres of it.
    /// </summary>
    /// <param name="centre">The map's origin, in the control's pixels.</param>
    /// <param name="scale">Pixels per metre.</param>
    /// <param name="labels">Each ring's name; drone zone numbers if null.</param>
    /// <param name="colors">Each ring's colour; the drone zone colours if null.</param>
    public static void Draw(DrawingHandleScreen handle, Font font, List<float> radii, Vector2 centre, float scale, Vector2 viewSize, float uiScale,
        IReadOnlyList<string>? labels = null, IReadOnlyList<Color>? colors = null)
    {
        var middle = viewSize / 2f;
        var reach = (double) middle.Length();
        var toView = middle - centre;
        var distance = (double) toView.Length();
        // Screen Y runs down, so this is straight up when the view is centred on the rings.
        var facing = distance > 1.0 ? Math.Atan2(toView.Y, toView.X) : -Math.PI / 2.0;
        Vector2? lastLabel = null;

        for (var i = 0; i < radii.Count; i++)
        {
            var radius = (double) radii[i] * scale;
            if (radius < DashLength || Math.Abs(distance - radius) > reach)
                continue;

            // Half the angle, seen from the centre, of the arc lying within the view's bounding circle.
            var half = Math.PI;
            if (distance > 1.0)
            {
                var cos = (distance * distance + radius * radius - reach * reach) / (2.0 * distance * radius);
                if (cos >= 1.0)
                    continue;

                if (cos > -1.0)
                    half = Math.Acos(cos);
            }

            var color = colors != null && i < colors.Count ? colors[i] : Colors[Math.Min(i, Colors.Length - 1)];
            DrawArc(handle, centre, radius, facing, half, uiScale, color.WithAlpha(0.6f));

            var text = labels != null && i < labels.Count ? labels[i] : Loc.GetString("wf-drone-ring-label", ("tier", i + 1));
            var dimensions = handle.GetDimensions(font, text, uiScale * 0.7f);
            var direction = new Vector2((float) Math.Cos(facing), (float) Math.Sin(facing));
            // Just outside the ring: past this line is the zone.
            var clear = 4f * uiScale + (MathF.Abs(direction.X) * dimensions.X + MathF.Abs(direction.Y) * dimensions.Y) / 2f;
            var position = centre + direction * ((float) radius + clear) - dimensions / 2f;
            var margin = new Vector2(4f * uiScale);
            var limit = viewSize - dimensions - margin;
            if (limit.X < margin.X || limit.Y < margin.Y)
                continue;

            position = Vector2.Clamp(position, margin, limit);
            if (lastLabel is { } last && Vector2.Distance(last, position) < dimensions.Y)
                continue;

            lastLabel = position;
            handle.DrawString(font, position, text, uiScale * 0.7f, color);
        }
    }

    /// <summary>A dashed arc of <paramref name="half"/> radians either side of <paramref name="facing"/>.</summary>
    private static void DrawArc(DrawingHandleScreen handle, Vector2 centre, double radius, double facing, double half, float uiScale, Color color)
    {
        var full = half >= Math.PI;
        var start = full ? 0.0 : facing - half;
        var end = full ? Math.Tau : facing + half;
        var period = (DashLength + GapLength) * uiScale / radius;
        period = Math.Max(period, (end - start) / MaxDashes);
        var dash = period * DashLength / (DashLength + GapLength);

        // Dashes sit at fixed angles, so they hold still as the view pans along the ring.
        var first = (long) Math.Ceiling(start / period);
        var last = (long) Math.Floor((end - dash) / period);
        if (last < first)
            return;

        var count = (int) (last - first + 1) * 2;
        if (_verts.Length < count)
            _verts = new Vector2[count];

        var index = 0;
        for (var k = first; k <= last; k++)
        {
            var from = k * period;
            _verts[index++] = Point(centre, radius, from);
            _verts[index++] = Point(centre, radius, from + dash);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.LineList, new Span<Vector2>(_verts, 0, count), color);
    }

    private static Vector2 Point(Vector2 centre, double radius, double angle)
    {
        return new Vector2((float) (centre.X + radius * Math.Cos(angle)), (float) (centre.Y + radius * Math.Sin(angle)));
    }
}
