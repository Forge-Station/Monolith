using Content.Shared._WF.Planets;

namespace Content.Server._WF.Planets.Bounds;

/// <summary>How far each forming world has got, for the world-forming bar.</summary>
public sealed partial class WFPlanetPreloadSystem
{
    // Shares of the bar, roughly the time each part takes on a road world.
    private const float HoldsShare = 0.4f;
    private const float TerrainShare = 0.4f;
    private const float BoundaryShare = 0.12f;
    private const float WildlifeShare = 0.08f;

    /// <summary>Called by a hold, such as the sites or cavern mouths, as it goes; the fraction runs from 0 to 1.</summary>
    public void ReportProgress(EntityUid ground, string source, WFWorldFormingStage stage, float fraction)
    {
        foreach (var preload in _queue)
        {
            if (preload.Ground != ground)
                continue;

            preload.Reports[source] = (stage, Math.Clamp(fraction, 0f, 1f));
            return;
        }
    }

    /// <summary>Every world queued or forming, first come first served.</summary>
    public IEnumerable<(EntityUid Ground, WFWorldFormingStage Stage, float Progress)> Forming()
    {
        foreach (var preload in _queue)
        {
            var (stage, progress) = Measure(preload);
            preload.Shown = MathF.Max(preload.Shown, progress);
            yield return (preload.Ground, stage, preload.Shown);
        }
    }

    /// <summary>A world's stage and its unclamped progress, from 0 to 1.</summary>
    private static (WFWorldFormingStage Stage, float Progress) Measure(Preload preload)
    {
        switch (preload.Phase)
        {
            case PreloadPhase.Waiting:
                return (WFWorldFormingStage.Waiting, 0f);
            case PreloadPhase.Holds:
                var (stage, holds) = HoldProgress(preload);
                return (stage, HoldsShare * holds);
            case PreloadPhase.Terrain:
                var chunks = preload.Chunks is { Count: > 0 } list ? (float) preload.Next / list.Count : 0f;
                return (WFWorldFormingStage.Terrain, HoldsShare + TerrainShare * chunks);
            case PreloadPhase.Boundary:
                var tile = preload.RingTiles > 0 ? (float) preload.RingTile / preload.RingTiles : 0f;
                var rings = Math.Clamp((preload.RingsLaid + tile) / Math.Max(1, preload.Rings), 0f, 1f);
                return (WFWorldFormingStage.Boundary, HoldsShare + TerrainShare + BoundaryShare * rings);
            default:
                var work = preload.Work > 0 ? (float) preload.WorkDone / preload.Work : 0f;
                return (WFWorldFormingStage.Wildlife, HoldsShare + TerrainShare + BoundaryShare + WildlifeShare * work);
        }
    }

    /// <summary>The holds' mean fraction, under the stage of whichever is furthest behind.</summary>
    private static (WFWorldFormingStage Stage, float Fraction) HoldProgress(Preload preload)
    {
        if (preload.Reports.Count == 0)
            return (WFWorldFormingStage.Sites, 0f);

        var total = 0f;
        var behind = WFWorldFormingStage.Sites;
        var least = float.MaxValue;
        foreach (var (stage, fraction) in preload.Reports.Values)
        {
            total += fraction;
            if (fraction >= 1f || fraction >= least)
                continue;

            least = fraction;
            behind = stage;
        }

        return (behind, total / preload.Reports.Count);
    }

    /// <summary>Where a preload has got to.</summary>
    private enum PreloadPhase : byte
    {
        Waiting,
        Holds,
        Terrain,
        Boundary,
        Wildlife,
    }
}
