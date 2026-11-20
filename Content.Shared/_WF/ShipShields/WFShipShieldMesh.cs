using System.Numerics;

namespace Content.Shared._WF.ShipShields;

/// <summary>Builds cached distance bands and a regular ship-local honeycomb field.</summary>
public sealed class WFShipShieldMesh
{
    /// <summary>Depth of the detailed hex field before it fades into the interior.</summary>
    public const float InwardDepth = 3.8f;
    /// <summary>Cached gradient triangles in shield-local coordinates.</summary>
    public readonly List<Vertex> Triangles = new();
    /// <summary>Cached clipped edges of the Cartesian honeycomb lattice.</summary>
    public readonly List<Vertex> HexLines = new();
    /// <summary>Impact colors share samples on a coarse local grid.</summary>
    public readonly List<Vector2> Samples = new();
    /// <summary>Local bounds used before any per-frame surface sampling.</summary>
    public Box2 Bounds;
    /// <summary>Half the distant segment spacing keeps sub-segment impacts visible.</summary>
    public float ImpactSampleRadius { get; private set; }
    private const float CellSize = 0.75f;
    private const float SampleStep = 1.5f;
    private readonly Dictionary<Vector2i, int> _samples = new();
    private readonly Dictionary<Vector2, int> _distantSamples = new();
    private readonly bool _distant;

    /// <summary>A local vertex blending four cached impact samples without visible cell boundaries.</summary>
    public readonly record struct Vertex(Vector2 Position, float Alpha, int Sample, int SampleX, int SampleY, int SampleXY, Vector2 Blend);
    private readonly record struct DistanceVertex(Vector2 Position, float Depth);

    /// <summary>Ignores fresh network arrays when contour coordinates have not changed.</summary>
    public static bool ContoursEqual(Vector2[][] first, Vector2[][] second)
    {
        if (ReferenceEquals(first, second))
            return true;
        if (first.Length != second.Length)
            return false;
        for (var i = 0; i < first.Length; i++)
        {
            if (first[i].Length != second[i].Length)
                return false;
            for (var j = 0; j < first[i].Length; j++)
            {
                if (first[i][j] != second[i][j])
                    return false;
            }
        }
        return true;
    }

    /// <summary>Builds a continuous detailed field or a sparse contour for distant ships.</summary>
    public WFShipShieldMesh(Vector2[][] contours, bool distant)
    {
        _distant = distant;
        var initialized = false;
        foreach (var contour in contours)
        foreach (var point in contour)
        {
            if (!initialized)
            {
                Bounds = new Box2(point, point);
                initialized = true;
            }
            Bounds = Bounds.ExtendToContain(point);
        }
        if (distant)
            BuildDistant(SimplifyDistantOvals(contours));
        else
        {
            var cells = BuildDistanceBand(contours);
            BuildHexes(contours, cells);
        }
        Bounds = new Box2(Bounds.BottomLeft - new Vector2(InwardDepth), Bounds.TopRight + new Vector2(InwardDepth));
    }

