using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._WF.Tether;

/// <summary>
/// Draws every simulated rope as a mitred quad strip: a darker, wider outline pass under a
/// lighter core, textured with the type's strand if it has one. Textured rope is cable, drawn as a
/// smooth curve through the chain. Taut rope thins, tints towards the type's taut colour and vibrates.
/// </summary>
public sealed class RopeOverlay : Overlay
{
    private readonly Dictionary<EntityUid, RopeChain> _chains;
    private readonly IGameTiming _timing;

    /// <summary>Drawn points between each pair of chain points on a smoothed rope.</summary>
    private const int Subdivisions = 3;

    private const int MaxDrawnPoints = RopeVerlet.MaxPoints * Subdivisions;

    /// <summary>Managed buffers: the client may not stackalloc, and Draw must not allocate.</summary>
    private readonly DrawVertexUV2DColor[] _vertices = new DrawVertexUV2DColor[MaxDrawnPoints * 6];

    private readonly Vector2[] _smoothed = new Vector2[MaxDrawnPoints];

    /// <summary>The most sparks one rope runs, and how many metres each covers per second.</summary>
    private const int MaxSparks = 40;

    private const float SparkSpeed = 8f;

    /// <summary>Seconds each spark keeps one shape before it flickers to another.</summary>
    private const float SparkFlicker = 0.06f;

