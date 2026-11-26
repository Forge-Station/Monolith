using System.Numerics;
using Robust.Client.Graphics;
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

    /// <summary>The rope type's per-segment texture, drawn along the rope instead of a plain strip.</summary>
    public Texture? SegmentTexture;

    /// <summary>Width at end B as a share of the width at end A.</summary>
    public float Taper = 1f;

    /// <summary>The repeating strand drawn along the core, if the rope type has one.</summary>
    public Texture? Texture;

    /// <summary>Metres of rope one repeat of <see cref="Texture"/> covers.</summary>
    public float TextureLength = 1f;

    /// <summary>How hard the chain resists folding.</summary>
    public float BendStiffness;

    /// <summary>Colour of the sparks running along it, if it has any.</summary>
    public Color? SparkColor;

    /// <summary>Metres of rope to each spark.</summary>
    public float SparkSpacing = 10f;

    /// <summary>The rope's length in metres.</summary>
    public float Length;

    /// <summary>False while the rope is off screen: it keeps its chain but is neither stepped nor drawn.</summary>
    public bool Visible;

    /// <summary>Stable per-rope noise seed so its curls do not change between frames.</summary>
    public int Seed;
}