    /// <summary>Returns positive distance inside the shield union and negative distance outside.</summary>
    public static float SignedDistance(Vector2[][] contours, Vector2 point)
    {
        var minimum = float.PositiveInfinity;
        var inside = false;
        foreach (var contour in contours)
        {
            var contained = false;
            for (var i = 0; i < contour.Length; i++)
            {
                var a = contour[i];
                var b = contour[(i + 1) % contour.Length];
                var edge = b - a;
                var squared = edge.LengthSquared();
                if (squared > 0.000001f)
                {
                    var closest = a + edge * Math.Clamp(Vector2.Dot(point - a, edge) / squared, 0f, 1f);
                    minimum = MathF.Min(minimum, Vector2.DistanceSquared(point, closest));
                }
                if ((a.Y > point.Y) != (b.Y > point.Y) &&
                    point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                    contained = !contained;
            }
            inside |= contained;
        }
        return MathF.Sqrt(minimum) * (inside ? 1f : -1f);
    }

    private int Sample(Vector2i key)
    {
        if (_samples.TryGetValue(key, out var sample))
            return sample;
        sample = Samples.Count;
        Samples.Add(new Vector2(key.X * SampleStep, key.Y * SampleStep));
        _samples.Add(key, sample);
        return sample;
    }

    private Vertex CreateVertex(Vector2 position, float alpha)
    {
        if (_distant)
        {
            if (!_distantSamples.TryGetValue(position, out var sample))
            {
                sample = Samples.Count;
                Samples.Add(position);
                _distantSamples.Add(position, sample);
            }
            return new Vertex(position, alpha, sample, sample, sample, sample, Vector2.Zero);
        }
        var scaled = position / SampleStep;
        var key = new Vector2i((int)MathF.Floor(scaled.X), (int)MathF.Floor(scaled.Y));
        return new Vertex(position, alpha, Sample(key), Sample(key + new Vector2i(1, 0)),
            Sample(key + new Vector2i(0, 1)), Sample(key + new Vector2i(1, 1)), scaled - new Vector2(key.X, key.Y));
    }

    /// <summary>Interpolates cached impact colours continuously across sample cells.</summary>
    public static Color Interpolate(Vertex vertex, IReadOnlyList<Color> colors)
    {
        if (vertex.Sample == vertex.SampleX && vertex.Sample == vertex.SampleY && vertex.Sample == vertex.SampleXY)
            return colors[vertex.Sample];
        var bottom = Color.InterpolateBetween(colors[vertex.Sample], colors[vertex.SampleX], vertex.Blend.X);
        var top = Color.InterpolateBetween(colors[vertex.SampleY], colors[vertex.SampleXY], vertex.Blend.X);
        return Color.InterpolateBetween(bottom, top, vertex.Blend.Y);
    }

    private HashSet<Vector2i> BuildDistanceBand(Vector2[][] contours)
    {
        var cells = new HashSet<Vector2i>();
        var radius = (int) MathF.Ceiling(InwardDepth / CellSize) + 1;
        foreach (var contour in contours)
        for (var i = 0; i < contour.Length; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Length];
            var steps = Math.Max(1, (int) MathF.Ceiling(Vector2.Distance(a, b) / CellSize));
            for (var part = 0; part <= steps; part++)
            {
                var point = Vector2.Lerp(a, b, (float) part / steps) / CellSize;
                var x = (int) MathF.Floor(point.X);
                var y = (int) MathF.Floor(point.Y);
                for (var dx = -radius; dx <= radius; dx++)
                for (var dy = -radius; dy <= radius; dy++)
                    cells.Add(new Vector2i(x + dx, y + dy));
            }
        }
        var distances = new Dictionary<Vector2i, DistanceVertex>();
        var polygon = new List<DistanceVertex>(6);
        var clipped = new List<DistanceVertex>(6);
        foreach (var cell in cells)
        {
            var a = Point(cell);
            var b = Point(cell + new Vector2i(1, 0));
            var c = Point(cell + new Vector2i(1, 1));
            var d = Point(cell + new Vector2i(0, 1));
            Triangle(a, b, c);
            Triangle(a, c, d);
        }
        return cells;

        DistanceVertex Point(Vector2i key)
        {
            if (distances.TryGetValue(key, out var vertex))
                return vertex;
            var position = new Vector2(key.X * CellSize, key.Y * CellSize);
            vertex = new DistanceVertex(position, SignedDistance(contours, position));
            distances.Add(key, vertex);
            return vertex;
        }

        void Triangle(DistanceVertex a, DistanceVertex b, DistanceVertex c)
        {
            if (MathF.Min(a.Depth, MathF.Min(b.Depth, c.Depth)) > InwardDepth ||
                MathF.Max(a.Depth, MathF.Max(b.Depth, c.Depth)) < -0.12f)
                return;
            Band(-0.12f, 0f);
            Band(0f, InwardDepth * 0.25f);
            Band(InwardDepth * 0.25f, InwardDepth);
            return;

            void Band(float outer, float inner)
            {
                polygon.Clear();
                polygon.Add(a); polygon.Add(b); polygon.Add(c);
                ClipDepth(polygon, clipped, outer, true);
                ClipDepth(clipped, polygon, inner, false);
                if (polygon.Count < 3)
                    return;
                for (var i = 1; i < polygon.Count - 1; i++)
                {
                    var first = polygon[0];
                    var second = polygon[i];
                    var third = polygon[i + 1];
                    var ab = second.Position - first.Position;
                    var ac = third.Position - first.Position;
                    if (ab.X * ac.Y - ab.Y * ac.X <= 0.000001f)
                        continue;
                    Triangles.Add(CreateVertex(first.Position, GlowOpacity(first.Depth)));
                    Triangles.Add(CreateVertex(second.Position, GlowOpacity(second.Depth)));
                    Triangles.Add(CreateVertex(third.Position, GlowOpacity(third.Depth)));
                }
            }
        }
    }

