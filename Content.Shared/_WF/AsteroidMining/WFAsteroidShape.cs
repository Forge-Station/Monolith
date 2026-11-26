using System.Numerics;

namespace Content.Shared._WF.AsteroidMining;

/// <summary>An irregular rock silhouette, in metres around the rock's centre, matching one sprite variant.</summary>
[DataDefinition]
public sealed partial class WFAsteroidShape
{
    /// <summary>Outline the grid's tiles fill; a tile is placed where its centre falls inside.</summary>
    [DataField(required: true)] public List<Vector2> Outline = new();

    /// <summary>Convex hull of at most eight points for the rock's own fixture, which rays and bullets hit.</summary>
    [DataField(required: true)] public List<Vector2> Hull = new();

    /// <summary>Rolled mass is scaled by this, as the shape covers less than the round variants.</summary>
    [DataField] public float MassScale = 1f;

    /// <summary>Whether a point lies inside the outline (even-odd rule).</summary>
    public bool Contains(Vector2 point)
    {
        var inside = false;
        for (int i = 0, j = Outline.Count - 1; i < Outline.Count; j = i++)
        {
            var a = Outline[i];
            var b = Outline[j];
            if (a.Y > point.Y != b.Y > point.Y && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }
}
