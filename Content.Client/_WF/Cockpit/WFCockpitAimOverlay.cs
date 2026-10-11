using System.Numerics;
using Content.Client._WF.Shuttles.UI;
using Content.Client.Viewport;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Map;

namespace Content.Client._WF.Cockpit;

/// <summary>Draws the weapon-to-aim preview lines in the cockpit's own world view, over space and hull alike.</summary>
public sealed class WFCockpitAimOverlay : Overlay
{
    /// <summary>Line widths in interface pixels: a dark halo under a bright core reads on both black space and pale hull.</summary>
    private const float CoreWidth = 2f;
    private const float HaloWidth = 4f;
    private const float CoreAlpha = 0.75f;
    private const float HaloAlpha = 0.45f;

    private readonly WFCockpitAimPreview _preview;
    private readonly ScalingViewport _world;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WFCockpitAimOverlay(WFCockpitAimPreview preview, ScalingViewport world)
    {
        _preview = preview;
        _world = world;
        // After the hull roof plates, which hide everything under them, and before the whole-picture effects.
        ZIndex = -5;
    }

    /// <summary>The lines to draw for a render pass; none unless it is the world view's own eye on the aim point's map.</summary>
    public bool TryGetSegments(IEye? pass, MapId map, out IReadOnlyList<WFCockpitAimSegment> segments)
    {
        segments = Array.Empty<WFCockpitAimSegment>();
        if (map == MapId.Nullspace || !_world.VisibleInTree || _world.Eye is not { } eye ||
            !ShuttleHullRoofOverlay.IsPilotPass(pass, eye, primaryOnly: true))
            return false;

        var lines = _preview.Refresh();
        if (lines.Count == 0 || _preview.Map != map)
            return false;

        segments = lines;
        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!TryGetSegments(args.Viewport.Eye, args.MapId, out var segments))
            return;

        var worldToLocal = args.Viewport.GetWorldToLocalMatrix();
        var localToScreen = _world.GetLocalToScreenMatrix();
        var pixelsPerMeter = new Vector2(worldToLocal.M11, worldToLocal.M12).Length() * localToScreen.M11;
        if (!float.IsFinite(pixelsPerMeter) || pixelsPerMeter <= 0f)
            return;

        var handle = args.WorldHandle;
        var scale = _world.UIScale / pixelsPerMeter;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            DrawBar(handle, segment.From, segment.To, HaloWidth * scale, Color.Black.WithAlpha(HaloAlpha));
            DrawBar(handle, segment.From, segment.To, CoreWidth * scale, segment.Color.WithAlpha(CoreAlpha));
        }
    }

    /// <summary>A filled strip of the given world width along the segment; plain world lines are always one pixel.</summary>
    private static void DrawBar(DrawingHandleWorld handle, Vector2 from, Vector2 to, float width, Color color)
    {
        var offset = to - from;
        var length = offset.Length();
        if (!float.IsFinite(length) || length < 0.001f)
            return;

        var centre = (from + to) / 2f;
        var box = new Box2(centre.X - length / 2f, centre.Y - width / 2f, centre.X + length / 2f, centre.Y + width / 2f);
        handle.DrawRect(new Box2Rotated(box, new Angle(MathF.Atan2(offset.Y, offset.X)), centre), color);
    }
}