    private static void ClipDepth(List<DistanceVertex> source, List<DistanceVertex> destination, float depth, bool greater)
    {
        destination.Clear();
        if (source.Count == 0)
            return;
        var previous = source[^1];
        var previousInside = greater ? previous.Depth >= depth : previous.Depth <= depth;
        foreach (var current in source)
        {
            var currentInside = greater ? current.Depth >= depth : current.Depth <= depth;
            if (currentInside != previousInside)
            {
                var fraction = (depth - previous.Depth) / (current.Depth - previous.Depth);
                destination.Add(new DistanceVertex(Vector2.Lerp(previous.Position, current.Position, fraction), depth));
            }
            if (currentInside)
                destination.Add(current);
            previous = current;
            previousInside = currentInside;
        }
    }

    private static float GlowOpacity(float depth)
    {
        if (depth <= 0f)
            return Math.Clamp((depth + 0.12f) / 0.12f, 0f, 1f) * 0.52f;
        var shoulder = InwardDepth * 0.25f;
        return depth <= shoulder ? 0.52f + (0.19f - 0.52f) * depth / shoulder
            : 0.19f * Math.Clamp((InwardDepth - depth) / (InwardDepth - shoulder), 0f, 1f);
    }

    private void BuildHexes(Vector2[][] contours, HashSet<Vector2i> cells)
    {
        const float radius = 0.85f;
        const float step = radius * 1.5f;
        const float height = radius * 1.7320508f;
        var hexes = new HashSet<Vector2i>();
        foreach (var cell in cells)
        {
            var column = (int) MathF.Floor(cell.X * CellSize / step);
            var row = (int) MathF.Floor(cell.Y * CellSize / height);
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
                hexes.Add(new Vector2i(column + dx, row + dy));
        }
        foreach (var hex in hexes)
        {
            var column = hex.X;
            var row = hex.Y;
            var center = new Vector2(column * step, row * height + (column % 2 == 0 ? 0f : height * 0.5f));
            var depth = SignedDistance(contours, center);
            if (depth < -radius || depth > InwardDepth + radius)
                continue;
            for (var edge = 0; edge < 3; edge++)
            {
                var angleA = edge * MathF.PI / 3f;
                var angleB = (edge + 1) * MathF.PI / 3f;
                var a = center + new Vector2(MathF.Cos(angleA), MathF.Sin(angleA)) * radius;
                var b = center + new Vector2(MathF.Cos(angleB), MathF.Sin(angleB)) * radius;
                for (var part = 0; part < 3; part++)
                {
                    var start = Vector2.Lerp(a, b, part / 3f);
                    var end = Vector2.Lerp(a, b, (part + 1) / 3f);
                    var d0 = SignedDistance(contours, start);
                    var d1 = SignedDistance(contours, end);
                    if (MathF.Max(d0, d1) < 0f || MathF.Min(d0, d1) > InwardDepth)
                        continue;
                    var low = 0f;
                    var high = 1f;
                    var delta = d1 - d0;
                    if (MathF.Abs(delta) > 0.000001f)
                    {
                        var firstHit = -d0 / delta;
                        var lastHit = (InwardDepth - d0) / delta;
                        low = MathF.Max(0f, MathF.Min(firstHit, lastHit));
                        high = MathF.Min(1f, MathF.Max(firstHit, lastHit));
                    }
                    if (low >= high)
                        continue;
                    var clippedStart = Vector2.Lerp(start, end, low);
                    var clippedEnd = Vector2.Lerp(start, end, high);
                    HexLines.Add(CreateVertex(clippedStart, HexOpacity(d0 + delta * low)));
                    HexLines.Add(CreateVertex(clippedEnd, HexOpacity(d0 + delta * high)));
                }
            }
        }
    }

    /// <summary>Fades hex edges smoothly to zero at the deepest part of the field.</summary>
    public static float HexOpacity(float depth)
    {
        var fraction = Math.Clamp(depth / InwardDepth, 0f, 1f);
        return 0.28f * (1f - fraction * fraction * (3f - 2f * fraction));
    }