    /// <summary>Strain at which a rope starts to buzz.</summary>
    private const float VibrationStrain = 0.8f;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public RopeOverlay(Dictionary<EntityUid, RopeChain> chains, IGameTiming timing)
    {
        _chains = chains;
        _timing = timing;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return;

        var handle = args.WorldHandle;
        var time = (float) (_timing.CurTime.TotalSeconds % 1000);
        foreach (var chain in _chains.Values)
        {
            if (!chain.Visible || chain.Count < 2 || chain.MapId != args.MapId)
                continue;

            var strain = Math.Clamp(chain.Strain, 0f, 2f);
            // A rope near its limit is a thinner, brighter, humming line.
            var width = chain.Width * (1f - 0.3f * MathF.Min(strain, 1f));
            var buzz = MathF.Max(0f, strain - VibrationStrain) / (1f - VibrationStrain);
            var vibration = MathF.Min(buzz, 1f) * chain.Width * 0.9f;

            if (!args.WorldAABB.Intersects(Bounds(chain, width * 2f)))
                continue;

            // A textured rope (a tentacle) is one pass of its texture, tinted by strain.
            if (chain.SegmentTexture is { } segmentTexture)
            {
                var written = BuildStrip(chain, chain.Points, Math.Min(chain.Count, chain.Points.Length), width, vibration, time,
                    Color.White.WithAlpha(1f), segmented: true);
                if (written > 0)
                    handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, segmentTexture, new ReadOnlySpan<DrawVertexUV2DColor>(_vertices, 0, written));
                continue;
            }

            var outline = Color.FromSrgb(new Color(chain.Color.R * 0.35f, chain.Color.G * 0.35f,
                chain.Color.B * 0.35f, 0.85f));
            var core = Color.FromSrgb(chain.Color.WithAlpha(1f));

            var points = chain.Points;
            var drawn = Math.Min(chain.Count, chain.Points.Length);
            if (chain.Texture != null)
            {
                points = _smoothed;
                drawn = Smooth(chain, drawn);
            }

            var count = BuildStrip(chain, points, drawn, width * 1.8f, vibration, time, outline);
            if (count == 0)
                continue;

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
                new ReadOnlySpan<DrawVertexUV2DColor>(_vertices, 0, count));
            count = BuildStrip(chain, points, drawn, width, vibration, time, core, chain.Texture != null);
            if (count > 0)
            {
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, chain.Texture ?? Texture.White,
                    new ReadOnlySpan<DrawVertexUV2DColor>(_vertices, 0, count));
            }

            if (chain.SparkColor is { } sparks)
                DrawSparks(handle, chain, points, drawn, sparks, time);
        }
    }

    /// <summary>Two triangles per segment, with the shared edge mitred along the averaged normal.</summary>
    private int BuildStrip(RopeChain chain, Vector2[] points, int count, float width, float vibration, float time, Color color, bool textured = false, bool segmented = false)
    {
        if (count < 2 || !float.IsFinite(width) || width <= 0f)
            return 0;

        var half = width * 0.5f;
        var written = 0;
        var previousLeft = Vector2.Zero;
        var previousRight = Vector2.Zero;
        var previousU = 0f;
        var previousNormal = Vector2.Zero;
        var along = 0f;
        for (var i = 0; i < count; i++)
        {
            var point = points[i];
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
                return 0;

            var tangent = Tangent(points, count, i);
            var normal = new Vector2(-tangent.Y, tangent.X);
            // Doubling back flips the normal, which would twist the strip into a needle at the fold.
            if (Vector2.Dot(normal, previousNormal) < 0f)
                normal = -normal;

            previousNormal = normal;
            if (vibration > 0f && i > 0 && i < count - 1)
                point += normal * (MathF.Sin(time * 45f + i * 1.7f) * vibration);

            // Narrows from end A to end B by the type's taper.
            var halfHere = half * (1f + (chain.Taper - 1f) * (count > 1 ? (float) i / (count - 1) : 0f));
            var left = point + normal * halfHere;
            var right = point - normal * halfHere;
            if (i > 0)
                along += Vector2.Distance(points[i], points[i - 1]);

            // The strand keeps its size however far the rope stretches.
            var u = textured ? along / chain.TextureLength : 0f;
            var bottom = textured ? 1f : 0f;
            if (i > 0 && written + 6 <= _vertices.Length)
            {
                if (segmented)
                {
                    // One copy of the texture per segment.
                    _vertices[written++] = new DrawVertexUV2DColor(previousLeft, new Vector2(0f, 0f), color);
                    _vertices[written++] = new DrawVertexUV2DColor(previousRight, new Vector2(1f, 0f), color);
                    _vertices[written++] = new DrawVertexUV2DColor(left, new Vector2(0f, 1f), color);
                    _vertices[written++] = new DrawVertexUV2DColor(previousRight, new Vector2(1f, 0f), color);
                    _vertices[written++] = new DrawVertexUV2DColor(right, new Vector2(1f, 1f), color);
                    _vertices[written++] = new DrawVertexUV2DColor(left, new Vector2(0f, 1f), color);
                }
                else
                {
                    _vertices[written++] = new DrawVertexUV2DColor(previousLeft, new Vector2(previousU, 0f), color);
                    _vertices[written++] = new DrawVertexUV2DColor(previousRight, new Vector2(previousU, bottom), color);
                    _vertices[written++] = new DrawVertexUV2DColor(left, new Vector2(u, 0f), color);
                    _vertices[written++] = new DrawVertexUV2DColor(previousRight, new Vector2(previousU, bottom), color);
                    _vertices[written++] = new DrawVertexUV2DColor(right, new Vector2(u, bottom), color);
                    _vertices[written++] = new DrawVertexUV2DColor(left, new Vector2(u, 0f), color);
                }
            }

            previousLeft = left;
            previousRight = right;
            previousU = u;
        }

        return written;
    }

    /// <summary>Writes a Catmull-Rom curve through the chain into the smoothing buffer and returns its point count.</summary>
    private int Smooth(RopeChain chain, int count)
    {
        var points = chain.Points;
        var written = 0;
        for (var i = 0; i < count - 1; i++)
        {
            var before = points[Math.Max(i - 1, 0)];
            var start = points[i];
            var end = points[i + 1];
            var after = points[Math.Min(i + 2, count - 1)];
            for (var step = 0; step < Subdivisions; step++)
            {
                var t = step / (float) Subdivisions;
                var t2 = t * t;
                _smoothed[written++] = 0.5f * (2f * start + (end - before) * t +
                    (2f * before - 5f * start + 4f * end - after) * t2 +
                    (3f * start - before - 3f * end + after) * t2 * t);
            }
        }

        _smoothed[written++] = points[count - 1];
        return written;
    }

    /// <summary>Sparks running from the gun to the far end, each a little spray that flickers to a new shape.</summary>
    private static void DrawSparks(DrawingHandleWorld handle, RopeChain chain, Vector2[] points, int count, Color color, float time)
    {
        var offset = Math.Abs(chain.Seed % 97) / 97f;
        var flicker = MathF.Floor(time / SparkFlicker);
        for (var i = 0; i < MaxSparks; i++)
        {
            var distance = RopeVerlet.SparkDistance(time, i, SparkSpeed, chain.SparkSpacing, offset);
            if (distance >= chain.Length)
                break;

            var at = distance / chain.Length * (count - 1);
            var index = Math.Min((int) at, count - 2);
            var point = Vector2.Lerp(points[index], points[index + 1], at - index);
            var tangent = Tangent(points, count, index);
            var normal = new Vector2(-tangent.Y, tangent.X);
            for (var strand = 0; strand < 3; strand++)
            {
                var seed = flicker + i * 7.1f + strand * 3.3f;
                var spread = normal * (Noise(seed) * 2f - 1f) + tangent * (Noise(seed + 1.7f) - 0.5f);
                var end = point + spread * (0.2f + 0.35f * Noise(seed + 2.9f));
                handle.DrawLine(point, end, color.WithAlpha(0.45f + 0.55f * Noise(seed + 4.3f)));
            }
        }
    }

    private static float Frac(float value) => value - MathF.Floor(value);

    /// <summary>A repeatable random number from 0 to 1.</summary>
    private static float Noise(float seed) => Frac(MathF.Sin(seed * 12.9898f) * 43758.547f);

    /// <summary>Averaged neighbour direction, so the strip mitres instead of pinching at corners.</summary>
    private static Vector2 Tangent(Vector2[] points, int count, int index)
    {
        var before = points[Math.Max(index - 1, 0)];
        var after = points[Math.Min(index + 1, count - 1)];
        var delta = after - before;
        var length = delta.Length();
        return float.IsFinite(length) && length > 0.00001f ? delta / length : Vector2.UnitX;
    }

    private static Box2 Bounds(RopeChain chain, float margin)
    {
        var min = chain.Points[0];
        var max = min;
        for (var i = 1; i < Math.Min(chain.Count, chain.Points.Length); i++)
        {
            min = Vector2.Min(min, chain.Points[i]);
            max = Vector2.Max(max, chain.Points[i]);
        }

        return new Box2(min, max).Enlarged(margin);
    }
}
