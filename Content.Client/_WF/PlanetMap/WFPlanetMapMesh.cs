using System.Numerics;
using Content.Shared._WF.PlanetMap;

namespace Content.Client._WF.PlanetMap;

/// <summary>Triangles for a planet map, in world coordinates: smoothed ground patches and lines of a set width.</summary>
public static class WFPlanetMapMesh
{
    /// <summary>Sample squares per culling bucket along each side.</summary>
    private const int Bucket = 16;

    /// <summary>The order patches are laid over the base colour; later ones cover earlier ones.</summary>
    public static readonly WFPlanetMapGround[] Layers =
    {
        WFPlanetMapGround.Grass, WFPlanetMapGround.Dry, WFPlanetMapGround.Snow, WFPlanetMapGround.Rock,
        WFPlanetMapGround.Vegetation, WFPlanetMapGround.Formation, WFPlanetMapGround.Flesh, WFPlanetMapGround.Void,
        WFPlanetMapGround.Water, WFPlanetMapGround.Plasma, WFPlanetMapGround.Lava,
    };

    /// <summary>A batch of triangles of one ground, with the box that holds them for culling.</summary>
    public readonly record struct Patch(WFPlanetMapGround Ground, Box2 Bounds, Vector2[] Triangles);

    /// <summary>A world box's corners through a view that may be turned, in order round the box.</summary>
    public static Vector2[] BoxCorners(Vector2 min, Vector2 max, Matrix3x2 worldToView)
    {
        return new[]
        {
            Vector2.Transform(min, worldToView),
            Vector2.Transform(new Vector2(max.X, min.Y), worldToView),
            Vector2.Transform(max, worldToView),
            Vector2.Transform(new Vector2(min.X, max.Y), worldToView),
        };
    }

    /// <summary>Marching squares over the cell centres, one ground at a time, so each patch has smooth edges.</summary>
    public static List<Patch> Terrain(WFPlanetMapTerrain terrain)
    {
        var patches = new List<Patch>();
        var width = terrain.Width;
        var height = terrain.Height;
        if (width <= 0 || height <= 0 || terrain.Cell <= 0 || (long) width * height > WFPlanetMapEncoding.MaxCells)
            return patches;

        var cells = WFPlanetMapEncoding.Decode(terrain.Runs, width * height);
        var present = new HashSet<WFPlanetMapGround>();
        foreach (var cell in cells)
            present.Add((WFPlanetMapGround) cell);

        var corner = new bool[4];
        var cornerAt = new Vector2[4];
        var polygon = new List<Vector2>(8);
        var buckets = new Dictionary<(int, int), List<Vector2>>();
        foreach (var ground in Layers)
        {
            if (ground == terrain.Base || !present.Contains(ground))
                continue;

            buckets.Clear();
            var value = (byte) ground;
            bool Inside(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && cells[y * width + x] == value;

            for (var y = -1; y < height; y++)
            for (var x = -1; x < width; x++)
            {
                corner[0] = Inside(x, y);
                corner[1] = Inside(x + 1, y);
                corner[2] = Inside(x + 1, y + 1);
                corner[3] = Inside(x, y + 1);
                if (!corner[0] && !corner[1] && !corner[2] && !corner[3])
                    continue;

                cornerAt[0] = Sample(terrain, x, y);
                cornerAt[1] = Sample(terrain, x + 1, y);
                cornerAt[2] = Sample(terrain, x + 1, y + 1);
                cornerAt[3] = Sample(terrain, x, y + 1);

                polygon.Clear();
                for (var i = 0; i < 4; i++)
                {
                    var next = (i + 1) % 4;
                    if (corner[i])
                        polygon.Add(cornerAt[i]);
                    if (corner[i] != corner[next])
                        polygon.Add((cornerAt[i] + cornerAt[next]) / 2f);
                }

                var key = (Math.Max(x, 0) / Bucket, Math.Max(y, 0) / Bucket);
                if (!buckets.TryGetValue(key, out var triangles))
                    buckets[key] = triangles = new List<Vector2>();
                for (var i = 1; i + 1 < polygon.Count; i++)
                {
                    triangles.Add(polygon[0]);
                    triangles.Add(polygon[i]);
                    triangles.Add(polygon[i + 1]);
                }
            }

            foreach (var triangles in buckets.Values)
            {
                if (triangles.Count == 0)
                    continue;
                patches.Add(new Patch(ground, Bounds(triangles), triangles.ToArray()));
            }
        }

        return patches;
    }

    /// <summary>The world position of a cell's centre.</summary>
    private static Vector2 Sample(WFPlanetMapTerrain terrain, int x, int y)
    {
        return new Vector2(terrain.Origin.X + (x + 0.5f) * terrain.Cell, terrain.Origin.Y + (y + 0.5f) * terrain.Cell);
    }

    /// <summary>A filled circle as a triangle fan.</summary>
    public static Vector2[] Disc(Vector2 centre, float radius, int segments = 96)
    {
        var triangles = new Vector2[segments * 3];
        for (var i = 0; i < segments; i++)
        {
            var a = MathF.Tau * i / segments;
            var b = MathF.Tau * (i + 1) / segments;
            triangles[i * 3] = centre;
            triangles[i * 3 + 1] = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            triangles[i * 3 + 2] = centre + new Vector2(MathF.Cos(b), MathF.Sin(b)) * radius;
        }

        return triangles;
    }

    /// <summary>A polyline as quads <paramref name="width"/> wide, with a square at every joint to close the corners.</summary>
    public static void Strip(List<Vector2> into, IReadOnlyList<Vector2> points, float width)
    {
        var half = width / 2f;
        for (var i = 0; i + 1 < points.Count; i++)
            Quad(into, points[i], points[i + 1], half);
        for (var i = 1; i + 1 < points.Count; i++)
            Quad(into, points[i] - new Vector2(half, 0f), points[i] + new Vector2(half, 0f), half);
    }

    /// <summary>A polyline as dashes: <paramref name="dash"/> drawn, <paramref name="gap"/> left out, carried across corners.</summary>
    public static void Dashes(List<Vector2> into, IReadOnlyList<Vector2> points, float width, float dash, float gap)
    {
        if (dash <= 0f)
            return;
        var half = width / 2f;
        var phase = 0f;
        var period = dash + MathF.Max(gap, 0f);
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var length = (b - a).Length();
            if (length <= 0f)
                continue;
            var direction = (b - a) / length;
            var at = 0f;
            while (at < length)
            {
                var step = MathF.Min(period - phase, length - at);
                if (phase < dash)
                {
                    var drawn = MathF.Min(dash - phase, length - at);
                    Quad(into, a + direction * at, a + direction * (at + drawn), half);
                }
                at += step;
                phase = (phase + step) % period;
            }
        }
    }

    private static void Quad(List<Vector2> into, Vector2 a, Vector2 b, float half)
    {
        var along = b - a;
        var length = along.Length();
        if (length <= 0f)
            return;
        var normal = new Vector2(-along.Y, along.X) / length * half;
        into.Add(a + normal);
        into.Add(b + normal);
        into.Add(b - normal);
        into.Add(a + normal);
        into.Add(b - normal);
        into.Add(a - normal);
    }

    /// <summary>The box round a set of points.</summary>
    public static Box2 Bounds(IReadOnlyList<Vector2> points)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var point in points)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        return new Box2(min, max);
    }
}
