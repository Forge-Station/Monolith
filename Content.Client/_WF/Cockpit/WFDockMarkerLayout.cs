using System.Numerics;

namespace Content.Client._WF.Cockpit;

/// <summary>A port's clickable area on the plot: a circle around its centre, plus its callout when one is drawn.</summary>
public readonly record struct WFDockTarget(Vector2 Center, float Reach, UIBox2? Callout);

/// <summary>Places compact dock callouts inside the plot without covering another callout.</summary>
public static class WFDockMarkerLayout
{
    /// <summary>Finds the port a click is aimed at: a callout under the point wins, else the nearest port in reach; -1 for none.</summary>
    public static int Pick(Vector2 point, IReadOnlyList<WFDockTarget> targets)
    {
        var best = -1;
        var bestDistance = float.PositiveInfinity;
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (target.Callout is { } callout && callout.Contains(point))
                return i;
            var distance = Vector2.DistanceSquared(point, target.Center);
            if (distance > target.Reach * target.Reach || distance >= bestDistance)
                continue;
            best = i;
            bestDistance = distance;
        }
        return best;
    }

    /// <summary>Finds the nearest free label slot, or leaves a crowded port identified by its action row.</summary>
    public static UIBox2? Place(Vector2 anchor, Vector2 size, Vector2 viewport, IReadOnlyList<UIBox2> occupied)
    {
        const float inset = 4;
        var bestDistance = float.PositiveInfinity;
        UIBox2? best = null;
        for (var y = inset; y + size.Y <= viewport.Y - inset; y += size.Y + 3)
        for (var x = inset; x + size.X <= viewport.X - inset; x += size.X + 3)
        {
            var bounds = UIBox2.FromDimensions(new Vector2(x, y), size);
            var distance = Vector2.DistanceSquared(bounds.Center, anchor);
            if (distance >= bestDistance)
                continue;
            var available = true;
            foreach (var other in occupied)
            {
                if (!bounds.Intersects(other))
                    continue;
                available = false;
                break;
            }
            if (!available)
                continue;
            best = bounds;
            bestDistance = distance;
        }
        return best;
    }
}
