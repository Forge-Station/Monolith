using System.Numerics;
using Content.Client._WF.Caverns;
using Content.Shared._WF.Planets;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    private WFCavernViewSystem? _wfCavernView;

    /// <summary>Adds the cavern under a ground layer as a pass and floor of the view, when a hole shows it to an observer on that ground.</summary>
    // Lower passes draw first and the ground's empty tiles draw nothing, so the cavern shows only through its holes.
    // A mouth, not any empty tile, opens it: unloaded ground at the edge of a far view keeps the sky it always had.
    // Seen from above the ground there is no pass: the server sends no cavern there, and the hole stays dark.
    private void WfAddCavernPass(EntityUid ground, float groundDepth, float ownDepth, ref float? occludeBelowDepth)
    {
        _wfCavernView ??= _entityManager.System<WFCavernViewSystem>();

        var onGround = _entityManager.GetComponentOrNull<TransformComponent>(_player.LocalEntity)?.MapUid == ground;
        var known = _wfCavernView.TryGetCavernBelow(ground, out var cavern);
        var mouth = onGround && known && _wfCavernView.AnyHoleWithin(ground, WfLevelView(ground, groundDepth, ownDepth));
        if (WFCavernViewSystem.CavernPassDepth(groundDepth, known, mouth, onGround) is not { } depth)
            return;

        _zPasses.Add((cavern!.Value, depth, false, false));
        occludeBelowDepth = depth;
    }

    /// <summary>The world box a pass at this depth shows, plus a tile.</summary>
    private Box2 WfLevelView(EntityUid ground, float depth, float ownDepth)
    {
        var drawBox = GetDrawBox();
        var bottomLeft = _eyeManager.ScreenToMap(drawBox.BottomLeft).Position;
        var bottomRight = _eyeManager.ScreenToMap(drawBox.BottomRight).Position;
        var topLeft = _eyeManager.ScreenToMap(drawBox.TopLeft).Position;
        var topRight = _eyeManager.ScreenToMap(drawBox.TopRight).Position;

        // The screen's corners on the map; a turned eye turns them, so take the box round all four.
        var min = Vector2.Min(Vector2.Min(bottomLeft, bottomRight), Vector2.Min(topLeft, topRight));
        var max = Vector2.Max(Vector2.Max(bottomLeft, bottomRight), Vector2.Max(topLeft, topRight));
        var view = new Box2(min, max);

        var rotation = _fallbackEye?.Rotation ?? Angle.Zero;
        return WFCavernViewSystem.LevelViewBox(view, rotation, depth, ownDepth, WFPlanetView.Shrink(_entityManager, ground)).Enlarged(1f);
    }
}
