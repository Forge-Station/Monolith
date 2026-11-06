using System.Numerics;
using System.Text;
using Content.Client._WF.Remade;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Lattice;

/// <summary>
/// A Drift pulse: vision closes to a tunnel that throbs in time with the bass, and red glyphs crawl round its edge,
/// for a few seconds. Drawn over the viewport only, so the HUD stays usable.
/// </summary>
public sealed class WFDriftPulseOverlay : Overlay
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IResourceCache _cache = default!;

    private static readonly ProtoId<ShaderPrototype> Shader = "WFDriftPulse";

    /// <summary>Seconds into the pulse of each heartbeat, matching the sound.</summary>
    private static readonly float[] Beats = { 0.25f, 1.25f, 2.25f };

    private const float DubDelay = 0.27f;
    private const int GlyphCount = 46;
    private const int GlyphSize = 22;
    private const float GlyphShiftSeconds = 0.14f;
    private static readonly Color Ink = Color.FromHex("#c0262a");

    private readonly ShaderInstance _shader;
    private readonly Font _font;
    private readonly StringBuilder _word = new();
    private TimeSpan _start;
    private float _seconds;
    private int _seed;

    /// <summary>True with reduced motion: the tunnel holds still and the glyphs don't crawl.</summary>
    public bool Still;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public WFDriftPulseOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(Shader).InstanceUnique();
        _font = WFMonolithTheme.Glyphs(_cache, GlyphSize);
    }

    /// <summary>Starts a pulse lasting <paramref name="seconds"/>.</summary>
    public void Start(float seconds, int seed)
    {
        _start = _timing.RealTime;
        _seconds = seconds;
        _seed = seed;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return Elapsed() < _seconds;
    }

    private float Elapsed() => (float) (_timing.RealTime - _start).TotalSeconds;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var t = Elapsed();
        var envelope = Smooth(0f, 0.3f, t) * (1f - Smooth(_seconds - 0.7f, _seconds, t));
        var beat = 0f;
        if (!Still)
        {
            foreach (var at in Beats)
            {
                beat += Kick(t - at) + 0.6f * Kick(t - at - DubDelay);
            }
        }

        var box = args.ViewportBounds;
        var size = new Vector2(box.Width, box.Height);
        var handle = args.ScreenHandle;

        _shader.SetParameter("strength", envelope);
        _shader.SetParameter("beat", Math.Clamp(beat, 0f, 1.5f));
        _shader.SetParameter("aspect", size.X / Math.Max(size.Y, 1f));
        _shader.SetParameter("still", Still);
        handle.UseShader(_shader);
        handle.DrawRect(box, Color.White);
        handle.UseShader(null);

        DrawGlyphs(handle, box.TopLeft, size, t, envelope, beat);
    }

    /// <summary>Glyph words on a ring at the tunnel's edge, crawling round it and rewriting themselves.</summary>
    private void DrawGlyphs(DrawingHandleScreen handle, Vector2 origin, Vector2 size, float t, float envelope, float beat)
    {
        var centre = origin + size / 2f;
        var radius = size / 2f * (0.78f + 0.06f * envelope - 0.04f * beat);
        var crawl = Still ? 0f : t * 0.06f;
        var shift = Still ? 0 : (int) (t / GlyphShiftSeconds);
        for (var i = 0; i < GlyphCount; i++)
        {
            var h = Hash(_seed, i);
            var angle = MathF.Tau * (i + h * 0.6f) / GlyphCount + crawl * (h > 0.5f ? 1f : -1f);
            var motion = Still ? 0f : t;
            var wobble = 1f + 0.07f * MathF.Sin(motion * 3f + h * 20f) + 0.05f * h;
            var at = centre + new Vector2(MathF.Cos(angle) * radius.X, MathF.Sin(angle) * radius.Y) * wobble;

            // Each word flickers in and out on its own, brighter on the beat.
            var flicker = 0.5f + 0.5f * MathF.Sin(motion * (2f + h * 5f) + h * 40f);
            var alpha = envelope * Math.Clamp(0.35f + 0.5f * flicker + 0.4f * beat, 0f, 1f);
            if (alpha <= 0.02f)
                continue;

            _word.Clear();
            var length = 2 + (int) (h * 4f);
            for (var c = 0; c < length; c++)
            {
                _word.Append((char) ('A' + (int) (Hash(_seed + shift * 31 + c, i) * 26f) % 26));
            }

            var text = _word.ToString();
            var scale = 1f + 0.35f * beat;
            var bounds = handle.GetDimensions(_font, text, scale);
            var ink = Ink;
            handle.DrawString(_font, at - bounds / 2f, text, scale, ink.WithAlpha(alpha));
        }
    }

    private static float Kick(float since)
    {
        return since < 0f ? 0f : MathF.Exp(-since / 0.16f);
    }

    private static float Smooth(float from, float to, float x)
    {
        var k = Math.Clamp((x - from) / (to - from), 0f, 1f);
        return k * k * (3f - 2f * k);
    }

    private static float Hash(int a, int b)
    {
        var state = (uint) a * 2654435761u ^ (uint) b * 2246822519u;
        state ^= state >> 15;
        state *= 2654435761u;
        state ^= state >> 13;
        return (state & 0xFFFFFF) / 16777216f;
    }
}
