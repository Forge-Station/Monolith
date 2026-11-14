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
/// Draws mining beams additively: a soft glow around a hot core, a pair of energy strands wound round it, pulses
/// running out to the rock, and a flaring, sparking impact point on its surface.
/// </summary>
public sealed class WFMiningBeamOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> AdditiveShader = "WFAdditiveUnshaded";
    private static readonly Color Overheat = new(1f, 0.22f, 0.12f);
    private const int Segments = 40;
    private const int Strands = 2;
    private const int Pulses = 5;
    private const int Sparks = 10;
    private const int FanSides = 20;

    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;
    private readonly IGameTiming _timing;
    private readonly ShaderInstance _additive;
    // Managed buffers avoid per-frame allocations; the client sandbox forbids stackalloc.
    private readonly DrawVertexUV2DColor[] _strip = new DrawVertexUV2DColor[(Segments + 1) * 2];
    // Shared by the strands and the impact sparks (doubled in the window), which draw one after the other.
    private readonly DrawVertexUV2DColor[] _lines = new DrawVertexUV2DColor[Math.Max(Segments * Strands * 2, Sparks * 2 * 2)];
    private readonly DrawVertexUV2DColor[] _fan = new DrawVertexUV2DColor[FanSides * 3];

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public WFMiningBeamOverlay(IEntityManager entities, IPrototypeManager prototypes, IGameTiming timing)
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
        var time = (float) (_timing.RealTime.TotalSeconds % 1000);
        var query = _entities.EntityQueryEnumerator<WFMiningBeamEmitterComponent, TransformComponent>();

        handle.UseShader(_additive);
        while (query.MoveNext(out var uid, out var beam, out var xform))
        {
            if (!beam.Active || xform.MapID != args.MapId)
                continue;

            // Fire control aims each pulse at the cursor from the gun's centre, so draw along the shot rather than the barrel,
            // from the barrel's reach out to where the pulse hit.
            var direction = beam.BeamDirection.LengthSquared() > 0.5f ? Vector2.Normalize(beam.BeamDirection) : -Vector2.UnitY;
            var reach = beam.MuzzleOffset.Length();
            var muzzle = _transform.GetWorldPosition(xform) + direction * reach;
            var length = MathF.Max(0.3f, beam.BeamLength - reach);
            var end = muzzle + direction * length;
            var bounds = new Box2(Vector2.Min(muzzle, end), Vector2.Max(muzzle, end)).Enlarged(1.5f);
            if (!args.WorldAABB.Intersects(bounds))
                continue;

            var heat = Math.Clamp(beam.Charge, 0f, 1.3f);
            var power = Math.Clamp(beam.Power, 0f, 1f);
            var line = WFAsteroidHeat.OverchargeLine;
            var color = Color.InterpolateBetween(beam.BeamColor, Overheat, Math.Clamp((heat - line + 0.2f) / 0.3f, 0f, 1f));
            var flicker = heat >= line ? 0.75f + 0.25f * MathF.Sin(time * 31f) : 1f;
            var width = (0.55f + 0.45f * power) * flicker * beam.BeamWidth;
            var normal = new Vector2(-direction.Y, direction.X);
            var phase = (float) (uid.Id % 97) * 0.37f;

            // Wide soft glow, a tighter body, then a near-white core.
            Ribbon(handle, muzzle, direction, normal, length, 0.42f * width, color.WithAlpha(0.22f), time, phase, 0.12f);
            Ribbon(handle, muzzle, direction, normal, length, 0.16f * width, color.WithAlpha(0.55f), time, phase, 0.2f);
            Ribbon(handle, muzzle, direction, normal, length, (0.05f + 0.03f * power) * beam.BeamWidth,
                Color.InterpolateBetween(color, Color.White, 0.75f).WithAlpha(0.95f), time, phase, 0.05f);

            DrawStrands(handle, muzzle, direction, normal, length, width, color, time);
            DrawPulses(handle, muzzle, direction, length, width, color, time, phase);
            DrawImpact(handle, end, direction, normal, width, heat, color, time, phase, beam.InWindow, beam.Hot);
            Glow(handle, _fan, muzzle, 0.35f * width + 0.1f, color.WithAlpha(0.6f));
        }

        handle.UseShader(null);
    }

    /// <summary>A strip whose width breathes along its length and fades to nothing at both edges.</summary>
    private void Ribbon(DrawingHandleWorld handle, Vector2 start, Vector2 direction, Vector2 normal, float length,
        float halfWidth, Color color, float time, float phase, float breathe)
    {
        var clear = color.WithAlpha(0f);
        for (var side = -1; side <= 1; side += 2)
        {
            var count = 0;
            for (var i = 0; i <= Segments; i++)
            {
                var t = (float) i / Segments;
                var along = start + direction * (length * t);
                // Narrow at the muzzle, full width by a metre out.
                var taper = MathF.Min(1f, 0.35f + length * t);
                var wave = 1f + breathe * MathF.Sin(t * length * 1.7f - time * 9f + phase);
                _strip[count++] = new DrawVertexUV2DColor(along, color);
                _strip[count++] = new DrawVertexUV2DColor(along + normal * (side * halfWidth * taper * wave), clear);
            }

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, Texture.White,
                new ReadOnlySpan<DrawVertexUV2DColor>(_strip, 0, count));
        }
    }

    /// <summary>Two thin strands spiralling round the core.</summary>
    private void DrawStrands(DrawingHandleWorld handle, Vector2 start, Vector2 direction, Vector2 normal, float length,
        float width, Color color, float time)
    {
        var count = 0;
        var strandColor = Color.InterpolateBetween(color, Color.White, 0.4f);
        for (var strand = 0; strand < Strands; strand++)
        {
            var offset = MathF.PI * strand;
            for (var i = 0; i < Segments; i++)
            {
                var a = (float) i / Segments;
                var b = (i + 1f) / Segments;
                _lines[count++] = new DrawVertexUV2DColor(StrandPoint(a), strandColor.WithAlpha(StrandAlpha(a)));
                _lines[count++] = new DrawVertexUV2DColor(StrandPoint(b), strandColor.WithAlpha(StrandAlpha(b)));
            }

            Vector2 StrandPoint(float t)
            {
                var swirl = MathF.Sin(t * length * 2.4f - time * 14f + offset);
                return start + direction * (length * t) + normal * (swirl * 0.13f * width * MathF.Min(1f, length * t));
            }
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.LineList, Texture.White,
            new ReadOnlySpan<DrawVertexUV2DColor>(_lines, 0, count));

        static float StrandAlpha(float t)
        {
            return 0.5f * MathF.Sin(MathF.PI * MathF.Min(1f, t * 1.15f));
        }
    }

    /// <summary>Bright packets of energy running from the emitter to the rock.</summary>
    private void DrawPulses(DrawingHandleWorld handle, Vector2 start, Vector2 direction, float length, float width,
        Color color, float time, float phase)
    {
        var speed = 14f / MathF.Max(length, 1f);
        for (var i = 0; i < Pulses; i++)
        {
            var t = Frac(time * speed + (float) i / Pulses + phase);
            var fade = MathF.Sin(MathF.PI * t);
            Glow(handle, _fan, start + direction * (length * t), (0.12f + 0.06f * fade) * width,
                Color.InterpolateBetween(color, Color.White, 0.6f).WithAlpha(0.55f * fade));
        }
    }

    /// <summary>
    /// A flare on the rock's surface with sparks spraying back toward the gun. Inside the cracking window the flare
    /// brightens and the spray doubles, so a gunner can feather the beam by eye; past it the spray turns red.
    /// </summary>
    private void DrawImpact(DrawingHandleWorld handle, Vector2 end, Vector2 direction, Vector2 normal, float width,
        float heat, Color color, float time, float phase, bool inWindow, bool hot)
    {
        var throb = 0.85f + 0.15f * MathF.Sin(time * (inWindow ? 26f : 17f) + phase);
        var flare = inWindow ? 1.45f : 1f;
        Glow(handle, _fan, end, (0.9f + 0.5f * heat) * width * throb * flare, color.WithAlpha(inWindow ? 0.6f : 0.45f));
        Glow(handle, _fan, end, 0.38f * width * throb * flare, Color.InterpolateBetween(color, Color.White, 0.8f).WithAlpha(0.9f));

        var count = 0;
        var tick = MathF.Floor(time * 20f);
        var sparks = inWindow || hot ? Sparks * 2 : Sparks;
        for (var i = 0; i < sparks; i++)
        {
            // A new spray every 50 ms, hashed so each spark keeps its path for that frame.
            var seed = Hash(tick * 13.1f + i * 7.7f + phase);
            var life = Hash(seed * 3.3f);
            var spread = (seed - 0.5f) * 2.4f;
            var sparkDirection = -direction * MathF.Cos(spread) + normal * MathF.Sin(spread);
            var reach = (0.25f + 0.85f * life) * (0.6f + 0.6f * heat) * (inWindow ? 1.5f : 1f);
            var from = end + sparkDirection * (0.15f * life);
            var sparkColor = hot ? Color.InterpolateBetween(color, Overheat, 0.6f) : Color.InterpolateBetween(color, Color.White, 0.5f);
            _lines[count++] = new DrawVertexUV2DColor(from, sparkColor.WithAlpha(0.9f));
            _lines[count++] = new DrawVertexUV2DColor(from + sparkDirection * reach, sparkColor.WithAlpha(0f));
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.LineList, Texture.White,
            new ReadOnlySpan<DrawVertexUV2DColor>(_lines, 0, count));
    }
}
