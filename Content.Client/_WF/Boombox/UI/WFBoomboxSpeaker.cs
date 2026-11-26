using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._WF.Boombox.UI;

/// <summary>
/// One of the boombox's round speakers: a grille ring around a cone that thumps while music plays.
/// </summary>
public sealed class WFBoomboxSpeaker : Control
{
    private static readonly Color Rim = Color.FromHex("#9a9aa3");
    private static readonly Color Grille = Color.FromHex("#1b1a1e");
    private static readonly Color GrilleDot = Color.FromHex("#3a3940");
    private static readonly Color Cone = Color.FromHex("#2b2a30");
    private static readonly Color ConeRing = Color.FromHex("#4a4952");
    private static readonly Color Cap = Color.FromHex("#c8c8d0");

    /// <summary>Whether the cone thumps.</summary>
    public bool Playing { get; set; }

    /// <summary>Offsets the beat so the two speakers don't move in lockstep.</summary>
    public float Phase { get; set; }

    private float _time;

    public WFBoomboxSpeaker()
    {
        MinSize = new Vector2(110, 110);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var center = PixelSize / 2f;
        var radius = MathF.Min(PixelSize.X, PixelSize.Y) / 2f - 2f;
        if (radius <= 4f)
            return;

        handle.DrawCircle(center, radius, Rim);
        handle.DrawCircle(center, radius * 0.92f, Grille);

        // Two rings of grille holes.
        for (var ring = 0; ring < 2; ring++)
        {
            var at = radius * (0.84f - ring * 0.08f);
            var holes = 28 - ring * 6;
            for (var i = 0; i < holes; i++)
            {
                var angle = MathF.Tau * i / holes + ring * 0.2f;
                handle.DrawCircle(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * at, MathF.Max(1f, radius * 0.025f), GrilleDot);
            }
        }

        // A kick on every beat at about 120 bpm, decaying between.
        var beat = Playing ? MathF.Exp(-6f * ((_time * 2f + Phase) % 1f)) : 0f;
        var cone = radius * (0.62f + 0.06f * beat);

        handle.DrawCircle(center, cone, Cone);
        handle.DrawCircle(center, cone * 0.8f, ConeRing, false);
        handle.DrawCircle(center, cone * 0.55f, ConeRing, false);
        handle.DrawCircle(center, cone * 0.28f, Cap);
    }
}
