using Content.Shared._WF.Planets;
using Content.Shared.Parallax.Biomes;

namespace Content.Server.Parallax;

/// <summary>Keeps the loader inside a bounded planet layer's circle.</summary>
public sealed partial class BiomeSystem
{
    /// <summary>Whether a chunk lies wholly outside its layer's circle, so it never loads.</summary>
    public bool WfOutOfBounds(EntityUid map, Vector2i chunkOrigin)
    {
        return TryComp<WFPlanetBoundsComponent>(map, out var bounds)
               && bounds.Radius > 0f
               && !bounds.MeetsSquare(chunkOrigin, ChunkSize);
    }

    /// <summary>Every chunk origin of a bounded layer's circle, nearest the centre first.</summary>
    public List<Vector2i> WfChunksInBounds(WFPlanetBoundsComponent bounds)
    {
        var chunks = new List<Vector2i>();
        var radius = (int) MathF.Ceiling(bounds.Radius);
        var min = SharedMapSystem.GetChunkIndices(new Vector2i((int) MathF.Floor(bounds.Centre.X) - radius, (int) MathF.Floor(bounds.Centre.Y) - radius), ChunkSize);
        var max = SharedMapSystem.GetChunkIndices(new Vector2i((int) MathF.Floor(bounds.Centre.X) + radius, (int) MathF.Floor(bounds.Centre.Y) + radius), ChunkSize);

        for (var x = min.X; x <= max.X; x++)
        for (var y = min.Y; y <= max.Y; y++)
        {
            var origin = new Vector2i(x, y) * ChunkSize;

            if (bounds.MeetsSquare(origin, ChunkSize))
                chunks.Add(origin);
        }

        var centre = bounds.Centre;
        chunks.Sort((a, b) =>
        {
            var da = (new System.Numerics.Vector2(a.X + ChunkSize / 2f, a.Y + ChunkSize / 2f) - centre).LengthSquared();
            var db = (new System.Numerics.Vector2(b.X + ChunkSize / 2f, b.Y + ChunkSize / 2f) - centre).LengthSquared();
            var order = da.CompareTo(db);
            if (order == 0)
                order = a.X.CompareTo(b.X);
            return order != 0 ? order : a.Y.CompareTo(b.Y);
        });

        return chunks;
    }
}
