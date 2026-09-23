using System.Numerics;
using Robust.Shared.Map;

namespace Content.Client._WF.Tether;

/// <summary>
/// Client-only simulation state for one rope, kept in a dictionary keyed by the rope entity and
/// dropped when the rope goes away. Nothing here is networked or predicted.
/// </summary>
public sealed class RopeChain
{
    public Vector2[] Points = Array.Empty<Vector2>();
    public Vector2[] Previous = Array.Empty<Vector2>();
    public int Count;
    public float SegmentRest;

    /// <summary>Last pinned end positions, used to detect teleports.</summary>
    public Vector2 EndA;
    public Vector2 EndB;

    public MapId MapId = MapId.Nullspace;
    public float Strain;
    public float Width = 0.1f;
    public Color Color = Color.White;

    /// <summary>False while the rope is off screen: it keeps its chain but is neither stepped nor drawn.</summary>
    public bool Visible;

    /// <summary>Stable per-rope noise seed so its curls do not change between frames.</summary>
    public int Seed;
}
