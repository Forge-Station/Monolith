using System.Diagnostics.CodeAnalysis;
using Content.Client._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.Caverns;

namespace Content.Client._WF.Caverns;

/// <summary>What the z-level renderer needs to show a cavern through the holes in the ground above it.</summary>
public sealed partial class WFCavernViewSystem : EntitySystem
{
    [Dependency] private CEClientZLevelsSystem _zLevels = default!;
    [Dependency] private WFCavernShadeVisualsSystem _shades = default!;

    /// <summary>The cavern under a ground layer, once this client has its map.</summary>
    public bool TryGetCavernBelow(EntityUid ground, [NotNullWhen(true)] out EntityUid? cavern)
    {
        cavern = null;

        if (!HasComp<CEZGroundLayerComponent>(ground)
            || !_zLevels.TryMapOffset(ground, -1, out var below)
            || !HasComp<WFCavernLayerComponent>(below))
            return false;

        cavern = below.Owner;
        return true;
    }

    /// <summary>Whether a map's pass must not draw the sky here: a cavern, or a ground with a hole in view.</summary>
    // So a hole shows the cavern under it or, before it streams in, plain dark.
    public bool HidesSky(EntityUid map, Box2 worldBox)
    {
        return HasComp<WFCavernLayerComponent>(map) || AnyHoleWithin(map, worldBox);
    }

    /// <summary>Whether one of a ground's cavern mouths has a hole tile inside a world box.</summary>
    public bool AnyHoleWithin(EntityUid ground, Box2 worldBox)
    {
        return _shades.AnyPitWithin(ground, worldBox);
    }

    /// <summary>The depth of the cavern pass under a ground drawn at this depth, or null to keep no pass.</summary>
    /// <param name="onGround">Whether the observer stands on that ground; from the air or orbit a hole shows dark, as the server sends no cavern there.</param>
    public static float? CavernPassDepth(float groundDepth, bool cavernKnown, bool mouthInView, bool onGround)
    {
        return onGround && cavernKnown && mouthInView ? groundDepth - 1f : null;
    }

    /// <summary>The world box a z-level pass at this depth shows, given the box the observer's own eye shows.</summary>
    /// <param name="shrink">The renderer's per-level scale. Monolith set it to 1, so levels below draw full size.</param>
    // As the renderer builds the pass eye: scale from the absolute depth, offset from the depth below the observer.
    public static Box2 LevelViewBox(Box2 view, Angle eyeRotation, float depth, float ownDepth,
        float shrink = CESharedZLevelsSystem.ZLevelViewShrink)
    {
        var centre = view.Center;
        var widen = MathF.Pow(shrink, depth);
        Angle rotation = eyeRotation * -1;
        var shift = rotation.ToWorldVec() * CESharedZLevelsSystem.ZLevelOffset * (depth - ownDepth);

        return new Box2(centre + (view.BottomLeft - centre) * widen + shift,
            centre + (view.TopRight - centre) * widen + shift);
    }
}
