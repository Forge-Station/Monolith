using System.Numerics;
using Content.Shared._WF.TractorBeam;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.TractorBeam;

/// <summary>
/// Draws lightweight replicated fields independently of the dish's visibility range.
/// No cached endpoints survive effect deletion or an unavailable target transform.
/// </summary>
public sealed class TractorBeamOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;
    private readonly IGameTiming _timing;
    private readonly ShaderInstance _unshaded;
    private const int Sections = 48;
    private const int Bands = 10;
    private const float HullClearance = 0.75f;
    // Managed buffers avoid allocations and the client's prohibition on stackalloc.
    private readonly DrawVertexUV2DColor[] _fill = new DrawVertexUV2DColor[Sections * 9];
    private readonly DrawVertexUV2DColor[] _lines = new DrawVertexUV2DColor[Sections * Bands * 2 + 8];

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public TractorBeamOverlay(IEntityManager entities, IPrototypeManager prototypes, IGameTiming timing)
    {
        _entities = entities;
        _transform = entities.System<SharedTransformSystem>();
        _timing = timing;
        _unshaded = prototypes.Index(UnshadedShader).Instance();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return;

        var handle = args.WorldHandle;
        var transforms = _entities.GetEntityQuery<TransformComponent>();
        var metadata = _entities.GetEntityQuery<MetaDataComponent>();
        var query = _entities.EntityQueryEnumerator<TractorBeamVisualComponent, TransformComponent>();
        var time = (float) (_timing.CurTime.TotalSeconds % 1000);

        handle.UseShader(_unshaded);
        while (query.MoveNext(out var uid, out var emitter, out var emitterTransform))
        {
            if (emitter.Target is not { } target ||
                _entities.Deleted(target) ||
                emitterTransform.MapID != args.MapId ||
                !transforms.TryGetComponent(target, out var targetTransform) ||
                targetTransform.MapID != args.MapId ||
                !metadata.TryGetComponent(uid, out var emitterMeta) ||
                !metadata.TryGetComponent(target, out var targetMeta) ||
                (emitterMeta.Flags & MetaDataFlags.Detached) != 0 ||
                (targetMeta.Flags & MetaDataFlags.Detached) != 0)
            {
                continue;
            }

            var start = _transform.GetWorldPosition(emitterTransform, transforms);
            var targetMatrix = _transform.GetWorldMatrix(targetTransform, transforms);
            if (!Matrix3x2.Invert(targetMatrix, out var worldToTarget))
                continue;
            var end = Vector2.Transform(emitter.TargetOffset, targetMatrix);
            var delta = end - start;
            var distance = delta.Length();
            if (!float.IsFinite(distance) || distance < 0.1f)
                continue;

            var perpendicular = new Vector2(-delta.Y, delta.X) / distance;
            var halfWidth = TractorBeamGeometry.GetTargetHalfWidth(start, end, emitter.TargetBounds, targetMatrix);
            halfWidth *= float.IsFinite(emitter.WidthScale) ? Math.Clamp(emitter.WidthScale, 0.05f, 1f) : 1f;
            if (!float.IsFinite(halfWidth) || halfWidth <= 0f)
                continue;
            var left = end + perpendicular * halfWidth;
            var right = end - perpendicular * halfWidth;
            var bounds = new Box2(Vector2.Min(start, Vector2.Min(left, right)),
                Vector2.Max(start, Vector2.Max(left, right)));
            if (!args.WorldAABB.Intersects(bounds))
                continue;

            var strain = float.IsFinite(emitter.Strain) ? Math.Clamp(emitter.Strain, 0f, 1f) : 0f;
            var pulse = 0.9f + 0.1f * MathF.Sin(time * (4f + 6f * strain));
            var cyan = new Color(0.25f, 0.8f, 1f);
            var amber = new Color(1f, 0.65f, 0.08f);
            var red = new Color(1f, 0.06f, 0.03f);
            var tint = strain < 0.65f
                ? Color.InterpolateBetween(cyan, amber, strain / 0.65f)
                : Color.InterpolateBetween(amber, red, Math.Clamp((strain - 0.65f) / 0.20f, 0f, 1f));
            // The last 15% of authority flickers red. Modulate all parts of the field,
            // keeping its existing transparency and hull clearance even at maximum load.
            var danger = Math.Clamp((strain - 0.85f) / 0.15f, 0f, 1f);
            var flutter = MathF.Sin(time * 13f) + 0.45f * MathF.Sin(time * 23f);
            var flicker = 1f - danger * (flutter > 0.35f ? 0.85f : 0.08f);
            var color = Color.FromSrgb(tint);
            var fillColor = color.WithAlpha((0.018f + strain * 0.022f) * pulse * flicker);
            var clearColor = color.WithAlpha(0f);
            var fillCount = 0;
            var lineCount = 0;
            var phase = time * (0.25f + strain * 0.35f);
            phase -= MathF.Floor(phase);
            for (var section = 0; section < Sections; section++)
            {
                var sectionLeft = Vector2.Lerp(left, right, (float) section / Sections);
                var sectionRight = Vector2.Lerp(left, right, (section + 1f) / Sections);
                if (!TractorBeamGeometry.ClipVisualSection(start, sectionLeft, sectionRight, emitter.TargetBounds,
                        worldToTarget, HullClearance, out var clippedLeft, out var clippedRight))
                    continue;

                // The last 1.2 metres fade away ahead of the rounded hull envelope.
                var innerLeft = FadeStart(start, clippedLeft);
                var innerRight = FadeStart(start, clippedRight);
                _fill[fillCount++] = new DrawVertexUV2DColor(start, fillColor);
                _fill[fillCount++] = new DrawVertexUV2DColor(innerLeft, fillColor);
                _fill[fillCount++] = new DrawVertexUV2DColor(innerRight, fillColor);
                _fill[fillCount++] = new DrawVertexUV2DColor(innerLeft, fillColor);
                _fill[fillCount++] = new DrawVertexUV2DColor(clippedLeft, clearColor);
                _fill[fillCount++] = new DrawVertexUV2DColor(clippedRight, clearColor);
                _fill[fillCount++] = new DrawVertexUV2DColor(innerLeft, fillColor);
                _fill[fillCount++] = new DrawVertexUV2DColor(clippedRight, clearColor);
                _fill[fillCount++] = new DrawVertexUV2DColor(innerRight, fillColor);

                // Bands follow the cutout instead of crossing the ship. Higher strain remains
                // visible through movement and color without making the field opaque.
                for (var i = 0; i < Bands; i++)
                {
                    var fraction = (i + 1f - phase) / Bands;
                    var bandColor = color.WithAlpha(MathF.Sin(fraction * MathF.PI) * (0.04f + strain * 0.08f) * flicker);
                    _lines[lineCount++] = new DrawVertexUV2DColor(Vector2.Lerp(start, clippedLeft, fraction), bandColor);
                    _lines[lineCount++] = new DrawVertexUV2DColor(Vector2.Lerp(start, clippedRight, fraction), bandColor);
                }

                if (section == 0 || section == Sections - 1)
                {
                    var edgeColor = color.WithAlpha((0.10f + strain * 0.10f) * flicker);
                    var inner = section == 0 ? innerLeft : innerRight;
                    var outer = section == 0 ? clippedLeft : clippedRight;
                    _lines[lineCount++] = new DrawVertexUV2DColor(start, edgeColor);
                    _lines[lineCount++] = new DrawVertexUV2DColor(inner, edgeColor);
                    _lines[lineCount++] = new DrawVertexUV2DColor(inner, edgeColor);
                    _lines[lineCount++] = new DrawVertexUV2DColor(outer, clearColor);
                }
            }

            if (fillCount > 0)
            {
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
                    new ReadOnlySpan<DrawVertexUV2DColor>(_fill, 0, fillCount));
            }
            if (lineCount > 0)
            {
                handle.DrawPrimitives(DrawPrimitiveTopology.LineList, Texture.White,
                    new ReadOnlySpan<DrawVertexUV2DColor>(_lines, 0, lineCount));
            }
            handle.DrawCircle(start, 0.12f + strain * 0.06f, color.WithAlpha(0.45f * flicker));
        }

        handle.UseShader(null);
    }

    private static Vector2 FadeStart(Vector2 start, Vector2 end)
    {
        var length = Vector2.Distance(start, end);
        return Vector2.Lerp(start, end, length > 0f ? MathF.Max(0f, 1f - 1.2f / length) : 0f);
    }
}
