using System.Numerics;
using Content.Shared._WF.Lattice;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Lattice;

/// <summary>
/// The Lattice made visible: a living beam of alien light between each pair of neighbouring primary anchors, leaving
/// each from the edge of its shield. A lost anchor breaks the two beams that ran to it.
/// </summary>
public sealed class WFLatticeBeamOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    /// <summary>How close an anchor must sit to its corner of the polygon to count as that corner's, in metres.</summary>
    private const float CornerMatch = 1200f;

    /// <summary>Full width of the beam's glow, in metres.</summary>
    private const float BeamWidth = 9f;

    private static readonly ProtoId<ShaderPrototype> Shader = "WFLatticeBeam";

    private readonly List<Vector2?> _corners = new();
    private readonly DrawVertexUV2D[] _quad = new DrawVertexUV2D[4];
    private readonly ShaderInstance _shader;

    public WFLatticeBeamOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 5;
        _shader = _proto.Index(Shader).InstanceUnique();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var map = args.MapUid;
        if (!_entities.TryGetComponent<WFLatticeFieldComponent>(map, out var field)
            || !_proto.TryIndex(field.Profile, out var profile)
            || profile.AnchorCount < 2)
            return;

        MatchCorners(field, profile);
        var view = args.WorldAABB.Enlarged(BeamWidth);
        var time = (float) _timing.RealTime.TotalSeconds;
        var handle = args.WorldHandle;
        for (var i = 0; i < _corners.Count; i++)
        {
            if (_corners[i] is not { } from || _corners[(i + 1) % _corners.Count] is not { } to)
                continue;

            var along = to - from;
            var length = along.Length();
            if (length <= profile.BeamOffset * 2f)
                continue;

            var dir = along / length;
            var start = from + dir * profile.BeamOffset;
            var end = to - dir * profile.BeamOffset;
            if (Clip(start, end, view) is not { } range)
                continue;

            var (t0, t1) = range;
            DrawBeam(handle, start, end, t0, t1, time, i);
        }
    }

    /// <summary>For each corner of the polygon, the live primary anchor standing at it, if there is one.</summary>
    private void MatchCorners(WFLatticeFieldComponent field, WFLatticeProfilePrototype profile)
    {
        _corners.Clear();
        foreach (var corner in WFLatticeMath.Corners(Vector2.Zero, profile.AnchorCount, profile.AnchorRadius, profile.AnchorAngle))
        {
            Vector2? found = null;
            var best = CornerMatch;
            for (var a = 0; a < field.Anchors.Count && a < field.Strengths.Count; a++)
            {
                // Relays hold less than a primary; only primaries carry the beams.
                if (field.Strengths[a] < 1f)
                    continue;

                var distance = Vector2.Distance(field.Anchors[a], corner);
                if (distance < best)
                {
                    best = distance;
                    found = field.Anchors[a];
                }
            }

            _corners.Add(found);
        }
    }

    private void DrawBeam(DrawingHandleWorld handle, Vector2 start, Vector2 end, float t0, float t1, float time, int seed)
    {
        var length = Vector2.Distance(start, end);
        var dir = (end - start) / length;
        var half = new Vector2(-dir.Y, dir.X) * (BeamWidth / 2f);
        var from = t0 * length;
        var to = t1 * length;
        var a = start + dir * from;
        var b = start + dir * to;

        // UV.x is metres along the whole beam, so the clipped piece on screen flows on from the rest of it.
        _quad[0] = new DrawVertexUV2D(a + half, new Vector2(from, 1f));
        _quad[1] = new DrawVertexUV2D(a - half, new Vector2(from, -1f));
        _quad[2] = new DrawVertexUV2D(b + half, new Vector2(to, 1f));
        _quad[3] = new DrawVertexUV2D(b - half, new Vector2(to, -1f));

        _shader.SetParameter("beamLength", length);
        _shader.SetParameter("seed", seed * 1.37f);
        _shader.SetParameter("shimmer", 0.5f + 0.5f * MathF.Sin(time * 3f + seed * 1.7f));
        handle.UseShader(_shader);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, Texture.White, _quad);
        handle.UseShader(null);
    }

    /// <summary>The share of a segment, 0 to 1 from start to end, that lies inside a box; null if none does.</summary>
    private static (float, float)? Clip(Vector2 start, Vector2 end, Box2 box)
    {
        var t0 = 0f;
        var t1 = 1f;
        var d = end - start;
        if (!Edge(-d.X, start.X - box.Left, ref t0, ref t1)
            || !Edge(d.X, box.Right - start.X, ref t0, ref t1)
            || !Edge(-d.Y, start.Y - box.Bottom, ref t0, ref t1)
            || !Edge(d.Y, box.Top - start.Y, ref t0, ref t1))
            return null;

        return t1 > t0 ? (t0, t1) : null;
    }

    private static bool Edge(float p, float q, ref float t0, ref float t1)
    {
        if (p == 0f)
            return q >= 0f;

        var r = q / p;
        if (p < 0f)
        {
            if (r > t1)
                return false;

            t0 = MathF.Max(t0, r);
        }
        else
        {
            if (r < t0)
                return false;

            t1 = MathF.Min(t1, r);
        }

        return true;
    }
}
