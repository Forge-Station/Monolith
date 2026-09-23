using System.Numerics;
using Content.Shared._WF.Tether;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Tether;

/// <summary>
/// Steps every visible rope's verlet chain once per frame and owns the overlay that draws them.
/// The chains live here, not on the networked component, so nothing about the simulation can
/// desynchronise or be mispredicted.
/// </summary>
public sealed class RopeVisualizerSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Metres of viewport margin kept simulated, so a rope entering the screen is already settled.</summary>
    private const float ViewMargin = 12f;

    /// <summary>Peak per-point wander of a fully slack rope, in m/s.</summary>
    private const float SlackDrift = 0.9f;

    private readonly Dictionary<EntityUid, RopeChain> _chains = new();
    private readonly HashSet<EntityUid> _seen = new();
    private readonly List<EntityUid> _stale = new();

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new RopeOverlay(_chains, _timing));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<RopeOverlay>();
        _chains.Clear();
        base.Shutdown();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        _seen.Clear();
        var view = _eye.GetWorldViewbounds().CalcBoundingBox().Enlarged(ViewMargin);
        var currentMap = _eye.CurrentMap;
        var time = (float) (_timing.CurTime.TotalSeconds % 1000);

        var query = EntityQueryEnumerator<RopeComponent>();
        while (query.MoveNext(out var uid, out var rope))
        {
            if (!TryResolveEnds(rope, out var endA, out var endB, out var mapId) ||
                !_protos.TryIndex(rope.RopeType, out var proto))
                continue;

            _seen.Add(uid);
            var chain = GetChain(uid, rope, proto, endA, endB, mapId);
            chain.Strain = float.IsFinite(rope.Strain) ? Math.Clamp(rope.Strain, 0f, 2f) : 0f;
            chain.MapId = mapId;

            var span = new Box2(Vector2.Min(endA, endB), Vector2.Max(endA, endB))
                .Enlarged(MathF.Max(rope.Length * 0.5f, 1f));
            chain.Visible = mapId == currentMap && view.Intersects(span);
            // A hidden chain keeps its last simulated ends, so a big move off screen reseeds it on return.
            if (!chain.Visible)
                continue;

            if (RopeVerlet.NeedsReseed(chain.EndA, chain.EndB, endA, endB))
                RopeVerlet.Seed(chain.Points, chain.Previous, chain.Count, endA, endB, MathF.Min(rope.Length * 0.1f, 0.6f));

            chain.EndA = endA;
            chain.EndB = endB;
            var distance = Vector2.Distance(endA, endB);
            var slack = rope.Length > 0.01f ? Math.Clamp(1f - distance / rope.Length, 0f, 1f) : 0f;
            // Slack rope wanders and curls; a taut chain is pulled straight by the constraint alone.
            var drift = SlackDrift * slack * slack;

            var remaining = Math.Clamp(frameTime, 0f, RopeVerlet.MaxStep * 3f);
            while (remaining > 0.0001f)
            {
                var step = MathF.Min(remaining, RopeVerlet.MaxStep);
                RopeVerlet.Step(chain.Points, chain.Previous, chain.Count, endA, endB, chain.SegmentRest,
                    step, drift, time, chain.Seed);
                remaining -= step;
            }
        }

        _stale.Clear();
        foreach (var uid in _chains.Keys)
        {
            if (!_seen.Contains(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            _chains.Remove(uid);
        }
    }

    /// <summary>Creates or resizes a rope's chain. A length change re-seeds it along the new line.</summary>
    private RopeChain GetChain(
        EntityUid uid,
        RopeComponent rope,
        RopeTypePrototype proto,
        Vector2 endA,
        Vector2 endB,
        MapId mapId)
    {
        var count = RopeVerlet.PointCount(rope.Length, proto.SegmentsPerMetre);
        var rest = MathF.Max(rope.Length, RopeMath.MinLength) / (count - 1);
        if (!_chains.TryGetValue(uid, out var chain))
        {
            chain = new RopeChain { Seed = uid.GetHashCode() };
            _chains[uid] = chain;
        }

        chain.Color = Color.InterpolateBetween(proto.Color, proto.TautColor,
            Math.Clamp(float.IsFinite(rope.Strain) ? rope.Strain : 0f, 0f, 1f));
        chain.Width = MathF.Max(proto.Width, 0.01f);

        if (chain.Count != count || chain.Points.Length < count || !MathHelper.CloseTo(chain.SegmentRest, rest, 0.0001f))
        {
            chain.Points = new Vector2[count];
            chain.Previous = new Vector2[count];
            chain.Count = count;
            chain.SegmentRest = rest;
            RopeVerlet.Seed(chain.Points, chain.Previous, count, endA, endB, MathF.Min(rope.Length * 0.1f, 0.6f));
            chain.EndA = endA;
            chain.EndB = endB;
            chain.MapId = mapId;
        }

        return chain;
    }

    /// <summary>
    /// World positions of both ends, including an attach point's local offset. Either end may be a
    /// plain entity (a player carrying a loose end), which simply uses its own position.
    /// </summary>
    private bool TryResolveEnds(RopeComponent rope, out Vector2 endA, out Vector2 endB, out MapId mapId)
    {
        endA = default;
        endB = default;
        mapId = MapId.Nullspace;
        // An end outside PVS range is missing or detached here; the server's coarse position
        // stands in so the rope keeps drawing towards it.
        var hasA = TryGetEntity(rope.EndA, out var a) && TryPoint(a.Value, out endA, out mapId);
        var mapA = mapId;
        var hasB = TryGetEntity(rope.EndB, out var b) && TryPoint(b.Value, out endB, out mapId);
        var mapB = mapId;
        mapId = MapId.Nullspace;
        if (!hasA && !hasB)
            return false;

        if (!hasA)
        {
            endA = rope.WorldA;
            mapA = mapB;
        }
        else if (!hasB)
        {
            endB = rope.WorldB;
            mapB = mapA;
        }

        if (mapA != mapB || mapA == MapId.Nullspace || (!hasA || !hasB) && rope.WorldA == rope.WorldB)
            return false;

        mapId = mapA;
        return true;
    }

    private bool TryPoint(EntityUid uid, out Vector2 position, out MapId mapId)
    {
        position = default;
        mapId = MapId.Nullspace;
        if (Deleted(uid) || !TryComp<TransformComponent>(uid, out var xform) ||
            !TryComp<MetaDataComponent>(uid, out var meta) || (meta.Flags & MetaDataFlags.Detached) != 0)
            return false;

        position = _transform.GetWorldPosition(xform);
        if (TryComp<RopeAttachPointComponent>(uid, out var attach) && attach.LocalOffset != Vector2.Zero)
            position += _transform.GetWorldRotation(xform).RotateVec(attach.LocalOffset);

        mapId = xform.MapID;
        return float.IsFinite(position.X) && float.IsFinite(position.Y);
    }
}