    /// <summary>Bounds distant oval geometry while retaining non-oval legacy contours.</summary>
    private static Vector2[][] SimplifyDistantOvals(Vector2[][] contours)
    {
        const int limit = 96;
        var simplified = new Vector2[contours.Length][];
        for (var c = 0; c < contours.Length; c++)
        {
            var points = contours[c];
            simplified[c] = points;
            if (points.Length <= limit)
                continue;
            var minimum = points[0];
            var maximum = points[0];
            foreach (var point in points)
            {
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }
            var center = (minimum + maximum) * 0.5f;
            var radii = (maximum - minimum) * 0.5f;
            if (radii.X <= 0f || radii.Y <= 0f)
                continue;
            var oval = true;
            foreach (var point in points)
            {
                if (MathF.Abs(((point - center) / radii).LengthSquared() - 1f) > 0.0001f)
                {
                    oval = false;
                    break;
                }
            }
            if (!oval)
                continue;
            var reduced = new Vector2[limit];
            for (var i = 0; i < limit; i++)
            {
                var angle = i * MathF.Tau / limit;
                reduced[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radii;
            }
            simplified[c] = reduced;
        }
        return simplified;
    }

    private void BuildDistant(Vector2[][] contours)
    {
        foreach (var points in contours)
        {
            for (var i = 0; i < points.Length; i++)
            {
                var start = points[i];
                var end = points[(i + 1) % points.Length];
                var length = Vector2.Distance(start, end);
                if (length < 0.001f)
                    continue;
                ImpactSampleRadius = MathF.Max(ImpactSampleRadius, length * 0.5f);
                var n0 = Normal(points, i);
                var n1 = Normal(points, (i + 1) % points.Length);
                {
                    var a = start;
                    var b = end;
                    var na = n0;
                    var nb = n1;
                    Band(-0.12f, 0f, 0f, 0.52f);
                    Band(0f, 0.075f, 0.52f, 0.19f);
                    Band(0.075f, 0.3f, 0.19f, 0f);
                    void Band(float outer, float inner, float outerAlpha, float innerAlpha)
                    {
                        var a0 = CreateVertex(a + na * outer, outerAlpha);
                        var b0 = CreateVertex(b + nb * outer, outerAlpha);
                        var a1 = CreateVertex(a + na * inner, innerAlpha);
                        var b1 = CreateVertex(b + nb * inner, innerAlpha);
                        Triangles.Add(a0); Triangles.Add(b0); Triangles.Add(a1);
                        Triangles.Add(b0); Triangles.Add(b1); Triangles.Add(a1);
                    }
                }
            }
        }
    }

    /// <summary>Limits distant inward joins to avoid triangle folds on tight corners.</summary>
    public static Vector2 Normal(Vector2[] points, int index)
    {
        var previous = points[index] - points[(index + points.Length - 1) % points.Length];
        var next = points[(index + 1) % points.Length] - points[index];
        if (previous.LengthSquared() < 0.0001f || next.LengthSquared() < 0.0001f)
            return Vector2.Zero;
        var adjacent = MathF.Min(previous.Length(), next.Length());
        previous = Vector2.Normalize(previous);
        next = Vector2.Normalize(next);
        var bend = MathF.Sqrt(MathF.Max(0f, 1f - Vector2.Dot(previous, next)));
        var limit = bend < 0.0001f ? 1f : MathF.Min(1f, adjacent * 0.5f / (InwardDepth * bend));
        var sum = new Vector2(-previous.Y - next.Y, previous.X + next.X);
        return sum.LengthSquared() < 0.0001f ? Vector2.Zero : Vector2.Normalize(sum) * limit;
    }

    /// <summary>Restores full width along straight distant edges while tapering tight joins.</summary>
    public static Vector2 EdgeNormal(Vector2 start, Vector2 end, Vector2 startNormal, Vector2 endNormal, float fraction)
    {
        var delta = end - start;
        var length = delta.Length();
        if (length < 0.001f)
            return Vector2.Zero;
        var straight = new Vector2(-delta.Y, delta.X) / length;
        var nearStart = MathF.Max(0f, 1f - fraction * length / (InwardDepth * 1.5f));
        var nearEnd = MathF.Max(0f, 1f - (1f - fraction) * length / (InwardDepth * 1.5f));
        if (nearStart + nearEnd > 1f)
            return Vector2.Lerp(startNormal, endNormal, fraction);
        return straight * (1f - nearStart - nearEnd) + startNormal * nearStart + endNormal * nearEnd;
    }
}
