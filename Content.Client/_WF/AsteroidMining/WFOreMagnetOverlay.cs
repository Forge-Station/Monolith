using System.Numerics;
using Content.Shared._WF.AsteroidMining;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Content.Client._WF.AsteroidMining.WFOverlayDraw;

namespace Content.Client._WF.AsteroidMining;

/// <summary>
/// Draws an ore magnet's pull additively: a faint arc from the magnet out to each ore it has locked onto, with sparks
/// running down it toward the magnet, and an aura round the ore that tightens as the lock comes due.
/// </summary>
public sealed class WFOreMagnetOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> AdditiveShader = "WFAdditiveUnshaded";
    private static readonly Color Field = Color.FromHex("#5ec8ff");
    private const int Segments = 28;
    private const int Sparks = 4;
    private const int RingSides = 32;
    private const int FanSides = 16;

    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;
    private readonly IGameTiming _timing;
    private readonly ShaderInstance _additive;
    // Managed buffers avoid per-frame allocations; the client sandbox forbids stackalloc.
    private readonly DrawVertexUV2DColor[] _strip = new DrawVertexUV2DColor[(Segments + 1) * 2];
    private readonly DrawVertexUV2DColor[] _ring = new DrawVertexUV2DColor[(RingSides + 1) * 2];
    private readonly DrawVertexUV2DColor[] _fan = new DrawVertexUV2DColor[FanSides * 3];
    private readonly Vector2[] _curve = new Vector2[Segments + 1];

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public WFOreMagnetOverlay(IEntityManager entities, IPrototypeManager prototypes, IGameTiming timing)
    {
        _entities = entities;
        _transform = entities.System<SharedTransformSystem>();
        _timing = timing;
        _additive = prototypes.Index(AdditiveShader).Instance();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return;

        var handle = args.WorldHandle;
        var now = _timing.CurTime;
        var time = (float) (_timing.RealTime.TotalSeconds % 1000);
        var drawn = false;
        var query = _entities.EntityQueryEnumerator<WFOreMagnetPulledComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var pulled, out var xform))
        {
            if (xform.MapID != args.MapId || !_entities.TryGetComponent(pulled.Magnet, out TransformComponent? magnetXform) ||
                magnetXform.MapID != args.MapId)
                continue;

            var ore = _transform.GetWorldPosition(xform);
            var magnet = _transform.GetWorldPosition(magnetXform);
            if (!args.WorldAABB.Intersects(new Box2(Vector2.Min(ore, magnet), Vector2.Max(ore, magnet)).Enlarged(2f)))
                continue;

            if (!drawn)
            {
                handle.UseShader(_additive);
                drawn = true;
            }

            var span = MathF.Max(0.05f, (float) (pulled.End - pulled.Start).TotalSeconds);
            var elapsed = (float) (now - pulled.Start).TotalSeconds;
            var progress = Math.Clamp(elapsed / span, 0f, 1f);
            var fade = Math.Clamp(elapsed / 0.15f, 0f, 1f);
            var phase = uid.Id % 97 * 0.37f;

            DrawArc(handle, magnet, ore, uid.Id % 2 == 0 ? 1f : -1f, fade, time, phase);
            DrawAura(handle, ore, progress, fade, time, phase);
        }

        if (drawn)
            handle.UseShader(null);
    }

    /// <summary>A soft arc bowed to one side, thin at the magnet and widening toward the ore, with sparks drawn inward.</summary>
    private void DrawArc(DrawingHandleWorld handle, Vector2 magnet, Vector2 ore, float side, float fade, float time, float phase)
    {
        var delta = ore - magnet;
        var length = delta.Length();
        if (length < 0.3f)
            return;

        var normal = new Vector2(-delta.Y, delta.X) / length;
        var control = (magnet + ore) / 2f + normal * side * MathF.Min(length * 0.22f, 4f);
        for (var i = 0; i <= Segments; i++)
        {
            var t = (float) i / Segments;
            var u = 1f - t;
            _curve[i] = u * u * magnet + 2f * u * t * control + t * t * ore;
        }

        var core = Field.WithAlpha(0.28f * fade);
        var clear = Field.WithAlpha(0f);
        for (var edge = -1; edge <= 1; edge += 2)
        {
            var count = 0;
            for (var i = 0; i <= Segments; i++)
            {
                var t = (float) i / Segments;
                var tangent = _curve[Math.Min(i + 1, Segments)] - _curve[Math.Max(i - 1, 0)];
                var across = tangent.LengthSquared() > 0f ? Vector2.Normalize(new Vector2(-tangent.Y, tangent.X)) : normal;
                var width = 0.03f + 0.07f * t;
                _strip[count++] = new DrawVertexUV2DColor(_curve[i], core);
                _strip[count++] = new DrawVertexUV2DColor(_curve[i] + across * edge * width, clear);
            }

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, Texture.White,
                new ReadOnlySpan<DrawVertexUV2DColor>(_strip, 0, count));
        }

        // Sparks run from the ore back to the magnet, the way the ore is about to go.
        var speed = 1.6f / MathF.Max(1f, MathF.Sqrt(length));
        for (var i = 0; i < Sparks; i++)
        {
            var t = 1f - Frac(time * speed + (float) i / Sparks + phase);
            var index = Math.Clamp(t * Segments, 0f, Segments);
            var low = (int) MathF.Floor(index);
            var point = Vector2.Lerp(_curve[low], _curve[Math.Min(low + 1, Segments)], index - low);
            Glow(handle, _fan, point, 0.12f, Field.WithAlpha(0.55f * fade * MathF.Sin(MathF.PI * t)));
        }

        Glow(handle, _fan, magnet, 0.3f, Field.WithAlpha(0.35f * fade));
    }

    /// <summary>A shimmering ring round the ore that closes in as the lock comes due, over a faint halo.</summary>
    private void DrawAura(DrawingHandleWorld handle, Vector2 ore, float progress, float fade, float time, float phase)
    {
        var pulse = 0.85f + 0.15f * MathF.Sin(time * 9f + phase);
        Glow(handle, _fan, ore, 0.5f, Field.WithAlpha(0.18f * fade * pulse));

        var radius = 0.55f - 0.2f * progress;
        Ring(handle, _ring, ore, radius - 0.09f, radius, Field.WithAlpha((0.35f + 0.3f * progress) * fade * pulse));
    }
}
